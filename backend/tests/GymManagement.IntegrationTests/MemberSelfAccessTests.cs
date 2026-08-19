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
/// A signed-in member may read their own records and nothing else.
/// </summary>
/// <remarks>
/// The Member role holds neither <c>members.view</c> nor <c>subscriptions.view</c>,
/// <c>payments.view</c> or <c>workouts.view</c>, so every case here proves the self-access rule in
/// <c>MemberSelfAccess</c> rather than a permission grant. The denial cases are the important half:
/// they change nothing but the id in the URL, which is exactly what an attacker would do.
/// </remarks>
[Collection(GymApiCollection.Name)]
public class MemberSelfAccessTests : ApiTestBase
{
    public MemberSelfAccessTests(GymApiFixture fixture) : base(fixture) { }

    /// <summary>
    /// Creates a real login account in the Member role linked to the given member. A null
    /// <paramref name="memberId"/> makes an account with no member behind it.
    /// </summary>
    private async Task<HttpClient> MemberClientAsync(int? memberId)
    {
        using var admin = await Factory.CreateAuthenticatedClientAsync();

        var roleId = await Factory.FromDbAsync(db => db.Roles
            .AsNoTracking()
            .Where(r => r.Name == RoleNames.Member)
            .Select(r => r.Id)
            .FirstAsync());

        var userName = $"m{Guid.NewGuid():N}"[..14];
        const string password = "SelfAccessPassw0rd!";

        await ReadDataAsync<TemporaryPasswordDto>(
            await admin.PostAsJsonAsync("/api/users", new CreateUserDto
            {
                UserName = userName,
                FullName = "Self Access Member",
                Email = UniqueEmail(),
                Password = password,
                MustChangePassword = false,
                RoleIds = new List<int> { roleId },
                MemberId = memberId,
                Status = UserStatus.Active
            }));

        return await Factory.CreateAuthenticatedClientAsync(userName, password);
    }

    /// <summary>
    /// A member with a fully paid Monthly subscription, plus the id of the payment that settled it.
    /// </summary>
    private async Task<(MemberDetailDto Member, int PaymentId)> MemberWithAPaymentAsync(HttpClient admin)
    {
        var (member, _) = await SellSubscriptionAsync(admin);

        var payments = await ReadDataAsync<List<PaymentDto>>(
            await admin.GetAsync($"/api/Payments/member/{member.Id}"));

        payments.Should().NotBeEmpty("selling a paid subscription records a payment");
        return (member, payments[0].Id);
    }

    /// <summary>The member-scoped reads a signed-in member is allowed to make for themselves.</summary>
    private static string[] SelfUrls(int memberId, int paymentId) =>
    [
        $"/api/members/{memberId}",
        $"/api/members/{memberId}/history",
        $"/api/subscriptions/member/{memberId}",
        $"/api/Payments/member/{memberId}",
        $"/api/workouts/members/{memberId}/plans",
        $"/api/Payments/{paymentId}/receipt/pdf"
    ];

    // ------------------------------------------------------------------ allowed

    [Fact(DisplayName = "A member may read their own record, history, subscriptions, payments, plans and receipt")]
    public async Task Member_ReadingTheirOwnRecords_Is200()
    {
        using var admin = await Factory.CreateAuthenticatedClientAsync();
        var (member, paymentId) = await MemberWithAPaymentAsync(admin);

        using var client = await MemberClientAsync(member.Id);

        foreach (var url in SelfUrls(member.Id, paymentId))
        {
            var response = await client.GetAsync(url);

            response.StatusCode.Should().Be(HttpStatusCode.OK,
                $"a member should reach their own {url}");
        }
    }

    [Fact(DisplayName = "The member's own record really is their own row, not a blank envelope")]
    public async Task Member_ReadingTheirOwnRecord_GetsTheirOwnData()
    {
        using var admin = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(admin);

        using var client = await MemberClientAsync(member.Id);

        var detail = await ReadDataAsync<MemberDetailDto>(
            await client.GetAsync($"/api/members/{member.Id}"));

        detail.Id.Should().Be(member.Id);
        detail.FullName.Should().Be(member.FullName);
    }

    // ------------------------------------------------------------------- denied

