using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using GymManagement.Domain.Constants;
using GymManagement.Infrastructure.Security;
using GymManagement.UnitTests.TestBase;
using Microsoft.Extensions.Options;
using Xunit;

namespace GymManagement.UnitTests.Security;

/// <summary>
/// Access and refresh token minting. Supports cases A-01 (the login response carries roles and
/// permissions) and A-07 (the token carries a real expiry).
/// </summary>
public class JwtTokenServiceTests
{
    private const string Secret = "unit-test-signing-secret-of-at-least-32-characters";

    private static readonly string[] Roles = { RoleNames.Staff, RoleNames.Trainer };

    private static readonly string[] PermissionCodes =
        { Permissions.MembersView, Permissions.MembersCreate, Permissions.AttendanceManage };

    private readonly FixedClock _clock = new();

    private JwtTokenService Service(Action<JwtOptions>? configure = null)
    {
        var options = new JwtOptions
        {
            Secret = Secret,
            Issuer = "GymManagement.Tests",
            Audience = "GymManagement.TestClient",
            AccessTokenMinutes = 60,
            RefreshTokenDays = 7
        };

        configure?.Invoke(options);
        return new JwtTokenService(Options.Create(options), _clock);
    }

    private JwtSecurityToken CreateAndRead(out string raw)
    {
        raw = Service().CreateAccessToken(
            userId: 42,
            userName: "jane.doe",
            fullName: "Jane Doe",
            memberId: 7,
            trainerId: null,
            roles: Roles,
            permissions: PermissionCodes).Token;

        return new JwtSecurityTokenHandler().ReadJwtToken(raw);
    }

    /// <summary>The handler shortens the well-known ClaimTypes URIs when it writes the token.</summary>
    private static IEnumerable<string> ValuesOf(JwtSecurityToken token, params string[] acceptedTypes) =>
        token.Claims.Where(c => acceptedTypes.Contains(c.Type, StringComparer.Ordinal)).Select(c => c.Value);

    // ------------------------------------------------------- access token

    [Fact(DisplayName = "A-01 The access token embeds the user id")]
    public void CreateAccessToken_EmbedsTheUserId()
    {
        var token = CreateAndRead(out _);

        ValuesOf(token, "sub", "nameid", System.Security.Claims.ClaimTypes.NameIdentifier)
            .Should().Contain("42");
    }

    [Fact(DisplayName = "A-01 The access token embeds the user name and full name")]
    public void CreateAccessToken_EmbedsTheUserName()
    {
        var token = CreateAndRead(out _);

        ValuesOf(token, "unique_name", System.Security.Claims.ClaimTypes.Name).Should().Contain("jane.doe");
        ValuesOf(token, "given_name", System.Security.Claims.ClaimTypes.GivenName).Should().Contain("Jane Doe");
    }

    [Fact(DisplayName = "A-01 The access token carries one role claim per role")]
    public void CreateAccessToken_CarriesEveryRole()
    {
        var token = CreateAndRead(out _);

        ValuesOf(token, "role", System.Security.Claims.ClaimTypes.Role)
            .Should().BeEquivalentTo(Roles);
    }

    [Fact(DisplayName = "A-01 The access token carries exactly one permission claim per code")]
    public void CreateAccessToken_CarriesOnePermissionClaimPerCode()
    {
        var token = CreateAndRead(out _);

        token.Claims
            .Where(c => c.Type == JwtTokenService.PermissionClaimType)
            .Select(c => c.Value)
            .Should().BeEquivalentTo(PermissionCodes);
    }

    [Fact(DisplayName = "The access token carries the linked member id when there is one")]
    public void CreateAccessToken_CarriesTheMemberId()
    {
        var token = CreateAndRead(out _);

        token.Claims.Should().Contain(c => c.Type == JwtTokenService.MemberIdClaimType && c.Value == "7");
        token.Claims.Should().NotContain(c => c.Type == JwtTokenService.TrainerIdClaimType);
    }

    [Fact(DisplayName = "A-07 The access token expires in the future, AccessTokenMinutes from now")]
    public void CreateAccessToken_SetsAFutureExpiry()
    {
        var result = Service().CreateAccessToken(1, "u", "U", null, null, Roles, PermissionCodes);

        result.ExpiresUtc.Should().Be(_clock.UtcNow.AddMinutes(60));
        result.ExpiresUtc.Should().BeAfter(_clock.UtcNow);
    }

    [Fact(DisplayName = "A-07 The encoded token's own expiry matches the returned expiry")]
    public void CreateAccessToken_EncodedExpiryMatchesTheReturnedValue()
    {
        var result = Service().CreateAccessToken(1, "u", "U", null, null, Roles, PermissionCodes);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);

