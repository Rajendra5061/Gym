using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using GymManagement.Api.Authorization;
using GymManagement.Api.Filters;
using GymManagement.Api.Middleware;
using GymManagement.Api.Services;
using GymManagement.Application;
using GymManagement.Application.Common;
using GymManagement.Application.Interfaces;
using GymManagement.Infrastructure;
using GymManagement.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------------------------
// Logging
// ---------------------------------------------------------------------------------------------
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// ---------------------------------------------------------------------------------------------
// Application & infrastructure
// ---------------------------------------------------------------------------------------------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.IsDevelopment());

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

builder.Services
    .AddControllers(options => options.Filters.Add<ValidationFilter>())
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        // Enums travel as integers, which the TypeScript client mirrors as numeric enums.
    });

builder.Services.AddEndpointsApiExplorer();

// ---------------------------------------------------------------------------------------------
// Authentication
// ---------------------------------------------------------------------------------------------
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtSecret = jwtSection["Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:Secret is missing or shorter than 32 characters. Supply it through an environment " +
        "variable (Jwt__Secret), user secrets or a secret store.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.SaveToken = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.FromSeconds(jwtSection.GetValue("ClockSkewSeconds", 30)),
            NameClaimType = ClaimTypes.NameIdentifier,
            RoleClaimType = ClaimTypes.Role
        };

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                if (context.Exception is SecurityTokenExpiredException)
                    context.Response.Headers.Append("Token-Expired", "true");
                return Task.CompletedTask;
            }
        };
    });

// ---------------------------------------------------------------------------------------------
// Authorization — permission policies materialised on demand
// ---------------------------------------------------------------------------------------------
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build();
});

// ---------------------------------------------------------------------------------------------
// Rate limiting
// ---------------------------------------------------------------------------------------------
var globalPermit = builder.Configuration.GetValue("RateLimiting:GlobalPermitPerMinute", 200);
var authPermit = builder.Configuration.GetValue("RateLimiting:AuthPermitPerMinute", 10);
var webhookPermit = builder.Configuration.GetValue("RateLimiting:WebhookPermitPerMinute", 300);

// Path prefix of the anonymous gateway webhook endpoint.
const string WebhookPathPrefix = "/api/payments/webhook";

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        // Gateway webhooks are excluded from the per-user limiter, but not from limiting.
        //
        // The per-user partition is the wrong bucket for them twice over: a gateway holds no JWT,
        // so every delivery would fall into the shared "anonymous"/by-IP partition and a burst of
        // retries could spend the budget of unrelated callers behind the same address — and a flood
        // aimed at the webhook would then lock staff out of the API. Giving the path a partition
        // namespace of its own keyed by source IP fixes both directions: webhook traffic can never
        // consume a signed-in user's allowance, and no amount of it can deny service to anyone
        // else. It is still capped, so this is an exclusion from the user limiter rather than an
        // exemption from rate limiting - which an anonymous, unauthenticated endpoint must never
        // have. Signature verification happens before any database work, and the body is capped in
        // the controller, so refused floods stay cheap.
        if (context.Request.Path.StartsWithSegments(WebhookPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var source = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            return RateLimitPartition.GetFixedWindowLimiter($"webhook|{source}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = webhookPermit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
        }

        // "user|" / "ip|" prefixes keep a caller's partition from ever colliding with the webhook
        // one, whatever a user id or address happens to look like.
        var partitionKey = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value is { } userId
            ? $"user|{userId}"
            : $"ip|{context.Connection.RemoteIpAddress?.ToString() ?? "anonymous"}";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = globalPermit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        var payload = ApiResponse.Fail(
            "Too many requests. Please wait a moment and try again.", "RATE_LIMITED");
        await context.HttpContext.Response.WriteAsync(
            JsonSerializer.Serialize(payload,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), ct);
    };
});

// ---------------------------------------------------------------------------------------------
// CORS
// ---------------------------------------------------------------------------------------------
const string CorsPolicy = "GymCors";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? Array.Empty<string>();

builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
{
    if (allowedOrigins.Length > 0)
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    else
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
}));

// ---------------------------------------------------------------------------------------------
// Swagger
// ---------------------------------------------------------------------------------------------
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Gym Management API",
        Version = "v1",
        Description =
            "Backend for the Gym Management web application. Every endpoint except the " +
            "authentication ones requires a JWT bearer token, and each is additionally guarded by a " +
            "role/permission policy. All money values are decimal and calculated server-side."
    });

    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the access token returned by /api/auth/login.",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };

    options.AddSecurityDefinition("Bearer", scheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });
});

builder.Services.AddHealthChecks().AddDbContextCheck<GymDbContext>("database");

var app = builder.Build();

// ---------------------------------------------------------------------------------------------
// Database migration & seeding
// ---------------------------------------------------------------------------------------------
if (app.Configuration.GetValue("Database:AutoMigrate", true))
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var context = scope.ServiceProvider.GetRequiredService<GymDbContext>();
        logger.LogInformation("Applying database migrations…");
        await context.Database.MigrateAsync();

        await scope.ServiceProvider.GetRequiredService<IDbSeeder>().SeedAsync();
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Database migration or seeding failed. The API cannot start.");
        throw;
    }
}

// ---------------------------------------------------------------------------------------------
// Pipeline
// ---------------------------------------------------------------------------------------------
app.UseExceptionHandling();

if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.UseHttpsRedirection();
app.UseSerilogRequestLogging();
app.UseCors(CorsPolicy);

// Ahead of the authorization middleware on purpose. The FallbackPolicy demands an authenticated
// user for anything carrying no authorization metadata, and the Swagger UI is served by
// middleware rather than as a routed endpoint — so registering it later made /swagger answer 401
// instead of rendering. Nothing here exposes data: the document only describes the API, and every
// endpoint it lists still enforces its own policy when called.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue("Swagger:Enabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Gym Management API v1");
        options.DocumentTitle = "Gym Management API";
    });
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

/// <summary>Exposed so the integration tests can host the API with <c>WebApplicationFactory</c>.</summary>
public partial class Program;
