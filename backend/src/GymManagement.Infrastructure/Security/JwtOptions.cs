namespace GymManagement.Infrastructure.Security;

/// <summary>
/// Strongly typed JWT configuration bound from the <c>"Jwt"</c> configuration section.
/// The signing secret is never hard-coded: it must come from configuration, user secrets,
/// an environment variable or a secret store.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>Configuration section name these options are bound from.</summary>
    public const string SectionName = "Jwt";

    /// <summary>HMAC-SHA256 needs at least 256 bits of key material.</summary>
    public const int MinimumSecretLength = 32;

    /// <summary>Symmetric signing key. Supplied by configuration only.</summary>
    public string Secret { get; set; } = string.Empty;

    public string Issuer { get; set; } = "GymManagement";

    public string Audience { get; set; } = "GymManagement.Client";

    /// <summary>Access token lifetime in minutes.</summary>
    public int AccessTokenMinutes { get; set; } = 60;

    /// <summary>Refresh token lifetime in days.</summary>
    public int RefreshTokenDays { get; set; } = 7;

    /// <summary>Allowed clock skew in seconds when validating tokens.</summary>
    public int ClockSkewSeconds { get; set; } = 30;

    /// <summary>
    /// Fails fast on a misconfigured or missing secret so the application never starts with a
    /// weak / default signing key.
    /// </summary>
    /// <exception cref="InvalidOperationException">The configuration is unusable.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Secret))
            throw new InvalidOperationException(
                $"Jwt:Secret is not configured. Provide it through configuration, user secrets or an " +
                $"environment variable ({MinimumSecretLength} characters or more). Secrets must never be hard-coded.");

        if (Secret.Trim().Length < MinimumSecretLength)
            throw new InvalidOperationException(
                $"Jwt:Secret must be at least {MinimumSecretLength} characters long; it is currently {Secret.Trim().Length}.");

        if (string.IsNullOrWhiteSpace(Issuer))
            throw new InvalidOperationException("Jwt:Issuer is not configured.");

        if (string.IsNullOrWhiteSpace(Audience))
            throw new InvalidOperationException("Jwt:Audience is not configured.");

        if (AccessTokenMinutes <= 0)
            throw new InvalidOperationException("Jwt:AccessTokenMinutes must be greater than zero.");

        if (RefreshTokenDays <= 0)
            throw new InvalidOperationException("Jwt:RefreshTokenDays must be greater than zero.");

        if (ClockSkewSeconds < 0)
            throw new InvalidOperationException("Jwt:ClockSkewSeconds cannot be negative.");
    }
}
