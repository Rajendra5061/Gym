using GymManagement.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace GymManagement.Infrastructure.Payments;

/// <summary>
/// Configuration for automatic payment reconciliation. <b>Off by default.</b>
///
/// Bound from the <c>Payments:Gateway</c> section, which means environment variables such as
/// <c>Payments__Gateway__Secret</c> and <c>Payments__Gateway__Enabled</c>. The secret is never read
/// from the database: <c>GymSettings</c> is readable by any administrator over the API, so a signing
/// key kept there would be a signing key handed to every admin account.
///
/// With nothing configured, <see cref="IsEnabled"/> is false, the webhook endpoint answers 503 and
/// the manual UPI flow is untouched.
/// </summary>
public sealed class PaymentGatewayOptions
{
    public const string SectionName = "Payments:Gateway";

    /// <summary>Master switch. A secret alone does not turn reconciliation on.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The provider slug accepted on <c>POST /api/payments/webhook/{provider}</c>. Leave empty and
    /// the single shared secret below serves whatever slug the gateway posts to.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Shared HMAC-SHA256 signing secret. Configuration / environment only
    /// (<c>Payments__Gateway__Secret</c>).
    /// </summary>
    public string? Secret { get; set; }

    /// <summary>
    /// <c>Test</c> or <c>Live</c>, purely so an operator screen can say which one the gym is
    /// pointed at. Nothing branches on it: a sandbox key and a production key differ only in the
    /// secret, and this exists so that difference is visible rather than guessed.
    /// </summary>
    public string? Mode { get; set; }

    /// <summary>Header carrying the signature. Gateways differ, so it is configurable.</summary>
    public string SignatureHeader { get; set; } = "X-Payment-Signature";

    /// <summary>
    /// Header carrying the event timestamp. May be omitted when the signature header itself carries
    /// a <c>t=</c> element in the Stripe style.
    /// </summary>
    public string TimestampHeader { get; set; } = "X-Payment-Timestamp";

    /// <summary>
    /// How far from now a signed timestamp may be before the delivery is treated as a replay.
    /// Five minutes matches what mainstream gateways recommend.
    /// </summary>
    public int ToleranceSeconds { get; set; } = 300;

    /// <summary>
    /// What the HMAC is computed over. <c>TimestampDotBody</c> (the default) signs
    /// <c>{timestamp}.{rawBody}</c>, which binds the timestamp into the signature so a captured
    /// payload cannot be re-dated. <c>BodyOnly</c> signs the raw body, for gateways that do that.
    /// </summary>
    public GatewaySignaturePayload SignaturePayload { get; set; } = GatewaySignaturePayload.TimestampDotBody;

    /// <summary>True when the gateway quotes amounts in paise / cents rather than rupees.</summary>
    public bool AmountsInMinorUnits { get; set; }

    /// <summary>Largest webhook body accepted, in bytes. Anything larger is refused unread.</summary>
    public int MaxBodyBytes { get; set; } = 64 * 1024;

    /// <summary>
    /// Per-provider overrides, for a gym that runs more than one gateway. Each entry may supply its
    /// own secret and headers; anything it leaves out falls back to the values above.
    /// </summary>
    public Dictionary<string, PaymentGatewayProviderOptions> Providers { get; set; } = new();

