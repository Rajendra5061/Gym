using System.Globalization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Enums;

namespace GymManagement.UnitTests.TestBase;

/// <summary>One entry written to the audit trail, captured so a test can assert it happened.</summary>
public sealed record AuditEntry(string Action, string EntityName, int? EntityId, string? Description);

/// <summary>
/// Records what would have been audited instead of writing rows, so tests such as M-01 can assert
/// that the operation was audited without pulling the real audit table into the arrangement.
/// </summary>
public sealed class RecordingAuditService : IAuditService
{
    public List<AuditEntry> Entries { get; } = new();

    public Task LogAsync(string action, string entityName, int? entityId = null,
        object? oldValues = null, object? newValues = null, string? description = null,
        CancellationToken ct = default)
    {
        Entries.Add(new AuditEntry(action, entityName, entityId, description));
        return Task.CompletedTask;
    }

    public Task LogForUserAsync(int? userId, string? userName, string action, string entityName,
        int? entityId = null, object? oldValues = null, object? newValues = null,
        string? description = null, string? ipAddress = null, string? deviceInfo = null,
        CancellationToken ct = default)
    {
        Entries.Add(new AuditEntry(action, entityName, entityId, description));
        return Task.CompletedTask;
    }

    public bool Contains(string action, string? entityName = null) =>
        Entries.Any(e => e.Action == action && (entityName is null || e.EntityName == entityName));
}

/// <summary>One notification that would have been raised.</summary>
public sealed record NotificationEntry(
    NotificationType Type, NotificationSeverity Severity, string Title, string Message,
    int? MemberId, int? UserId, string? EntityName, int? EntityId, string? DeduplicationKey);

/// <summary>
/// Captures notifications rather than persisting them. De-duplicates on the key exactly like the
/// real service so idempotency cases (S-19, F-02) behave the same way.
/// </summary>
public sealed class RecordingNotificationService : INotificationService
{
    public List<NotificationEntry> Raised { get; } = new();

    /// <summary>Set to true to make every notification throw, proving the caller is unaffected.</summary>
    public bool ThrowOnNotify { get; set; }

    public Task NotifyAsync(NotificationType type, NotificationSeverity severity, string title,
        string message, int? memberId = null, int? userId = null, string? entityName = null,
        int? entityId = null, string? deduplicationKey = null, CancellationToken ct = default)
    {
        if (ThrowOnNotify) throw new InvalidOperationException("Notification sink is unavailable.");

        if (deduplicationKey is not null &&
            Raised.Any(n => n.DeduplicationKey == deduplicationKey))
            return Task.CompletedTask;

        Raised.Add(new NotificationEntry(type, severity, title, message, memberId, userId,
            entityName, entityId, deduplicationKey));

        return Task.CompletedTask;
    }

    public int CountOf(NotificationType type) => Raised.Count(n => n.Type == type);

    // The rest of the surface is never exercised by the services under test.
    public Task<PagedResult<NotificationDto>> GetPagedAsync(NotificationQueryDto query, CancellationToken ct = default) =>
        Task.FromResult(PagedResult<NotificationDto>.Empty());

    public Task<NotificationCountsDto> GetCountsAsync(CancellationToken ct = default) =>
        Task.FromResult(new NotificationCountsDto());

    public Task<NotificationDto> CreateAsync(CreateNotificationDto dto, CancellationToken ct = default) =>
        Task.FromResult(new NotificationDto { Title = dto.Title, Message = dto.Message });

    public Task MarkAsReadAsync(IEnumerable<int> ids, CancellationToken ct = default) => Task.CompletedTask;

    public Task MarkAllAsReadAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task DeleteAsync(int id, CancellationToken ct = default) => Task.CompletedTask;

    public Task<int> GenerateSystemAlertsAsync(CancellationToken ct = default) => Task.FromResult(0);
}

/// <summary>
/// Serves a single, mutable <see cref="GymSettingsDto"/>. Tests change one field (for example
/// <c>AllowExpiredMemberCheckIn</c> or <c>DefaultGracePeriodDays</c>) and re-run the operation.
/// </summary>
public sealed class StubSettingsService : ISettingsService
{
    public GymSettingsDto Settings { get; set; } = new()
    {
        Id = 1,
        GymName = "Test Gym",
        CurrencyCode = "INR",
        CurrencySymbol = "₹",
        OpeningTime = new TimeSpan(6, 0, 0),
        ClosingTime = new TimeSpan(22, 0, 0),
        ReceiptPrefix = "RCP",
        MemberCodePrefix = "GYM",
        ExpiryReminderDays = 7,
        DefaultGracePeriodDays = 0,
        MaxFailedLoginAttempts = 5,
        LockoutMinutes = 15,
        AllowExpiredMemberCheckIn = false,
        UpiId = "testgym@upi",
        UpiPayeeName = "Test Gym"
    };

