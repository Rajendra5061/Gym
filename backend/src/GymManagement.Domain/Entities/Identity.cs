using GymManagement.Domain.Common;
using GymManagement.Domain.Enums;

namespace GymManagement.Domain.Entities;

/// <summary>An application login account. Members, staff and trainers all authenticate through this table.</summary>
public class User : SoftDeletableEntity
{
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string FullName { get; set; } = string.Empty;

    /// <summary>BCrypt hash. The plain password is never persisted or logged.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public UserStatus Status { get; set; } = UserStatus.Active;
    public bool MustChangePassword { get; set; }

    public int FailedLoginAttempts { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }

    /// <summary>Single-use hashed token issued by the forgot-password flow.</summary>
    public string? PasswordResetTokenHash { get; set; }
    public DateTime? PasswordResetTokenExpiresUtc { get; set; }

    public string? ProfilePhotoPath { get; set; }

    // Optional links to the business profile this account represents.
    public int? MemberId { get; set; }
    public Member? Member { get; set; }
    public int? TrainerId { get; set; }
    public Trainer? Trainer { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}

public class Role : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>System roles cannot be renamed or deleted.</summary>
    public bool IsSystemRole { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public class Permission : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public class UserRole : BaseEntity
{
    public int UserId { get; set; }
    public User? User { get; set; }
    public int RoleId { get; set; }
    public Role? Role { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public int? AssignedBy { get; set; }
}

public class RolePermission : BaseEntity
{
    public int RoleId { get; set; }
    public Role? Role { get; set; }
    public int PermissionId { get; set; }
    public Permission? Permission { get; set; }
}

/// <summary>Rotating refresh token. Only a hash of the token value is stored.</summary>
public class RefreshToken : BaseEntity
{
    public int UserId { get; set; }
    public User? User { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CreatedByIp { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? RevokedReason { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public bool IsActive => RevokedAtUtc == null && DateTime.UtcNow < ExpiresAtUtc;
}

/// <summary>Every authentication attempt, successful or not. Feeds lockout and the audit report.</summary>
public class LoginAttempt : BaseEntity
{
    public int? UserId { get; set; }
    public User? User { get; set; }
    public string UserNameOrEmail { get; set; } = string.Empty;
    public LoginResult Result { get; set; }
    public DateTime AttemptedAtUtc { get; set; } = DateTime.UtcNow;
    public string? IpAddress { get; set; }
    public string? DeviceInfo { get; set; }
    public string? FailureReason { get; set; }
}
