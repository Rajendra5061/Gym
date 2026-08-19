using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Domain.Enums;
using Xunit;

namespace GymManagement.IntegrationTests;

/// <summary>
/// Gym floor check-in / check-out over HTTP — cases N-01 to N-12.
///
/// Every case registers its own member, because the front-desk rule is one attendance row per
/// member per calendar day: sharing a member between cases would make them order dependent.
/// Reads that see the whole gym (the register, the summary, in-gym) are asserted by containment
/// rather than by exact counts, since the suite shares one host and one database.
/// </summary>
[Collection(GymApiCollection.Name)]
public class AttendanceEndpointTests : ApiTestBase
{
    public AttendanceEndpointTests(GymApiFixture fixture) : base(fixture) { }

    // ------------------------------------------------------------- helpers

    private static Task<HttpResponseMessage> CheckInAsync(
        HttpClient client, int? memberId = null, string? memberCode = null,
        string method = "Manual", bool overrideExpired = false, string? notes = null) =>
        client.PostAsJsonAsync("/api/Attendance/check-in", new CheckInRequestDto
        {
            MemberId = memberId,
            MemberCode = memberCode,
            CheckInMethod = method,
            OverrideExpiredMembership = overrideExpired,
            Notes = notes
        });

    private static Task<HttpResponseMessage> CheckOutAsync(
        HttpClient client, int? attendanceId = null, int? memberId = null, string? notes = null) =>
        client.PostAsJsonAsync("/api/Attendance/check-out", new CheckOutRequestDto
        {
            AttendanceId = attendanceId,
            MemberId = memberId,
            Notes = notes
        });

    /// <summary>A member holding a Monthly subscription that is valid today.</summary>
    private async Task<MemberDetailDto> MemberWithActiveMembershipAsync(HttpClient client)
    {
        var (member, _) = await SellSubscriptionAsync(client);
        return member;
    }

    // ------------------------------------------------------------ check in

