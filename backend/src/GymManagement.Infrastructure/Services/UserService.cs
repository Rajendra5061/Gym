using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Services;

/// <summary>
/// Administration of login accounts. Password hashes are never returned by any member of this
/// service; generated temporary passwords are handed back once, in clear, to the administrator.
/// </summary>
public sealed class UserService : IUserService
{
    private readonly GymDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;
    private readonly ILogger<UserService> _logger;

    public UserService(
        GymDbContext db,
        IPasswordHasher hasher,
        IDateTimeProvider clock,
        ICurrentUserService currentUser,
        IAuditService audit,
        ILogger<UserService> logger)
    {
        _db = db;
        _hasher = hasher;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
        _logger = logger;
    }

    // ----------------------------------------------------------------- read

    public async Task<PagedResult<UserListDto>> GetPagedAsync(UserQueryDto query,
        CancellationToken ct = default)
    {
        query ??= new UserQueryDto();
        var now = _clock.UtcNow;

        IQueryable<User> source = _db.Users.AsNoTracking();
        if (query.IncludeDeleted) source = source.IgnoreQueryFilters();

        if (query.Status.HasValue)
            source = source.Where(u => u.Status == query.Status.Value);

        if (query.RoleId.HasValue)
            source = source.Where(u => u.UserRoles.Any(ur => ur.RoleId == query.RoleId.Value));

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(u =>
                EF.Functions.Like(u.UserName, $"%{term}%") ||
                EF.Functions.Like(u.FullName, $"%{term}%") ||
                EF.Functions.Like(u.Email, $"%{term}%") ||
                (u.Phone != null && EF.Functions.Like(u.Phone, $"%{term}%")));
        }

        var total = await source.CountAsync(ct).ConfigureAwait(false);
        if (total == 0)
            return new PagedResult<UserListDto>(Array.Empty<UserListDto>(), 0, query.PageNumber, query.PageSize);

        source = ApplySort(source, query.SortBy, query.SortDescending);