    /// <summary>
    /// Resolves the settings for one provider slug, or null when that slug is not configured.
    /// Never returns a resolution without a secret — an enabled gateway with no key must behave
    /// exactly like no gateway at all rather than like a gateway that accepts every signature.
    /// </summary>
    public ResolvedGateway? Resolve(string? provider)
    {
        if (!Enabled) return null;
        if (string.IsNullOrWhiteSpace(provider)) return null;

        var slug = provider.Trim();

        // Per-provider entry first. Dictionary comparers do not survive configuration binding, so
        // the lookup is done explicitly and case-insensitively.
        var entry = Providers
            .FirstOrDefault(p => string.Equals(p.Key, slug, StringComparison.OrdinalIgnoreCase));

        if (entry.Value is { } overrides)
        {
            var secret = FirstNonEmpty(overrides.Secret, Secret);
            return secret is null ? null : Build(entry.Key, secret, overrides);
        }

        // Otherwise the top-level secret, which serves either the pinned slug or any slug when no
        // provider name is pinned.
        if (!string.IsNullOrWhiteSpace(Provider) &&
            !string.Equals(Provider.Trim(), slug, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var shared = FirstNonEmpty(Secret);
        return shared is null ? null : Build(slug, shared, null);
    }

    private ResolvedGateway Build(string slug, string secret, PaymentGatewayProviderOptions? overrides) => new(
        Provider: slug,
        Secret: secret,
        SignatureHeader: FirstNonEmpty(overrides?.SignatureHeader, SignatureHeader) ?? "X-Payment-Signature",
        TimestampHeader: FirstNonEmpty(overrides?.TimestampHeader, TimestampHeader) ?? "X-Payment-Timestamp",
        ToleranceSeconds: Math.Clamp(overrides?.ToleranceSeconds ?? ToleranceSeconds, 5, 3600),
        SignaturePayload: overrides?.SignaturePayload ?? SignaturePayload,
        AmountsInMinorUnits: overrides?.AmountsInMinorUnits ?? AmountsInMinorUnits,
        MaxBodyBytes: Math.Clamp(MaxBodyBytes, 1024, 1024 * 1024));

    private static string? FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();
}

/// <summary>Per-provider overrides. Anything left null falls back to the top-level value.</summary>
public sealed class PaymentGatewayProviderOptions
{
    public string? Secret { get; set; }
    public string? SignatureHeader { get; set; }
    public string? TimestampHeader { get; set; }
    public int? ToleranceSeconds { get; set; }
    public GatewaySignaturePayload? SignaturePayload { get; set; }
    public bool? AmountsInMinorUnits { get; set; }
}

/// <summary>What the HMAC is computed over.</summary>
public enum GatewaySignaturePayload
{
    /// <summary><c>{timestamp}.{rawBody}</c> — the timestamp cannot be swapped without breaking the MAC.</summary>
    TimestampDotBody = 0,

    /// <summary>The raw body alone.</summary>
    BodyOnly = 1
}

/// <summary>Fully resolved settings for one provider slug. Only ever built with a real secret.</summary>
public sealed record ResolvedGateway(
    string Provider,
    string Secret,
    string SignatureHeader,
    string TimestampHeader,
    int ToleranceSeconds,
    GatewaySignaturePayload SignaturePayload,
    bool AmountsInMinorUnits,
    int MaxBodyBytes);

/// <summary>
/// Reads the gateway configuration for the rest of the application — chiefly so that the UPI intent
/// can stop telling the operator that nothing will be verified automatically.
/// </summary>
public sealed class PaymentGatewayConfiguration : IPaymentGatewayConfiguration
{
    private readonly IOptionsMonitor<PaymentGatewayOptions> _options;

    public PaymentGatewayConfiguration(IOptionsMonitor<PaymentGatewayOptions> options) =>
        _options = options ?? throw new ArgumentNullException(nameof(options));

    public PaymentGatewayOptions Current => _options.CurrentValue;

    public bool IsEnabled
    {
        get
        {
            var current = _options.CurrentValue;
            if (!current.Enabled) return false;

            // "Enabled with no key" is not enabled. Anything else would leave the UI claiming
            // automatic verification that no secret can actually perform.
            if (!string.IsNullOrWhiteSpace(current.Secret)) return true;

            return current.Providers.Values.Any(p => !string.IsNullOrWhiteSpace(p?.Secret));
        }
    }

    public bool IsConfiguredFor(string provider) => _options.CurrentValue.Resolve(provider) is not null;
}
