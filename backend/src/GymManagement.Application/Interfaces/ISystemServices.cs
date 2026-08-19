using GymManagement.Application.Common;
using GymManagement.Application.DTOs;

namespace GymManagement.Application.Interfaces;

public interface IDashboardService
{
    Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default);
    Task<DashboardStatsDto> GetStatsAsync(CancellationToken ct = default);
    Task<MemberDashboardDto> GetMemberDashboardAsync(int memberId, CancellationToken ct = default);

    /// <summary>
    /// The compact dashboard for the signed-in trainer, resolved from the caller's trainer claim.
    /// Throws NotFound when the account has no trainer link.
    /// </summary>
    Task<TrainerDashboardDto> GetTrainerDashboardAsync(CancellationToken ct = default);
}

public interface IReportService
{
    Task<ReportResultDto> RunAsync(ReportRequestDto request, CancellationToken ct = default);
    Task<FileExportDto> ExportExcelAsync(ReportRequestDto request, CancellationToken ct = default);
    Task<FileExportDto> ExportPdfAsync(ReportRequestDto request, CancellationToken ct = default);
    Task<ProfitLossDto> GetProfitAndLossAsync(DateTime from, DateTime to, CancellationToken ct = default);
}

/// <summary>Renders a <see cref="ReportResultDto"/> to a spreadsheet.</summary>
public interface IExcelExportService
{
    byte[] Export(ReportResultDto report);
    byte[] ExportSheets(string title, IEnumerable<ReportResultDto> reports);
}

/// <summary>Renders reports and receipts to PDF.</summary>
public interface IPdfExportService
{
    byte[] ExportReport(ReportResultDto report, GymSettingsDto gym);
    byte[] ExportReceipt(PaymentReceiptDto receipt);
}

/// <summary>
/// Renders a receipt as the subject / HTML / plain-text triple that is emailed to a member.
/// Separate from <see cref="IPdfExportService"/> because a mail client is a far more hostile
/// rendering target than a PDF page: table layout, inline styles, no external assets.
/// </summary>
public interface IReceiptEmailBuilder
{
    ReceiptEmailContent Build(PaymentReceiptDto receipt);
}

public interface INotificationService
{
    Task<PagedResult<NotificationDto>> GetPagedAsync(NotificationQueryDto query, CancellationToken ct = default);
    Task<NotificationCountsDto> GetCountsAsync(CancellationToken ct = default);
    Task<NotificationDto> CreateAsync(CreateNotificationDto dto, CancellationToken ct = default);
    Task MarkAsReadAsync(IEnumerable<int> ids, CancellationToken ct = default);
    Task MarkAllAsReadAsync(CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Raises expiry / pending-payment reminders. Idempotent for a given day.</summary>
    Task<int> GenerateSystemAlertsAsync(CancellationToken ct = default);

    /// <summary>Fire-and-forget helper used by other services; failures never break the caller.</summary>
    Task NotifyAsync(Domain.Enums.NotificationType type, Domain.Enums.NotificationSeverity severity,
        string title, string message, int? memberId = null, int? userId = null,
        string? entityName = null, int? entityId = null, string? deduplicationKey = null,
        CancellationToken ct = default);
}

public interface IAuditQueryService
{
    Task<PagedResult<AuditLogDto>> GetPagedAsync(AuditQueryDto query, CancellationToken ct = default);
    Task<PagedResult<LoginAttemptDto>> GetLoginAttemptsAsync(PagedRequest query, CancellationToken ct = default);
    Task<List<RecentActivityDto>> GetRecentActivityAsync(int take = 10, CancellationToken ct = default);
    Task<List<string>> GetDistinctActionsAsync(CancellationToken ct = default);
    Task<List<string>> GetDistinctEntitiesAsync(CancellationToken ct = default);
}

public interface ISettingsService
{
    Task<GymSettingsDto> GetGymSettingsAsync(CancellationToken ct = default);
    Task<GymSettingsDto> UpdateGymSettingsAsync(GymSettingsDto dto, CancellationToken ct = default);
    /// <summary>
    /// Name, logo, currency and front-desk contact only — the slice of the profile that is safe for
    /// every signed-in user. Defaulted to a narrowing of the full profile so that any existing
    /// implementation is correct without changes; the database-backed one overrides it to select
    /// only those columns in the first place.
    /// </summary>
    async Task<GymBrandingDto> GetGymBrandingAsync(CancellationToken ct = default) =>
        GymBrandingDto.From(await GetGymSettingsAsync(ct).ConfigureAwait(false));
    Task<List<SystemSettingDto>> GetSystemSettingsAsync(string? category = null, CancellationToken ct = default);
    Task<SystemSettingDto> UpsertSystemSettingAsync(SystemSettingDto dto, CancellationToken ct = default);
    Task<string?> GetValueAsync(string key, CancellationToken ct = default);
    Task<T?> GetValueAsync<T>(string key, T? defaultValue = default, CancellationToken ct = default);
}

/// <summary>
/// Reports the mail configuration in force and puts a single test message through it.
///
/// Read-only over the configuration: nothing here can change how mail is set up, because none of
/// it lives in the database. Changing a provider means editing the server's Email section and
/// restarting the API.
/// </summary>
public interface IEmailDiagnosticsService
{
    /// <summary>The provider that was built at start-up and the settings it resolved. Carries no password.</summary>
    EmailStatusDto GetStatus();

    /// <summary>
    /// Sends one fixed message so an operator can see where mail actually goes. Never throws for a
    /// mail reason: a refused relay, an unwritable folder or a provider that is off all come back
    /// as <c>Sent = false</c> with the reason, which is the whole point of pressing the button.
    /// </summary>
    Task<EmailTestResultDto> SendTestAsync(SendTestEmailDto dto, CancellationToken ct = default);
}

public interface ILicenseService
{
    Task<LicenseStatusDto> GetStatusAsync(CancellationToken ct = default);
    Task<LicenseStatusDto> StartTrialAsync(StartTrialDto dto, CancellationToken ct = default);
    Task<LicenseStatusDto> ActivateAsync(ActivateLicenseDto dto, CancellationToken ct = default);
    /// <summary>Throws <see cref="LicenseAppException"/> when the installation may not be used.</summary>
    Task EnsureValidAsync(CancellationToken ct = default);
    /// <summary>Checks a member/user quota before a new record is created.</summary>
    Task EnsureCapacityAsync(string resource, CancellationToken ct = default);
}

public interface IBackupService
{
    Task<List<BackupRecordDto>> GetHistoryAsync(CancellationToken ct = default);
    Task<BackupRecordDto> CreateBackupAsync(CreateBackupDto dto, CancellationToken ct = default);
    Task<BackupRecordDto> RestoreBackupAsync(RestoreBackupDto dto, CancellationToken ct = default);
    Task DeleteBackupRecordAsync(int id, bool deleteFile, CancellationToken ct = default);
}

public interface IRecycleBinService
{
    Task<PagedResult<RecycleBinItemDto>> GetPagedAsync(RecycleBinQueryDto query, CancellationToken ct = default);
    Task<List<LookupDto>> GetEntityTypesAsync(CancellationToken ct = default);
    Task<int> RestoreAsync(RecycleBinActionDto dto, CancellationToken ct = default);
    Task<int> PurgeAsync(RecycleBinActionDto dto, CancellationToken ct = default);
}

/// <summary>Combo box data used across many screens.</summary>
public interface ILookupService
{
    Task<Dictionary<string, List<LookupDto>>> GetAllAsync(CancellationToken ct = default);
    Task<List<LookupDto>> GetEnumAsync(string enumName, CancellationToken ct = default);
}
