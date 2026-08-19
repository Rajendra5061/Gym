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
/// Member feedback over HTTP — cases F-01 to F-11.
///
/// Feedback is the one module where the scoping rule lives in the service rather than in a
/// permission attribute: staff holding <c>feedback.view</c> see every row, while a member account
/// is silently restricted to their own. Both halves of that rule are covered here with a real
/// Member-role login rather than a stub.
/// </summary>
[Collection(GymApiCollection.Name)]
public class FeedbackEndpointTests : ApiTestBase
{
    public FeedbackEndpointTests(GymApiFixture fixture) : base(fixture) { }

    // ------------------------------------------------------------- helpers

    /// <summary>Registers a member, gives them a Member-role login and returns a client for it.</summary>
    private async Task<(HttpClient Client, MemberDetailDto Member)> MemberClientAsync()
    {
        using var admin = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(admin);

        var roleId = await Factory.FromDbAsync(db =>
            db.Roles.AsNoTracking().Where(r => r.Name == RoleNames.Member).Select(r => r.Id).FirstAsync());

        var userName = $"m{Guid.NewGuid():N}"[..14];
        const string password = "MemberPassw0rd!";

        await ReadDataAsync<TemporaryPasswordDto>(
            await admin.PostAsJsonAsync("/api/users", new CreateUserDto
            {
                UserName = userName,
                FullName = member.FullName,
                Email = UniqueEmail(),
                Password = password,
                MustChangePassword = false,
                RoleIds = new List<int> { roleId },
                MemberId = member.Id,
                Status = UserStatus.Active
            }));

        return (await Factory.CreateAuthenticatedClientAsync(userName, password), member);
    }

    /// <summary>Files feedback on behalf of a member, which staff holding feedback.manage may do.</summary>
    private static async Task<FeedbackDetailDto> SubmitForAsync(
        HttpClient client, int memberId, string? subject = null, int? rating = 4) =>
        await ReadDataAsync<FeedbackDetailDto>(
            await client.PostAsJsonAsync("/api/feedback", new CreateFeedbackDto
            {
                MemberId = memberId,
                Subject = subject ?? "Air conditioning",
                Message = "The cardio floor gets very warm in the evening slot.",
                Rating = rating,
                IsPrivate = true
            }));

    // -------------------------------------------------------------- submit

    [Fact(DisplayName = "F-01 A member submits their own feedback and it opens as New")]
    public async Task Submit_AsAMember_RecordsTheirOwnFeedback()
    {
        var (client, member) = await MemberClientAsync();
        using var _client = client;

        var feedback = await ReadDataAsync<FeedbackDetailDto>(
            await client.PostAsJsonAsync("/api/feedback", new CreateFeedbackDto
            {
                Subject = "Great trainers",
                Message = "The evening trainer is very helpful.",
                Rating = 5
            }));

        feedback.Id.Should().BeGreaterThan(0);
        feedback.MemberId.Should().Be(member.Id, "a member always posts against their own record");
        feedback.MemberCode.Should().Be(member.MemberCode);
        feedback.Subject.Should().Be("Great trainers");
        feedback.Rating.Should().Be(5);
        feedback.Status.Should().Be(FeedbackStatus.New);
        feedback.HasResponse.Should().BeFalse();
        feedback.AdminResponse.Should().BeNull();
    }

    [Fact(DisplayName = "F-01 A member cannot post feedback against somebody else's record")]
    public async Task Submit_AsAMember_IgnoresAForeignMemberId()
    {
        using var admin = await Factory.CreateAuthenticatedClientAsync();
        var someoneElse = await CreateMemberAsync(admin);

        var (client, member) = await MemberClientAsync();
        using var _client = client;

        var feedback = await ReadDataAsync<FeedbackDetailDto>(
            await client.PostAsJsonAsync("/api/feedback", new CreateFeedbackDto
            {
                MemberId = someoneElse.Id, // must be ignored
                Subject = "Attempted impersonation",
                Message = "This should be filed against the caller, not the named member.",
                Rating = 3
            }));

        feedback.MemberId.Should().Be(member.Id);
        feedback.MemberId.Should().NotBe(someoneElse.Id);
    }

