using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GymManagement.Application.Common;
using GymManagement.Application.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GymManagement.Infrastructure.Security;

/// <summary>
/// Issues signed JWT access tokens and opaque refresh tokens. Only the SHA-256 hash of a refresh
/// token is ever persisted, so a database leak cannot be replayed against the API.
/// </summary>
public sealed class JwtTokenService : IJwtTokenService
{
    /// <summary>Claim type carrying a single permission code. Repeated once per permission.</summary>
    public const string PermissionClaimType = "permission";
    public const string MemberIdClaimType = "memberId";
    public const string TrainerIdClaimType = "trainerId";

    private const int RefreshTokenBytes = 64;

    private readonly JwtOptions _options;
    private readonly IDateTimeProvider _clock;
    private readonly SigningCredentials _signingCredentials;

    public JwtTokenService(IOptions<JwtOptions> options, IDateTimeProvider clock)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        // Fail fast: a missing or weak secret must stop the application, not produce weak tokens.
        _options.Validate();

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public (string Token, DateTime ExpiresUtc) CreateAccessToken(int userId, string userName, string fullName,
        int? memberId, int? trainerId, IEnumerable<string> roles, IEnumerable<string> permissions)
    {
        var issuedAt = _clock.UtcNow;
        var expiresUtc = issuedAt.AddMinutes(_options.AccessTokenMinutes);

        var userIdText = userId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userIdText),
            new(ClaimTypes.NameIdentifier, userIdText),
            new(JwtRegisteredClaimNames.UniqueName, userName ?? string.Empty),
            new(JwtRegisteredClaimNames.GivenName, fullName ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Iat,
                EpochTime.GetIntDate(issuedAt).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64)
        };

        if (memberId is > 0)
            claims.Add(new Claim(MemberIdClaimType,
                memberId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        if (trainerId is > 0)
            claims.Add(new Claim(TrainerIdClaimType,
                trainerId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        foreach (var role in Distinct(roles))
            claims.Add(new Claim(ClaimTypes.Role, role));

        foreach (var permission in Distinct(permissions))
            claims.Add(new Claim(PermissionClaimType, permission));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: issuedAt,
            expires: expiresUtc,
            signingCredentials: _signingCredentials);

        var encoded = new JwtSecurityTokenHandler().WriteToken(token);
        return (encoded, expiresUtc);
    }

    public (string Token, string TokenHash, DateTime ExpiresUtc) CreateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(RefreshTokenBytes);
        var token = Base64UrlEncoder.Encode(bytes);
        var expiresUtc = _clock.UtcNow.AddDays(_options.RefreshTokenDays);

        return (token, HashToken(token), expiresUtc);
    }

    /// <summary>SHA-256 (base64) of the raw token so the database never holds a usable token.</summary>
    public string HashToken(string token)
    {
        if (string.IsNullOrEmpty(token))
            throw new ArgumentException("Token cannot be empty.", nameof(token));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hash);
    }

    private static IEnumerable<string> Distinct(IEnumerable<string>? values) =>
        (values ?? Enumerable.Empty<string>())
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .Select(v => v.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase);
}
