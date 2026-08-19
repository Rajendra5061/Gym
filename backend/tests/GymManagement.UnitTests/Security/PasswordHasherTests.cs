using FluentAssertions;
using GymManagement.Infrastructure.Security;
using Xunit;

namespace GymManagement.UnitTests.Security;

/// <summary>BCrypt hashing and the temporary password generator used for new accounts and resets.</summary>
public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    private const string Password = "Str0ng!Password";

    [Fact(DisplayName = "A hash verifies against the password it was produced from")]
    public void Verify_WithTheOriginalPassword_ReturnsTrue()
    {
        var hash = _hasher.Hash(Password);

        _hasher.Verify(Password, hash).Should().BeTrue();
    }

    [Fact(DisplayName = "A hash does not verify against a different password")]
    public void Verify_WithADifferentPassword_ReturnsFalse()
    {
        var hash = _hasher.Hash(Password);

        _hasher.Verify("Wr0ng!Password", hash).Should().BeFalse();
    }

    [Fact(DisplayName = "Verification is case sensitive")]
    public void Verify_WithDifferentCasing_ReturnsFalse()
    {
        var hash = _hasher.Hash(Password);

        _hasher.Verify(Password.ToUpperInvariant(), hash).Should().BeFalse();
    }

    [Fact(DisplayName = "Two hashes of the same password differ because each one is salted")]
    public void Hash_CalledTwice_ProducesDifferentHashes()
    {
        var first = _hasher.Hash(Password);
        var second = _hasher.Hash(Password);

        first.Should().NotBe(second);
        _hasher.Verify(Password, first).Should().BeTrue();
        _hasher.Verify(Password, second).Should().BeTrue();
    }

    [Fact(DisplayName = "The plain password never appears inside the hash")]
    public void Hash_DoesNotContainThePlainPassword()
    {
        _hasher.Hash(Password).Should().NotContain(Password);
    }

    [Theory(DisplayName = "A corrupt or empty hash returns false rather than throwing")]
    [InlineData("not-a-hash")]
    [InlineData("$2a$12$tooshort")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("$$$$$$")]
    public void Verify_WithACorruptHash_ReturnsFalseWithoutThrowing(string corruptHash)
    {
        var act = () => _hasher.Verify(Password, corruptHash);

        act.Should().NotThrow();
        _hasher.Verify(Password, corruptHash).Should().BeFalse();
    }

    [Fact(DisplayName = "An empty password never verifies")]
    public void Verify_WithAnEmptyPassword_ReturnsFalse()
    {
        var hash = _hasher.Hash(Password);

        _hasher.Verify(string.Empty, hash).Should().BeFalse();
    }

    [Fact(DisplayName = "Hashing an empty password is rejected outright")]
    public void Hash_WithAnEmptyPassword_Throws()
    {
        var act = () => _hasher.Hash(string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    // ------------------------------------------------- temporary passwords

    [Fact(DisplayName = "A temporary password contains an upper case, lower case, digit and symbol")]
    public void GenerateTemporaryPassword_ContainsEveryCharacterClass()
    {
        var password = _hasher.GenerateTemporaryPassword();

        password.Any(char.IsUpper).Should().BeTrue("an upper case letter is required");
        password.Any(char.IsLower).Should().BeTrue("a lower case letter is required");
        password.Any(char.IsDigit).Should().BeTrue("a digit is required");
        password.Any(c => "!@#$%*?-+".Contains(c)).Should().BeTrue("a symbol is required");
    }

    [Theory(DisplayName = "A temporary password is never shorter than 8 characters")]
    [InlineData(12)]
    [InlineData(8)]
    [InlineData(4)]
    [InlineData(0)]
    [InlineData(-1)]
    public void GenerateTemporaryPassword_IsAtLeastEightCharacters(int requestedLength)
    {
        var password = _hasher.GenerateTemporaryPassword(requestedLength);

        password.Length.Should().BeGreaterThanOrEqualTo(8);
        password.Length.Should().Be(Math.Max(requestedLength, 8));
    }

    [Fact(DisplayName = "The default temporary password is 12 characters long")]
    public void GenerateTemporaryPassword_DefaultLengthIsTwelve()
    {
        _hasher.GenerateTemporaryPassword().Should().HaveLength(12);
    }

    [Fact(DisplayName = "Temporary passwords are random, not a fixed string")]
    public void GenerateTemporaryPassword_ProducesDistinctValues()
    {
        var generated = Enumerable.Range(0, 25)
            .Select(_ => _hasher.GenerateTemporaryPassword())
            .ToList();

        generated.Distinct().Should().HaveCountGreaterThan(20);
    }

    [Fact(DisplayName = "A generated temporary password can be hashed and verified")]
    public void GenerateTemporaryPassword_RoundTripsThroughTheHasher()
    {
        var password = _hasher.GenerateTemporaryPassword();

        _hasher.Verify(password, _hasher.Hash(password)).Should().BeTrue();
    }
}
