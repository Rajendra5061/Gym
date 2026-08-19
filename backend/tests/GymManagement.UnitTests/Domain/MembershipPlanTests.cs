using FluentAssertions;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using Xunit;

namespace GymManagement.UnitTests.Domain;

/// <summary>
/// Case P-01 — every <see cref="PlanDurationType"/> must compute the right total length and the
/// right inclusive end date from a given start date.
/// </summary>
public class MembershipPlanTests
{
    private static readonly DateTime Start = new(2026, 8, 17);

    private static MembershipPlan Plan(PlanDurationType type, int value) =>
        new() { Name = $"{type} x{value}", DurationType = type, DurationValue = value };

    [Theory(DisplayName = "P-01 TotalDays is derived from the duration type and value")]
    [InlineData(PlanDurationType.Day, 1, 1)]
    [InlineData(PlanDurationType.Day, 10, 10)]
    [InlineData(PlanDurationType.Week, 1, 7)]
    [InlineData(PlanDurationType.Week, 2, 14)]
    [InlineData(PlanDurationType.Month, 1, 30)]
    [InlineData(PlanDurationType.Month, 3, 90)]
    [InlineData(PlanDurationType.Quarter, 1, 90)]
    [InlineData(PlanDurationType.Quarter, 2, 180)]
    [InlineData(PlanDurationType.HalfYear, 1, 182)]
    [InlineData(PlanDurationType.Year, 1, 365)]
    [InlineData(PlanDurationType.Year, 2, 730)]
    [InlineData(PlanDurationType.Custom, 45, 45)]
    public void TotalDays_MatchesTheDurationType(PlanDurationType type, int value, int expected)
    {
        var plan = Plan(type, value);

        plan.TotalDays.Should().Be(expected);
    }

    [Fact(DisplayName = "P-01 A one month plan starting 2026-08-17 ends 2026-09-16")]
    public void CalculateEndDate_Monthly_EndsTheDayBeforeTheNextMonthlyAnniversary()
    {
        var plan = Plan(PlanDurationType.Month, 1);

        plan.CalculateEndDate(Start).Should().Be(new DateTime(2026, 9, 16));
    }

    [Fact(DisplayName = "P-01 A one year plan starting 2026-08-17 ends 2027-08-16")]
    public void CalculateEndDate_Yearly_EndsTheDayBeforeTheAnniversary()
    {
        var plan = Plan(PlanDurationType.Year, 1);

        plan.CalculateEndDate(Start).Should().Be(new DateTime(2027, 8, 16));
    }

    [Theory(DisplayName = "P-01 Every duration type produces the documented inclusive end date")]
    [InlineData(PlanDurationType.Day, 1, "2026-08-17")]
    [InlineData(PlanDurationType.Day, 10, "2026-08-26")]
    [InlineData(PlanDurationType.Week, 1, "2026-08-23")]
    [InlineData(PlanDurationType.Week, 2, "2026-08-30")]
    [InlineData(PlanDurationType.Month, 1, "2026-09-16")]
    [InlineData(PlanDurationType.Month, 3, "2026-11-16")]
    [InlineData(PlanDurationType.Quarter, 1, "2026-11-16")]
    [InlineData(PlanDurationType.Quarter, 2, "2027-02-16")]
    [InlineData(PlanDurationType.HalfYear, 1, "2027-02-16")]
    [InlineData(PlanDurationType.Year, 1, "2027-08-16")]
    [InlineData(PlanDurationType.Custom, 45, "2026-09-30")]
    public void CalculateEndDate_CoversEveryDurationType(PlanDurationType type, int value, string expected)
    {
        var plan = Plan(type, value);

        plan.CalculateEndDate(Start).Should().Be(DateTime.Parse(expected));
    }

    [Fact(DisplayName = "P-01 The end date is inclusive, so the term spans TotalDays days for day based plans")]
    public void CalculateEndDate_DayBasedPlan_SpansExactlyTotalDays()
    {
        var plan = Plan(PlanDurationType.Custom, 30);

        var end = plan.CalculateEndDate(Start);

        ((end - Start).Days + 1).Should().Be(plan.TotalDays);
    }

    [Fact(DisplayName = "P-01 A leap day start on a yearly plan lands on the day before the anniversary")]
    public void CalculateEndDate_YearlyAcrossALeapYear_StaysOnTheCalendarAnniversary()
    {
        var plan = Plan(PlanDurationType.Year, 1);

        plan.CalculateEndDate(new DateTime(2028, 2, 29)).Should().Be(new DateTime(2029, 2, 27));
    }

    [Fact(DisplayName = "P-01 CalculateEndDate ignores the time component of the start date")]
    public void CalculateEndDate_KeepsTheTimeComponentOfTheStartDate()
    {
        var plan = Plan(PlanDurationType.Month, 1);

        var end = plan.CalculateEndDate(new DateTime(2026, 8, 17, 13, 45, 0));

        end.Date.Should().Be(new DateTime(2026, 9, 16));
    }
}
