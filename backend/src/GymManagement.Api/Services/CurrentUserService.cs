using System.Globalization;
using System.Security.Claims;
using GymManagement.Application.Common;
using GymManagement.Domain.Constants;

namespace GymManagement.Api.Services;

/// <summary>
/// Resolves the caller's identity from the validated JWT on the current request. Nothing here ever
/// trusts a request body: every value comes from a signed claim or from the connection itself.
/// </summary>
public sealed class CurrentUserService : ICurrentUserService
{
    // Claim types issued by Infrastructure/Security/JwtTokenService.
    private const string PermissionClaimType = "permission";
    private const string MemberIdClaimType = "memberId";
    private const string TrainerIdClaimType = "trainerId";
    private const string SubClaimType = "sub";
    private const string UniqueNameClaimType = "unique_name";
    private const string GivenNameClaimType = "given_name";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor) =>
        _httpContextAccessor = httpContextAccessor;

    private HttpContext? Context => _httpContextAccessor.HttpContext;

    private ClaimsPrincipal? Principal => Context?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    /// <summary>The JWT carries the user id both as <c>sub</c> and as the name identifier claim.</summary>
    public int? UserId => ParseInt(FindFirst(ClaimTypes.NameIdentifier, SubClaimType));

    public string? UserName => FindFirst(UniqueNameClaimType, ClaimTypes.Name);

    public string? FullName => FindFirst(GivenNameClaimType, ClaimTypes.GivenName);

    public int? MemberId => ParseInt(FindFirst(MemberIdClaimType));

    public int? TrainerId => ParseInt(FindFirst(TrainerIdClaimType));

    public IReadOnlyList<string> Roles => Principal?.FindAll(ClaimTypes.Role)
        .Select(c => c.Value)
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();

    public IReadOnlyList<string> Permissions => Principal?.FindAll(PermissionClaimType)
        .Select(c => c.Value)
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>
    /// Client IP. Honours the first entry of <c>X-Forwarded-For</c> when the API sits behind a
    /// reverse proxy, otherwise falls back to the socket address.
    /// </summary>
    public string? IpAddress
    {
        get
        {
            var context = Context;
            if (context is null) return null;

            var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                var first = forwarded.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(first))
                    return Truncate(first, 64);
            }

            return context.Connection.RemoteIpAddress?.ToString();
        }
    }

    public string? DeviceInfo
    {
        get
        {
            var userAgent = Context?.Request.Headers.UserAgent.ToString();
            return string.IsNullOrWhiteSpace(userAgent) ? null : Truncate(userAgent, 400);
        }
    }

    public bool IsInRole(string role) =>
        !string.IsNullOrWhiteSpace(role) &&
        Roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));

    /// <summary>An Admin implicitly holds every permission.</summary>
    public bool HasPermission(string permissionCode)
    {
        if (string.IsNullOrWhiteSpace(permissionCode)) return false;
        if (IsInRole(RoleNames.Admin)) return true;

        return Permissions.Any(p => string.Equals(p, permissionCode, StringComparison.OrdinalIgnoreCase));
    }

    private string? FindFirst(params string[] claimTypes)
    {
        var principal = Principal;
        if (principal is null) return null;

        foreach (var claimType in claimTypes)
        {
            var value = principal.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return null;
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
