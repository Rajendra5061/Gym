using FluentAssertions;
using GymManagement.Infrastructure.Services;
using Xunit;

namespace GymManagement.UnitTests.Services;

/// <summary>
/// The member-dashboard streak math: consecutive distinct gym days, where today only breaks
/// the streak once it has fully passed without a visit.
/// </summary>
public class AttendanceStreakTests
{
    private static readonly DateTime Today = new(2026, 8, 21);

    private static List<DateTime> Days(params int[] daysAgo) =>
        daysAgo.Select(n => Today.AddDays(-n)).OrderBy(d => d).ToList();

    [Fact]
    public void NoVisitsMeansNoStreaks()
    {
        DashboardService.ComputeStreaks(new List<DateTime>(), Today).Should().Be((0, 0));
    }

    [Fact]
    public void VisitTodayStartsAStreakOfOne()
    {
        DashboardService.ComputeStreaks(Days(0), Today).Should().Be((1, 1));
    }

    [Fact]
    public void ConsecutiveDaysThroughTodayAllCount()
    {
        DashboardService.ComputeStreaks(Days(0, 1, 2, 3), Today).Should().Be((4, 4));
    }

    [Fact]
    public void MissingTodayKeepsYesterdaysStreakAlive()
    {
        // The member trains in the evening: at breakfast the streak must still read 3.
        DashboardService.ComputeStreaks(Days(1, 2, 3), Today).Should().Be((3, 3));
    }

    [Fact]
    public void AFullDayMissedEndsTheCurrentStreak()
    {
        DashboardService.ComputeStreaks(Days(2, 3, 4), Today).Should().Be((0, 3));
    }

    [Fact]
    public void BestStreakSurvivesFromAnOlderRun()
    {
        // Five straight days two weeks ago; a fresh two-day run now.
        DashboardService.ComputeStreaks(Days(14, 15, 16, 17, 18, 0, 1), Today).Should().Be((2, 5));
    }

    [Fact]
    public void GapsResetTheRunButNotTheRecord()
    {
        DashboardService.ComputeStreaks(Days(0, 2, 4, 6), Today).Should().Be((1, 1));
    }

    [Fact]
    public void DuplicateFreeInputIsAssumedDistinctDays()
    {
        // The service feeds Distinct() dates; equal neighbours would be a caller bug, but the
        // math still terminates and never inflates the run.
        DashboardService.ComputeStreaks(Days(0, 1, 1, 2).Distinct().ToList(), Today).Should().Be((3, 3));
    }
}
