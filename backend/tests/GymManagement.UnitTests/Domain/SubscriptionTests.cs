using FluentAssertions;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using Xunit;

namespace GymManagement.UnitTests.Domain;

/// <summary>
/// Domain level cover for the grace period rules behind cases S-20 and N-04, plus the money and
/// countdown helpers the whole billing module leans on.
/// </summary>
public class SubscriptionTests
{
    private static readonly DateTime Start = new(2026, 8, 1);
    private static readonly DateTime End = new(2026, 8, 31);

    private static Subscription Sub(
        SubscriptionStatus status = SubscriptionStatus.Active,
        int gracePeriodDays = 0,
        decimal finalAmount = 0m,
        decimal paidAmount = 0m) => new()
    {
        SubscriptionCode = "SUB-2026-0001",
        StartDate = Start,
        EndDate = End,
        Status = status,
        GracePeriodDays = gracePeriodDays,
        FinalAmount = finalAmount,
        PaidAmount = paidAmount
    };

    // ------------------------------------------------------------------ money

    [Fact(DisplayName = "OutstandingAmount is the unpaid remainder of the final amount")]
    public void OutstandingAmount_PartlyPaid_ReturnsTheRemainder()
    {
        var sub = Sub(finalAmount: 3304.00m, paidAmount: 1000.00m);

        sub.OutstandingAmount.Should().Be(2304.00m);
    }

    [Fact(DisplayName = "OutstandingAmount is zero once the subscription is fully paid")]
    public void OutstandingAmount_FullyPaid_IsZero()
    {
        var sub = Sub(finalAmount: 3304.00m, paidAmount: 3304.00m);

        sub.OutstandingAmount.Should().Be(0m);
    }

    [Fact(DisplayName = "OutstandingAmount never goes negative when more was paid than billed")]
    public void OutstandingAmount_OverPaid_IsFlooredAtZero()
    {
        var sub = Sub(finalAmount: 3304.00m, paidAmount: 4000.00m);

        sub.OutstandingAmount.Should().Be(0m);
    }

    // ------------------------------------------------- effective end date

    [Fact(DisplayName = "EffectiveEndDate equals EndDate when the plan grants no grace period")]
    public void EffectiveEndDate_WithoutAGracePeriod_EqualsTheEndDate()
    {
        var sub = Sub(gracePeriodDays: 0);

        sub.EffectiveEndDate.Should().Be(End);
    }

    [Fact(DisplayName = "EffectiveEndDate extends the term by the grace period")]
    public void EffectiveEndDate_WithAGracePeriod_ExtendsTheTerm()
    {
        var sub = Sub(gracePeriodDays: 3);

        sub.EffectiveEndDate.Should().Be(new DateTime(2026, 9, 3));
    }

    // ------------------------------------------------------- days remaining

    [Theory(DisplayName = "DaysRemaining counts whole days to the inclusive end date")]
    [InlineData("2026-08-01", 30)]
    [InlineData("2026-08-31", 0)]
    [InlineData("2026-08-30", 1)]
    [InlineData("2026-09-05", -5)]
    public void DaysRemaining_CountsWholeDaysFromTheAsOfDate(string asOf, int expected)
    {
        var sub = Sub();

        sub.DaysRemaining(DateTime.Parse(asOf)).Should().Be(expected);
    }

    [Fact(DisplayName = "DaysRemaining ignores the time of day")]
    public void DaysRemaining_IgnoresTheTimeComponent()
    {
        var sub = Sub();

        sub.DaysRemaining(new DateTime(2026, 8, 30, 23, 59, 0)).Should().Be(1);
    }

    // ------------------------------------------------------------ usability

    [Fact(DisplayName = "S-20 An active subscription is usable on a day inside its term")]
    public void IsUsableOn_InsideTheTerm_IsTrue()
    {
        var sub = Sub(gracePeriodDays: 3);

        sub.IsUsableOn(new DateTime(2026, 8, 15)).Should().BeTrue();
    }

    [Fact(DisplayName = "S-20 An active subscription is usable on the last day of its term")]
    public void IsUsableOn_OnTheEndDate_IsTrue()
    {
        var sub = Sub(gracePeriodDays: 0);

        sub.IsUsableOn(End).Should().BeTrue();
    }

    [Fact(DisplayName = "S-20 A subscription one day past its end is usable inside a 3 day grace window")]
    public void IsUsableOn_InsideTheGraceWindow_IsTrue()
    {
        var sub = Sub(gracePeriodDays: 3);

        sub.IsUsableOn(new DateTime(2026, 9, 1)).Should().BeTrue();
    }

    [Fact(DisplayName = "S-20 The last day of the grace window is still usable")]
    public void IsUsableOn_OnTheLastGraceDay_IsTrue()
    {
        var sub = Sub(gracePeriodDays: 3);

        sub.IsUsableOn(new DateTime(2026, 9, 3)).Should().BeTrue();
    }

    [Fact(DisplayName = "N-04 A subscription past its grace window is not usable")]
    public void IsUsableOn_PastTheGraceWindow_IsFalse()
    {
        var sub = Sub(gracePeriodDays: 3);

        sub.IsUsableOn(new DateTime(2026, 9, 4)).Should().BeFalse();
    }

    [Fact(DisplayName = "N-04 A subscription with no grace period is not usable the day after it ends")]
    public void IsUsableOn_DayAfterTheEndWithNoGrace_IsFalse()
    {
        var sub = Sub(gracePeriodDays: 0);

        sub.IsUsableOn(new DateTime(2026, 9, 1)).Should().BeFalse();
    }

    [Fact(DisplayName = "A subscription is not usable before it starts")]
    public void IsUsableOn_BeforeTheStartDate_IsFalse()
    {
        var sub = Sub(gracePeriodDays: 3);

        sub.IsUsableOn(new DateTime(2026, 7, 31)).Should().BeFalse();
    }

    [Theory(DisplayName = "N-04 Only an Active subscription is usable")]
    [InlineData(SubscriptionStatus.Frozen)]
    [InlineData(SubscriptionStatus.Expired)]
    [InlineData(SubscriptionStatus.Cancelled)]
    [InlineData(SubscriptionStatus.Pending)]
    [InlineData(SubscriptionStatus.Upgraded)]
    [InlineData(SubscriptionStatus.Downgraded)]
    public void IsUsableOn_NonActiveStatus_IsFalse(SubscriptionStatus status)
    {
        var sub = Sub(status, gracePeriodDays: 3);

        sub.IsUsableOn(new DateTime(2026, 8, 15)).Should().BeFalse();
    }

    [Fact(DisplayName = "IsUsableOn ignores the time of day")]
    public void IsUsableOn_IgnoresTheTimeComponent()
    {
        var sub = Sub(gracePeriodDays: 0);

        sub.IsUsableOn(End.AddHours(23)).Should().BeTrue();
    }
}
