using GymManagement.Application.Common;
using GymManagement.Domain.Enums;

namespace GymManagement.Application.DTOs;

public class UserListDto
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public UserStatus Status { get; set; }
    public string StatusText => Status.ToString();
    public string Roles { get; set; } = string.Empty;
    public DateTime? LastLoginAtUtc { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsLockedOut { get; set; }
}

public class UserDetailDto : UserListDto
{
    public int? MemberId { get; set; }
    public int? TrainerId { get; set; }
    public bool MustChangePassword { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public string? ProfilePhotoPath { get; set; }
    public List<int> RoleIds { get; set; } = new();
}

public class CreateUserDto
{
    public string UserName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    /// <summary>Optional. When empty the service generates a temporary password.</summary>
    public string? Password { get; set; }
    public bool MustChangePassword { get; set; } = true;
    public List<int> RoleIds { get; set; } = new();
    public int? MemberId { get; set; }
    public int? TrainerId { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Active;
}

public class UpdateUserDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public UserStatus Status { get; set; }
    public List<int> RoleIds { get; set; } = new();
}

public class UserQueryDto : PagedRequest
{
    public UserStatus? Status { get; set; }
    public int? RoleId { get; set; }
    public bool IncludeDeleted { get; set; }
}

/// <summary>Returned when an administrator creates an account or resets a password.</summary>
public class TemporaryPasswordDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string TemporaryPassword { get; set; } = string.Empty;
}

public class RoleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
    public int UserCount { get; set; }
    public List<string> Permissions { get; set; } = new();
}

public class SaveRoleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> Permissions { get; set; } = new();
}

public class PermissionDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>Permissions grouped by module for the roles &amp; permissions screen.</summary>
public class PermissionGroupDto
{
    public string Module { get; set; } = string.Empty;
    public List<PermissionDto> Permissions { get; set; } = new();
}
