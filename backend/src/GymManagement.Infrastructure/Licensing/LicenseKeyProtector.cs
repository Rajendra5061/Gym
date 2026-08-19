using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GymManagement.Infrastructure.Licensing;

/// <summary>
/// Creates and verifies offline licence keys.
/// <para>
/// A key looks like <c>GYM1-{base64url(json payload)}-{base64url(HMAC-SHA256 of the payload segment)}</c>.
/// The payload is readable by anyone holding the key (it carries no secret), but it cannot be edited
/// without invalidating the signature, because only the issuer knows the shared secret.
/// </para>
/// <para>
/// A symmetric HMAC means the verifying installation holds the same secret the issuer uses, so the
/// secret must be treated as a deployment secret (configuration / user secrets / vault) and never be
/// committed. A production build should move to an asymmetric signature (issuer signs with a private
/// key, the installation verifies with the embedded public key) and validate against a server-side
/// licensing service.
/// </para>
/// </summary>
public static class LicenseKeyProtector
{
    /// <summary>Version prefix so a future key format can be told apart from this one.</summary>
    public const string KeyPrefix = "GYM1";

    /// <summary>Length of the machine fingerprint returned by <see cref="ComputeMachineId"/>.</summary>
    private const int MachineIdLength = 32;

    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false
    };

    /// <summary>
    /// Stable fingerprint of this installation, used to tie a licence to one machine.
    /// <para>
    /// This is deliberately cheap and portable: it only mixes values every .NET host exposes. A
    /// production build should use a stronger fingerprint (motherboard / BIOS serial, machine GUID,
    /// primary NIC MAC, volume serial) and tolerate hardware changes with a re-activation flow.
    /// </para>
    /// </summary>
    public static string ComputeMachineId()
    {
        var raw = string.Join('|',
            Environment.MachineName,
            Environment.UserDomainName,
            Environment.ProcessorCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Environment.OSVersion.Platform.ToString());

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..MachineIdLength];
    }

    /// <summary>HMAC-SHA256 of <paramref name="payload"/> as standard base64.</summary>
    public static string Sign(string payload, string secret)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (string.IsNullOrEmpty(secret))
            throw new ArgumentException("A signing secret is required.", nameof(secret));

        return Convert.ToBase64String(ComputeHmac(payload, secret));
    }

    /// <summary>
    /// Fixed-time verification of a base64 signature produced by <see cref="Sign"/>.
    /// Returns false for malformed input instead of throwing.
    /// </summary>
    public static bool Verify(string payload, string signature, string secret)
    {
        if (payload is null || string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(secret))
            return false;

        if (!TryFromBase64(signature, out var provided))
            return false;

        var expected = ComputeHmac(payload, secret);

        // Fixed-time comparison keeps the check free of timing side channels.
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    /// <summary>Builds a signed licence key. Used by the licence issuer and by the tests.</summary>
    public static string CreateKey(LicenseKeyPayload payload, string secret)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (string.IsNullOrEmpty(secret))
            throw new ArgumentException("A signing secret is required.", nameof(secret));

        var json = JsonSerializer.Serialize(payload, PayloadJsonOptions);
        var payloadSegment = ToBase64Url(Encoding.UTF8.GetBytes(json));
        var signatureSegment = ToBase64Url(ComputeHmac(payloadSegment, secret));

        return string.Concat(KeyPrefix, "-", payloadSegment, "-", signatureSegment);
    }

    /// <summary>
    /// Parses and verifies a licence key. Never throws: any malformed key, wrong prefix, bad base64,
    /// unreadable JSON or failed signature simply returns false.
    /// </summary>
    public static bool TryParseKey(string licenseKey, string secret, out LicenseKeyPayload? payload)
    {
        payload = null;

        if (string.IsNullOrWhiteSpace(licenseKey) || string.IsNullOrEmpty(secret))
            return false;

        var key = licenseKey.Trim();

        if (!key.StartsWith(KeyPrefix + "-", StringComparison.Ordinal))
            return false;

        var body = key[(KeyPrefix.Length + 1)..];

        // base64url may itself contain '-', so every '-' is tried as the payload/signature separator
        // and the first candidate whose signature verifies wins. Keys have a single separator in
        // practice, so this loop is short and unambiguous.
        for (var separator = body.IndexOf('-'); separator >= 0; separator = body.IndexOf('-', separator + 1))
        {
            var payloadSegment = body[..separator];
            var signatureSegment = body[(separator + 1)..];

            if (payloadSegment.Length == 0 || signatureSegment.Length == 0) continue;

            if (!TryFromBase64Url(signatureSegment, out var providedSignature)) continue;

            var expected = ComputeHmac(payloadSegment, secret);
            if (!CryptographicOperations.FixedTimeEquals(expected, providedSignature)) continue;

            if (!TryFromBase64Url(payloadSegment, out var jsonBytes)) continue;

            try
            {
                var parsed = JsonSerializer.Deserialize<LicenseKeyPayload>(jsonBytes, PayloadJsonOptions);
                if (parsed is null || string.IsNullOrWhiteSpace(parsed.GymId)) continue;

                payload = parsed;
                return true;
            }
            catch (JsonException)
            {
                // Signature matched but the body is not a payload we understand: treat as invalid.
                return false;
            }
        }

        return false;
    }

    private static byte[] ComputeHmac(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryFromBase64Url(string value, out byte[] bytes)
    {
        var standard = value.Replace('-', '+').Replace('_', '/');

        switch (standard.Length % 4)
        {
            case 1: bytes = Array.Empty<byte>(); return false;
            case 2: standard += "=="; break;
            case 3: standard += "="; break;
        }

        return TryFromBase64(standard, out bytes);
    }

    private static bool TryFromBase64(string value, out byte[] bytes)
    {
        var buffer = new byte[value.Length / 4 * 3 + 3];

        if (!Convert.TryFromBase64String(value, buffer, out var written))
        {
            bytes = Array.Empty<byte>();
            return false;
        }

        bytes = buffer.AsSpan(0, written).ToArray();
        return true;
    }
}

/// <summary>The signed body of a licence key.</summary>
public sealed record LicenseKeyPayload(
    string Customer,
    string GymId,
    DateTime ExpiryUtc,
    int? MaxMembers,
    int? MaxUsers,
    string[]? Features,
    bool IsTrial);
