using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Domain.Enums;
using Xunit;

namespace GymManagement.IntegrationTests;

/// <summary>Member endpoints — cases M-01, M-02, M-05, M-08 and M-11.</summary>
[Collection(GymApiCollection.Name)]
public class MemberEndpointTests : ApiTestBase
{
    public MemberEndpointTests(GymApiFixture fixture) : base(fixture) { }

    // -------------------------------------------------------------- create

    [Fact(DisplayName = "M-01 Creating a member returns a generated member code")]
    public async Task Create_ReturnsAGeneratedMemberCode()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewMemberDto();

        var response = await client.PostAsJsonAsync("/api/members", dto);
        var result = await ReadDataAsync<CreateMemberResponse>(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Member.Id.Should().BeGreaterThan(0);
        result.Member.MemberCode.Should().NotBeNullOrWhiteSpace();
        result.Member.MemberCode.Should().MatchRegex(@"^GYM-\d{4}-\d{4}$");
        result.Member.FullName.Should().Be(dto.FullName);
        result.Member.Status.Should().Be(MemberStatus.Active);
        result.Account.Should().BeNull();
    }

    [Fact(DisplayName = "M-01 Two members never receive the same code")]
    public async Task Create_ProducesUniqueCodes()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var first = await CreateMemberAsync(client);
        var second = await CreateMemberAsync(client);

