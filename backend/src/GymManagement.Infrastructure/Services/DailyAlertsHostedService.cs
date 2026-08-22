using GymManagement.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Services;

/// <summary>
/// Raises the expiry / pending-payment reminders once a day without anyone pressing the Generate
/// button. The first run happens at the next local occurrence of the configured hour — never
/// immediately on start-up, so a restart storm cannot hammer the alert tables — and every run
/// after that follows 24 hours later. <see cref="INotificationService.GenerateSystemAlertsAsync"/>
/// deduplicates per day, so an extra run is always harmless.
/// </summary>
/// <remarks>
/// Configuration (all optional):
/// <list type="bullet">
///   <item><c>Notifications:DailyAlerts:Enabled</c> — default <c>true</c>; false disables the timer.</item>
///   <item><c>Notifications:DailyAlerts:Hour</c> — local hour 0–23 of the daily run; default 8.</item>
/// </list>
/// </remarks>
public sealed class DailyAlertsHostedService : BackgroundService
{
    private const int DefaultHour = 8;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DailyAlertsHostedService> _logger;

    public DailyAlertsHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<DailyAlertsHostedService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("Notifications:DailyAlerts:Enabled", true))
        {
            _logger.LogInformation("Daily alert generation is disabled (Notifications:DailyAlerts:Enabled).");
            return;
        }

        var hour = _configuration.GetValue("Notifications:DailyAlerts:Hour", DefaultHour);
        if (hour is < 0 or > 23)
        {
            _logger.LogWarning(
                "Notifications:DailyAlerts:Hour is {Hour}; it must be 0-23. Using {Default} instead.",
                hour, DefaultHour);
            hour = DefaultHour;
        }

        _logger.LogInformation("Daily alert generation scheduled for {Hour:00}:00 local time.", hour);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Always wait for the NEXT occurrence of the configured hour first, so a restart never
            // triggers an immediate run.
            var now = DateTime.Now;
            var nextRun = now.Date.AddHours(hour);
            if (nextRun <= now) nextRun = nextRun.AddDays(1);

            try
            {
                await Task.Delay(nextRun - now, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return; // Host is shutting down.
            }

            await RunOnceAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// One generation pass: in-app alerts, then the renewal-reminder emails, then the day's
    /// birthday and festival wishes. The halves fail independently — a broken mail provider must
    /// not stop alerts, and vice versa — and every failure is logged and swallowed: the timer
    /// must survive.
    /// </summary>
    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();

        try
        {
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
            var created = await notifications.GenerateSystemAlertsAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("Daily alert run finished; {Count} notification(s) raised.", created);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return; // Host is shutting down mid-run; nothing to report.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Daily alert generation failed; it will be retried at the next scheduled run.");
        }

        try
        {
            var reminders = scope.ServiceProvider.GetRequiredService<IExpiryReminderMailer>();
            var sent = await reminders.SendDueRemindersAsync(ct).ConfigureAwait(false);
            if (sent > 0)
                _logger.LogInformation("Renewal reminder emails sent to {Count} member(s).", sent);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Host is shutting down mid-run; nothing to report.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Renewal reminder emails failed; they will be retried at the next scheduled run.");
        }

        try
        {
            var wishes = scope.ServiceProvider.GetRequiredService<IWishesDispatcher>();
            var wished = await wishes.SendTodaysWishesAsync(ct).ConfigureAwait(false);
            if (wished > 0)
                _logger.LogInformation("Birthday/festival wishes sent to {Count} member(s).", wished);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Host is shutting down mid-run; nothing to report.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Birthday/festival wishes failed; they will be retried at the next scheduled run.");
        }
    }
}
