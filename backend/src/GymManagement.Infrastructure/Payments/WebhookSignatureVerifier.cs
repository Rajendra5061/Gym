using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GymManagement.Infrastructure.Payments;

/// <summary>Why a delivery was refused, or that it was accepted.</summary>
public enum SignatureVerdict
{
    Valid = 0,

    /// <summary>No signature header, or one that carries no usable digest.</summary>
    Missing = 1,

    /// <summary>A signature header that is not a recognisable hex/base64 MAC.</summary>
    Malformed = 2,

    /// <summary>A well-formed signature that is not the right one for these bytes.</summary>
    Mismatch = 3,

    /// <summary>No timestamp to check freshness against.</summary>
    TimestampMissing = 4,

    /// <summary>A timestamp that is not a number or an ISO-8601 instant.</summary>
    TimestampMalformed = 5,

    /// <summary>A timestamp outside the tolerance window — a captured payload being replayed.</summary>
    Stale = 6
}

public sealed record SignatureCheck(SignatureVerdict Verdict, string Reason)
{
    public bool IsValid => Verdict == SignatureVerdict.Valid;

    public bool IsStale => Verdict == SignatureVerdict.Stale;
}

/// <summary>
/// HMAC-SHA256 verification of a webhook delivery.
///
/// Three properties matter here and are each deliberate:
/// <list type="number">
///   <item>
///     <b>The MAC is taken over the bytes that arrived</b>, never over a re-serialised object. Any
///     round trip through a JSON model would reorder keys, normalise numbers and re-encode escapes,
///     and the signature would then be checked against something the gateway never signed.
///   </item>
///   <item>
///     <b>The comparison is constant time.</b> <see cref="CryptographicOperations.FixedTimeEquals"/>
///     does not stop at the first differing byte, so timing cannot be used to walk a forged
///     signature towards the right one.
///   </item>
///   <item>
///     <b>The timestamp is inside the signed material.</b> With the default
///     <see cref="GatewaySignaturePayload.TimestampDotBody"/> scheme the MAC covers
///     <c>{timestamp}.{body}</c>, so re-dating a captured payload to defeat the freshness window
///     breaks the signature.
///   </item>
/// </list>
/// </summary>
public static class WebhookSignatureVerifier
{
    /// <summary>
    /// Verifies signature and freshness together. Returns the first failure and never says why in
    /// enough detail for a caller to use the answer as an oracle — the reason is for the log.
    /// </summary>
    public static SignatureCheck Verify(
        ResolvedGateway gateway,
        byte[] rawBody,
        string? signatureHeader,
        string? timestampHeader,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(rawBody);

        if (string.IsNullOrWhiteSpace(signatureHeader))
            return new SignatureCheck(SignatureVerdict.Missing, $"no {gateway.SignatureHeader} header");

        var parsed = ParseSignatureHeader(signatureHeader);
        if (parsed.Digest is null)
            return new SignatureCheck(SignatureVerdict.Malformed, "signature header carries no digest");

        var supplied = DecodeDigest(parsed.Digest);
        if (supplied is null || supplied.Length != 32)
            return new SignatureCheck(SignatureVerdict.Malformed, "signature is not a 32-byte SHA-256 MAC");

        // The timestamp may travel in its own header or inside the signature header as t=…
        var timestampText = string.IsNullOrWhiteSpace(timestampHeader) ? parsed.Timestamp : timestampHeader.Trim();

        if (string.IsNullOrWhiteSpace(timestampText))
            return new SignatureCheck(SignatureVerdict.TimestampMissing, $"no {gateway.TimestampHeader} header");

        if (!TryParseTimestamp(timestampText, out var signedAt))
            return new SignatureCheck(SignatureVerdict.TimestampMalformed, "timestamp is not a unix time or ISO-8601 instant");

        var drift = utcNow - signedAt.UtcDateTime;
        if (Math.Abs(drift.TotalSeconds) > gateway.ToleranceSeconds)
        {
            return new SignatureCheck(SignatureVerdict.Stale,
                $"timestamp is {drift.TotalSeconds:0} s from now, outside the ±{gateway.ToleranceSeconds} s window");
        }

        var expected = ComputeMac(gateway, rawBody, timestampText);

        return CryptographicOperations.FixedTimeEquals(expected, supplied)
            ? new SignatureCheck(SignatureVerdict.Valid, "signature verified")
            : new SignatureCheck(SignatureVerdict.Mismatch, "signature does not match the request body");
    }

    /// <summary>The MAC a gateway is expected to send. Also used by the test signer.</summary>
    public static byte[] ComputeMac(ResolvedGateway gateway, byte[] rawBody, string timestamp)
    {
        var key = Encoding.UTF8.GetBytes(gateway.Secret);

        byte[] material;
        if (gateway.SignaturePayload == GatewaySignaturePayload.BodyOnly)
        {
            material = rawBody;
        }
        else
        {
            var prefix = Encoding.UTF8.GetBytes(timestamp + ".");
            material = new byte[prefix.Length + rawBody.Length];
            Buffer.BlockCopy(prefix, 0, material, 0, prefix.Length);
            Buffer.BlockCopy(rawBody, 0, material, prefix.Length, rawBody.Length);
        }

        return HMACSHA256.HashData(key, material);
    }

    /// <summary>Lowercase hex of <see cref="ComputeMac"/>, the form this application documents.</summary>
    public static string ComputeHexSignature(ResolvedGateway gateway, byte[] rawBody, string timestamp) =>
        Convert.ToHexString(ComputeMac(gateway, rawBody, timestamp)).ToLowerInvariant();

    /// <summary>
    /// Accepts the header shapes gateways actually use: a bare digest, <c>sha256=digest</c>, or a
    /// comma-separated element list such as <c>t=1718000000,v1=digest</c>.
    /// </summary>
    private static (string? Digest, string? Timestamp) ParseSignatureHeader(string header)
    {
        var value = header.Trim();

        if (!value.Contains('=', StringComparison.Ordinal))
            return (value, null);

        string? digest = null;
        string? timestamp = null;

        foreach (var part in value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                digest ??= part;
                continue;
            }

            var name = part[..separator].Trim();
            var element = part[(separator + 1)..].Trim();

            switch (name.ToLowerInvariant())
            {
                case "t":
                case "ts":
                case "timestamp":
                    timestamp ??= element;
                    break;

                case "v1":
                case "s":
                case "sha256":
                case "signature":
                    digest ??= element;
                    break;
            }
        }

        return (digest, timestamp);
    }

    /// <summary>Hex or base64, whichever the gateway chose.</summary>
    private static byte[]? DecodeDigest(string digest)
    {
        var value = digest.Trim();

        if (value.Length == 64 && value.All(Uri.IsHexDigit))
        {
            try { return Convert.FromHexString(value); }
            catch (FormatException) { return null; }
        }

        try { return Convert.FromBase64String(value); }
        catch (FormatException) { return null; }
    }

    /// <summary>Unix seconds, unix milliseconds, or an ISO-8601 instant.</summary>
    public static bool TryParseTimestamp(string value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        var text = value.Trim();

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
        {
            try
            {
                timestamp = Math.Abs(epoch) > 100_000_000_000L
                    ? DateTimeOffset.FromUnixTimeMilliseconds(epoch)
                    : DateTimeOffset.FromUnixTimeSeconds(epoch);

                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out timestamp);
    }
}
