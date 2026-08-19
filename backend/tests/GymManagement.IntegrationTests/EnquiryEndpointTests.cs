using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Domain.Enums;
using Xunit;

namespace GymManagement.IntegrationTests;

/// <summary>
/// Sales enquiries / leads over HTTP — cases Q-01 to Q-10.
///
/// The interesting behaviour here is the conversion handshake: an enquiry becomes Converted only
/// by being linked to a real member, converting twice is refused, and a converted lead can never
/// be walked back to an earlier status.
/// </summary>
[Collection(GymApiCollection.Name)]
public class EnquiryEndpointTests : ApiTestBase
{
    public EnquiryEndpointTests(GymApiFixture fixture) : base(fixture) { }

    // ------------------------------------------------------------- helpers

    private static SaveEnquiryDto NewEnquiryDto(string? phone = null) => new()
    {
        FullName = UniqueName("Lead"),
        Phone = phone ?? UniquePhone(),
        Email = UniqueEmail(),
        Source = EnquirySource.WalkIn,
        Status = EnquiryStatus.New,
        Message = "Asked about the morning batch and the quarterly price.",
        FollowUpDate = DateTime.Today.AddDays(3)
    };

    private static async Task<EnquiryDetailDto> CreateEnquiryAsync(
        HttpClient client, SaveEnquiryDto? dto = null) =>
        await ReadDataAsync<EnquiryDetailDto>(
            await client.PostAsJsonAsync("/api/enquiries", dto ?? NewEnquiryDto()));

    // -------------------------------------------------------------- create

    [Fact(DisplayName = "Q-01 Recording an enquiry stores the lead and stamps the caller as owner")]
    public async Task Create_RecordsTheLead()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewEnquiryDto();

        var created = await CreateEnquiryAsync(client, dto);

        created.Id.Should().BeGreaterThan(0);
        created.FullName.Should().Be(dto.FullName);
        created.Phone.Should().Be(dto.Phone);
        created.Email.Should().Be(dto.Email);
        created.Source.Should().Be(EnquirySource.WalkIn);
        created.Status.Should().Be(EnquiryStatus.New);
        created.Message.Should().Be(dto.Message);
        created.FollowUpDate!.Value.Date.Should().Be(DateTime.Today.AddDays(3));
        created.ConvertedMemberId.Should().BeNull();
        created.IsDeleted.Should().BeFalse();

