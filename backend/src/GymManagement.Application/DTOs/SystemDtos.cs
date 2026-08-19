using GymManagement.Application.Common;
using GymManagement.Domain.Enums;

namespace GymManagement.Application.DTOs;

public class NotificationDto
{
    public int Id { get; set; }
    public NotificationType Type { get; set; }
    public string TypeText => Type.ToString();
    public NotificationSeverity Severity { get; set; }
    public string SeverityText => Severity.ToString();
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int? UserId { get; set; }
    public int? MemberId { get; set; }
    public string? MemberName { get; set; }
    public string? EntityName { get; set; }
    public int? EntityId { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
}

public class NotificationQueryDto : PagedRequest
{
    public NotificationType? Type { get; set; }
    public NotificationSeverity? Severity { get; set; }
    public bool? IsRead { get; set; }
    public int? MemberId { get; set; }
    /// <summary>Restrict to notifications addressed to the caller or broadcast to all staff.</summary>
    public bool OnlyMine { get; set; } = true;
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

public class CreateNotificationDto
{
    public NotificationType Type { get; set; } = NotificationType.General;
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int? UserId { get; set; }
    public int? MemberId { get; set; }
    public string? EntityName { get; set; }
    public int? EntityId { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
}

public class NotificationCountsDto
{
    public int Total { get; set; }
    public int Unread { get; set; }
    public int Critical { get; set; }
    public int Warning { get; set; }
}

public class AuditLogDto
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? Description { get; set; }
    public string? IpAddress { get; set; }
    public string? DeviceInfo { get; set; }
    public DateTime ChangedAtUtc { get; set; }
}

public class AuditQueryDto : PagedRequest
{
    public int? UserId { get; set; }
    public string? Action { get; set; }
    public string? EntityName { get; set; }
    public int? EntityId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

public class LoginAttemptDto
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string UserNameOrEmail { get; set; } = string.Empty;
    public LoginResult Result { get; set; }
    public string ResultText => Result.ToString();
    public DateTime AttemptedAtUtc { get; set; }
    public string? IpAddress { get; set; }
    public string? DeviceInfo { get; set; }
    public string? FailureReason { get; set; }
}

public class SystemSettingDto
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string DataType { get; set; } = "string";
    public string Category { get; set; } = "General";
    public string? Description { get; set; }
    public bool IsReadOnly { get; set; }
}

/// <summary>
/// The slice of the gym profile that any signed-in user may read, so the application shell can
/// render the gym's identity and format money. Deliberately a separate allow-listed type rather
/// than a filtered <see cref="GymSettingsDto"/>: a column added to the gym profile later cannot
/// leak through this endpoint unless somebody adds it here on purpose.
/// </summary>
public class GymBrandingDto
{
    /// <summary>Shown in the navbar and on the public screens.</summary>
    public string GymName { get; set; } = string.Empty;

    /// <summary>Relative path to the gym logo used beside the name.</summary>
    public string? LogoPath { get; set; }

    /// <summary>ISO code used when formatting amounts.</summary>
    public string CurrencyCode { get; set; } = "INR";

    /// <summary>Symbol prefixed to every amount the member sees.</summary>
    public string CurrencySymbol { get; set; } = "₹";

    /// <summary>Front-desk telephone number, already published on the anonymous landing page.</summary>
    public string? Phone { get; set; }

    /// <summary>Front-desk e-mail address, already published on the anonymous landing page.</summary>
    public string? Email { get; set; }

    /// <summary>Street address and city, for the landing page footer. Public by nature: it is
    /// what the gym prints on its receipts and tells every caller who asks where to come.</summary>
    public string? Address { get; set; }
    public string? City { get; set; }