    /// <summary>Arbitrary values behind <see cref="GetValueAsync{T}"/>.</summary>
    public Dictionary<string, string?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<GymSettingsDto> GetGymSettingsAsync(CancellationToken ct = default) => Task.FromResult(Settings);

    public Task<GymSettingsDto> UpdateGymSettingsAsync(GymSettingsDto dto, CancellationToken ct = default)
    {
        Settings = dto;
        return Task.FromResult(Settings);
    }

    public Task<List<SystemSettingDto>> GetSystemSettingsAsync(string? category = null, CancellationToken ct = default) =>
        Task.FromResult(new List<SystemSettingDto>());

    public Task<SystemSettingDto> UpsertSystemSettingAsync(SystemSettingDto dto, CancellationToken ct = default) =>
        Task.FromResult(dto);

    public Task<string?> GetValueAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(Values.TryGetValue(key, out var value) ? value : null);

    public Task<T?> GetValueAsync<T>(string key, T? defaultValue = default, CancellationToken ct = default)
    {
        if (!Values.TryGetValue(key, out var raw) || raw is null) return Task.FromResult(defaultValue);

        try
        {
            return Task.FromResult((T?)Convert.ChangeType(raw, typeof(T), CultureInfo.InvariantCulture));
        }
        catch (Exception)
        {
            return Task.FromResult(defaultValue);
        }
    }
}

/// <summary>Predictable sequential business codes so assertions can name the expected value.</summary>
public sealed class SequentialCodeGenerator : ICodeGeneratorService
{
    private int _member;
    private int _trainer;
    private int _plan;
    private int _subscription;
    private int _receipt;
    private int _refund;
    private int _expense;

    /// <summary>Year stamped into the generated codes.</summary>
    public int Year { get; set; } = 2026;

    public Task<string> NextMemberCodeAsync(CancellationToken ct = default) =>
        Task.FromResult($"GYM-{Year}-{++_member:0000}");

    public Task<string> NextTrainerCodeAsync(CancellationToken ct = default) =>
        Task.FromResult($"TRN-{Year}-{++_trainer:0000}");

    public Task<string> NextPlanCodeAsync(CancellationToken ct = default) =>
        Task.FromResult($"PLN-{Year}-{++_plan:0000}");

    public Task<string> NextSubscriptionCodeAsync(CancellationToken ct = default) =>
        Task.FromResult($"SUB-{Year}-{++_subscription:0000}");

    public Task<string> NextReceiptNumberAsync(CancellationToken ct = default) =>
        Task.FromResult($"RCP-{Year}-{++_receipt:000000}");

    public Task<string> NextRefundNumberAsync(CancellationToken ct = default) =>
        Task.FromResult($"REF-{Year}-{++_refund:000000}");

    public Task<string> NextExpenseNumberAsync(CancellationToken ct = default) =>
        Task.FromResult($"EXP-{Year}-{++_expense:000000}");
}

/// <summary>A hasher that is fast enough for tests but still salted and one-way-ish.</summary>
public sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => $"hashed::{Guid.NewGuid():N}::{password}";

    public bool Verify(string password, string hash) =>
        hash.StartsWith("hashed::", StringComparison.Ordinal) &&
        hash.EndsWith($"::{password}", StringComparison.Ordinal);

    public string GenerateTemporaryPassword(int length = 12) => "Temp@Pass123";
}

/// <summary>Licence stub: capacity and validity are always fine unless a test says otherwise.</summary>
public sealed class StubLicenseService : ILicenseService
{
    /// <summary>Set to throw from <see cref="EnsureCapacityAsync"/> (case G-09).</summary>
    public Exception? CapacityFailure { get; set; }

    /// <summary>Set to throw from <see cref="EnsureValidAsync"/>.</summary>
    public Exception? ValidityFailure { get; set; }

