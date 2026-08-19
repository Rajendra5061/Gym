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
    public List<AttendanceDto> RecentAttendance { get; set; } = new();
    public List<PaymentDto> RecentPayments { get; set; } = new();
    public MemberWorkoutPlanDto? ActiveWorkoutPlan { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
    public List<ChartSeriesDto> AttendanceTrend { get; set; } = new();
}