    /// <summary>
    /// Narrows a full profile down to the branding slice. The single place the allow-list is
    /// applied, so reviewing what a member can read means reading this method.
    /// </summary>
    public static GymBrandingDto From(GymSettingsDto gym)
    {
        ArgumentNullException.ThrowIfNull(gym);
        return new GymBrandingDto
        {
            GymName = gym.GymName,
            LogoPath = gym.LogoPath,
            CurrencyCode = gym.CurrencyCode,
            CurrencySymbol = gym.CurrencySymbol,
            Phone = gym.Phone,
            Email = gym.Email,
            Address = gym.Address,
            City = gym.City
        };
    }
}

public class GymSettingsDto
{
    public int Id { get; set; }
    public string GymName { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? LogoPath { get; set; }
    public string? TaxNumber { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public string CurrencySymbol { get; set; } = "₹";
    public string? UpiId { get; set; }
    public string? UpiPayeeName { get; set; }
    public string? UpiQrImagePath { get; set; }
    public TimeSpan OpeningTime { get; set; }
    public TimeSpan ClosingTime { get; set; }
    public string? ReceiptPrefix { get; set; }
    public string? MemberCodePrefix { get; set; }
    public string? ReceiptFooterText { get; set; }
    public int ExpiryReminderDays { get; set; } = 7;
    public int DefaultGracePeriodDays { get; set; }
    public int MaxFailedLoginAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public bool AllowExpiredMemberCheckIn { get; set; }
}

public class LicenseStatusDto
{
    public LicenseStatus Status { get; set; }
    public string StatusText => Status.ToString();
    public string? LicenseKey { get; set; }
    public string? MaskedLicenseKey { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string GymIdentifier { get; set; } = string.Empty;
    public bool IsTrial { get; set; }
    public DateTime? StartDateUtc { get; set; }
    public DateTime? ExpiryDateUtc { get; set; }
    public int DaysRemaining { get; set; }
    public bool IsValid { get; set; }
    public bool IsExpiringSoon { get; set; }
    public int? MaxMembers { get; set; }
    public int CurrentMembers { get; set; }
    public int? MaxUsers { get; set; }
    public int CurrentUsers { get; set; }
    public List<string> EnabledFeatures { get; set; } = new();
    public string? Message { get; set; }
    /// <summary>True when the local clock appears to have been moved backwards.</summary>
    public bool ClockTamperDetected { get; set; }
}

public class ActivateLicenseDto
{
    public string LicenseKey { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
}

public class StartTrialDto
{
    public string CustomerName { get; set; } = string.Empty;
    public string? GymIdentifier { get; set; }
}

public class BackupRecordDto
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string FileSizeText => FileSizeBytes < 1024 * 1024
        ? $"{FileSizeBytes / 1024.0:F1} KB"
        : $"{FileSizeBytes / (1024.0 * 1024.0):F1} MB";
    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedByName { get; set; }
    public string BackupType { get; set; } = "Full";
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? RestoredAtUtc { get; set; }
    public string? Notes { get; set; }
}

public class CreateBackupDto
{
    /// <summary>Server-side folder. When empty the configured default backup folder is used.</summary>
    public string? TargetFolder { get; set; }
    public string? Notes { get; set; }
}

public class RestoreBackupDto
{
    public int BackupId { get; set; }
    /// <summary>Must be the exact database name; guards against restoring over the wrong database.</summary>
    public string ConfirmationText { get; set; } = string.Empty;
}

/// <summary>One soft-deleted row shown in the recycle bin.</summary>
public class RecycleBinItemDto
{
    public string EntityName { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int? DeletedBy { get; set; }
    public string? DeletedByName { get; set; }
}

public class RecycleBinQueryDto : PagedRequest
{
    /// <summary>Members, Trainers, Subscriptions, Payments, Exercises, WorkoutPlans, Expenses, Users…</summary>
    public string? EntityName { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

public class RecycleBinActionDto
{
    public string EntityName { get; set; } = string.Empty;
    public List<int> EntityIds { get; set; } = new();
    /// <summary>Required for permanent deletion: must equal "PERMANENTLY DELETE".</summary>
    public string? ConfirmationText { get; set; }
}

/// <summary>Lightweight id/name pairs for combo boxes.</summary>
public class LookupDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Extra { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// How mail is actually configured on this server, as resolved once at start-up from the
/// <c>Email</c> configuration section — appsettings, user secrets or environment variables — and
/// never from the database.
///
/// There is deliberately no password member and never will be: the SMTP password is supplied
/// through <c>Email__Smtp__Password</c>, the settings table any administrator can read back over
/// the API never holds it, and this type reports only whether one resolved.
/// </summary>
public class EmailStatusDto
{
    /// <summary><c>None</c>, <c>File</c> or <c>Smtp</c> — the sender that was actually built.</summary>
    public string Provider { get; set; } = "None";

    /// <summary>False for the null sender: every message is accepted and discarded.</summary>
    public bool IsEnabled { get; set; }

    public string? FromAddress { get; set; }
    public string? FromName { get; set; }
    public string? ReplyToAddress { get; set; }

    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    public bool? SmtpUseStartTls { get; set; }
    public string? SmtpUserName { get; set; }

    /// <summary>Whether a password resolved from configuration. Never the value.</summary>
    public bool? SmtpCredentialsConfigured { get; set; }

    /// <summary>Absolute path of the mail-drop folder, when the provider is <c>File</c>.</summary>
    public string? FileSinkDirectory { get; set; }

    /// <summary>Why sending is off, when the server knows — e.g. a provider that could not be built.</summary>
    public string? Reason { get; set; }
}

/// <summary>Asks the server to put one message through whichever provider is live.</summary>
public class SendTestEmailDto
{
    public string ToAddress { get; set; } = string.Empty;
}

/// <summary>What the server did with a test message.</summary>
public class EmailTestResultDto
{
    public bool Sent { get; set; }
    public string? Provider { get; set; }
    public string? ToAddress { get; set; }

    /// <summary>Where it landed: the mail-drop file for <c>File</c>, <c>host:port</c> for <c>Smtp</c>.</summary>
    public string? Destination { get; set; }

    /// <summary>Why it was skipped or refused, when it was.</summary>
    public string? Reason { get; set; }
}
