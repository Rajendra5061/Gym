using GymManagement.Domain.Common;
using GymManagement.Domain.Enums;

namespace GymManagement.Domain.Entities;

public class Notification : BaseEntity
{
    public NotificationType Type { get; set; } = NotificationType.General;
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    /// <summary>Recipient account. Null means the notification targets all staff/admins.</summary>
    public int? UserId { get; set; }
    public User? User { get; set; }
    public int? MemberId { get; set; }
    public Member? Member { get; set; }

    /// <summary>Entity the notification points at so the UI can navigate to it.</summary>
    public string? EntityName { get; set; }
    public int? EntityId { get; set; }

    public bool IsRead { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAtUtc { get; set; }

    /// <summary>Guards against generating the same reminder twice for the same entity/day.</summary>
    public string? DeduplicationKey { get; set; }
}

public class AuditLog : BaseEntity
{
    public int? UserId { get; set; }
    public User? User { get; set; }
    public string? UserName { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? Description { get; set; }
    public string? IpAddress { get; set; }
    public string? DeviceInfo { get; set; }
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Key/value application settings that an administrator may change at runtime.</summary>
public class SystemSetting : AuditableEntity
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string DataType { get; set; } = "string";
    public string Category { get; set; } = "General";
    public string? Description { get; set; }
    /// <summary>Read-only settings are shown but cannot be edited from the UI.</summary>
    public bool IsReadOnly { get; set; }
}

/// <summary>Single-row table holding the branding and business rules of this gym.</summary>
public class GymSetting : AuditableEntity
{
    public string GymName { get; set; } = "My Gym";
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

    public TimeSpan OpeningTime { get; set; } = new(6, 0, 0);
    public TimeSpan ClosingTime { get; set; } = new(22, 0, 0);

    public string? ReceiptPrefix { get; set; } = "RCP";
    public string? MemberCodePrefix { get; set; } = "GYM";
    public string? ReceiptFooterText { get; set; }

    public int ExpiryReminderDays { get; set; } = 7;
    public int DefaultGracePeriodDays { get; set; } = 0;
    public int MaxFailedLoginAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public bool AllowExpiredMemberCheckIn { get; set; }
}

/// <summary>Trial / commercial licence record for this installation.</summary>
public class License : AuditableEntity
{
    public string? LicenseKey { get; set; }
    public LicenseStatus Status { get; set; } = LicenseStatus.NotActivated;
    public string CustomerName { get; set; } = string.Empty;
    public string GymIdentifier { get; set; } = string.Empty;
    /// <summary>Hardware fingerprint the licence was issued for.</summary>
    public string? MachineId { get; set; }

    public DateTime? StartDateUtc { get; set; }
    public DateTime? ExpiryDateUtc { get; set; }
    public bool IsTrial { get; set; }
    public int TrialDays { get; set; } = 30;

    public int? MaxMembers { get; set; }
    public int? MaxUsers { get; set; }
    /// <summary>Comma separated feature flags enabled by this licence.</summary>
    public string? EnabledFeatures { get; set; }

    /// <summary>Signature over the licence payload, verified against the issuer public key.</summary>
    public string? Signature { get; set; }
    /// <summary>Last time the licence was validated against the server clock.</summary>
    public DateTime? LastValidatedUtc { get; set; }
    /// <summary>Highest date seen so far; protects against rolling the local clock backwards.</summary>
    public DateTime? HighWaterMarkUtc { get; set; }
    public string? Notes { get; set; }
}

public class BackupRecord : BaseEntity
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }
    public string BackupType { get; set; } = "Full";
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? RestoredAtUtc { get; set; }
    public int? RestoredByUserId { get; set; }
    public string? Notes { get; set; }
}