        second.MemberCode.Should().NotBe(first.MemberCode);
    }

    [Fact(DisplayName = "M-03 Asking for a login account returns a temporary password")]
    public async Task Create_WithALoginAccount_ReturnsATemporaryPassword()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewMemberDto();
        dto.CreateUserAccount = true;

        var result = await ReadDataAsync<CreateMemberResponse>(
            await client.PostAsJsonAsync("/api/members", dto));

        result.Account.Should().NotBeNull();
        result.Account!.TemporaryPassword.Should().NotBeNullOrWhiteSpace();
        result.Account.UserName.Should().Be(result.Member.MemberCode);
    }

    [Fact(DisplayName = "M-02 A second member with the same phone number is a 409 conflict")]
    public async Task Create_WithADuplicatePhone_Is409()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var phone = UniquePhone();

        await CreateMemberAsync(client, NewMemberDto(phone: phone));

        var response = await client.PostAsJsonAsync("/api/members", NewMemberDto(phone: phone));

        await AssertFailureAsync(response, HttpStatusCode.Conflict, "CONFLICT");
        (await ReadErrorAsync(response)).Message.Should().Contain(phone);
    }

    [Fact(DisplayName = "M-05 A future date of birth is a 400 with a validation error on DateOfBirth")]
    public async Task Create_WithAFutureDateOfBirth_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewMemberDto();
        dto.DateOfBirth = DateTime.Today.AddDays(1);

        var response = await client.PostAsJsonAsync("/api/members", dto);

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var error = await ReadErrorAsync(response);
        error.ValidationErrors.Should().ContainKey(nameof(CreateMemberDto.DateOfBirth));
        error.ValidationErrors![nameof(CreateMemberDto.DateOfBirth)].Should().NotBeEmpty();
    }

    [Fact(DisplayName = "M-05 An under-age member is a 400 on DateOfBirth")]
    public async Task Create_WithAnUnderFiveMember_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewMemberDto();
        dto.DateOfBirth = DateTime.Today.AddYears(-3);

        var response = await client.PostAsJsonAsync("/api/members", dto);

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(CreateMemberDto.DateOfBirth));
    }

    [Fact(DisplayName = "M-04 Requesting a login account with no email is a 400 on Email")]
    public async Task Create_WithAnAccountButNoEmail_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewMemberDto();
        dto.Email = null;
        dto.CreateUserAccount = true;

        var response = await client.PostAsJsonAsync("/api/members", dto);

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors.Should().ContainKey("Email");
    }

    // -------------------------------------------------------------- delete

    [Fact(DisplayName = "M-08 Deleting a member with an active subscription is a 422")]
    public async Task Delete_WithAnActiveSubscription_Is422()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, subscription) = await SellSubscriptionAsync(client);

        subscription.Status.Should().Be(SubscriptionStatus.Active);

        var response = await client.DeleteAsync($"/api/members/{member.Id}");

        await AssertFailureAsync(response, HttpStatusCode.UnprocessableEntity, "BUSINESS_RULE");
        (await ReadErrorAsync(response)).Message.Should().Contain("active subscription");
    }

    [Fact(DisplayName = "M-09 Deleting a member with nothing outstanding succeeds and hides the row")]
    public async Task Delete_WithNothingOutstanding_Succeeds()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);

        var delete = await client.DeleteAsync($"/api/members/{member.Id}");
        delete.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await ReadDataAsync<PagedResult<MemberListDto>>(
            await client.GetAsync($"/api/members?Search={member.MemberCode}"));
        page.TotalCount.Should().Be(0);
    }

    [Fact(DisplayName = "M-10 A deleted member can be restored through the members endpoint")]
    public async Task Restore_BringsTheMemberBack()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);

        await client.DeleteAsync($"/api/members/{member.Id}");

        var restore = await client.PostAsync($"/api/members/{member.Id}/restore", null);
        restore.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await ReadDataAsync<PagedResult<MemberListDto>>(
            await client.GetAsync($"/api/members?Search={member.MemberCode}"));
        page.TotalCount.Should().Be(1);
    }

    // -------------------------------------------------------------- search

    [Fact(DisplayName = "M-11 The search term narrows the list to the matching member")]
    public async Task Search_NarrowsTheList()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);

        var byCode = await ReadDataAsync<PagedResult<MemberListDto>>(
            await client.GetAsync($"/api/members?Search={member.MemberCode}"));
        byCode.TotalCount.Should().Be(1);
        byCode.Items.Single().Id.Should().Be(member.Id);

        var byPhone = await ReadDataAsync<PagedResult<MemberListDto>>(
            await client.GetAsync($"/api/members?Search={member.Phone}"));
        byPhone.Items.Should().Contain(i => i.Id == member.Id);
    }

    [Fact(DisplayName = "M-11 The status filter only returns members in that status")]
    public async Task StatusFilter_OnlyReturnsThatStatus()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);

        var suspend = await client.PostAsJsonAsync(
            $"/api/members/{member.Id}/status?reason=Testing", MemberStatus.Suspended);
        suspend.StatusCode.Should().Be(HttpStatusCode.OK);

        var suspended = await ReadDataAsync<PagedResult<MemberListDto>>(
            await client.GetAsync($"/api/members?Status={(int)MemberStatus.Suspended}&PageSize=200"));

        suspended.Items.Should().Contain(i => i.Id == member.Id);
        suspended.Items.Should().OnlyContain(i => i.Status == MemberStatus.Suspended);

        var active = await ReadDataAsync<PagedResult<MemberListDto>>(
            await client.GetAsync($"/api/members?Status={(int)MemberStatus.Active}&PageSize=200"));
        active.Items.Should().NotContain(i => i.Id == member.Id);
    }

    [Fact(DisplayName = "M-11 A search that matches nothing returns an empty page")]
    public async Task Search_WithNoMatches_ReturnsAnEmptyPage()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var page = await ReadDataAsync<PagedResult<MemberListDto>>(
            await client.GetAsync($"/api/members?Search=no-such-member-{Guid.NewGuid():N}"));

        page.TotalCount.Should().Be(0);
        page.Items.Should().BeEmpty();
    }

    // ---------------------------------------------------------- by-code / history

    [Fact(DisplayName = "GET by-code/{code} resolves the same member")]
    public async Task GetByCode_ResolvesTheMember()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);

        var found = await ReadDataAsync<MemberDetailDto>(
            await client.GetAsync($"/api/members/by-code/{member.MemberCode}"));

        found.Id.Should().Be(member.Id);
        found.MemberCode.Should().Be(member.MemberCode);
    }

    [Fact(DisplayName = "GET by-code with an unknown code succeeds with no data")]
    public async Task GetByCode_WithAnUnknownCode_ReturnsNoData()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/members/by-code/GYM-1900-0001");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await ReadEnvelopeAsync<MemberDetailDto>(response);
        envelope.Success.Should().BeTrue();
        envelope.Data.Should().BeNull();
    }

    [Fact(DisplayName = "GET {id}/history returns the member's subscriptions and payments")]
    public async Task GetHistory_ReturnsTheSubscriptionsAndPayments()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, subscription) = await SellSubscriptionAsync(client);

        var history = await ReadDataAsync<MemberHistoryDto>(
            await client.GetAsync($"/api/members/{member.Id}/history"));

        history.Member.Id.Should().Be(member.Id);
        history.Subscriptions.Should().ContainSingle().Which.Id.Should().Be(subscription.Id);
        history.Payments.Should().ContainSingle();
        history.Payments.Single().FinalAmount.Should().Be(subscription.FinalAmount);
    }

    [Fact(DisplayName = "GET {id}/history for an unknown member is a 404")]
    public async Task GetHistory_ForAnUnknownMember_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/members/999999/history");

        await AssertFailureAsync(response, HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact(DisplayName = "M-06 Updating a member persists the change")]
    public async Task Update_PersistsTheChange()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);

        var update = new UpdateMemberDto
        {
            Id = member.Id,
            FullName = member.FullName,
            Gender = member.Gender,
            Phone = member.Phone,
            Email = member.Email,
            Address = "42 New Street",
            City = "Bengaluru",
            JoiningDate = member.JoiningDate,
            Status = MemberStatus.Active
        };

        var updated = await ReadDataAsync<MemberDetailDto>(
            await client.PutAsJsonAsync($"/api/members/{member.Id}", update));

        updated.Address.Should().Be("42 New Street");
        updated.City.Should().Be("Bengaluru");
    }
}
