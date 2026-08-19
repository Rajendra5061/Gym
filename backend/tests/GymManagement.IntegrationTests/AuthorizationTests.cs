using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Domain.Constants;
using GymManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GymManagement.IntegrationTests;

/// <summary>
/// Authentication and permission gating over HTTP — cases Z-01, Z-02, Z-04, Z-05, Z-07 and Z-08.
/// </summary>
[Collection(GymApiCollection.Name)]
public class AuthorizationTests : ApiTestBase
{
    public AuthorizationTests(GymApiFixture fixture) : base(fixture) { }

    /// <summary>Creates a real login account in the given role and returns a client using its token.</summary>
    private async Task<(HttpClient Client, int UserId, int? MemberId)> ClientInRoleAsync(
        string role, int? memberId = null)
    {
        using var admin = await Factory.CreateAuthenticatedClientAsync();

        var roleId = await Factory.FromDbAsync(db =>
            db.Roles.AsNoTracking().Where(r => r.Name == role).Select(r => r.Id).FirstAsync());

        var userName = $"u{Guid.NewGuid():N}"[..14];
        const string password = "RolePassw0rd!";

        var created = await ReadDataAsync<TemporaryPasswordDto>(
            await admin.PostAsJsonAsync("/api/users", new CreateUserDto
            {
                UserName = userName,
                FullName = $"{role} User",
                Email = UniqueEmail(),
                Password = password,
                MustChangePassword = false,
                RoleIds = new List<int> { roleId },
                MemberId = memberId,
                Status = UserStatus.Active
            }));

        var client = await Factory.CreateAuthenticatedClientAsync(userName, password);
        return (client, created.UserId, memberId);
    }

    // ---------------------------------------------------------------- Z-01

    [Fact(DisplayName = "Z-01 Calling /api/members with no token is 401")]
    public async Task Members_WithoutAToken_Is401()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync("/api/members");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory(DisplayName = "Z-01 Every protected endpoint refuses an anonymous caller")]
    [InlineData("/api/members")]
    [InlineData("/api/subscriptions")]
    [InlineData("/api/Payments")]
    [InlineData("/api/Attendance")]
    [InlineData("/api/Audit")]
    [InlineData("/api/recycle-bin")]
    [InlineData("/api/Settings/gym")]
    [InlineData("/api/users")]
    public async Task ProtectedEndpoints_WithoutAToken_Are401(string url)
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------- Z-02

    [Fact(DisplayName = "Z-02 A Trainer cannot create a member because members.create is not granted")]
    public async Task Trainer_CannotCreateAMember()
    {
        var (client, _, _) = await ClientInRoleAsync(RoleNames.Trainer);
        using var _client = client;

        var response = await client.PostAsJsonAsync("/api/members", NewMemberDto());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "Z-02 A Trainer may still read the member list, which they do hold")]
    public async Task Trainer_CanReadTheMemberList()
    {
        var (client, _, _) = await ClientInRoleAsync(RoleNames.Trainer);
        using var _client = client;

        var response = await client.GetAsync("/api/members");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "Z-02 A Staff user cannot reach the user administration screens")]
    public async Task Staff_CannotAdministerUsers()
    {
        var (client, _, _) = await ClientInRoleAsync(RoleNames.Staff);
        using var _client = client;

        (await client.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/Audit")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/recycle-bin")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/Settings/gym")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- Z-04

    [Fact(DisplayName = "Z-04 An administrator reaches every module without an explicit grant")]
    public async Task Admin_ReachesEveryModule()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        foreach (var url in new[]
                 {
                     "/api/members", "/api/subscriptions", "/api/Payments", "/api/Attendance",
                     "/api/Audit", "/api/recycle-bin", "/api/Settings/gym", "/api/users",
                     "/api/Dashboard", "/api/membership-plans"
                 })
        {
            var response = await client.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"an admin should reach {url}");
        }
    }

    // ---------------------------------------------------------------- Z-05

    [Fact(DisplayName = "Z-05 A Member may open their own dashboard")]
    public async Task Member_CanOpenTheirOwnDashboard()
    {
        using var admin = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(admin);

        var (client, _, _) = await ClientInRoleAsync(RoleNames.Member, member.Id);
        using var _client = client;

        var response = await client.GetAsync($"/api/Dashboard/member/{member.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "Z-05 A Member is refused somebody else's dashboard")]
    public async Task Member_CannotOpenSomebodyElsesDashboard()
    {
        using var admin = await Factory.CreateAuthenticatedClientAsync();
        var mine = await CreateMemberAsync(admin);
        var theirs = await CreateMemberAsync(admin);

        var (client, _, _) = await ClientInRoleAsync(RoleNames.Member, mine.Id);
        using var _client = client;

        var response = await client.GetAsync($"/api/Dashboard/member/{theirs.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // This 403 is raised in application code, so it carries the full envelope.
        var error = await ReadErrorAsync(response);
        error.ErrorCode.Should().Be("FORBIDDEN");
        error.Message.Should().Contain("your own dashboard");
    }

    // ---------------------------------------------------------------- Z-07

    [Fact(DisplayName = "Z-07 A SQL injection attempt in the search box is treated as a literal string")]
    public async Task Search_WithASqlInjectionAttempt_IsHarmless()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        await CreateMemberAsync(client);

        var injection = Uri.EscapeDataString("'; DROP TABLE Members;--");
        var page = await ReadDataAsync<PagedResult<MemberListDto>>(
            await client.GetAsync($"/api/members?Search={injection}"));

        page.TotalCount.Should().Be(0);

        // The table is still there and still has rows.
        var everything = await ReadDataAsync<PagedResult<MemberListDto>>(
            await client.GetAsync("/api/members"));
        everything.TotalCount.Should().BeGreaterThan(0);
    }

    // ---------------------------------------------------------------- Z-08

    [Fact(DisplayName = "Z-08 A page size of 100000 is clamped to 200")]
    public async Task PageSize_IsClampedTo200()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var page = await ReadDataAsync<PagedResult<MemberListDto>>(
            await client.GetAsync("/api/members?pageSize=100000"));

        page.PageSize.Should().Be(200);
        page.Items.Count.Should().BeLessThanOrEqualTo(200);
    }

    [Fact(DisplayName = "Z-08 A page number below 1 is clamped to the first page")]
    public async Task PageNumber_IsClampedToOne()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var page = await ReadDataAsync<PagedResult<MemberListDto>>(
            await client.GetAsync("/api/members?pageNumber=0"));

        page.PageNumber.Should().Be(1);
    }

    [Fact(DisplayName = "Z-08 The clamp applies to every paged endpoint, not just members")]
    public async Task PageSize_IsClampedOnEveryPagedEndpoint()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var subscriptions = await ReadDataAsync<PagedResult<SubscriptionDto>>(
            await client.GetAsync("/api/subscriptions?pageSize=100000"));
        subscriptions.PageSize.Should().Be(200);

        var payments = await ReadDataAsync<PagedResult<PaymentDto>>(
            await client.GetAsync("/api/Payments?pageSize=100000"));
        payments.PageSize.Should().Be(200);
    }
}
