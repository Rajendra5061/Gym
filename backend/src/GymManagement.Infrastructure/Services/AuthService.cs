using System.Security.Cryptography;
using System.Text;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Services;

/// <summary>
/// Authentication, refresh-token rotation and password lifecycle. Deliberately returns the same
/// generic message for "unknown user" and "wrong password" so that accounts cannot be enumerated.
/// </summary>
public sealed class AuthService : IAuthService
{
    private const string InvalidCredentials = "Invalid credentials.";
    private const int ResetTokenLifetimeMinutes = 30;
    private const int DefaultMaxFailedLoginAttempts = 5;
    private const int DefaultLockoutMinutes = 15;

    private readonly GymDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;
    private readonly ISettingsService _settings;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        GymDbContext db,
        IPasswordHasher hasher,
        IJwtTokenService jwt,
        IDateTimeProvider clock,
        ICurrentUserService currentUser,
        IAuditService audit,
        ISettingsService settings,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _db = db;
        _hasher = hasher;
        _jwt = jwt;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
        _settings = settings;
        _configuration = configuration;
        _logger = logger;
    }

    // ---------------------------------------------------------------- login

    public async Task<LoginResponseDto> LoginAsync(LoginRequestDto request, string? ipAddress,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var identifier = (request.UserNameOrEmail ?? string.Empty).Trim();
        var device = request.DeviceInfo ?? _currentUser.DeviceInfo;
        var ip = ipAddress ?? _currentUser.IpAddress;
        var now = _clock.UtcNow;

        if (identifier.Length == 0 || string.IsNullOrEmpty(request.Password))
        {
            await RecordAttemptAsync(null, identifier, LoginResult.UserNotFound,
                "User name or password was not supplied.", ip, device, ct).ConfigureAwait(false);
            throw new UnauthorizedAppException(InvalidCredentials);
        }

        var key = identifier.ToLowerInvariant();

        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .ThenInclude(r => r!.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.UserName.ToLower() == key || u.Email.ToLower() == key, ct)
            .ConfigureAwait(false);

        if (user is null)
        {
            await RecordAttemptAsync(null, identifier, LoginResult.UserNotFound,
                "No account matches the supplied user name or email.", ip, device, ct).ConfigureAwait(false);
            _logger.LogInformation("Login failed: unknown account '{Identifier}'.", identifier);
            throw new UnauthorizedAppException(InvalidCredentials);
        }

        if (user.LockoutEndUtc.HasValue && user.LockoutEndUtc.Value > now)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((user.LockoutEndUtc.Value - now).TotalMinutes));
            await RecordAttemptAsync(user.Id, identifier, LoginResult.AccountLocked,
                $"Account locked for a further {minutes} minute(s).", ip, device, ct).ConfigureAwait(false);
            throw new UnauthorizedAppException(
                $"This account is locked. Try again in {minutes} minute(s).");
        }

        if (user.Status != UserStatus.Active)
        {
            await RecordAttemptAsync(user.Id, identifier, LoginResult.AccountInactive,
                $"Account status is {user.Status}.", ip, device, ct).ConfigureAwait(false);
            throw new UnauthorizedAppException("This account is not active.");
        }

        if (!_hasher.Verify(request.Password, user.PasswordHash))
        {
            var (maxAttempts, lockoutMinutes) = await GetLockoutPolicyAsync(ct).ConfigureAwait(false);

            user.FailedLoginAttempts++;
            var reason = $"Incorrect password ({user.FailedLoginAttempts} of {maxAttempts} attempts).";

            if (user.FailedLoginAttempts >= maxAttempts)
            {
                user.LockoutEndUtc = now.AddMinutes(lockoutMinutes);
                user.FailedLoginAttempts = 0;
                reason = $"Incorrect password. Account locked for {lockoutMinutes} minute(s).";
            }

            await RecordAttemptAsync(user.Id, identifier, LoginResult.InvalidPassword,
                reason, ip, device, ct).ConfigureAwait(false);

            throw new UnauthorizedAppException(InvalidCredentials);
        }

        // Success.
        user.FailedLoginAttempts = 0;
        user.LockoutEndUtc = null;
        user.LastLoginAtUtc = now;

        var roles = ExtractRoles(user);
        var permissions = ExtractPermissions(user);

        var access = _jwt.CreateAccessToken(user.Id, user.UserName, user.FullName,
            user.MemberId, user.TrainerId, roles, permissions);
        var refresh = _jwt.CreateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refresh.TokenHash,
            ExpiresAtUtc = refresh.ExpiresUtc,
            CreatedAtUtc = now,
            CreatedByIp = ip
        });

        _db.LoginAttempts.Add(BuildAttempt(user.Id, identifier, LoginResult.Success, null, ip, device, now));

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogForUserAsync(user.Id, user.UserName, AuditActions.Login, nameof(User), user.Id,
            description: "Signed in successfully.", ipAddress: ip, deviceInfo: device, ct: ct)
            .ConfigureAwait(false);

        return new LoginResponseDto
        {
            AccessToken = access.Token,
            AccessTokenExpiresUtc = access.ExpiresUtc,
            RefreshToken = refresh.Token,
            RefreshTokenExpiresUtc = refresh.ExpiresUtc,
            MustChangePassword = user.MustChangePassword,
            User = MapCurrentUser(user, roles, permissions)
        };
    }

    // -------------------------------------------------------------- refresh

    public async Task<LoginResponseDto> RefreshTokenAsync(RefreshTokenRequestDto request, string? ipAddress,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            throw new UnauthorizedAppException("A refresh token is required.");

        var now = _clock.UtcNow;
        var ip = ipAddress ?? _currentUser.IpAddress;
        var incomingHash = _jwt.HashToken(request.RefreshToken.Trim());

        var existing = await _db.RefreshTokens
            .Include(t => t.User).ThenInclude(u => u!.UserRoles).ThenInclude(ur => ur.Role)
                .ThenInclude(r => r!.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(t => t.TokenHash == incomingHash, ct)
            .ConfigureAwait(false);

        if (existing is null)
            throw new UnauthorizedAppException("The refresh token is invalid.");

        // Reuse of an already-revoked token means the token family is compromised: kill all of them.
        if (existing.RevokedAtUtc is not null)
        {
            var revoked = await RevokeAllActiveTokensAsync(existing.UserId,
                "Revoked after refresh token reuse was detected.", now, ct).ConfigureAwait(false);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            _logger.LogWarning(
                "Refresh token reuse detected for user {UserId}; {Count} active token(s) revoked.",
                existing.UserId, revoked);

            throw new UnauthorizedAppException("The refresh token is no longer valid.");
        }

        if (existing.ExpiresAtUtc <= now)
            throw new UnauthorizedAppException("The refresh token has expired.");

        var user = existing.User;
        if (user is null)
            throw new UnauthorizedAppException("The account for this token no longer exists.");

        if (user.Status != UserStatus.Active)
            throw new UnauthorizedAppException("This account is not active.");

        var roles = ExtractRoles(user);
        var permissions = ExtractPermissions(user);

        var access = _jwt.CreateAccessToken(user.Id, user.UserName, user.FullName,
            user.MemberId, user.TrainerId, roles, permissions);
        var refresh = _jwt.CreateRefreshToken();

        // Rotate: the presented token dies and points at its replacement.
        existing.RevokedAtUtc = now;
        existing.RevokedReason = "Rotated";
        existing.ReplacedByTokenHash = refresh.TokenHash;

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refresh.TokenHash,
            ExpiresAtUtc = refresh.ExpiresUtc,
            CreatedAtUtc = now,
            CreatedByIp = ip
        });

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new LoginResponseDto
        {
            AccessToken = access.Token,
            AccessTokenExpiresUtc = access.ExpiresUtc,
            RefreshToken = refresh.Token,
            RefreshTokenExpiresUtc = refresh.ExpiresUtc,
            MustChangePassword = user.MustChangePassword,
            User = MapCurrentUser(user, roles, permissions)
        };
    }

    // --------------------------------------------------------------- logout

    public async Task LogoutAsync(LogoutRequestDto request, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var token = request?.RefreshToken;
        int? userId = _currentUser.UserId;

        if (!string.IsNullOrWhiteSpace(token))
        {
            var hash = _jwt.HashToken(token.Trim());
            var row = await _db.RefreshTokens
                .FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
                .ConfigureAwait(false);

            if (row is not null && row.RevokedAtUtc is null)
            {
                row.RevokedAtUtc = now;
                row.RevokedReason = "Logout";
                userId ??= row.UserId;
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }
        else if (userId.HasValue)
        {
            await RevokeAllActiveTokensAsync(userId.Value, "Logout", now, ct).ConfigureAwait(false);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        else
        {
            throw new UnauthorizedAppException("Authentication is required.");
        }

        await _audit.LogForUserAsync(userId, _currentUser.UserName, AuditActions.Logout, nameof(User),
            userId, description: "Signed out.", ipAddress: _currentUser.IpAddress,
            deviceInfo: _currentUser.DeviceInfo, ct: ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------ forgot password

    public async Task<ForgotPasswordResponseDto> ForgotPasswordAsync(ForgotPasswordRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The response shape never changes, so a caller cannot probe which accounts exist.
        var response = new ForgotPasswordResponseDto
        {
            Message = "If the account exists a reset token has been issued."
        };

        var identifier = (request.UserNameOrEmail ?? string.Empty).Trim();
        if (identifier.Length == 0) return response;

        var key = identifier.ToLowerInvariant();
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.UserName.ToLower() == key || u.Email.ToLower() == key, ct)
            .ConfigureAwait(false);

        if (user is null)
        {
            _logger.LogInformation("Password reset requested for an unknown account.");
            return response;
        }

        // Whether the token may travel back in the response is a separate question from whether it
        // was issued. Staff resetting on a member's behalf need to read it out; an anonymous caller
        // must not, because a response that differs for a real account is exactly how an attacker
        // enumerates valid user names. Without this the endpoint contradicted its own comment above.
        var callerMayReadToken = _currentUser.IsAuthenticated
                                 && _currentUser.HasPermission(Permissions.UsersManage);
        var discloseToAnonymous =
            _configuration.GetValue("Auth:ReturnResetTokenToAnonymousCallers", false);

        var rawToken = CreateSecureToken();
        var expires = _clock.UtcNow.AddMinutes(ResetTokenLifetimeMinutes);

        user.PasswordResetTokenHash = Sha256(rawToken);
        user.PasswordResetTokenExpiresUtc = expires;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (!callerMayReadToken && !discloseToAnonymous)
        {
            // Identical to the unknown-account reply, so the two are indistinguishable.
            _logger.LogInformation(
                "Password reset token issued but withheld from an anonymous caller.");
            return response;
        }

        response.ResetToken = rawToken;
        response.ExpiresUtc = expires;
        response.Message =
            "No email provider is configured in this build, so the reset token is returned directly " +
            $"for the administrator to hand over to the user. It expires in {ResetTokenLifetimeMinutes} minutes.";

        return response;
    }

    // ------------------------------------------------------- reset password

    public async Task ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ResetToken))
            throw new ValidationAppException(nameof(request.ResetToken), "The reset token is required.");

        ValidatePasswordPair(request.NewPassword, request.ConfirmPassword);

        var identifier = (request.UserNameOrEmail ?? string.Empty).Trim();
        if (identifier.Length == 0)
            throw new ValidationAppException(nameof(request.UserNameOrEmail),
                "The user name or email is required.");

        var key = identifier.ToLowerInvariant();
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.UserName.ToLower() == key || u.Email.ToLower() == key, ct)
            .ConfigureAwait(false);

        if (user is null)
            throw new UnauthorizedAppException("The reset token is invalid or has expired.");

        var now = _clock.UtcNow;
        var suppliedHash = Sha256(request.ResetToken.Trim());

        if (string.IsNullOrEmpty(user.PasswordResetTokenHash)
            || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(user.PasswordResetTokenHash),
                    Encoding.UTF8.GetBytes(suppliedHash))
            || user.PasswordResetTokenExpiresUtc is null
            || user.PasswordResetTokenExpiresUtc.Value <= now)
        {
            throw new UnauthorizedAppException("The reset token is invalid or has expired.");
        }

        user.PasswordHash = _hasher.Hash(request.NewPassword);
        user.PasswordResetTokenHash = null;
        user.PasswordResetTokenExpiresUtc = null;
        user.FailedLoginAttempts = 0;
        user.LockoutEndUtc = null;
        user.MustChangePassword = false;
        if (user.Status == UserStatus.Locked) user.Status = UserStatus.Active;

        await RevokeAllActiveTokensAsync(user.Id, "Password reset", now, ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogForUserAsync(user.Id, user.UserName, AuditActions.PasswordReset, nameof(User),
            user.Id, description: "Password reset using a reset token.",
            ipAddress: _currentUser.IpAddress, deviceInfo: _currentUser.DeviceInfo, ct: ct)
            .ConfigureAwait(false);
    }

    // ------------------------------------------------------ change password

    public async Task ChangePasswordAsync(int userId, ChangePasswordRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false)
            ?? throw new NotFoundAppException("User", userId);

        if (string.IsNullOrEmpty(request.CurrentPassword)
            || !_hasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw new ValidationAppException(nameof(request.CurrentPassword),
                "The current password is incorrect.");
        }

        ValidatePasswordPair(request.NewPassword, request.ConfirmPassword);

        if (_hasher.Verify(request.NewPassword, user.PasswordHash))
            throw new ValidationAppException("NewPassword",
                "The new password must be different from the current password.");

        var now = _clock.UtcNow;

        user.PasswordHash = _hasher.Hash(request.NewPassword);
        user.MustChangePassword = false;
        user.PasswordResetTokenHash = null;
        user.PasswordResetTokenExpiresUtc = null;

        await RevokeAllActiveTokensAsync(user.Id, "Password changed", now, ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogForUserAsync(user.Id, user.UserName, AuditActions.PasswordChanged, nameof(User),
            user.Id, description: "Password changed by the account owner.",
            ipAddress: _currentUser.IpAddress, deviceInfo: _currentUser.DeviceInfo, ct: ct)
            .ConfigureAwait(false);
    }

    // ---------------------------------------------------------- current user

    public async Task<CurrentUserDto> GetCurrentUserAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .ThenInclude(r => r!.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundAppException("User", userId);

        return MapCurrentUser(user, ExtractRoles(user), ExtractPermissions(user));
    }

    // -------------------------------------------------------------- helpers

    private async Task<(int MaxFailedLoginAttempts, int LockoutMinutes)> GetLockoutPolicyAsync(
        CancellationToken ct)
    {
        try
        {
            var settings = await _settings.GetGymSettingsAsync(ct).ConfigureAwait(false);
            var max = settings.MaxFailedLoginAttempts > 0
                ? settings.MaxFailedLoginAttempts
                : DefaultMaxFailedLoginAttempts;
            var minutes = settings.LockoutMinutes > 0
                ? settings.LockoutMinutes
                : DefaultLockoutMinutes;
            return (max, minutes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read the lockout policy from settings; using defaults.");
            return (DefaultMaxFailedLoginAttempts, DefaultLockoutMinutes);
        }
    }

    /// <summary>Revokes every still-active refresh token of a user. Caller saves.</summary>
    private async Task<int> RevokeAllActiveTokensAsync(int userId, string reason, DateTime now,
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

        return active.Count;
    }

    private async Task RecordAttemptAsync(int? userId, string identifier, LoginResult result,
        string? failureReason, string? ip, string? device, CancellationToken ct)
    {
        _db.LoginAttempts.Add(BuildAttempt(userId, identifier, result, failureReason, ip, device,
            _clock.UtcNow));

        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An audit write must never mask the authentication outcome.
            _logger.LogError(ex, "Failed to persist the login attempt for '{Identifier}'.", identifier);
        }
    }

    private static LoginAttempt BuildAttempt(int? userId, string identifier, LoginResult result,
        string? failureReason, string? ip, string? device, DateTime now) => new()
        {
            UserId = userId,
            UserNameOrEmail = Truncate(identifier, 160) ?? string.Empty,
            Result = result,
            AttemptedAtUtc = now,
            IpAddress = Truncate(ip, 64),
            DeviceInfo = Truncate(device, 256),
            FailureReason = Truncate(failureReason, 256)
        };

    private static List<string> ExtractRoles(User user) => user.UserRoles
        .Select(ur => ur.Role?.Name)
        .Where(n => !string.IsNullOrWhiteSpace(n))
        .Select(n => n!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static List<string> ExtractPermissions(User user) => user.UserRoles
        .Where(ur => ur.Role is not null)
        .SelectMany(ur => ur.Role!.RolePermissions)
        .Select(rp => rp.Permission?.Code)
        .Where(c => !string.IsNullOrWhiteSpace(c))
        .Select(c => c!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static CurrentUserDto MapCurrentUser(User user, List<string> roles, List<string> permissions) => new()
    {
        Id = user.Id,
        UserName = user.UserName,
        FullName = user.FullName,
        Email = user.Email,
        Phone = user.Phone,
        ProfilePhotoPath = user.ProfilePhotoPath,
        MemberId = user.MemberId,
        TrainerId = user.TrainerId,
        Roles = roles,
        Permissions = permissions
    };

    /// <summary>Applies the shared new-password policy. Field names match the request DTO.</summary>
    private static void ValidatePasswordPair(string? newPassword, string? confirmPassword)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(newPassword))
        {
            errors["NewPassword"] = new[] { "The new password is required." };
        }
        else
        {
            var problems = new List<string>();
            if (newPassword.Length < 8) problems.Add("The new password must be at least 8 characters long.");
            if (!newPassword.Any(char.IsLetter)) problems.Add("The new password must contain at least one letter.");
            if (!newPassword.Any(char.IsDigit)) problems.Add("The new password must contain at least one digit.");
            if (problems.Count > 0) errors["NewPassword"] = problems.ToArray();
        }

        if (string.IsNullOrEmpty(confirmPassword))
            errors["ConfirmPassword"] = new[] { "Please confirm the new password." };
        else if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
            errors["ConfirmPassword"] = new[] { "The confirmation does not match the new password." };

        if (errors.Count > 0) throw new ValidationAppException(errors);
    }

    /// <summary>256 bits of cryptographic randomness rendered as base64url.</summary>
    private static string CreateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string Sha256(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash);
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
