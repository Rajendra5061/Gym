using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GymManagement.IntegrationTests;

/// <summary>
/// Hosts the real API in-process against the EF Core in-memory provider, so the whole request
/// pipeline (routing, authentication, permission policies, validation filter and the exception
/// middleware) is exercised without needing a SQL Server instance.
/// </summary>
public sealed class GymApiFactory : WebApplicationFactory<Program>
{
    public const string AdminUserName = "admin";
    public const string AdminPassword = "Admin@12345";

    /// <summary>A JSON secret long enough for the 32 character minimum the host enforces.</summary>
    private const string JwtSecret = "integration-test-jwt-signing-secret-of-at-least-48-characters-long";

    public const string LicenseSecret = "integration-test-licence-signing-secret-value";

    private readonly string _databaseName = $"gym-integration-{Guid.NewGuid():N}";

    /// <summary>camelCase-insensitive options matching what the API writes.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // These must be UseSetting rather than ConfigureAppConfiguration: Program.cs reads several
        // of them (the JWT secret, both rate limits) while the WebApplicationBuilder is still being
        // assembled, and ConfigureAppConfiguration sources are only merged in at Build() time.
        // UseSetting feeds host configuration, which is in place before the first read.
        var settings = new Dictionary<string, string>
        {
            // AddInfrastructure reads this eagerly, so it must exist even though it is never used.
            ["ConnectionStrings:DefaultConnection"] = "Server=(unused);Database=GymTests;",

            // Never migrate or seed at startup: the in-memory provider cannot run migrations.
            ["Database:AutoMigrate"] = "false",

            ["Jwt:Secret"] = JwtSecret,
            ["Jwt:Issuer"] = "GymManagement.IntegrationTests",
            ["Jwt:Audience"] = "GymManagement.IntegrationTests.Client",
            ["Jwt:AccessTokenMinutes"] = "60",
            ["Jwt:RefreshTokenDays"] = "7",

            ["License:Secret"] = LicenseSecret,
            ["License:TrialDays"] = "30",
            ["License:TrialMaxMembers"] = "5000",

            // The suite signs in far more often than a human would; the production limits would
            // start returning 429 half way through and make the run non-deterministic.
            ["RateLimiting:GlobalPermitPerMinute"] = "1000000",
            ["RateLimiting:AuthPermitPerMinute"] = "1000000",

            // Off by default in production so that a reply which differs for a real account cannot
            // be used to enumerate user names. The reset-flow tests need the token to carry on with,
            // exactly as the local Forgot Password page does, so the suite opts in here.
            // ForgotPasswordDisclosureTests covers the secure default by turning this back off.
            ["Auth:ReturnResetTokenToAnonymousCallers"] = "true",

            ["Serilog:MinimumLevel:Default"] = "Warning",
            ["Serilog:MinimumLevel:Override:Microsoft"] = "Warning",
            ["Serilog:MinimumLevel:Override:System"] = "Warning",

            ["Swagger:Enabled"] = "false"
        };

        foreach (var (key, value) in settings) builder.UseSetting(key, value);

        builder.ConfigureServices(services =>
        {
            // Drop the SQL Server registration before adding the in-memory one, otherwise EF sees
            // two providers configured for the same context.
            foreach (var descriptor in services
                         .Where(d => d.ServiceType == typeof(DbContextOptions<GymDbContext>) ||
                                     d.ServiceType == typeof(DbContextOptions) ||
                                     d.ServiceType == typeof(GymDbContext))
                         .ToList())
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<GymDbContext>(options => options
                .UseInMemoryDatabase(_databaseName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        });
    }

    /// <summary>Creates the schema and seeds roles, permissions, plans, methods and the admin user.</summary>
    public async Task InitialiseDatabaseAsync()
    {
        using var scope = Services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<GymDbContext>();
        await context.Database.EnsureCreatedAsync();

        await scope.ServiceProvider.GetRequiredService<IDbSeeder>().SeedAsync();
    }

    /// <summary>Runs an action against a scoped <see cref="GymDbContext"/>.</summary>
    public async Task WithDbAsync(Func<GymDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<GymDbContext>());
    }

    /// <summary>Reads a value out of the database inside its own scope.</summary>
    public async Task<T> FromDbAsync<T>(Func<GymDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<GymDbContext>());
    }

    // ---------------------------------------------------------------- auth

    /// <summary>Logs in and returns the whole login payload.</summary>
    public async Task<LoginResponseDto> LoginAsync(
        string userName = AdminUserName, string password = AdminPassword)
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            UserNameOrEmail = userName,
            Password = password,
            DeviceInfo = "integration-tests"
        });

        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponseDto>>(Json);

        if (envelope?.Data is null)
            throw new InvalidOperationException($"Login as '{userName}' did not return a token.");

        return envelope.Data;
    }

    /// <summary>A client whose bearer header carries a freshly issued access token.</summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync(
        string userName = AdminUserName, string password = AdminPassword)
    {
        var login = await LoginAsync(userName, password);

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    /// <summary>A client carrying an explicit token, for cases that mint their own.</summary>
    public HttpClient CreateClientWithToken(string accessToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}

/// <summary>
/// One shared host for the whole integration suite. xunit runs the classes in a collection
/// sequentially, so a test may safely change a global setting as long as it restores it.
/// </summary>
[CollectionDefinition(Name)]
public sealed class GymApiCollection : ICollectionFixture<GymApiFixture>
{
    public const string Name = "Gym API";
}

/// <summary>Owns the factory and guarantees the database is created and seeded exactly once.</summary>
public sealed class GymApiFixture : IAsyncLifetime
{
    public GymApiFactory Factory { get; } = new();

    public async Task InitializeAsync()
    {
        // Touching Services builds the host; the seeder then runs against the in-memory database.
        await Factory.InitialiseDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        Factory.Dispose();
        return Task.CompletedTask;
    }
}
