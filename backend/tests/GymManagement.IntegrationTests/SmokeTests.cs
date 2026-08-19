using System.Net;
using FluentAssertions;
using Xunit;

namespace GymManagement.IntegrationTests;

/// <summary>Proves the in-process host starts, seeds and answers before anything else runs.</summary>
[Collection(GymApiCollection.Name)]
public class SmokeTests : ApiTestBase
{
    public SmokeTests(GymApiFixture fixture) : base(fixture) { }

    [Fact(DisplayName = "GET /health is anonymous and reports the API is up")]
    public async Task Health_IsAnonymousAndHealthy()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact(DisplayName = "The seeded admin account can sign in")]
    public async Task SeededAdmin_CanSignIn()
    {
        var login = await Factory.LoginAsync();

        login.AccessToken.Should().NotBeNullOrWhiteSpace();
        login.User.UserName.Should().Be(GymApiFactory.AdminUserName);
    }
}
