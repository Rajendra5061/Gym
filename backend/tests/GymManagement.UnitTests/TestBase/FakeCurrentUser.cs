using GymManagement.Application.Common;
using GymManagement.Domain.Constants;

namespace GymManagement.UnitTests.TestBase;

/// <summary>
/// Settable <see cref="ICurrentUserService"/> stub. <see cref="HasPermission"/> honours the
/// <see cref="Permissions"/> list and, like the API handler, treats the Admin role as holding
/// every permission.
/// </summary>
public sealed class FakeCurrentUser : ICurrentUserService
{
    public int? UserId { get; set; } = 1;
    public string? UserName { get; set; } = "admin";
    public string? FullName { get; set; } = "System Administrator";
    public int? MemberId { get; set; }
    public int? TrainerId { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = new[] { RoleNames.Admin };
    public IReadOnlyList<string> Permissions { get; set; } = Array.Empty<string>();
    public string? IpAddress { get; set; } = "127.0.0.1";
    public string? DeviceInfo { get; set; } = "xunit";
    public bool IsAuthenticated { get; set; } = true;

    public bool IsInRole(string role) =>
        Roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));

    public bool HasPermission(string permissionCode) =>
        IsInRole(RoleNames.Admin) ||
        Permissions.Any(p => string.Equals(p, permissionCode, StringComparison.OrdinalIgnoreCase));

    /// <summary>An administrator: every permission is implied.</summary>
    public static FakeCurrentUser Admin(int userId = 1) => new()
    {
        UserId = userId,
        UserName = "admin",
        FullName = "System Administrator",
        Roles = new[] { RoleNames.Admin }
    };

    /// <summary>A caller in a named role holding only the permissions passed in.</summary>
    public static FakeCurrentUser With(string role, params string[] permissions) => new()
    {
        UserId = 2,
        UserName = role.ToLowerInvariant(),
        FullName = $"{role} User",
        Roles = new[] { role },
        Permissions = permissions
    };

    /// <summary>A caller holding no permissions at all.</summary>
    public static FakeCurrentUser Anonymous() => new()
    {
        UserId = null,
        UserName = null,
        FullName = null,
        Roles = Array.Empty<string>(),
        Permissions = Array.Empty<string>(),
        IsAuthenticated = false
    };
}
