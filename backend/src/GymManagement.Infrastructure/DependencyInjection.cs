using GymManagement.Application.Common;
using GymManagement.Application.Interfaces;
using GymManagement.Infrastructure.Common;
using GymManagement.Infrastructure.Data;
using GymManagement.Infrastructure.Messaging;
using GymManagement.Infrastructure.Payments;
using GymManagement.Infrastructure.Reporting;
using GymManagement.Infrastructure.Security;
using GymManagement.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GymManagement.Infrastructure;

/// <summary>Registers the data access layer and every business service implementation.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <param name="isDevelopment">
    /// Decides the default mail provider when the <c>Email</c> section says nothing: the local file
    /// sink in Development, no sending at all anywhere else. Passing nothing keeps the safe option.
    /// </param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services,
        IConfiguration configuration, bool isDevelopment = false)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is not configured.");

        services.AddDbContext<GymDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(GymDbContext).Assembly.FullName);
                sql.CommandTimeout(120);

                // Deliberately NOT calling EnableRetryOnFailure here. Its retrying execution
                // strategy rejects user-initiated transactions, and the billing services depend on
                // explicit BeginTransactionAsync blocks so that a subscription and its payment
                // commit or roll back as one unit. Transaction safety is a hard requirement;
                // transient-fault retry is not. If this is ever deployed against a cloud database
                // where retries matter, re-enable it and wrap every transactional block in
                // Database.CreateExecutionStrategy().ExecuteAsync(...) so both work together.
            });

            if (configuration.GetValue("Database:EnableSensitiveDataLogging", false))
                options.EnableSensitiveDataLogging();

            options.EnableDetailedErrors(configuration.GetValue("Database:EnableDetailedErrors", false));
        });

        // Cross-cutting infrastructure.
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        // Outbound email. Credentials are bound from configuration only — appsettings, environment
        // variables or user secrets — and never from the database, whose settings table any
        // administrator can read back over the API. With no Email section configured this resolves
        // to the null sender outside Development and to the local file sink inside it, so a fresh
        // checkout sends nothing to anybody and throws nothing while doing so.
        //
        // The options are handed over as an accessor rather than a value on purpose: reading .Value
        // is what binds the section, and binding can throw on a bad value. Evaluating it inside the
        // factory lets a broken Email section degrade to the null sender instead of taking down the
        // first module that resolves IEmailSender — which is payments.
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.AddSingleton<IEmailSender>(sp => EmailSenderFactory.CreateFromOptionsAccessor(
            () => sp.GetRequiredService<IOptions<EmailOptions>>().Value,
            sp.GetRequiredService<ILoggerFactory>(),
            isDevelopment));

        services.AddScoped<ICodeGeneratorService, CodeGeneratorService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<ILookupService, LookupService>();

        // Reads the sender built above and puts a test message through it. Scoped because it
        // reads the gym name; the sender it reports on stays the singleton every module shares.
        services.AddScoped<IEmailDiagnosticsService, EmailDiagnosticsService>();

        // Business services, grouped by module.
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();

        services.AddScoped<IMemberService, MemberService>();
        services.AddScoped<ITrainerService, TrainerService>();

        services.AddScoped<IExerciseService, ExerciseService>();
        services.AddScoped<IWorkoutService, WorkoutService>();
        services.AddScoped<IDietPlanService, DietPlanService>();
        services.AddScoped<IAttendanceService, AttendanceService>();

        services.AddScoped<IMembershipPlanService, MembershipPlanService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPaymentReceiptMailer, PaymentReceiptMailer>();

        // Automatic payment reconciliation. Off unless Payments:Gateway says otherwise, and the
        // signing secret is bound from configuration/environment only — never from the gym settings
        // table, which any administrator can read back over GET /api/settings. Supply it as
        // Payments__Gateway__Secret or with 'dotnet user-secrets set Payments:Gateway:Secret ...'.
        services.Configure<PaymentGatewayOptions>(configuration.GetSection(PaymentGatewayOptions.SectionName));
        services.AddSingleton<IPaymentGatewayConfiguration, PaymentGatewayConfiguration>();
        services.AddScoped<IPaymentWebhookService, PaymentWebhookService>();
        services.AddScoped<IExpenseService, ExpenseService>();
        services.AddScoped<ISalaryPaymentService, SalaryPaymentService>();

        services.AddScoped<IEquipmentService, EquipmentService>();
        services.AddScoped<IEnquiryService, EnquiryService>();
        services.AddScoped<IFeedbackService, FeedbackService>();

        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IExcelExportService, ExcelExportService>();
        services.AddScoped<IPdfExportService, PdfExportService>();
        services.AddScoped<IReceiptEmailBuilder, ReceiptEmailBuilder>();

        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddScoped<ILicenseService, LicenseService>();
        services.AddScoped<IBackupService, BackupService>();
        services.AddScoped<IRecycleBinService, RecycleBinService>();

        services.AddScoped<IDbSeeder, DbSeeder>();

        // Raises expiry / pending-payment reminders once a day at the configured local hour.
        // Controlled by Notifications:DailyAlerts:Enabled / :Hour; never fires on start-up.
        services.AddHostedService<DailyAlertsHostedService>();

        return services;
    }
}