    public Task<LicenseStatusDto> GetStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new LicenseStatusDto { Status = LicenseStatus.Trial, IsTrial = true, IsValid = true });

    public Task<LicenseStatusDto> StartTrialAsync(StartTrialDto dto, CancellationToken ct = default) =>
        GetStatusAsync(ct);

    public Task<LicenseStatusDto> ActivateAsync(ActivateLicenseDto dto, CancellationToken ct = default) =>
        GetStatusAsync(ct);

    public Task EnsureValidAsync(CancellationToken ct = default) =>
        ValidityFailure is null ? Task.CompletedTask : Task.FromException(ValidityFailure);

    public Task EnsureCapacityAsync(string resource, CancellationToken ct = default) =>
        CapacityFailure is null ? Task.CompletedTask : Task.FromException(CapacityFailure);
}

/// <summary>Produces a byte array that begins with the real PDF magic number.</summary>
public sealed class StubPdfExportService : IPdfExportService
{
    public byte[] ExportReport(ReportResultDto report, GymSettingsDto gym) => Pdf($"report:{report.Title}");

    public byte[] ExportReceipt(PaymentReceiptDto receipt) => Pdf($"receipt:{receipt.ReceiptNumber}");

    private static byte[] Pdf(string tag) =>
        System.Text.Encoding.ASCII.GetBytes($"%PDF-1.7\n% {tag}\n%%EOF\n");
}

/// <summary>
/// Records which payments were asked to have their receipt emailed, without composing or sending
/// anything. <see cref="ThrowOnSend"/> makes it fail the way a dead mail server would, so a test
/// can prove that a payment is still recorded when the receipt cannot go out.
/// </summary>
public sealed class RecordingReceiptMailer : IPaymentReceiptMailer
{
    public List<int> Requests { get; } = new();

    /// <summary>Set to make every call throw, as a broken mail provider would.</summary>
    public bool ThrowOnSend { get; set; }

    /// <summary>What a non-throwing call reports back.</summary>
    public ReceiptEmailOutcome Outcome { get; set; } = ReceiptEmailOutcome.Disabled;

    public int CountFor(int paymentId) => Requests.Count(id => id == paymentId);

    public Task<ReceiptEmailOutcome> SendReceiptAsync(int paymentId, CancellationToken ct = default)
    {
        Requests.Add(paymentId);

        if (ThrowOnSend) throw new InvalidOperationException("The mail provider is unavailable.");

        return Task.FromResult(Outcome);
    }
}

/// <summary>
/// Keeps every message in memory instead of delivering it, so a test can read the subject, both
/// bodies and the attachment. <see cref="ThrowOnSend"/> simulates a provider that is down.
/// </summary>
public sealed class CapturingEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = new();

    public bool IsEnabled { get; set; } = true;

    public string ProviderName => "Capture";

    /// <summary>Set to make every send throw the way an unreachable SMTP host would.</summary>
    public bool ThrowOnSend { get; set; }

    /// <summary>Set to make the provider decline the message rather than throw.</summary>
    public bool SkipEverything { get; set; }

    public Task<EmailDeliveryResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (ThrowOnSend) throw new InvalidOperationException("The mail provider is unavailable.");

        if (SkipEverything) return Task.FromResult(EmailDeliveryResult.Skipped(ProviderName, "declined"));

        Sent.Add(message);
        return Task.FromResult(EmailDeliveryResult.Sent(ProviderName));
    }
}

/// <summary>Produces a byte array that begins with the ZIP magic number an .xlsx uses.</summary>
public sealed class StubExcelExportService : IExcelExportService
{
    public byte[] Export(ReportResultDto report) => Zip();

    public byte[] ExportSheets(string title, IEnumerable<ReportResultDto> reports) => Zip();

    private static byte[] Zip() => new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00 };
}

/// <summary>Audit reads are not what the report tests are about, so they return an empty page.</summary>
public sealed class StubAuditQueryService : IAuditQueryService
{
    public Task<PagedResult<AuditLogDto>> GetPagedAsync(AuditQueryDto query, CancellationToken ct = default) =>
        Task.FromResult(PagedResult<AuditLogDto>.Empty());

    public Task<PagedResult<LoginAttemptDto>> GetLoginAttemptsAsync(PagedRequest query, CancellationToken ct = default) =>
        Task.FromResult(PagedResult<LoginAttemptDto>.Empty());

    public Task<List<RecentActivityDto>> GetRecentActivityAsync(int take = 10, CancellationToken ct = default) =>
        Task.FromResult(new List<RecentActivityDto>());

    public Task<List<string>> GetDistinctActionsAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<string>());

    public Task<List<string>> GetDistinctEntitiesAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<string>());
}