    [Fact(DisplayName = "A member is refused every one of another member's records")]
    public async Task Member_ReadingSomebodyElsesRecords_Is403()
    {
        using var admin = await Factory.CreateAuthenticatedClientAsync();
        var (theirs, theirPaymentId) = await MemberWithAPaymentAsync(admin);
        var mine = await CreateMemberAsync(admin);

        using var client = await MemberClientAsync(mine.Id);

        foreach (var url in SelfUrls(theirs.Id, theirPaymentId))
        {
            var response = await client.GetAsync(url);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
                $"changing the id in the URL must not widen what a member sees: {url}");
        }
    }

    [Fact(DisplayName = "The refusal is a FORBIDDEN envelope, and leaks none of the other member's data")]
    public async Task Member_ReadingSomebodyElsesRecord_GetsAForbiddenEnvelope()
    {
        using var admin = await Factory.CreateAuthenticatedClientAsync();
        var theirs = await CreateMemberAsync(admin);
        var mine = await CreateMemberAsync(admin);

        using var client = await MemberClientAsync(mine.Id);

        var response = await client.GetAsync($"/api/members/{theirs.Id}");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var error = await ReadErrorAsync(response);
        error.ErrorCode.Should().Be("FORBIDDEN");

        body.Should().NotContain(theirs.FullName, "a refusal must not echo the record it withheld");
    }

    [Fact(DisplayName = "A receipt for somebody else's payment is refused, and so is one that does not exist")]
    public async Task Member_DownloadingSomebodyElsesReceipt_Is403()
    {
        using var admin = await Factory.CreateAuthenticatedClientAsync();
        var (_, theirPaymentId) = await MemberWithAPaymentAsync(admin);
        var mine = await CreateMemberAsync(admin);

        using var client = await MemberClientAsync(mine.Id);

        (await client.GetAsync($"/api/Payments/{theirPaymentId}/receipt/pdf"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // A payment id that does not exist is refused rather than reported, so a member cannot use
        // the 403/404 split to discover which payment ids are real.
        (await client.GetAsync("/api/Payments/2147483647/receipt/pdf"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "A login account with no linked member reaches no member's records at all")]
    public async Task UserWithoutALinkedMember_IsRefused()
    {
        using var admin = await Factory.CreateAuthenticatedClientAsync();
        var (member, paymentId) = await MemberWithAPaymentAsync(admin);

        // A Member-role account whose MemberId claim is absent: nothing is "their own".
        using var client = await MemberClientAsync(memberId: null);

        foreach (var url in SelfUrls(member.Id, paymentId))
        {
            (await client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden,
                $"an account with no linked member should reach nothing: {url}");
        }
    }

    // ----------------------------------------------------------- no staff regression

    [Fact(DisplayName = "An administrator still reaches every member-scoped read")]
    public async Task Admin_StillReachesEveryMemberScopedRead()
    {
        using var admin = await Factory.CreateAuthenticatedClientAsync();
        var (member, paymentId) = await MemberWithAPaymentAsync(admin);

        foreach (var url in SelfUrls(member.Id, paymentId))
        {
            (await admin.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK,
                $"an admin should still reach {url}");
        }
    }

    [Fact(DisplayName = "Staff still reach a member they are not linked to")]
    public async Task Staff_StillReachAnyMember()
    {
        using var admin = await Factory.CreateAuthenticatedClientAsync();
        var (member, paymentId) = await MemberWithAPaymentAsync(admin);

        var roleId = await Factory.FromDbAsync(db => db.Roles
            .AsNoTracking()
            .Where(r => r.Name == RoleNames.Staff)
            .Select(r => r.Id)
            .FirstAsync());

        var userName = $"s{Guid.NewGuid():N}"[..14];
        const string password = "SelfAccessPassw0rd!";

        await ReadDataAsync<TemporaryPasswordDto>(
            await admin.PostAsJsonAsync("/api/users", new CreateUserDto
            {
                UserName = userName,
                FullName = "Front Desk",
                Email = UniqueEmail(),
                Password = password,
                MustChangePassword = false,
                RoleIds = new List<int> { roleId },
                Status = UserStatus.Active
            }));

        using var staff = await Factory.CreateAuthenticatedClientAsync(userName, password);

        // Staff hold members.view, subscriptions.view and payments.view, so the self-access rule
        // short-circuits and their access is exactly what it was before the change.
        foreach (var url in new[]
                 {
                     $"/api/members/{member.Id}",
                     $"/api/members/{member.Id}/history",
                     $"/api/subscriptions/member/{member.Id}",
                     $"/api/Payments/member/{member.Id}",
                     $"/api/Payments/{paymentId}/receipt/pdf"
                 })
        {
            (await staff.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK,
                $"staff should still reach {url}");
        }
    }
}
