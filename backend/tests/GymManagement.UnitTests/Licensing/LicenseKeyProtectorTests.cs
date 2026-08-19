using FluentAssertions;
using GymManagement.Infrastructure.Licensing;
using Xunit;

namespace GymManagement.UnitTests.Licensing;

/// <summary>
/// Signing and parsing of licence keys — cases G-05 (a correctly signed key activates) and G-06
/// (one altered character makes the key invalid).
/// </summary>
public class LicenseKeyProtectorTests
{
    private const string Secret = "unit-test-licence-signing-secret-value";
    private const string OtherSecret = "a-completely-different-licence-secret";

    private static LicenseKeyPayload Payload() => new(
        Customer: "Iron Temple Fitness",
        GymId: "IRON-TEMPLE-001",
        ExpiryUtc: new DateTime(2027, 12, 31, 23, 59, 59, DateTimeKind.Utc),
        MaxMembers: 500,
        MaxUsers: 25,
        Features: new[] { "reports.financial", "backup", "multi-branch" },
        IsTrial: false);

    // ---------------------------------------------------------- round trip

    [Fact(DisplayName = "G-05 A signed key round-trips every payload field unchanged")]
    public void CreateKey_ThenTryParseKey_PreservesEveryField()
    {
        var original = Payload();

        var key = LicenseKeyProtector.CreateKey(original, Secret);
        var parsed = LicenseKeyProtector.TryParseKey(key, Secret, out var result);

        parsed.Should().BeTrue();
        result.Should().NotBeNull();
        result!.Customer.Should().Be(original.Customer);
        result.GymId.Should().Be(original.GymId);
        result.ExpiryUtc.Should().Be(original.ExpiryUtc);
        result.MaxMembers.Should().Be(original.MaxMembers);
        result.MaxUsers.Should().Be(original.MaxUsers);
        result.Features.Should().BeEquivalentTo(original.Features);
        result.IsTrial.Should().Be(original.IsTrial);
    }

    [Fact(DisplayName = "G-05 A trial key with no quotas round-trips its nulls")]
    public void CreateKey_WithNullQuotas_RoundTripsTheNulls()
    {
        var original = new LicenseKeyPayload("Small Gym", "SMALL-001",
            new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), null, null, null, true);

        LicenseKeyProtector.TryParseKey(LicenseKeyProtector.CreateKey(original, Secret), Secret, out var result)
            .Should().BeTrue();

        result!.MaxMembers.Should().BeNull();
        result.MaxUsers.Should().BeNull();
        result.Features.Should().BeNull();
        result.IsTrial.Should().BeTrue();
    }

    [Fact(DisplayName = "A generated key starts with the GYM1 format marker")]
    public void CreateKey_UsesTheDocumentedPrefix()
    {
        LicenseKeyProtector.CreateKey(Payload(), Secret)
            .Should().StartWith(LicenseKeyProtector.KeyPrefix + "-");
    }

    [Fact(DisplayName = "The same payload and secret always produce the same key")]
    public void CreateKey_IsDeterministic()
    {
        LicenseKeyProtector.CreateKey(Payload(), Secret)
            .Should().Be(LicenseKeyProtector.CreateKey(Payload(), Secret));
    }

    // ------------------------------------------------------------ tamper

    [Fact(DisplayName = "G-06 A key with one altered character in its payload fails to parse")]
    public void TryParseKey_WithATamperedPayload_ReturnsFalse()
    {
        var key = LicenseKeyProtector.CreateKey(Payload(), Secret);
        var tampered = Tamper(key, key.IndexOf('-') + 3);

        LicenseKeyProtector.TryParseKey(tampered, Secret, out var result).Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact(DisplayName = "G-06 A key with an altered signature fails to parse")]
    public void TryParseKey_WithATamperedSignature_ReturnsFalse()
    {
        var key = LicenseKeyProtector.CreateKey(Payload(), Secret);
        var tampered = Tamper(key, key.Length - 2);

        LicenseKeyProtector.TryParseKey(tampered, Secret, out var result).Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact(DisplayName = "G-06 Truncating a key makes it invalid")]
    public void TryParseKey_WithATruncatedKey_ReturnsFalse()
    {
        var key = LicenseKeyProtector.CreateKey(Payload(), Secret);

        LicenseKeyProtector.TryParseKey(key[..^5], Secret, out var result).Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact(DisplayName = "A key signed with one secret does not parse with another")]
    public void TryParseKey_WithTheWrongSecret_ReturnsFalse()
    {
        var key = LicenseKeyProtector.CreateKey(Payload(), Secret);

        LicenseKeyProtector.TryParseKey(key, OtherSecret, out var result).Should().BeFalse();
        result.Should().BeNull();
    }

    // ----------------------------------------------------------- garbage

    [Theory(DisplayName = "Garbage input returns false without throwing")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-licence-key")]
    [InlineData("GYM1")]
    [InlineData("GYM1-")]
    [InlineData("GYM1-!!!!-????")]
    [InlineData("XXXX-abc-def")]
    [InlineData("GYM1-YWJj-ZGVm")]
    public void TryParseKey_WithGarbage_ReturnsFalseWithoutThrowing(string input)
    {
        var act = () => LicenseKeyProtector.TryParseKey(input, Secret, out _);

        act.Should().NotThrow();
        LicenseKeyProtector.TryParseKey(input, Secret, out var result).Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact(DisplayName = "Parsing with a blank secret returns false rather than throwing")]
    public void TryParseKey_WithABlankSecret_ReturnsFalse()
    {
        var key = LicenseKeyProtector.CreateKey(Payload(), Secret);

        var act = () => LicenseKeyProtector.TryParseKey(key, string.Empty, out _);

        act.Should().NotThrow();
        LicenseKeyProtector.TryParseKey(key, string.Empty, out var result).Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact(DisplayName = "Creating a key without a secret is rejected")]
    public void CreateKey_WithoutASecret_Throws()
    {
        var act = () => LicenseKeyProtector.CreateKey(Payload(), string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    // -------------------------------------------------------- sign/verify

    [Fact(DisplayName = "Verify accepts a signature produced by Sign")]
    public void Verify_WithItsOwnSignature_ReturnsTrue()
    {
        const string payload = "some-signed-content";

        var signature = LicenseKeyProtector.Sign(payload, Secret);

        LicenseKeyProtector.Verify(payload, signature, Secret).Should().BeTrue();
    }

    [Fact(DisplayName = "Verify rejects a signature made with a different secret")]
    public void Verify_WithTheWrongSecret_ReturnsFalse()
    {
        const string payload = "some-signed-content";

        var signature = LicenseKeyProtector.Sign(payload, Secret);

        LicenseKeyProtector.Verify(payload, signature, OtherSecret).Should().BeFalse();
    }

    [Fact(DisplayName = "Verify returns false for a malformed signature rather than throwing")]
    public void Verify_WithAMalformedSignature_ReturnsFalse()
    {
        var act = () => LicenseKeyProtector.Verify("content", "not-base64!!", Secret);

        act.Should().NotThrow();
        LicenseKeyProtector.Verify("content", "not-base64!!", Secret).Should().BeFalse();
    }

    /// <summary>Flips one character of the key at <paramref name="index"/>.</summary>
    private static string Tamper(string key, int index)
    {
        var chars = key.ToCharArray();
        chars[index] = chars[index] == 'A' ? 'B' : 'A';
        return new string(chars);
    }
}
