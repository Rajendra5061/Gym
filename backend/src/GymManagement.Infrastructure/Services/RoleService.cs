using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Services;

/// <summary>
/// Roles and their permission grants. System roles are protected: they may have their permission
/// set adjusted but they can never be renamed or deleted.
/// </summary>
public sealed class RoleService : IRoleService
{
    private readonly GymDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditService _audit;
    private readonly ILogger<RoleService> _logger;

    public RoleService(
        GymDbContext db,
        IDateTimeProvider clock,
        IAuditService audit,
        ILogger<RoleService> logger)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
        _logger = logger;
    }

    // ----------------------------------------------------------------- read

    public async Task<List<RoleDto>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Roles
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RoleDto
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                IsSystemRole = r.IsSystemRole,
                UserCount = r.UserRoles.Count,
                Permissions = r.RolePermissions
                    .Select(rp => rp.Permission!.Code)
                    .OrderBy(c => c)
                    .ToList()
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<RoleDto> GetByIdAsync(int id, CancellationToken ct = default) =>
        await _db.Roles
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new RoleDto
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                IsSystemRole = r.IsSystemRole,
                UserCount = r.UserRoles.Count,
                Permissions = r.RolePermissions
                    .Select(rp => rp.Permission!.Code)
                    .OrderBy(c => c)
                    .ToList()
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false)
        ?? throw new NotFoundAppException("Role", id);

    public async Task<List<PermissionGroupDto>> GetPermissionGroupsAsync(CancellationToken ct = default)
    {
        var permissions = await _db.Permissions
            .AsNoTracking()
            .OrderBy(p => p.Module)
            .ThenBy(p => p.Description)
            .Select(p => new PermissionDto
            {
                Id = p.Id,
                Code = p.Code,
                Module = p.Module,
                Description = p.Description
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return permissions
            .GroupBy(p => p.Module, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PermissionGroupDto
            {
                Module = g.Key,
                Permissions = g.ToList()
            })
            .ToList();
    }

    // --------------------------------------------------------------- create

    public async Task<RoleDto> CreateAsync(SaveRoleDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var name = (dto.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new ValidationAppException(nameof(dto.Name), "The role name is required.");

        var nameKey = name.ToLowerInvariant();
        if (await _db.Roles.AnyAsync(r => r.Name.ToLower() == nameKey, ct).ConfigureAwait(false))
            throw new ConflictAppException($"A role named '{name}' already exists.");

        var role = new Role
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            IsSystemRole = false,
            CreatedAt = _clock.UtcNow
        };

        _db.Roles.Add(role);

        var resolved = await ResolvePermissionsAsync(dto.Permissions, ct).ConfigureAwait(false);
        foreach (var permission in resolved.Matched)
            role.RolePermissions.Add(new RolePermission { PermissionId = permission.Id });

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.RoleChanged, nameof(Role), role.Id,
            newValues: new
            {
                role.Name,
                role.Description,
                Permissions = resolved.Matched.Select(p => p.Code).OrderBy(c => c).ToArray()
            },
            description: BuildDescription($"Created role '{role.Name}' with {resolved.Matched.Count} permission(s).",
                resolved.Unknown),
            ct: ct).ConfigureAwait(false);

        _logger.LogInformation("Role {RoleId} ('{RoleName}') created.", role.Id, role.Name);

        return await GetByIdAsync(role.Id, ct).ConfigureAwait(false);
    }

    // --------------------------------------------------------------- update

    public async Task<RoleDto> UpdateAsync(SaveRoleDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var role = await _db.Roles
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(r => r.Id == dto.Id, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundAppException("Role", dto.Id);

        var name = (dto.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new ValidationAppException(nameof(dto.Name), "The role name is required.");

        var renaming = !string.Equals(role.Name, name, StringComparison.Ordinal);

        if (renaming && role.IsSystemRole)
            throw new BusinessRuleAppException($"The built-in role '{role.Name}' cannot be renamed.");

        if (renaming)
        {
            var nameKey = name.ToLowerInvariant();
            if (await _db.Roles.AnyAsync(r => r.Id != role.Id && r.Name.ToLower() == nameKey, ct)
                    .ConfigureAwait(false))
            {
                throw new ConflictAppException($"A role named '{name}' already exists.");
            }
        }

        var oldPermissions = role.RolePermissions
            .Select(rp => rp.Permission?.Code)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .OrderBy(c => c)
            .ToArray();
        var oldName = role.Name;
        var oldDescription = role.Description;

        role.Name = name;
        role.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();

        var resolved = await ReplacePermissionsAsync(role, dto.Permissions, ct).ConfigureAwait(false);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.RoleChanged, nameof(Role), role.Id,
            oldValues: new { Name = oldName, Description = oldDescription, Permissions = oldPermissions },
            newValues: new
            {
                role.Name,
                role.Description,
                Permissions = resolved.Matched.Select(p => p.Code).OrderBy(c => c).ToArray()
            },
            description: BuildDescription($"Updated role '{role.Name}'.", resolved.Unknown),
            ct: ct).ConfigureAwait(false);

        return await GetByIdAsync(role.Id, ct).ConfigureAwait(false);
    }

    // --------------------------------------------------------------- delete

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var role = await _db.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundAppException("Role", id);

        if (role.IsSystemRole)
            throw new BusinessRuleAppException($"The built-in role '{role.Name}' cannot be deleted.");

        var userCount = await _db.UserRoles.CountAsync(ur => ur.RoleId == role.Id, ct).ConfigureAwait(false);
        if (userCount > 0)
            throw new BusinessRuleAppException(
                $"The role '{role.Name}' is still assigned to {userCount} user(s). Reassign them first.");

        _db.RolePermissions.RemoveRange(role.RolePermissions);
        _db.Roles.Remove(role);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Delete, nameof(Role), id,
            oldValues: new { role.Name, role.Description },
            description: $"Deleted role '{role.Name}'.", ct: ct).ConfigureAwait(false);
    }

    // ----------------------------------------------------- role permissions

    public async Task SetRolePermissionsAsync(int roleId, IEnumerable<string> permissionCodes,
        CancellationToken ct = default)
    {
        var role = await _db.Roles
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(r => r.Id == roleId, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundAppException("Role", roleId);

        var oldPermissions = role.RolePermissions
            .Select(rp => rp.Permission?.Code)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .OrderBy(c => c)
            .ToArray();

        var resolved = await ReplacePermissionsAsync(role, permissionCodes, ct).ConfigureAwait(false);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.RoleChanged, nameof(Role), role.Id,
            oldValues: new { Permissions = oldPermissions },
            newValues: new { Permissions = resolved.Matched.Select(p => p.Code).OrderBy(c => c).ToArray() },
            description: BuildDescription(
                $"Permission set for role '{role.Name}' replaced with {resolved.Matched.Count} permission(s).",
                resolved.Unknown),
            ct: ct).ConfigureAwait(false);
    }

    // -------------------------------------------------------------- helpers

    /// <summary>Swaps the role's grants for the supplied codes. Caller saves.</summary>
    private async Task<(List<Permission> Matched, List<string> Unknown)> ReplacePermissionsAsync(
        Role role, IEnumerable<string>? codes, CancellationToken ct)
    {
        var resolved = await ResolvePermissionsAsync(codes, ct).ConfigureAwait(false);
        var keepIds = resolved.Matched.Select(p => p.Id).ToHashSet();

        var removals = role.RolePermissions.Where(rp => !keepIds.Contains(rp.PermissionId)).ToList();
        foreach (var removal in removals)
        {
            role.RolePermissions.Remove(removal);
            _db.RolePermissions.Remove(removal);
        }

        var existingIds = role.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();
        foreach (var permission in resolved.Matched.Where(p => !existingIds.Contains(p.Id)))
        {
            role.RolePermissions.Add(new RolePermission
            {
                RoleId = role.Id,
                PermissionId = permission.Id
            });
        }

        return resolved;
    }

    /// <summary>Unknown codes are ignored rather than rejected, but they are reported to the audit trail.</summary>
    private async Task<(List<Permission> Matched, List<string> Unknown)> ResolvePermissionsAsync(
        IEnumerable<string>? codes, CancellationToken ct)
    {
        var requested = (codes ?? Enumerable.Empty<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requested.Count == 0)
            return (new List<Permission>(), new List<string>());

        var matched = await _db.Permissions
            .AsNoTracking()
            .Where(p => requested.Contains(p.Code))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var unknown = requested
            .Except(matched.Select(p => p.Code), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unknown.Count > 0)
            _logger.LogWarning("Ignored {Count} unknown permission code(s): {Codes}.",
                unknown.Count, string.Join(", ", unknown));

        return (matched, unknown);
    }

    private static string BuildDescription(string summary, List<string> unknown) =>
        unknown.Count == 0
            ? summary
            : $"{summary} Ignored unknown permission code(s): {string.Join(", ", unknown)}.";
}