        var rows = await source
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(u => new
            {
                u.Id,
                u.UserName,
                u.FullName,
                u.Email,
                u.Phone,
                u.Status,
                u.LastLoginAtUtc,
                u.CreatedAt,
                u.LockoutEndUtc,
                RoleList = u.UserRoles.Select(ur => ur.Role!.Name).ToList()
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var items = rows.Select(r => new UserListDto
        {
            Id = r.Id,
            UserName = r.UserName,
            FullName = r.FullName,
            Email = r.Email,
            Phone = r.Phone,
            Status = r.Status,
            LastLoginAtUtc = r.LastLoginAtUtc,
            CreatedAt = r.CreatedAt,
            IsLockedOut = r.LockoutEndUtc.HasValue && r.LockoutEndUtc.Value > now,
            Roles = string.Join(", ", r.RoleList.Where(n => !string.IsNullOrWhiteSpace(n))
                                               .Distinct(StringComparer.OrdinalIgnoreCase)
                                               .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        }).ToList();

        return new PagedResult<UserListDto>(items, total, query.PageNumber, query.PageSize);
    }

    public async Task<UserDetailDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == id, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundAppException("User", id);

        return MapDetail(user, now);
    }

    public async Task<List<LookupDto>> GetLookupAsync(CancellationToken ct = default) =>
        await _db.Users
            .AsNoTracking()
            .OrderBy(u => u.FullName)
            .Select(u => new LookupDto
            {
                Id = u.Id,
                Name = u.FullName,
                Code = u.UserName,
                Extra = u.Email,
                IsActive = u.Status == UserStatus.Active
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

    // --------------------------------------------------------------- create

    public async Task<TemporaryPasswordDto> CreateAsync(CreateUserDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userName = (dto.UserName ?? string.Empty).Trim();
        var email = (dto.Email ?? string.Empty).Trim();
        var fullName = (dto.FullName ?? string.Empty).Trim();

        var errors = new Dictionary<string, string[]>();
        if (userName.Length == 0) errors[nameof(dto.UserName)] = new[] { "The user name is required." };
        if (email.Length == 0) errors[nameof(dto.Email)] = new[] { "The email is required." };
        if (fullName.Length == 0) errors[nameof(dto.FullName)] = new[] { "The full name is required." };
        if (errors.Count > 0) throw new ValidationAppException(errors);

        var userNameKey = userName.ToLowerInvariant();
        var emailKey = email.ToLowerInvariant();

        if (await _db.Users.AnyAsync(u => u.UserName.ToLower() == userNameKey, ct).ConfigureAwait(false))
            throw new ConflictAppException($"The user name '{userName}' is already in use.");

        if (await _db.Users.AnyAsync(u => u.Email.ToLower() == emailKey, ct).ConfigureAwait(false))
            throw new ConflictAppException($"The email '{email}' is already in use.");

        var roleIds = (dto.RoleIds ?? new List<int>()).Distinct().ToList();
        var roles = await LoadRolesAsync(roleIds, ct).ConfigureAwait(false);

        var generated = string.IsNullOrWhiteSpace(dto.Password);
        var password = generated ? _hasher.GenerateTemporaryPassword() : dto.Password!;

        var now = _clock.UtcNow;
        var user = new User
        {
            UserName = userName,
            Email = email,
            FullName = fullName,
            Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim(),
            PasswordHash = _hasher.Hash(password),
            Status = dto.Status,
            MustChangePassword = generated || dto.MustChangePassword,
            MemberId = dto.MemberId,
            TrainerId = dto.TrainerId,
            CreatedAt = now
        };

        foreach (var role in roles)
        {
            user.UserRoles.Add(new UserRole
            {
                RoleId = role.Id,
                AssignedAt = now,
                AssignedBy = _currentUser.UserId
            });
        }

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Create, nameof(User), user.Id,
            newValues: new
            {
                user.UserName,
                user.FullName,
                user.Email,
                user.Phone,
                Status = user.Status.ToString(),
                Roles = roles.Select(r => r.Name).ToArray()
            },
            description: $"Created user account '{user.UserName}'.", ct: ct).ConfigureAwait(false);

        _logger.LogInformation("User account {UserId} ('{UserName}') created.", user.Id, user.UserName);

        return new TemporaryPasswordDto
        {
            UserId = user.Id,
            UserName = user.UserName,
            TemporaryPassword = generated ? password : string.Empty
        };
    }

    // --------------------------------------------------------------- update

    public async Task<UserDetailDto> UpdateAsync(UpdateUserDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == dto.Id, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundAppException("User", dto.Id);

        var fullName = (dto.FullName ?? string.Empty).Trim();
        var email = (dto.Email ?? string.Empty).Trim();

        var errors = new Dictionary<string, string[]>();
        if (fullName.Length == 0) errors[nameof(dto.FullName)] = new[] { "The full name is required." };
        if (email.Length == 0) errors[nameof(dto.Email)] = new[] { "The email is required." };
        if (errors.Count > 0) throw new ValidationAppException(errors);

        var emailKey = email.ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Id != user.Id && u.Email.ToLower() == emailKey, ct)
                .ConfigureAwait(false))
        {
            throw new ConflictAppException($"The email '{email}' is already in use.");
        }

        var before = new
        {
            user.FullName,
            user.Email,
            user.Phone,
            Status = user.Status.ToString(),
            Roles = user.UserRoles.Select(ur => ur.Role?.Name).ToArray()
        };

        var previousStatus = user.Status;

        user.FullName = fullName;
        user.Email = email;
        user.Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim();
        user.Status = dto.Status;

        var now = _clock.UtcNow;
        var requestedRoleIds = (dto.RoleIds ?? new List<int>()).Distinct().ToList();
        var currentRoleIds = user.UserRoles.Select(ur => ur.RoleId).Distinct().ToList();
        var rolesChanged = requestedRoleIds.Count != currentRoleIds.Count
                           || requestedRoleIds.Except(currentRoleIds).Any()
                           || currentRoleIds.Except(requestedRoleIds).Any();

        List<Role> newRoles = new();
        if (rolesChanged)
        {
            newRoles = await LoadRolesAsync(requestedRoleIds, ct).ConfigureAwait(false);

            var removals = user.UserRoles.Where(ur => !requestedRoleIds.Contains(ur.RoleId)).ToList();
            foreach (var removal in removals)
            {
                user.UserRoles.Remove(removal);
                _db.UserRoles.Remove(removal);
            }

            foreach (var role in newRoles.Where(r => !currentRoleIds.Contains(r.Id)))
            {
                user.UserRoles.Add(new UserRole
                {
                    UserId = user.Id,
                    RoleId = role.Id,
                    AssignedAt = now,
                    AssignedBy = _currentUser.UserId
                });
            }
        }

        // Losing Active status must not leave live sessions behind.
        if (previousStatus == UserStatus.Active && user.Status != UserStatus.Active)
            await RevokeAllActiveTokensAsync(user.Id, "Account status changed", now, ct).ConfigureAwait(false);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Update, nameof(User), user.Id,
            oldValues: before,
            newValues: new
            {
                user.FullName,
                user.Email,
                user.Phone,
                Status = user.Status.ToString()
            },
            description: $"Updated user account '{user.UserName}'.", ct: ct).ConfigureAwait(false);

