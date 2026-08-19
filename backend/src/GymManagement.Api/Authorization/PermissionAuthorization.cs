using GymManagement.Domain.Constants;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace GymManagement.Api.Authorization;

/// <summary>
/// Declares that the caller must hold a specific permission code. The attribute only names a policy;
/// <see cref="PermissionPolicyProvider"/> materialises that policy on first use, so no permission has
/// to be registered by hand at start-up.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    /// <summary>Prefix that marks a policy name as a permission requirement.</summary>
    public const string PolicyPrefix = "perm:";

    public HasPermissionAttribute(string permissionCode)
    {
        if (string.IsNullOrWhiteSpace(permissionCode))
            throw new ArgumentException("A permission code is required.", nameof(permissionCode));

        PermissionCode = permissionCode;
        Policy = PolicyPrefix + permissionCode;
    }

    public string PermissionCode { get; }
}

/// <summary>Requirement carrying the permission code that must be present on the principal.</summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permissionCode) => PermissionCode = permissionCode;

    public string PermissionCode { get; }

    public override string ToString() => $"Permission '{PermissionCode}'";
}

/// <summary>
/// Succeeds when the JWT carries a matching <c>permission</c> claim, or when the caller is an Admin
/// (an Admin implicitly holds every permission, mirroring <c>ICurrentUserService.HasPermission</c>).
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    /// <summary>Claim type issued by Infrastructure/Security/JwtTokenService.</summary>
    private const string PermissionClaimType = "permission";

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var user = context.User;

        if (user.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;

        if (user.IsInRole(RoleNames.Admin))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var granted = user.FindAll(PermissionClaimType)
            .Any(c => string.Equals(c.Value, requirement.PermissionCode, StringComparison.OrdinalIgnoreCase));

        if (granted) context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

/// <summary>
/// Turns any <c>perm:{code}</c> policy name into a policy on demand and delegates everything else to
/// the default provider, so <c>[Authorize(Roles = ...)]</c> and the fallback policy keep working.
/// </summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallbackProvider;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) =>
        _fallbackProvider = new DefaultAuthorizationPolicyProvider(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallbackProvider.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallbackProvider.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!string.IsNullOrEmpty(policyName) &&
            policyName.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var code = policyName[HasPermissionAttribute.PolicyPrefix.Length..];

            var policy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(code))
                .Build();

            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallbackProvider.GetPolicyAsync(policyName);
    }
}
