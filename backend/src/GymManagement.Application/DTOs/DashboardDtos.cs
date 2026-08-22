namespace GymManagement.Application.DTOs;

/// <summary>Every card and chart on the admin dashboard, fetched in a single call.</summary>
public class DashboardDto
{
    public DashboardStatsDto Stats { get; set; } = new();
    public List<ChartSeriesDto> RevenueDaily { get; set; } = new();
    public List<ChartSeriesDto> RevenueWeekly { get; set; } = new();
    public List<ChartSeriesDto> RevenueMonthly { get; set; } = new();
    public List<ChartSeriesDto> MembershipGrowth { get; set; } = new();
    public List<ChartSeriesDto> AttendanceTrend { get; set; } = new();
    public List<ChartSeriesDto> PlanDistribution { get; set; } = new();
    public List<ChartSeriesDto> PaymentMethodDistribution { get; set; } = new();
    public List<PaymentDto> RecentTransactions { get; set; } = new();
    public List<RecentActivityDto> RecentActivities { get; set; } = new();
    public List<MemberListDto> ExpiringSoonMembers { get; set; } = new();
    public DateTime GeneratedAtUtc { get; set; }
}

public class DashboardStatsDto
{
    public int TotalMembers { get; set; }
    public int ActiveMembers { get; set; }
    public int InactiveMembers { get; set; }
    public int ExpiredMemberships { get; set; }
    public int ExpiringSoon { get; set; }

    /// <summary>Active memberships inside the renewal-email window  14 the members being emailed daily.</summary>
    public int ExpiringInEmailWindow { get; set; }

    /// <summary>The window in days, so the dashboard can label the count honestly.</summary>
    public int ExpiryEmailWindowDays { get; set; }
    public int TodayAttendance { get; set; }
    public int CurrentlyInGym { get; set; }
    public decimal TodayRevenue { get; set; }
    public decimal MonthRevenue { get; set; }
    public decimal YearRevenue { get; set; }
    public decimal MonthExpenses { get; set; }
    public decimal MonthNetIncome => MonthRevenue - MonthExpenses;
    public decimal PendingPaymentsAmount { get; set; }
    public int PendingPaymentsCount { get; set; }
    public int ActiveSubscriptions { get; set; }
    public int FrozenSubscriptions { get; set; }
    public int NewMembersThisMonth { get; set; }
    public int NewMembersToday { get; set; }
    public int TotalTrainers { get; set; }
    public int ActiveTrainers { get; set; }
    public int UnreadNotifications { get; set; }
    public string CurrencySymbol { get; set; } = "₹";
    /// <summary>Percentage change in revenue against the previous month.</summary>
    public decimal RevenueGrowthPercent { get; set; }
    public decimal MemberGrowthPercent { get; set; }
}

/// <summary>Generic label/value pair used by every chart in the client.</summary>
public class ChartSeriesDto
{
    public string Label { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public decimal? SecondaryValue { get; set; }
    public DateTime? Date { get; set; }
    public string? Category { get; set; }
}

public class RecentActivityDto
{
    public DateTime WhenUtc { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    public string? Description { get; set; }
    public string? UserName { get; set; }
    public string? Icon { get; set; }
}

/// <summary>Compact dashboard for the signed-in trainer's own portal view.</summary>
public class TrainerDashboardDto
{
    public int TrainerId { get; set; }
    public string TrainerName { get; set; } = string.Empty;
    public int AssignedMemberCount { get; set; }
    public int ActiveWorkoutPlanCount { get; set; }
    public int ActiveDietPlanCount { get; set; }
    public int TodayCheckInCount { get; set; }
    public List<TrainerExpiringMemberDto> ExpiringSoon { get; set; } = new();
    public List<TrainerMemberSummaryDto> MyMembers { get; set; } = new();
}

/// <summary>An assigned member whose membership runs out within the reminder window.</summary>
public class TrainerExpiringMemberDto
{
    public int MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public DateTime EndDate { get; set; }
    public int DaysLeft { get; set; }
}

/// <summary>One row of the trainer's member roster.</summary>
public class TrainerMemberSummaryDto
{
    public int MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public string MemberCode { get; set; } = string.Empty;
    public string? PlanName { get; set; }
    public DateTime? EndDate { get; set; }
    public int? DaysLeft { get; set; }
    public DateTime? LastCheckInDate { get; set; }
}

/// <summary>Compact dashboard for the logged-in member's own portal view.</summary>
public class MemberDashboardDto
{
    public MemberDetailDto Member { get; set; } = new();
    public SubscriptionDto? ActiveSubscription { get; set; }
    public int DaysRemaining { get; set; }
    public decimal OutstandingAmount { get; set; }
    public int VisitsThisMonth { get; set; }
    public int TotalVisits { get; set; }
    public DateTime? LastVisit { get; set; }
    public AttendanceInsightsDto AttendanceInsights { get; set; } = new();
    public List<AttendanceDto> RecentAttendance { get; set; } = new();
    public List<PaymentDto> RecentPayments { get; set; } = new();
    public MemberWorkoutPlanDto? ActiveWorkoutPlan { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
    public List<ChartSeriesDto> AttendanceTrend { get; set; } = new();
}

/// <summary>
/// The member's training consistency, computed from the distinct calendar days they checked
/// in. A day with three check-ins counts once — streaks reward showing up, not door traffic.
/// </summary>
public class AttendanceInsightsDto
{
    /// <summary>Consecutive gym days running through today. A day without a visit yet does
    /// not break it until it is over — the streak ends only when a full day is missed.</summary>
    public int CurrentStreakDays { get; set; }

    /// <summary>The longest run of consecutive gym days on record.</summary>
    public int BestStreakDays { get; set; }

    /// <summary>Distinct gym days in the current Monday-to-Sunday week.</summary>
    public int VisitsThisWeek { get; set; }

    /// <summary>Sessions per week the portal presents as the goal line.</summary>
    public int WeeklyTargetDays { get; set; }

    /// <summary>Distinct gym days in the last 30 days — the consistency score's numerator.</summary>
    public int ActiveDaysLast30 { get; set; }

    /// <summary>Distinct visit dates over the trailing 12 weeks, oldest first, for the
    /// activity heat strip. Dates only, normalised to midnight.</summary>
    public List<DateTime> ActiveDays { get; set; } = new();
}