        // With no explicit assignee the lead falls to whoever recorded it.
        created.AssignedToUserId.Should().NotBeNull();
        created.AssignedToUserName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact(DisplayName = "Q-01 An enquiry can be linked to the plan the lead asked about")]
    public async Task Create_WithAnInterestedPlan_LinksIt()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var planId = await MonthlyPlanIdAsync(client);

        var dto = NewEnquiryDto();
        dto.InterestedPlanId = planId;

        var created = await CreateEnquiryAsync(client, dto);

        created.InterestedPlanId.Should().Be(planId);
        created.InterestedPlanName.Should().Be("Monthly");
    }

    [Fact(DisplayName = "Q-02 A blank name is a 400 on FullName")]
    public async Task Create_WithoutAName_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewEnquiryDto();
        dto.FullName = "   ";

        var response = await client.PostAsJsonAsync("/api/enquiries", dto);

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(SaveEnquiryDto.FullName));
    }

    [Fact(DisplayName = "Q-02 A blank phone number is a 400 on Phone")]
    public async Task Create_WithoutAPhone_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewEnquiryDto();
        dto.Phone = "";

        var response = await client.PostAsJsonAsync("/api/enquiries", dto);

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(SaveEnquiryDto.Phone));
    }

    [Fact(DisplayName = "Q-02 An email without an @ is a 400 on Email")]
    public async Task Create_WithAMalformedEmail_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewEnquiryDto();
        dto.Email = "not-an-email";

        var response = await client.PostAsJsonAsync("/api/enquiries", dto);

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(SaveEnquiryDto.Email));
    }

    [Fact(DisplayName = "Q-03 An unknown interested plan is a 404")]
    public async Task Create_WithAnUnknownPlan_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewEnquiryDto();
        dto.InterestedPlanId = 999999;

        await AssertFailureAsync(
            await client.PostAsJsonAsync("/api/enquiries", dto), HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact(DisplayName = "Q-03 An unknown assignee is a 404")]
    public async Task Create_WithAnUnknownAssignee_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewEnquiryDto();
        dto.AssignedToUserId = 999999;

        await AssertFailureAsync(
            await client.PostAsJsonAsync("/api/enquiries", dto), HttpStatusCode.NotFound, "NOT_FOUND");
    }

    // -------------------------------------------------------------- update

    [Fact(DisplayName = "Q-04 Updating an enquiry moves it along the pipeline")]
    public async Task Update_MovesTheLeadAlong()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var created = await CreateEnquiryAsync(client);

        var update = NewEnquiryDto(created.Phone);
        update.Id = created.Id;
        update.FullName = created.FullName;
        update.Status = EnquiryStatus.Contacted;
        update.FollowUpDate = DateTime.Today.AddDays(1);
        update.Notes = "Called back, wants a trial session.";

        var updated = await ReadDataAsync<EnquiryDetailDto>(
            await client.PutAsJsonAsync($"/api/enquiries/{created.Id}", update));

        updated.Id.Should().Be(created.Id);
        updated.Status.Should().Be(EnquiryStatus.Contacted);
        updated.Notes.Should().Be("Called back, wants a trial session.");
        updated.FollowUpDate!.Value.Date.Should().Be(DateTime.Today.AddDays(1));
        updated.UpdatedAt.Should().NotBeNull();
    }

    [Fact(DisplayName = "Q-04 Updating an unknown enquiry is a 404")]
    public async Task Update_AnUnknownEnquiry_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var update = NewEnquiryDto();
        update.Id = 999999;

        await AssertFailureAsync(
            await client.PutAsJsonAsync("/api/enquiries/999999", update),
            HttpStatusCode.NotFound, "NOT_FOUND");
    }

    // ------------------------------------------------------------- convert

    [Fact(DisplayName = "Q-05 Converting links the member and marks the lead Converted")]
    public async Task Convert_LinksTheMemberAndClosesTheLead()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var enquiry = await CreateEnquiryAsync(client);
        var member = await CreateMemberAsync(client);

        var converted = await ReadDataAsync<EnquiryDetailDto>(
            await client.PostAsync($"/api/enquiries/{enquiry.Id}/convert/{member.Id}", null));

        converted.Id.Should().Be(enquiry.Id);
        converted.Status.Should().Be(EnquiryStatus.Converted);
        converted.ConvertedMemberId.Should().Be(member.Id);
        converted.ConvertedMemberName.Should().Be(member.FullName);
        converted.FollowUpDate.Should().BeNull("a converted lead needs no further follow-up");
    }

    [Fact(DisplayName = "Q-06 Converting the same enquiry twice is a 409")]
    public async Task Convert_Twice_IsAConflict()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var enquiry = await CreateEnquiryAsync(client);
        var first = await CreateMemberAsync(client);
        var second = await CreateMemberAsync(client);

        await ReadDataAsync<EnquiryDetailDto>(
            await client.PostAsync($"/api/enquiries/{enquiry.Id}/convert/{first.Id}", null));

        var response = await client.PostAsync($"/api/enquiries/{enquiry.Id}/convert/{second.Id}", null);

        await AssertFailureAsync(response, HttpStatusCode.Conflict, "CONFLICT");
        (await ReadErrorAsync(response)).Message.Should().Contain("already converted");
    }

    [Fact(DisplayName = "Q-06 Converting to an unknown member is a 404 and changes nothing")]
    public async Task Convert_ToAnUnknownMember_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var enquiry = await CreateEnquiryAsync(client);

        await AssertFailureAsync(
            await client.PostAsync($"/api/enquiries/{enquiry.Id}/convert/999999", null),
            HttpStatusCode.NotFound, "NOT_FOUND");

        var reloaded = await ReadDataAsync<EnquiryDetailDto>(
            await client.GetAsync($"/api/enquiries/{enquiry.Id}"));
        reloaded.Status.Should().Be(EnquiryStatus.New);
        reloaded.ConvertedMemberId.Should().BeNull();
    }

    [Fact(DisplayName = "Q-07 A converted enquiry cannot be moved back to an earlier status")]
    public async Task Update_AConvertedEnquiryBackwards_Is422()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var enquiry = await CreateEnquiryAsync(client);
        var member = await CreateMemberAsync(client);

        await ReadDataAsync<EnquiryDetailDto>(
            await client.PostAsync($"/api/enquiries/{enquiry.Id}/convert/{member.Id}", null));

        var update = NewEnquiryDto(enquiry.Phone);
        update.Id = enquiry.Id;
        update.FullName = enquiry.FullName;
        update.Status = EnquiryStatus.FollowUp;

        var response = await client.PutAsJsonAsync($"/api/enquiries/{enquiry.Id}", update);

        await AssertFailureAsync(response, HttpStatusCode.UnprocessableEntity, "BUSINESS_RULE");
        (await ReadErrorAsync(response)).Message.Should().Contain("cannot be moved back");
    }

    // --------------------------------------------------------------- reads

    [Fact(DisplayName = "Q-08 The status filter only returns leads in that status")]
    public async Task GetPaged_FilteredByStatus_OnlyReturnsThatStatus()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var dto = NewEnquiryDto();
        dto.Status = EnquiryStatus.Lost;
        var lost = await CreateEnquiryAsync(client, dto);

        var page = await ReadDataAsync<PagedResult<EnquiryListDto>>(
            await client.GetAsync($"/api/enquiries?Status={(int)EnquiryStatus.Lost}&PageSize=200"));

        page.Items.Should().Contain(e => e.Id == lost.Id);
        page.Items.Should().OnlyContain(e => e.Status == EnquiryStatus.Lost);
    }

    [Fact(DisplayName = "Q-08 The source filter only returns leads from that source")]
    public async Task GetPaged_FilteredBySource_OnlyReturnsThatSource()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var dto = NewEnquiryDto();
        dto.Source = EnquirySource.SocialMedia;
        var social = await CreateEnquiryAsync(client, dto);

        var page = await ReadDataAsync<PagedResult<EnquiryListDto>>(
            await client.GetAsync($"/api/enquiries?Source={(int)EnquirySource.SocialMedia}&PageSize=200"));

        page.Items.Should().Contain(e => e.Id == social.Id);
        page.Items.Should().OnlyContain(e => e.Source == EnquirySource.SocialMedia);
    }

    [Fact(DisplayName = "Q-09 The follow-up-due filter returns leads whose date has arrived, and no closed ones")]
    public async Task GetPaged_FollowUpDue_ReturnsOnlyOpenOverdueLeads()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var dueDto = NewEnquiryDto();
        dueDto.FollowUpDate = DateTime.Today.AddDays(-1);
        dueDto.Status = EnquiryStatus.Contacted;
        var due = await CreateEnquiryAsync(client, dueDto);

        var laterDto = NewEnquiryDto();
        laterDto.FollowUpDate = DateTime.Today.AddDays(10);
        var later = await CreateEnquiryAsync(client, laterDto);

        var lostDto = NewEnquiryDto();
        lostDto.FollowUpDate = DateTime.Today.AddDays(-1);
        lostDto.Status = EnquiryStatus.Lost;
        var lost = await CreateEnquiryAsync(client, lostDto);

        var page = await ReadDataAsync<PagedResult<EnquiryListDto>>(
            await client.GetAsync("/api/enquiries?FollowUpDue=true&PageSize=200"));

        page.Items.Should().Contain(e => e.Id == due.Id);
        page.Items.Should().NotContain(e => e.Id == later.Id, "its follow-up date has not arrived");
        page.Items.Should().NotContain(e => e.Id == lost.Id, "a lost lead is not chased");
        page.Items.Should().OnlyContain(e =>
            e.Status != EnquiryStatus.Converted && e.Status != EnquiryStatus.Lost);
    }

    [Fact(DisplayName = "Q-09 Searching by phone finds the lead")]
    public async Task GetPaged_SearchedByPhone_FindsTheLead()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewEnquiryDto();
        var created = await CreateEnquiryAsync(client, dto);

        var page = await ReadDataAsync<PagedResult<EnquiryListDto>>(
            await client.GetAsync($"/api/enquiries?Search={dto.Phone}"));

        page.Items.Should().ContainSingle().Which.Id.Should().Be(created.Id);
    }

    [Fact(DisplayName = "Q-09 The seeded sample leads are listed")]
    public async Task GetPaged_ListsTheSeededLeads()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var page = await ReadDataAsync<PagedResult<EnquiryListDto>>(
            await client.GetAsync("/api/enquiries?PageSize=200"));

        page.TotalCount.Should().BeGreaterThan(0);
        page.Items.Should().Contain(e => e.Phone == "9800000001");
        page.Items.Should().OnlyContain(e => !e.IsDeleted);
    }

    [Fact(DisplayName = "Q-10 Reading an unknown enquiry is a 404")]
    public async Task GetById_ForAnUnknownEnquiry_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        await AssertFailureAsync(
            await client.GetAsync("/api/enquiries/999999"), HttpStatusCode.NotFound, "NOT_FOUND");
    }

    // ------------------------------------------------- delete / restore

    [Fact(DisplayName = "Q-10 A deleted enquiry disappears from the list and can be restored")]
    public async Task DeleteThenRestore_RoundTrips()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewEnquiryDto();
        var created = await CreateEnquiryAsync(client, dto);

        (await client.DeleteAsync($"/api/enquiries/{created.Id}")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var afterDelete = await ReadDataAsync<PagedResult<EnquiryListDto>>(
            await client.GetAsync($"/api/enquiries?Search={dto.Phone}"));
        afterDelete.TotalCount.Should().Be(0);

        // IncludeDeleted still finds it, and the row is flagged rather than gone.
        var includingDeleted = await ReadDataAsync<PagedResult<EnquiryListDto>>(
            await client.GetAsync($"/api/enquiries?Search={dto.Phone}&IncludeDeleted=true"));
        includingDeleted.Items.Should().ContainSingle().Which.IsDeleted.Should().BeTrue();

        (await client.PostAsync($"/api/enquiries/{created.Id}/restore", null)).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var afterRestore = await ReadDataAsync<PagedResult<EnquiryListDto>>(
            await client.GetAsync($"/api/enquiries?Search={dto.Phone}"));
        afterRestore.Items.Should().ContainSingle().Which.Id.Should().Be(created.Id);
    }

    [Fact(DisplayName = "Q-10 Deleting an unknown enquiry is a 404")]
    public async Task Delete_AnUnknownEnquiry_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        await AssertFailureAsync(
            await client.DeleteAsync("/api/enquiries/999999"), HttpStatusCode.NotFound, "NOT_FOUND");
    }
}