        if (rolesChanged)
        {
            await _audit.LogAsync(AuditActions.RoleChanged, nameof(User), user.Id,
                oldValues: new { Roles = before.Roles },
                newValues: new { Roles = newRoles.Select(r => r.Name).ToArray() },
                description: $"Role assignments changed for '{user.UserName}'.", ct: ct)
                .ConfigureAwait(false);
        }

        return await GetByIdAsync(user.Id, ct).ConfigureAwait(false);
    }

    // -------------------------------------------------------- admin actions

    public async Task<TemporaryPasswordDto> ResetPasswordAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false)
            ?? throw new NotFoundAppException("User", userId);

        var now = _clock.UtcNow;
        var temporaryPassword = _hasher.GenerateTemporaryPassword();

        user.PasswordHash = _hasher.Hash(temporaryPassword);
        user.MustChangePassword = true;
        user.PasswordResetTokenHash = null;
        user.PasswordResetTokenExpiresUtc = null;
        user.FailedLoginAttempts = 0;
        user.LockoutEndUtc = null;
        if (user.Status == UserStatus.Locked) user.Status = UserStatus.Active;

        await RevokeAllActiveTokensAsync(user.Id, "Administrator password reset", now, ct)
            .ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.PasswordReset, nameof(User), user.Id,
            description: $"Administrator reset the password for '{user.UserName}'.", ct: ct)
            .ConfigureAwait(false);

        return new TemporaryPasswordDto
        {
            UserId = user.Id,
            UserName = user.UserName,
            TemporaryPassword = temporaryPassword
        };
    }

    public async Task SetStatusAsync(int userId, UserStatus status, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false)
            ?? throw new NotFoundAppException("User", userId);

        if (user.Status == status) return;

        if (status != UserStatus.Active)
            await EnsureNotLastActiveAdminAsync(user, "deactivate", ct).ConfigureAwait(false);

        var now = _clock.UtcNow;
        var previous = user.Status;
        user.Status = status;

        if (status == UserStatus.Active)
        {
            user.LockoutEndUtc = null;
            user.FailedLoginAttempts = 0;
        }
        else
        {
            await RevokeAllActiveTokensAsync(user.Id, $"Account set to {status}", now, ct)
                .ConfigureAwait(false);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var action = status == UserStatus.Active ? AuditActions.Reactivate : AuditActions.Deactivate;
        await _audit.LogAsync(action, nameof(User), user.Id,
            oldValues: new { Status = previous.ToString() },
            newValues: new { Status = status.ToString() },
            description: $"Status of '{user.UserName}' changed from {previous} to {status}.", ct: ct)
            .ConfigureAwait(false);
    }

    public async Task UnlockAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false)
            ?? throw new NotFoundAppException("User", userId);

        user.LockoutEndUtc = null;
        user.FailedLoginAttempts = 0;
        if (user.Status == UserStatus.Locked) user.Status = UserStatus.Active;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Update, nameof(User), user.Id,
            description: $"Unlocked user account '{user.UserName}'.", ct: ct).ConfigureAwait(false);
    }

    public async Task SoftDeleteAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundAppException("User", userId);

        await EnsureNotLastActiveAdminAsync(user, "delete", ct).ConfigureAwait(false);

        var now = _clock.UtcNow;

        user.IsDeleted = true;
        user.DeletedAt = now;
        user.DeletedBy = _currentUser.UserId;

        await RevokeAllActiveTokensAsync(user.Id, "Account deleted", now, ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.SoftDelete, nameof(User), user.Id,
            description: $"Soft deleted user account '{user.UserName}'.", ct: ct).ConfigureAwait(false);
    }

    // -------------------------------------------------------------- helpers

    private static IQueryable<User> ApplySort(IQueryable<User> source, string? sortBy, bool descending) =>
        (sortBy?.Trim().ToLowerInvariant()) switch
        {
            "username" => descending
                ? source.OrderByDescending(u => u.UserName)
                : source.OrderBy(u => u.UserName),
            "email" => descending
                ? source.OrderByDescending(u => u.Email)
                : source.OrderBy(u => u.Email),
            "status" => descending
                ? source.OrderByDescending(u => u.Status).ThenBy(u => u.FullName)
                : source.OrderBy(u => u.Status).ThenBy(u => u.FullName),
            "createdat" => descending
                ? source.OrderByDescending(u => u.CreatedAt)
                : source.OrderBy(u => u.CreatedAt),
            "lastloginatutc" or "lastlogin" => descending
                ? source.OrderByDescending(u => u.LastLoginAtUtc)
                : source.OrderBy(u => u.LastLoginAtUtc),
            _ => descending
                ? source.OrderByDescending(u => u.FullName)
                : source.OrderBy(u => u.FullName)
        };

    private async Task<List<Role>> LoadRolesAsync(List<int> roleIds, CancellationToken ct)
    {
        if (roleIds.Count == 0) return new List<Role>();

        var roles = await _db.Roles
            .AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (roles.Count != roleIds.Count)
        {
            var missing = roleIds.Except(roles.Select(r => r.Id)).ToArray();
            throw new ValidationAppException("RoleIds",
                $"One or more selected roles do not exist: {string.Join(", ", missing)}.");
        }

        return roles;
    }

    /// <summary>Blocks an operation that would leave the installation without a usable administrator.</summary>
    private async Task EnsureNotLastActiveAdminAsync(User user, string verb, CancellationToken ct)
    {
        var isAdmin = user.UserRoles.Any(ur => ur.Role != null
                                               && ur.Role.Name == RoleNames.Admin);

        if (!isAdmin)
        {
            isAdmin = await _db.UserRoles
                .AsNoTracking()
                .AnyAsync(ur => ur.UserId == user.Id && ur.Role!.Name == RoleNames.Admin, ct)
                .ConfigureAwait(false);
        }

        if (!isAdmin) return;

        var otherActiveAdmins = await _db.Users
            .AsNoTracking()
            .CountAsync(u => u.Id != user.Id
                             && u.Status == UserStatus.Active
                             && u.UserRoles.Any(ur => ur.Role!.Name == RoleNames.Admin), ct)
            .ConfigureAwait(false);

        if (otherActiveAdmins == 0)
            throw new BusinessRuleAppException(
                $"You cannot {verb} the last remaining active {RoleNames.Admin} account.");
    }

    /// <summary>Revokes every still-active refresh token of a user. Caller saves.</summary>
    private async Task RevokeAllActiveTokensAsync(int userId, string reason, DateTime now,
        CancellationToken ct)
    {
        var active = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null && t.ExpiresAtUtc > now)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var token in active)
        {
            token.RevokedAtUtc = now;
            token.RevokedReason = reason;
        }
    }

    private static UserDetailDto MapDetail(User user, DateTime now) => new()
    {
        Id = user.Id,
        UserName = user.UserName,
        FullName = user.FullName,
        Email = user.Email,
        Phone = user.Phone,
        Status = user.Status,
        LastLoginAtUtc = user.LastLoginAtUtc,
        CreatedAt = user.CreatedAt,
        IsLockedOut = user.LockoutEndUtc.HasValue && user.LockoutEndUtc.Value > now,
        MemberId = user.MemberId,
        TrainerId = user.TrainerId,
        MustChangePassword = user.MustChangePassword,
        FailedLoginAttempts = user.FailedLoginAttempts,
        LockoutEndUtc = user.LockoutEndUtc,
        ProfilePhotoPath = user.ProfilePhotoPath,
        RoleIds = user.UserRoles.Select(ur => ur.RoleId).Distinct().ToList(),
        Roles = string.Join(", ", user.UserRoles
            .Select(ur => ur.Role?.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
    };
}
