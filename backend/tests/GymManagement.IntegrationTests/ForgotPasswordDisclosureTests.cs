using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GymManagement.IntegrationTests;

/// <summary>
/// Covers the production default of <c>Auth:ReturnResetTokenToAnonymousCallers</c>, which the rest
/// of the suite deliberately turns on so the reset-flow cases have a token to carry forward.
///
/// The rule under test: issuing a reset token and disclosing it are separate decisions. An
/// anonymous caller must not be able to tell a real account from an unknown one, because a reply
/// that differs is all an attacker needs to enumerate valid user names. Staff holding
/// <c>users.manage</c> still read the token out, since there is no email provider in this build.
/// </summary>
[Collection(GymApiCollection.Name)]
public class ForgotPasswordDisclosureTests : ApiTestBase
{
    public ForgotPasswordDisclosureTests(GymApiFixture fixture) : base(fixture) { }

    /// <summary>The same host with disclosure back at its production default.</summary>
    private WebApplicationFactory<Program> WithDisclosureOff() =>
        Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Auth:ReturnResetTokenToAnonymousCallers", "false"));

    [Fact(DisplayName = "A-11 An anonymous reset request looks identical for a real and an unknown account")]
    public async Task ForgotPassword_IsIndistinguishable_ForAnonymousCallers()
    {
        using var factory = WithDisclosureOff();
        using var client = factory.CreateClient();

        var real = await client.PostAsJsonAsync("/api/auth/forgot-password",
            new ForgotPasswordRequestDto { UserNameOrEmail = GymApiFactory.AdminUserName });

        var unknown = await client.PostAsJsonAsync("/api/auth/forgot-password",
            new ForgotPasswordRequestDto { UserNameOrEmail = $"no-such-account-{Guid.NewGuid():N}" });

        real.StatusCode.Should().Be(HttpStatusCode.OK);
        unknown.StatusCode.Should().Be(HttpStatusCode.OK);

        var realBody = await real.Content.ReadFromJsonAsync<ApiResponse<ForgotPasswordResponseDto>>(
            GymApiFactory.Json);
        var unknownBody = await unknown.Content.ReadFromJsonAsync<ApiResponse<ForgotPasswordResponseDto>>(
            GymApiFactory.Json);

        realBody!.Data!.ResetToken.Should().BeNull(
            "disclosing the token to an anonymous caller is what makes accounts enumerable");
        realBody.Data.ExpiresUtc.Should().BeNull();

        realBody.Data.Message.Should().Be(unknownBody!.Data!.Message,
            "a different message for a real account is itself the enumeration oracle");
        unknownBody.Data.ResetToken.Should().BeNull();
    }

    [Fact(DisplayName = "A-11b Staff who may manage users still receive the reset token to hand over")]
    public async Task ForgotPassword_StillReturnsToken_ForAnOperatorWhoMayManageUsers()
    {
        using var factory = WithDisclosureOff();
        using var client = factory.CreateClient();

        // The derived host shares this factory's in-memory database and JWT settings, so a token
        // minted through the usual helper authenticates against it.
        var login = await Factory.LoginAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.PostAsJsonAsync("/api/auth/forgot-password",
            new ForgotPasswordRequestDto { UserNameOrEmail = GymApiFactory.AdminUserName });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<ForgotPasswordResponseDto>>(
            GymApiFactory.Json);

        body!.Data!.ResetToken.Should().NotBeNullOrWhiteSpace(
            "an operator resetting on someone's behalf has to be able to read the token out");
        body.Data.ExpiresUtc.Should().NotBeNull();
    }
}