    [Fact(DisplayName = "N-01 A check-in is recorded and linked to the membership that covers today")]
    public async Task CheckIn_WithAnActiveMembership_RecordsTheVisit()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, subscription) = await SellSubscriptionAsync(client);

        var attendance = await ReadDataAsync<AttendanceDto>(await CheckInAsync(client, member.Id));

        attendance.Id.Should().BeGreaterThan(0);
        attendance.MemberId.Should().Be(member.Id);
        attendance.MemberCode.Should().Be(member.MemberCode);
        attendance.MemberName.Should().Be(member.FullName);
        attendance.Status.Should().Be(AttendanceStatus.CheckedIn);
        attendance.AttendanceDate.Date.Should().Be(DateTime.Today);
        attendance.CheckOutTime.Should().BeNull();
        attendance.DurationMinutes.Should().BeNull();
        attendance.CheckInMethod.Should().Be("Manual");

        // The visit is attributed to the subscription that paid for it.
        attendance.SubscriptionId.Should().Be(subscription.Id);
        attendance.PlanName.Should().Be("Monthly");
    }

    [Fact(DisplayName = "N-01 A member can be checked in by member code instead of id")]
    public async Task CheckIn_ByMemberCode_RecordsTheVisit()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await MemberWithActiveMembershipAsync(client);

        var attendance = await ReadDataAsync<AttendanceDto>(
            await CheckInAsync(client, memberCode: member.MemberCode));

        attendance.MemberId.Should().Be(member.Id);
        attendance.Status.Should().Be(AttendanceStatus.CheckedIn);
    }

    [Fact(DisplayName = "N-02 A second check-in on the same day is a 409 conflict")]
    public async Task CheckIn_Twice_IsAConflict()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await MemberWithActiveMembershipAsync(client);

        await ReadDataAsync<AttendanceDto>(await CheckInAsync(client, member.Id));

        var response = await CheckInAsync(client, member.Id);

        await AssertFailureAsync(response, HttpStatusCode.Conflict, "CONFLICT");
        (await ReadErrorAsync(response)).Message.Should().Contain("already checked in");
    }

    [Fact(DisplayName = "N-02 The duplicate rule still applies after the member has checked out")]
    public async Task CheckIn_AfterCheckingOut_IsStillAConflict()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await MemberWithActiveMembershipAsync(client);

        var attendance = await ReadDataAsync<AttendanceDto>(await CheckInAsync(client, member.Id));
        await ReadDataAsync<AttendanceDto>(await CheckOutAsync(client, attendance.Id));

        var response = await CheckInAsync(client, member.Id);

        await AssertFailureAsync(response, HttpStatusCode.Conflict, "CONFLICT");
        (await ReadErrorAsync(response)).Message.Should().Contain("already recorded for today");
    }

    [Fact(DisplayName = "N-03 A member with no membership at all is refused with a 422")]
    public async Task CheckIn_WithoutAMembership_Is422()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);

        var response = await CheckInAsync(client, member.Id);

        await AssertFailureAsync(response, HttpStatusCode.UnprocessableEntity, "BUSINESS_RULE");
        (await ReadErrorAsync(response)).Message.Should().Contain("no membership that is valid today");
    }

    [Fact(DisplayName = "N-04 An operator holding attendance.manage may override a missing membership")]
    public async Task CheckIn_WithAnOperatorOverride_IsAllowedAndNoted()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);

        var attendance = await ReadDataAsync<AttendanceDto>(
            await CheckInAsync(client, member.Id, overrideExpired: true));

        attendance.Status.Should().Be(AttendanceStatus.CheckedIn);
        attendance.SubscriptionId.Should().BeNull("there is no membership to attribute the visit to");
        attendance.Notes.Should().Contain("override", "the override must be recorded on the visit");
    }

    [Fact(DisplayName = "N-04 The AllowExpiredMemberCheckIn setting lets a lapsed member in without an override")]
    public async Task CheckIn_WhenTheGymAllowsExpiredMembers_IsAllowed()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);

        await WithGymSettingsAsync(client, s => s.AllowExpiredMemberCheckIn = true, async () =>
        {
            var attendance = await ReadDataAsync<AttendanceDto>(await CheckInAsync(client, member.Id));

            attendance.Status.Should().Be(AttendanceStatus.CheckedIn);
            attendance.SubscriptionId.Should().BeNull();
            attendance.Notes.Should().Contain("AllowExpiredMemberCheckIn");
        });

        // And the setting really was put back.
        var restored = await ReadDataAsync<GymSettingsDto>(await client.GetAsync("/api/Settings/gym"));
        restored.AllowExpiredMemberCheckIn.Should().BeFalse();
    }

    [Fact(DisplayName = "N-05 A suspended member may not enter, even with a valid membership")]
    public async Task CheckIn_ForASuspendedMember_Is422()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await MemberWithActiveMembershipAsync(client);

        (await client.PostAsJsonAsync($"/api/members/{member.Id}/status?reason=Testing", MemberStatus.Suspended))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await CheckInAsync(client, member.Id);

        await AssertFailureAsync(response, HttpStatusCode.UnprocessableEntity, "BUSINESS_RULE");
        (await ReadErrorAsync(response)).Message.Should().Contain("suspended");
    }

    [Fact(DisplayName = "N-06 An unknown member is a 404")]
    public async Task CheckIn_ForAnUnknownMember_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        await AssertFailureAsync(await CheckInAsync(client, 999999), HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact(DisplayName = "N-06 A request naming neither a member id nor a code is a 400")]
    public async Task CheckIn_WithNeitherIdNorCode_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var response = await CheckInAsync(client);

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact(DisplayName = "N-06 An unsupported check-in method is a 400 on CheckInMethod")]
    public async Task CheckIn_WithAnUnsupportedMethod_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await MemberWithActiveMembershipAsync(client);

        var response = await CheckInAsync(client, member.Id, method: "Telepathy");

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(CheckInRequestDto.CheckInMethod));
    }

    [Theory(DisplayName = "N-01 Every supported check-in method is accepted and stored")]
    [InlineData("QrCode")]
    [InlineData("Barcode")]
    [InlineData("Rfid")]
    [InlineData("Biometric")]
    public async Task CheckIn_WithASupportedMethod_StoresIt(string method)
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await MemberWithActiveMembershipAsync(client);

        var attendance = await ReadDataAsync<AttendanceDto>(
            await CheckInAsync(client, member.Id, method: method));

        attendance.CheckInMethod.Should().Be(method);
    }

    // ----------------------------------------------------------- check out

    [Fact(DisplayName = "N-07 Checking out closes the visit and records the duration")]
    public async Task CheckOut_ByAttendanceId_ClosesTheVisit()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await MemberWithActiveMembershipAsync(client);

        var attendance = await ReadDataAsync<AttendanceDto>(await CheckInAsync(client, member.Id));

        var closed = await ReadDataAsync<AttendanceDto>(await CheckOutAsync(client, attendance.Id));

        closed.Id.Should().Be(attendance.Id);
        closed.Status.Should().Be(AttendanceStatus.CheckedOut);
        closed.CheckOutTime.Should().NotBeNull();
        closed.CheckOutTime!.Value.Should().BeOnOrAfter(attendance.CheckInTime);
        closed.DurationMinutes.Should().NotBeNull();
        closed.DurationMinutes!.Value.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact(DisplayName = "N-07 A member's open visit can be closed by member id alone")]
    public async Task CheckOut_ByMemberId_ClosesTheOpenVisit()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await MemberWithActiveMembershipAsync(client);

        var attendance = await ReadDataAsync<AttendanceDto>(await CheckInAsync(client, member.Id));

        var closed = await ReadDataAsync<AttendanceDto>(await CheckOutAsync(client, memberId: member.Id));

        closed.Id.Should().Be(attendance.Id);
        closed.Status.Should().Be(AttendanceStatus.CheckedOut);
    }

    [Fact(DisplayName = "N-08 Checking out twice is a 409 quoting the original check-out")]
    public async Task CheckOut_Twice_IsAConflict()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await MemberWithActiveMembershipAsync(client);

        var attendance = await ReadDataAsync<AttendanceDto>(await CheckInAsync(client, member.Id));
        await ReadDataAsync<AttendanceDto>(await CheckOutAsync(client, attendance.Id));

        var response = await CheckOutAsync(client, attendance.Id);

        await AssertFailureAsync(response, HttpStatusCode.Conflict, "CONFLICT");
        (await ReadErrorAsync(response)).Message.Should().Contain("already checked out");
    }

    [Fact(DisplayName = "N-08 Checking out a member who never checked in is a 404")]
    public async Task CheckOut_WithNoOpenVisit_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await MemberWithActiveMembershipAsync(client);

        await AssertFailureAsync(
            await CheckOutAsync(client, memberId: member.Id), HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact(DisplayName = "N-08 Checking out an unknown attendance id is a 404")]
    public async Task CheckOut_WithAnUnknownAttendanceId_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        await AssertFailureAsync(
            await CheckOutAsync(client, attendanceId: 999999), HttpStatusCode.NotFound, "NOT_FOUND");
    }

    // --------------------------------------------------------------- reads

    [Fact(DisplayName = "N-09 The register can be filtered down to one member's visits")]
    public async Task GetPaged_FilteredByMember_ReturnsOnlyThatMember()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await MemberWithActiveMembershipAsync(client);

        var attendance = await ReadDataAsync<AttendanceDto>(await CheckInAsync(client, member.Id));

        var page = await ReadDataAsync<PagedResult<AttendanceDto>>(
            await client.GetAsync($"/api/Attendance?MemberId={member.Id}"));

        page.Items.Should().ContainSingle().Which.Id.Should().Be(attendance.Id);
        page.Items.Should().OnlyContain(a => a.MemberId == member.Id);
    }

    [Fact(DisplayName = "N-09 The register can be narrowed to today and to the checked-in status")]
    public async Task GetPaged_WithStatusAndTodayFilters_NarrowsTheList()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await MemberWithActiveMembershipAsync(client);

        var attendance = await ReadDataAsync<AttendanceDto>(await CheckInAsync(client, member.Id));

        var today = await ReadDataAsync<PagedResult<AttendanceDto>>(
            await client.GetAsync(
                $"/api/Attendance?OnlyToday=true&Status={(int)AttendanceStatus.CheckedIn}&PageSize=200"));

        today.Items.Should().Contain(a => a.Id == attendance.Id);
        today.Items.Should().OnlyContain(a => a.Status == AttendanceStatus.CheckedIn);
        today.Items.Should().OnlyContain(a => a.AttendanceDate.Date == DateTime.Today);
    }

    [Fact(DisplayName = "N-09 Searching the register by member code finds the visit")]
    public async Task GetPaged_SearchedByMemberCode_FindsTheVisit()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await MemberWithActiveMembershipAsync(client);

        var attendance = await ReadDataAsync<AttendanceDto>(await CheckInAsync(client, member.Id));

        var page = await ReadDataAsync<PagedResult<AttendanceDto>>(
            await client.GetAsync($"/api/Attendance?Search={member.MemberCode}"));

        page.Items.Should().ContainSingle().Which.Id.Should().Be(attendance.Id);
    }

    [Fact(DisplayName = "N-10 in-gym lists a member who is inside and drops them once they leave")]
    public async Task InGym_TracksTheMemberInAndOut()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await MemberWithActiveMembershipAsync(client);

        var attendance = await ReadDataAsync<AttendanceDto>(await CheckInAsync(client, member.Id));

        var inside = await ReadDataAsync<List<AttendanceDto>>(await client.GetAsync("/api/Attendance/in-gym"));
        inside.Should().Contain(a => a.Id == attendance.Id);
        inside.Should().OnlyContain(a => a.Status == AttendanceStatus.CheckedIn);

        await ReadDataAsync<AttendanceDto>(await CheckOutAsync(client, attendance.Id));

        var afterwards = await ReadDataAsync<List<AttendanceDto>>(await client.GetAsync("/api/Attendance/in-gym"));
        afterwards.Should().NotContain(a => a.Id == attendance.Id);
    }

    [Fact(DisplayName = "N-11 The daily summary counts the visit and its hour")]
    public async Task Summary_CountsTheVisit()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await MemberWithActiveMembershipAsync(client);

        var before = await ReadDataAsync<AttendanceSummaryDto>(await client.GetAsync("/api/Attendance/summary"));

        var attendance = await ReadDataAsync<AttendanceDto>(await CheckInAsync(client, member.Id));

        var after = await ReadDataAsync<AttendanceSummaryDto>(await client.GetAsync("/api/Attendance/summary"));

        after.Date.Date.Should().Be(DateTime.Today);
        after.TotalCheckIns.Should().Be(before.TotalCheckIns + 1);
        after.CurrentlyInGym.Should().Be(before.CurrentlyInGym + 1);
        after.HourlyBreakdown.Should().Contain(h => h.Hour == attendance.CheckInTime.Hour && h.Count > 0);
    }

    [Fact(DisplayName = "N-11 Checking out moves the visit from in-gym to checked-out in the summary")]
    public async Task Summary_MovesTheVisitOnCheckOut()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await MemberWithActiveMembershipAsync(client);

        var attendance = await ReadDataAsync<AttendanceDto>(await CheckInAsync(client, member.Id));
        var before = await ReadDataAsync<AttendanceSummaryDto>(await client.GetAsync("/api/Attendance/summary"));

        await ReadDataAsync<AttendanceDto>(await CheckOutAsync(client, attendance.Id));

        var after = await ReadDataAsync<AttendanceSummaryDto>(await client.GetAsync("/api/Attendance/summary"));

        after.TotalCheckIns.Should().Be(before.TotalCheckIns, "checking out does not add a visit");
        after.CurrentlyInGym.Should().Be(before.CurrentlyInGym - 1);
        after.CheckedOut.Should().Be(before.CheckedOut + 1);
    }

    [Fact(DisplayName = "N-12 The trend series covers every day in the range, filling the gaps with zero")]
    public async Task Trend_ReturnsOnePointPerDay()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await MemberWithActiveMembershipAsync(client);

        await ReadDataAsync<AttendanceDto>(await CheckInAsync(client, member.Id));

        var from = DateTime.Today.AddDays(-6);
        var to = DateTime.Today;

        var series = await ReadDataAsync<List<ChartSeriesDto>>(
            await client.GetAsync($"/api/Attendance/trend?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"));

        series.Should().HaveCount(7);
        series.Select(p => p.Date!.Value.Date).Should().BeInAscendingOrder();
        series.Last().Date!.Value.Date.Should().Be(to);
        series.Last().Value.Should().BeGreaterThan(0, "at least this test checked somebody in today");
    }

    [Fact(DisplayName = "N-12 A trend range that ends before it starts is a 400")]
    public async Task Trend_WithAnInvertedRange_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            $"/api/Attendance/trend?from={DateTime.Today:yyyy-MM-dd}&to={DateTime.Today.AddDays(-3):yyyy-MM-dd}");

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    // -------------------------------------------------------------- delete

    [Fact(DisplayName = "N-12 An attendance row captured in error can be deleted")]
    public async Task Delete_RemovesTheRow()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await MemberWithActiveMembershipAsync(client);

        var attendance = await ReadDataAsync<AttendanceDto>(await CheckInAsync(client, member.Id));

        (await client.DeleteAsync($"/api/Attendance/{attendance.Id}")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var page = await ReadDataAsync<PagedResult<AttendanceDto>>(
            await client.GetAsync($"/api/Attendance?MemberId={member.Id}"));
        page.TotalCount.Should().Be(0);

        // Deleting the row frees the member to be checked in again today.
        var again = await ReadDataAsync<AttendanceDto>(await CheckInAsync(client, member.Id));
        again.Id.Should().NotBe(attendance.Id);
    }

    [Fact(DisplayName = "N-12 Deleting an unknown attendance row is a 404")]
    public async Task Delete_AnUnknownRow_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        await AssertFailureAsync(
            await client.DeleteAsync("/api/Attendance/999999"), HttpStatusCode.NotFound, "NOT_FOUND");
    }
}