        // JWT exp has one second resolution.
        token.ValidTo.Should().BeCloseTo(result.ExpiresUtc, TimeSpan.FromSeconds(1));
    }

    [Fact(DisplayName = "The access token is issued for the configured issuer and audience")]
    public void CreateAccessToken_UsesTheConfiguredIssuerAndAudience()
    {
        var token = CreateAndRead(out _);

        token.Issuer.Should().Be("GymManagement.Tests");
        token.Audiences.Should().Contain("GymManagement.TestClient");
    }

    [Fact(DisplayName = "Duplicate and blank roles or permissions are collapsed")]
    public void CreateAccessToken_DeduplicatesRolesAndPermissions()
    {
        var raw = Service().CreateAccessToken(1, "u", "U", null, null,
            new[] { RoleNames.Staff, RoleNames.Staff, "  ", null! },
            new[] { Permissions.MembersView, Permissions.MembersView }).Token;

        var token = new JwtSecurityTokenHandler().ReadJwtToken(raw);

        ValuesOf(token, "role", System.Security.Claims.ClaimTypes.Role).Should().ContainSingle();
        token.Claims.Count(c => c.Type == JwtTokenService.PermissionClaimType).Should().Be(1);
    }

    [Fact(DisplayName = "Each access token gets its own jti so tokens are individually identifiable")]
    public void CreateAccessToken_ProducesAUniqueJti()
    {
        var service = Service();
        var handler = new JwtSecurityTokenHandler();

        var first = handler.ReadJwtToken(service.CreateAccessToken(1, "u", "U", null, null, Roles, PermissionCodes).Token);
        var second = handler.ReadJwtToken(service.CreateAccessToken(1, "u", "U", null, null, Roles, PermissionCodes).Token);

        first.Id.Should().NotBe(second.Id);
    }

    // ------------------------------------------------------ refresh token

    [Fact(DisplayName = "A-08 Two refresh tokens are never the same value")]
    public void CreateRefreshToken_ProducesDistinctValues()
    {
        var service = Service();

        var tokens = Enumerable.Range(0, 20).Select(_ => service.CreateRefreshToken().Token).ToList();

        tokens.Should().OnlyHaveUniqueItems();
    }

    [Fact(DisplayName = "A refresh token's hash matches HashToken of its own value")]
    public void CreateRefreshToken_HashMatchesHashToken()
    {
        var service = Service();

        var (token, tokenHash, _) = service.CreateRefreshToken();

        tokenHash.Should().Be(service.HashToken(token));
    }

    [Fact(DisplayName = "The refresh token hash is not the token itself")]
    public void CreateRefreshToken_HashIsNotThePlainToken()
    {
        var (token, tokenHash, _) = Service().CreateRefreshToken();

        tokenHash.Should().NotBe(token);
        tokenHash.Should().NotContain(token);
    }

    [Fact(DisplayName = "A refresh token expires RefreshTokenDays from now")]
    public void CreateRefreshToken_ExpiresAfterTheConfiguredNumberOfDays()
    {
        var (_, _, expiresUtc) = Service().CreateRefreshToken();

        expiresUtc.Should().Be(_clock.UtcNow.AddDays(7));
    }

    [Fact(DisplayName = "HashToken is deterministic for the same input")]
    public void HashToken_IsDeterministic()
    {
        var service = Service();

        service.HashToken("a-token").Should().Be(service.HashToken("a-token"));
        service.HashToken("a-token").Should().NotBe(service.HashToken("another-token"));
    }

    [Fact(DisplayName = "HashToken rejects an empty token")]
    public void HashToken_WithAnEmptyToken_Throws()
    {
        var act = () => Service().HashToken(string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    // ------------------------------------------------------- construction

    [Theory(DisplayName = "A secret shorter than 32 characters makes construction fail")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too-short")]
    [InlineData("31-characters-long-secret-value")]
    public void Constructor_WithAShortSecret_Throws(string secret)
    {
        var act = () => Service(o => o.Secret = secret);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact(DisplayName = "A secret of exactly 32 characters is accepted")]
    public void Constructor_WithAThirtyTwoCharacterSecret_Succeeds()
    {
        var act = () => Service(o => o.Secret = new string('x', 32));

        act.Should().NotThrow();
    }

    [Fact(DisplayName = "A missing issuer or audience makes construction fail")]
    public void Constructor_WithAMissingIssuerOrAudience_Throws()
    {
        FluentActions.Invoking(() => Service(o => o.Issuer = string.Empty))
            .Should().Throw<InvalidOperationException>();

        FluentActions.Invoking(() => Service(o => o.Audience = string.Empty))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact(DisplayName = "A non-positive access token lifetime makes construction fail")]
    public void Constructor_WithANonPositiveLifetime_Throws()
    {
        FluentActions.Invoking(() => Service(o => o.AccessTokenMinutes = 0))
            .Should().Throw<InvalidOperationException>();
    }
}