    [Fact(DisplayName = "F-02 Staff holding feedback.manage may file on behalf of a member")]
    public async Task Submit_AsStaffOnBehalf_RecordsAgainstThatMember()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);

        var feedback = await SubmitForAsync(client, member.Id);

        feedback.MemberId.Should().Be(member.Id);
        feedback.Status.Should().Be(FeedbackStatus.New);
    }

    [Fact(DisplayName = "F-02 Filing on behalf of nobody is refused")]
    public async Task Submit_AsStaffWithoutAMember_IsRefused()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/feedback", new CreateFeedbackDto
        {
            Subject = "No member named",
            Message = "An administrator is not a member, so this cannot be filed."
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadErrorAsync(response)).ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact(DisplayName = "F-03 A blank message is a 400 on Message")]
    public async Task Submit_WithoutAMessage_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);

        var response = await client.PostAsJsonAsync("/api/feedback", new CreateFeedbackDto
        {
            MemberId = member.Id,
            Message = "   "
        });

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(CreateFeedbackDto.Message));
    }

    [Theory(DisplayName = "F-03 A rating outside 1-5 is a 400")]
    [InlineData(0)]
    [InlineData(6)]
    public async Task Submit_WithAnOutOfRangeRating_Is400(int rating)
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);

        var response = await client.PostAsJsonAsync("/api/feedback", new CreateFeedbackDto
        {
            MemberId = member.Id,
            Message = "A rating outside the allowed band.",
            Rating = rating
        });

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact(DisplayName = "F-03 Feedback with no rating at all is accepted")]
    public async Task Submit_WithoutARating_IsAccepted()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);

        var feedback = await SubmitForAsync(client, member.Id, rating: null);

        feedback.Rating.Should().BeNull();
    }

    [Fact(DisplayName = "F-03 Filing against an unknown member is a 404")]
    public async Task Submit_ForAnUnknownMember_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/feedback", new CreateFeedbackDto
        {
            MemberId = 999999,
            Message = "Filed against a member who does not exist."
        });

        await AssertFailureAsync(response, HttpStatusCode.NotFound, "NOT_FOUND");
    }

    // ------------------------------------------------------------- respond

    [Fact(DisplayName = "F-04 Responding records the reply and moves the item to Reviewed")]
    public async Task Respond_RecordsTheReplyAndReviews()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);
        var feedback = await SubmitForAsync(client, member.Id);

        var answered = await ReadDataAsync<FeedbackDetailDto>(
            await client.PostAsJsonAsync($"/api/feedback/{feedback.Id}/respond",
                new { Response = "Two extra fans were installed this week." }));

        answered.Id.Should().Be(feedback.Id);
        answered.Status.Should().Be(FeedbackStatus.Reviewed);
        answered.AdminResponse.Should().Be("Two extra fans were installed this week.");
        answered.HasResponse.Should().BeTrue();
        answered.RespondedAt.Should().NotBeNull();
        answered.RespondedByUserId.Should().NotBeNull();
        answered.RespondedByUserName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact(DisplayName = "F-04 A blank response is a 400")]
    public async Task Respond_WithABlankReply_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);
        var feedback = await SubmitForAsync(client, member.Id);

        var response = await client.PostAsJsonAsync($"/api/feedback/{feedback.Id}/respond",
            new { Response = "   " });

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact(DisplayName = "F-04 Responding to an unknown item is a 404")]
    public async Task Respond_ToAnUnknownItem_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        await AssertFailureAsync(
            await client.PostAsJsonAsync("/api/feedback/999999/respond", new { Response = "Nobody home." }),
            HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact(DisplayName = "F-05 A response on an already resolved item does not drag it back to Reviewed")]
    public async Task Respond_OnAResolvedItem_KeepsItsStatus()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);
        var feedback = await SubmitForAsync(client, member.Id);

        var resolved = await ReadDataAsync<FeedbackDetailDto>(
            await client.PutAsJsonAsync($"/api/feedback/{feedback.Id}", new SaveFeedbackDto
            {
                Id = feedback.Id,
                MemberId = member.Id,
                Subject = feedback.Subject,
                Message = feedback.Message,
                Rating = feedback.Rating,
                Status = FeedbackStatus.Resolved,
                IsPrivate = feedback.IsPrivate
            }));
        resolved.Status.Should().Be(FeedbackStatus.Resolved);

        var answered = await ReadDataAsync<FeedbackDetailDto>(
            await client.PostAsJsonAsync($"/api/feedback/{feedback.Id}/respond",
                new { Response = "Adding a note after the fact." }));

        answered.Status.Should().Be(FeedbackStatus.Resolved, "only a New item is promoted to Reviewed");
        answered.AdminResponse.Should().Be("Adding a note after the fact.");
    }

    // --------------------------------------------------------------- reads

    [Fact(DisplayName = "F-06 Staff see every member's feedback")]
    public async Task GetPaged_AsStaff_SeesEveryMember()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var first = await CreateMemberAsync(client);
        var second = await CreateMemberAsync(client);

        var a = await SubmitForAsync(client, first.Id);
        var b = await SubmitForAsync(client, second.Id);

        var page = await ReadDataAsync<PagedResult<FeedbackListDto>>(
            await client.GetAsync("/api/feedback?PageSize=200"));

        page.Items.Should().Contain(f => f.Id == a.Id);
        page.Items.Should().Contain(f => f.Id == b.Id);
    }

    [Fact(DisplayName = "F-06 The member filter narrows the list to one member")]
    public async Task GetPaged_FilteredByMember_NarrowsTheList()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);
        var feedback = await SubmitForAsync(client, member.Id);

        var page = await ReadDataAsync<PagedResult<FeedbackListDto>>(
            await client.GetAsync($"/api/feedback?MemberId={member.Id}&PageSize=200"));

        page.Items.Should().ContainSingle().Which.Id.Should().Be(feedback.Id);
        page.Items.Should().OnlyContain(f => f.MemberId == member.Id);
    }

    [Fact(DisplayName = "F-07 The rating band filter only returns feedback inside it")]
    public async Task GetPaged_FilteredByRating_OnlyReturnsThatBand()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);

        var unhappy = await SubmitForAsync(client, member.Id, "Too crowded", rating: 1);

        var page = await ReadDataAsync<PagedResult<FeedbackListDto>>(
            await client.GetAsync("/api/feedback?MinRating=1&MaxRating=2&PageSize=200"));

        page.Items.Should().Contain(f => f.Id == unhappy.Id);
        page.Items.Should().OnlyContain(f => f.Rating >= 1 && f.Rating <= 2);
    }

    [Fact(DisplayName = "F-08 The staff feedback list is closed to a member: it needs feedback.view")]
    public async Task GetPaged_AsAMember_IsRefused()
    {
        var (client, _) = await MemberClientAsync();
        using var _client = client;

        // The Member role holds feedback.submit but not feedback.view, so the permission filter
        // stops the request before it reaches the service. Members read their own through `mine`.
        (await client.GetAsync("/api/feedback?PageSize=200")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "F-08 GET mine returns the member's own feedback and nobody else's")]
    public async Task GetMine_IsScopedToTheSignedInMember()
    {
        using var admin = await Factory.CreateAuthenticatedClientAsync();
        var stranger = await CreateMemberAsync(admin);
        var strangersFeedback = await SubmitForAsync(admin, stranger.Id);

        var (client, member) = await MemberClientAsync();
        using var _client = client;

        var mine = await ReadDataAsync<FeedbackDetailDto>(
            await client.PostAsJsonAsync("/api/feedback", new CreateFeedbackDto
            {
                Subject = "Locker room",
                Message = "Please add another bench in the locker room.",
                Rating = 4
            }));

        var page = await ReadDataAsync<PagedResult<FeedbackListDto>>(
            await client.GetAsync("/api/feedback/mine"));

        page.Items.Should().ContainSingle().Which.Id.Should().Be(mine.Id);
        page.Items.Should().OnlyContain(f => f.MemberId == member.Id);
        page.Items.Should().NotContain(f => f.Id == strangersFeedback.Id);
    }

    [Fact(DisplayName = "F-08 GET mine from an account with no member record is refused")]
    public async Task GetMine_AsStaff_IsRefused()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/feedback/mine");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadErrorAsync(response)).ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact(DisplayName = "F-09 A member is refused somebody else's feedback by id")]
    public async Task GetById_AsAMember_CannotReadSomebodyElses()
    {
        using var admin = await Factory.CreateAuthenticatedClientAsync();
        var stranger = await CreateMemberAsync(admin);
        var strangersFeedback = await SubmitForAsync(admin, stranger.Id);

        var (client, _) = await MemberClientAsync();
        using var _client = client;

        // Refused by the permission filter, which returns a bare 403 with no envelope body.
        (await client.GetAsync($"/api/feedback/{strangersFeedback.Id}")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "F-09 GET by id is a staff screen: a member is refused even their own row")]
    public async Task GetById_AsAMember_IsRefusedEvenForTheirOwn()
    {
        var (client, _) = await MemberClientAsync();
        using var _client = client;

        var mine = await ReadDataAsync<FeedbackDetailDto>(
            await client.PostAsJsonAsync("/api/feedback", new CreateFeedbackDto
            {
                Subject = "Readable by me",
                Message = "Submitted by me, but read back through `mine` rather than by id.",
                Rating = 5
            }));

        // GET {id} carries [HasPermission(feedback.view)], which the Member role does not hold, so
        // the request never reaches the ownership check inside FeedbackService.GetByIdAsync.
        (await client.GetAsync($"/api/feedback/{mine.Id}")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        // The member's own route does return it.
        var page = await ReadDataAsync<PagedResult<FeedbackListDto>>(
            await client.GetAsync("/api/feedback/mine"));
        page.Items.Should().ContainSingle().Which.Id.Should().Be(mine.Id);
    }

    [Fact(DisplayName = "F-09 Staff holding feedback.view may read any member's feedback by id")]
    public async Task GetById_AsStaff_ReadsAnyMembersFeedback()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);
        var feedback = await SubmitForAsync(client, member.Id);

        var read = await ReadDataAsync<FeedbackDetailDto>(
            await client.GetAsync($"/api/feedback/{feedback.Id}"));

        read.Id.Should().Be(feedback.Id);
        read.MemberId.Should().Be(member.Id);
    }

    [Fact(DisplayName = "F-09 A member may not reach the back-office write endpoints")]
    public async Task Member_CannotUseTheBackOfficeEndpoints()
    {
        using var admin = await Factory.CreateAuthenticatedClientAsync();
        var target = await CreateMemberAsync(admin);
        var feedback = await SubmitForAsync(admin, target.Id);

        var (client, _) = await MemberClientAsync();
        using var _client = client;

        (await client.PostAsJsonAsync($"/api/feedback/{feedback.Id}/respond", new { Response = "Not allowed." }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await client.DeleteAsync($"/api/feedback/{feedback.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "F-10 Reading an unknown feedback item is a 404")]
    public async Task GetById_ForAnUnknownItem_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        await AssertFailureAsync(
            await client.GetAsync("/api/feedback/999999"), HttpStatusCode.NotFound, "NOT_FOUND");
    }

    // ------------------------------------------------- delete / restore

    [Fact(DisplayName = "F-11 A deleted item disappears from the list and can be restored")]
    public async Task DeleteThenRestore_RoundTrips()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);
        var feedback = await SubmitForAsync(client, member.Id);

        (await client.DeleteAsync($"/api/feedback/{feedback.Id}")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var afterDelete = await ReadDataAsync<PagedResult<FeedbackListDto>>(
            await client.GetAsync($"/api/feedback?MemberId={member.Id}&PageSize=200"));
        afterDelete.TotalCount.Should().Be(0);

        var includingDeleted = await ReadDataAsync<PagedResult<FeedbackListDto>>(
            await client.GetAsync($"/api/feedback?MemberId={member.Id}&IncludeDeleted=true&PageSize=200"));
        includingDeleted.Items.Should().ContainSingle().Which.IsDeleted.Should().BeTrue();

        (await client.PostAsync($"/api/feedback/{feedback.Id}/restore", null)).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var afterRestore = await ReadDataAsync<PagedResult<FeedbackListDto>>(
            await client.GetAsync($"/api/feedback?MemberId={member.Id}&PageSize=200"));
        afterRestore.Items.Should().ContainSingle().Which.Id.Should().Be(feedback.Id);
    }

    [Fact(DisplayName = "F-11 Deleting an unknown feedback item is a 404")]
    public async Task Delete_AnUnknownItem_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        await AssertFailureAsync(
            await client.DeleteAsync("/api/feedback/999999"), HttpStatusCode.NotFound, "NOT_FOUND");
    }
}
