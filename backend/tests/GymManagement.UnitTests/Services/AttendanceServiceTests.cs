using FluentAssertions;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Domain.Constants;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using GymManagement.Infrastructure.Services;
using GymManagement.UnitTests.TestBase;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GymManagement.UnitTests.Services;

/// <summary>
/// Front-desk check-in and check-out — cases N-01 to N-10 plus the S-20 grace period rule.
/// </summary>
public class AttendanceServiceTests : IAsyncLifetime
{
    private readonly FixedClock _clock = new();
    private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Admin();
    private readonly StubSettingsService _settings = new();
    private readonly RecordingAuditService _audit = new();
    private readonly RecordingNotificationService _notifications = new();

    private GymDbContext _db = null!;
    private ReferenceData _data = null!;
    private AttendanceService _service = null!;
    private Member _member = null!;

    public async Task InitializeAsync()
    {
        _db = InMemoryDbContextFactory.Create(_currentUser, _clock);
        _data = await InMemoryDbContextFactory.SeedReferenceDataAsync(_db);
        _member = await InMemoryDbContextFactory.AddMemberAsync(_db);

        _service = new AttendanceService(_db, _clock, _currentUser, _audit, _settings, _notifications,
            NullLogger<AttendanceService>.Instance);
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------- helpers

    /// <summary>A term that has ended and whose grace period has run out.</summary>
    private async Task<Subscription> AddLapsedSubscriptionAsync(int? memberId = null)
    {
        var subscription = new Subscription
        {
            SubscriptionCode = $"SUB-LAPSED-{Guid.NewGuid():N}"[..20],
            MemberId = memberId ?? _member.Id,
            MembershipPlanId = _data.MonthlyPlanId,
            StartDate = _clock.Today.AddDays(-60),
            EndDate = _clock.Today.AddDays(-30),
            GracePeriodDays = 3,
            Status = SubscriptionStatus.Expired,
            PlanAmount = 2500m,
            FinalAmount = 3304m,
            PaidAmount = 3304m,
            PaymentStatus = PaymentStatus.Paid
        };

        _db.Subscriptions.Add(subscription);
        await _db.SaveChangesAsync();
        return subscription;
    }

    /// <summary>Puts an attendance row straight into the database, bypassing the check-in rules.</summary>
    private async Task<Attendance> AddVisitAsync(
        AttendanceStatus status = AttendanceStatus.CheckedIn,
        DateTime? checkInTime = null,
        int? memberId = null)
    {
        var visit = new Attendance
        {
            MemberId = memberId ?? _member.Id,
            AttendanceDate = _clock.Today,
            CheckInTime = checkInTime ?? _clock.Now,
            Status = status,
            CheckInMethod = "Manual",
            CheckOutTime = status == AttendanceStatus.CheckedOut ? _clock.Now.AddHours(1) : null,
            DurationMinutes = status == AttendanceStatus.CheckedOut ? 60 : null
        };

        _db.Attendance.Add(visit);
        await _db.SaveChangesAsync();
        return visit;
    }

    private CheckInRequestDto CheckIn(
        int? memberId = null, string? memberCode = null, string method = "Manual",
        bool overrideExpired = false) => new()
    {
        MemberId = memberCode is null ? memberId ?? _member.Id : null,
        MemberCode = memberCode,
        CheckInMethod = method,
        OverrideExpiredMembership = overrideExpired
    };

    // ------------------------------------------------------------ check in

    [Fact(DisplayName = "N-01 A member with a usable subscription is checked in and linked to it")]
    public async Task CheckIn_WithAUsableSubscription_RecordsTheVisit()
    {
        var subscription = new Subscription
        {
            SubscriptionCode = "SUB-ACTIVE-1",
            MemberId = _member.Id,
            MembershipPlanId = _data.MonthlyPlanId,
            StartDate = _clock.Today.AddDays(-5),
            EndDate = _clock.Today.AddDays(25),
            GracePeriodDays = 3,
            Status = SubscriptionStatus.Active,
            FinalAmount = 3304m,
            PaidAmount = 3304m,
            PaymentStatus = PaymentStatus.Paid
        };
        _db.Subscriptions.Add(subscription);
        await _db.SaveChangesAsync();

        var result = await _service.CheckInAsync(CheckIn());

        result.Status.Should().Be(AttendanceStatus.CheckedIn);
        result.SubscriptionId.Should().Be(subscription.Id);
    }

    [Fact(DisplayName = "S-20 A check-in inside the grace period is allowed")]
    public async Task CheckIn_InsideTheGracePeriod_IsAllowed()
    {
        var subscription = new Subscription
        {
            SubscriptionCode = "SUB-GRACE-1",
            MemberId = _member.Id,
            MembershipPlanId = _data.MonthlyPlanId,
            StartDate = _clock.Today.AddDays(-31),
            EndDate = _clock.Today.AddDays(-1), // ended yesterday
            GracePeriodDays = 3,
            Status = SubscriptionStatus.Active,
            FinalAmount = 3304m,
            PaidAmount = 3304m,
            PaymentStatus = PaymentStatus.Paid
        };
        _db.Subscriptions.Add(subscription);
        await _db.SaveChangesAsync();

        var result = await _service.CheckInAsync(CheckIn());

        result.Status.Should().Be(AttendanceStatus.CheckedIn);
        result.SubscriptionId.Should().Be(subscription.Id);
    }

    [Fact(DisplayName = "N-01 A check-in is recorded, audited and counted for today")]
    public async Task CheckIn_WhenThePolicyAllowsIt_RecordsAndAuditsTheVisit()
    {
        _settings.Settings.AllowExpiredMemberCheckIn = true;

        var result = await _service.CheckInAsync(CheckIn());

        result.Status.Should().Be(AttendanceStatus.CheckedIn);
        result.MemberId.Should().Be(_member.Id);
        result.AttendanceDate.Should().Be(_clock.Today);
        result.CheckInTime.Should().Be(_clock.Now);
        result.CheckInMethod.Should().Be("Manual");
        _audit.Contains(AuditActions.Create, "Attendance").Should().BeTrue();

        (await _db.Attendance.AsNoTracking().CountAsync(a => a.AttendanceDate == _clock.Today))
            .Should().Be(1);
    }

    [Fact(DisplayName = "N-02 Checking the same member in twice today is a conflict")]
    public async Task CheckIn_Twice_ThrowsConflict()
    {
        await AddVisitAsync();

        var act = () => _service.CheckInAsync(CheckIn());

        var error = await act.Should().ThrowAsync<ConflictAppException>();
        error.Which.Message.Should().Contain("already checked in");
    }

    [Fact(DisplayName = "N-03 Checking in again after checking out today is a conflict")]
    public async Task CheckIn_AfterCheckingOutToday_ThrowsConflict()
    {
        await AddVisitAsync(AttendanceStatus.CheckedOut);

        var act = () => _service.CheckInAsync(CheckIn());

        var error = await act.Should().ThrowAsync<ConflictAppException>();
        error.Which.Message.Should().Contain("already recorded for today");
    }

    [Fact(DisplayName = "N-04 A member whose term ended beyond the grace period is refused")]
    public async Task CheckIn_WithALapsedMembership_ThrowsBusinessRule()
    {
        await AddLapsedSubscriptionAsync();
        _settings.Settings.AllowExpiredMemberCheckIn = false;

        var act = () => _service.CheckInAsync(CheckIn());

        var error = await act.Should().ThrowAsync<BusinessRuleAppException>();
        error.Which.Message.Should().Contain("renewed");
    }

    [Fact(DisplayName = "N-04 A member who never bought a subscription is refused")]
    public async Task CheckIn_WithNoSubscriptionAtAll_ThrowsBusinessRule()
    {
        _settings.Settings.AllowExpiredMemberCheckIn = false;

        var act = () => _service.CheckInAsync(CheckIn());

        await act.Should().ThrowAsync<BusinessRuleAppException>();
    }

    [Fact(DisplayName = "N-05 An operator holding attendance.manage may override a lapsed membership")]
    public async Task CheckIn_WithOverrideAndPermission_Succeeds()
    {
        await AddLapsedSubscriptionAsync();
        _settings.Settings.AllowExpiredMemberCheckIn = false;
        _currentUser.Roles = new[] { RoleNames.Staff };
        _currentUser.Permissions = new[] { Permissions.AttendanceManage };
        _currentUser.FullName = "Front Desk";

        var result = await _service.CheckInAsync(CheckIn(overrideExpired: true));

        result.Status.Should().Be(AttendanceStatus.CheckedIn);
        result.SubscriptionId.Should().BeNull();
        result.Notes.Should().Contain("override").And.Contain("Front Desk");
    }

    [Fact(DisplayName = "N-05 An override raises a membership-expired notification")]
    public async Task CheckIn_WithOverride_RaisesAnExpiredNotification()
    {
        await AddLapsedSubscriptionAsync();
        _currentUser.Roles = new[] { RoleNames.Staff };
        _currentUser.Permissions = new[] { Permissions.AttendanceManage };

        await _service.CheckInAsync(CheckIn(overrideExpired: true));

        _notifications.CountOf(NotificationType.MembershipExpired).Should().Be(1);
    }

    [Fact(DisplayName = "N-06 The same override without attendance.manage is still refused")]
    public async Task CheckIn_WithOverrideButNoPermission_ThrowsBusinessRule()
    {
        await AddLapsedSubscriptionAsync();
        _settings.Settings.AllowExpiredMemberCheckIn = false;
        _currentUser.Roles = new[] { RoleNames.Staff };
        _currentUser.Permissions = new[] { Permissions.AttendanceView };

        var act = () => _service.CheckInAsync(CheckIn(overrideExpired: true));

        await act.Should().ThrowAsync<BusinessRuleAppException>();
    }

    [Fact(DisplayName = "N-09 A member code resolves to the same member as the id")]
    public async Task CheckIn_ByMemberCode_ResolvesTheSameMember()
    {
        _settings.Settings.AllowExpiredMemberCheckIn = true;

        var result = await _service.CheckInAsync(CheckIn(memberCode: _member.MemberCode));

        result.MemberId.Should().Be(_member.Id);
        result.MemberCode.Should().Be(_member.MemberCode);
    }

    [Theory(DisplayName = "N-10 An unrecognised check-in method is refused")]
    [InlineData("Telepathy")]
    [InlineData("Facial")]
    [InlineData("!!!")]
    public async Task CheckIn_WithAnUnknownMethod_ThrowsValidation(string method)
    {
        var act = () => _service.CheckInAsync(CheckIn(method: method));

        var error = await act.Should().ThrowAsync<ValidationAppException>();
        error.Which.Errors.Should().ContainKey(nameof(CheckInRequestDto.CheckInMethod));
    }

    [Theory(DisplayName = "N-10 Supported check-in methods are normalised to their canonical spelling")]
    [InlineData("qrcode", "QrCode")]
    [InlineData("BIOMETRIC", "Biometric")]
    [InlineData("  rfid  ", "Rfid")]
    public async Task CheckIn_NormalisesTheMethodName(string supplied, string expected)
    {
        _settings.Settings.AllowExpiredMemberCheckIn = true;

        var result = await _service.CheckInAsync(CheckIn(method: supplied));

        result.CheckInMethod.Should().Be(expected);
    }

    [Fact(DisplayName = "A suspended member may not enter the gym")]
    public async Task CheckIn_ForASuspendedMember_ThrowsBusinessRule()
    {
        var suspended = await InMemoryDbContextFactory.AddMemberAsync(
            _db, "Suspended Member", status: MemberStatus.Suspended);
        _settings.Settings.AllowExpiredMemberCheckIn = true;

        var act = () => _service.CheckInAsync(CheckIn(memberId: suspended.Id));

        await act.Should().ThrowAsync<BusinessRuleAppException>();
    }

    [Fact(DisplayName = "Checking in an unknown member is a not-found error")]
    public async Task CheckIn_ForAnUnknownMember_ThrowsNotFound()
    {
        var act = () => _service.CheckInAsync(CheckIn(memberId: 999_999));

        await act.Should().ThrowAsync<NotFoundAppException>();
    }

    // ----------------------------------------------------------- check out

    [Fact(DisplayName = "N-07 Checking out records the time, the status and a non-negative duration")]
    public async Task CheckOut_RecordsTheDuration()
    {
        var visit = await AddVisitAsync();
        _clock.Advance(TimeSpan.FromMinutes(90));

        var result = await _service.CheckOutAsync(new CheckOutRequestDto { AttendanceId = visit.Id });

        result.Status.Should().Be(AttendanceStatus.CheckedOut);
        result.CheckOutTime.Should().Be(_clock.Now);
        result.DurationMinutes.Should().Be(90);
        result.DurationMinutes.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact(DisplayName = "N-07 A member id alone is enough to close today's open visit")]
    public async Task CheckOut_ByMemberId_ClosesTodaysVisit()
    {
        await AddVisitAsync();
        _clock.Advance(TimeSpan.FromMinutes(45));

        var result = await _service.CheckOutAsync(new CheckOutRequestDto { MemberId = _member.Id });

        result.Status.Should().Be(AttendanceStatus.CheckedOut);
        result.DurationMinutes.Should().Be(45);
    }

    [Fact(DisplayName = "N-07 The duration is whole minutes, floored, and never negative")]
    public async Task CheckOut_WithASecondsLevelVisit_FloorsTheDuration()
    {
        var visit = await AddVisitAsync();
        _clock.Advance(TimeSpan.FromSeconds(119));

        var result = await _service.CheckOutAsync(new CheckOutRequestDto { AttendanceId = visit.Id });

        result.DurationMinutes.Should().Be(1);
    }

    [Fact(DisplayName = "N-08 Checking out twice is a conflict")]
    public async Task CheckOut_Twice_ThrowsConflict()
    {
        var visit = await AddVisitAsync();
        _clock.Advance(TimeSpan.FromMinutes(30));
        await _service.CheckOutAsync(new CheckOutRequestDto { AttendanceId = visit.Id });

        var act = () => _service.CheckOutAsync(new CheckOutRequestDto { AttendanceId = visit.Id });

        var error = await act.Should().ThrowAsync<ConflictAppException>();
        error.Which.Message.Should().Contain("already checked out");
    }

    [Fact(DisplayName = "N-08 Checking out with nothing open is a not-found error")]
    public async Task CheckOut_WithNoOpenVisit_ThrowsNotFound()
    {
        var act = () => _service.CheckOutAsync(new CheckOutRequestDto { MemberId = _member.Id });

        await act.Should().ThrowAsync<NotFoundAppException>();
    }

    // ------------------------------------------------------------- summary

    [Fact(DisplayName = "N-12 The daily summary counts who is in the gym and who has left")]
    public async Task GetSummary_ReportsCoherentCounters()
    {
        var second = await InMemoryDbContextFactory.AddMemberAsync(_db, "Second Member");
        var third = await InMemoryDbContextFactory.AddMemberAsync(_db, "Third Member");

        await AddVisitAsync(AttendanceStatus.CheckedIn, _clock.Today.AddHours(7));
        await AddVisitAsync(AttendanceStatus.CheckedIn, _clock.Today.AddHours(7), second.Id);
        await AddVisitAsync(AttendanceStatus.CheckedOut, _clock.Today.AddHours(18), third.Id);

        var summary = await _service.GetSummaryAsync(_clock.Today);

        summary.Date.Should().Be(_clock.Today);
        summary.TotalCheckIns.Should().Be(3);
        summary.CurrentlyInGym.Should().Be(2);
        summary.CheckedOut.Should().Be(1);
        summary.TotalCheckIns.Should().Be(summary.CurrentlyInGym + summary.CheckedOut);
        summary.PeakHour.Should().Be(7);
        summary.HourlyBreakdown.Sum(h => h.Count).Should().Be(3);
    }

    [Fact(DisplayName = "N-12 An empty day summarises to zeros rather than throwing")]
    public async Task GetSummary_OnAnEmptyDay_ReturnsZeros()
    {
        var summary = await _service.GetSummaryAsync(_clock.Today);

        summary.TotalCheckIns.Should().Be(0);
        summary.CurrentlyInGym.Should().Be(0);
        summary.CheckedOut.Should().Be(0);
        summary.AverageDurationMinutes.Should().Be(0d);
        summary.PeakHour.Should().Be(0);
        summary.HourlyBreakdown.Should().BeEmpty();
    }

    [Fact(DisplayName = "N-12 Currently-in-gym lists only the members who have not checked out")]
    public async Task GetCurrentlyInGym_ListsOnlyOpenVisits()
    {
        var second = await InMemoryDbContextFactory.AddMemberAsync(_db, "Second Member");
        await AddVisitAsync(AttendanceStatus.CheckedIn);
        await AddVisitAsync(AttendanceStatus.CheckedOut, memberId: second.Id);

        var inGym = await _service.GetCurrentlyInGymAsync();

        inGym.Should().ContainSingle();
        inGym[0].MemberId.Should().Be(_member.Id);
    }
}
