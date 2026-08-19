using System.Globalization;
using System.Linq.Expressions;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Services;

/// <summary>
/// Read-only dashboard aggregates. Every counter and chart is computed with a SQL aggregate
/// (COUNT / SUM / GROUP BY); no table is ever materialised into memory. Chart gaps are
/// zero-filled after the grouped query returns so the client always receives a dense series.
/// </summary>
public sealed class DashboardService : IDashboardService
{
    private const int TrendDays = 30;
    private const int TrendWeeks = 12;
    private const int TrendMonths = 12;
    private const int RecentTake = 10;
    private const int PaymentMethodWindowDays = 90;

    private const string DayLabelFormat = "dd MMM";
    private const string MonthLabelFormat = "MMM yy";

    /// <summary>Kept identical to the payment shape returned by the payments module.</summary>
    private static readonly Expression<Func<Payment, PaymentDto>> PaymentProjection = p => new PaymentDto
    {
        Id = p.Id,
        ReceiptNumber = p.ReceiptNumber,
        MemberId = p.MemberId,
        MemberCode = p.Member != null ? p.Member.MemberCode : string.Empty,
        MemberName = p.Member != null ? p.Member.FullName : string.Empty,
        MemberPhone = p.Member != null ? p.Member.Phone : null,
        SubscriptionId = p.SubscriptionId,
        SubscriptionCode = p.Subscription != null ? p.Subscription.SubscriptionCode : null,
        PlanName = p.Subscription != null && p.Subscription.MembershipPlan != null
            ? p.Subscription.MembershipPlan.Name
            : null,
        Amount = p.Amount,
        DiscountAmount = p.DiscountAmount,
        TaxAmount = p.TaxAmount,
        FinalAmount = p.FinalAmount,
        RefundedAmount = p.RefundedAmount,
        PaymentMethodId = p.PaymentMethodId,
        PaymentMethodName = p.PaymentMethod != null ? p.PaymentMethod.Name : string.Empty,
        TransactionReference = p.TransactionReference,
        PayerVpa = p.PayerVpa,
        PaymentDate = p.PaymentDate,
        Status = p.Status,
        CollectedByUserId = p.CollectedByUserId,
        CollectedByName = p.CollectedByUser != null ? p.CollectedByUser.FullName : null,
        ConfirmedAtUtc = p.ConfirmedAtUtc,
        Notes = p.Notes,
        CreatedAt = p.CreatedAt
    };

    private static readonly Expression<Func<Attendance, AttendanceDto>> AttendanceProjection = a => new AttendanceDto
    {
        Id = a.Id,
        MemberId = a.MemberId,
        MemberCode = a.Member != null ? a.Member.MemberCode : string.Empty,
        MemberName = a.Member != null ? a.Member.FullName : string.Empty,
        ProfilePhotoPath = a.Member != null ? a.Member.ProfilePhotoPath : null,
        AttendanceDate = a.AttendanceDate,
        CheckInTime = a.CheckInTime,
        CheckOutTime = a.CheckOutTime,
        Status = a.Status,
        DurationMinutes = a.DurationMinutes,
        CheckInMethod = a.CheckInMethod,
        SubscriptionId = a.SubscriptionId,
        PlanName = a.Subscription != null && a.Subscription.MembershipPlan != null
            ? a.Subscription.MembershipPlan.Name
            : null,
        Notes = a.Notes
    };

    private static readonly Expression<Func<Subscription, SubscriptionDto>> SubscriptionProjection =
        s => new SubscriptionDto
        {
            Id = s.Id,
            SubscriptionCode = s.SubscriptionCode,
            MemberId = s.MemberId,
            MemberCode = s.Member != null ? s.Member.MemberCode : string.Empty,
            MemberName = s.Member != null ? s.Member.FullName : string.Empty,
            MemberPhone = s.Member != null ? s.Member.Phone : null,
            MembershipPlanId = s.MembershipPlanId,
            PlanName = s.MembershipPlan != null ? s.MembershipPlan.Name : string.Empty,
            StartDate = s.StartDate,
            EndDate = s.EndDate,
            PlanAmount = s.PlanAmount,
            RegistrationFee = s.RegistrationFee,
            DiscountAmount = s.DiscountAmount,
            TaxPercent = s.TaxPercent,
            TaxAmount = s.TaxAmount,
            FinalAmount = s.FinalAmount,
            PaidAmount = s.PaidAmount,
            OutstandingAmount = s.FinalAmount - s.PaidAmount > 0m ? s.FinalAmount - s.PaidAmount : 0m,
            PaymentStatus = s.PaymentStatus,
            Status = s.Status,
            GracePeriodDays = s.GracePeriodDays,
            IsRenewal = s.IsRenewal,
            PreviousSubscriptionId = s.PreviousSubscriptionId,
            FreezeStartDate = s.FreezeStartDate,
            FreezeEndDate = s.FreezeEndDate,
            FrozenDaysUsed = s.FrozenDaysUsed,
            CancelledAt = s.CancelledAt,
            CancellationReason = s.CancellationReason,
            AssignedTrainerId = s.AssignedTrainerId,
            AssignedTrainerName = s.AssignedTrainer != null ? s.AssignedTrainer.FullName : null,
            Notes = s.Notes,
            CreatedAt = s.CreatedAt
        };

    private static readonly Expression<Func<Notification, NotificationDto>> NotificationProjection =
        n => new NotificationDto
        {
            Id = n.Id,
            Type = n.Type,
            Severity = n.Severity,
            Title = n.Title,
            Message = n.Message,
            UserId = n.UserId,
            MemberId = n.MemberId,
            MemberName = n.Member != null ? n.Member.FullName : null,
            EntityName = n.EntityName,
            EntityId = n.EntityId,
            IsRead = n.IsRead,
            ReadAtUtc = n.ReadAtUtc,
            CreatedAtUtc = n.CreatedAtUtc,
            ExpiresAtUtc = n.ExpiresAtUtc
        };

    private readonly GymDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly ISettingsService _settings;
    private readonly IAuditQueryService _auditQuery;
    private readonly ILogger<DashboardService> _logger;

    public DashboardService(
        GymDbContext db,
        IDateTimeProvider clock,
        ICurrentUserService currentUser,
        ISettingsService settings,
        IAuditQueryService auditQuery,
        ILogger<DashboardService> logger)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _settings = settings;
        _auditQuery = auditQuery;
        _logger = logger;
    }

    // ---------------------------------------------------------------- stats

    public Task<DashboardStatsDto> GetStatsAsync(CancellationToken ct = default) =>
        BuildStatsAsync(null, ct);

    /// <summary>
    /// Builds every counter card. <paramref name="gym"/> is passed in by
    /// <see cref="GetDashboardAsync"/> so the settings row is read only once per call.
    /// </summary>
    private async Task<DashboardStatsDto> BuildStatsAsync(GymSettingsDto? gym, CancellationToken ct)
    {
        gym ??= await _settings.GetGymSettingsAsync(ct).ConfigureAwait(false);

        var today = _clock.Today.Date;
        var tomorrow = today.AddDays(1);
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var nextMonthStart = monthStart.AddMonths(1);
        var lastMonthStart = monthStart.AddMonths(-1);
        var yearStart = new DateTime(today.Year, 1, 1);
        var nextYearStart = yearStart.AddYears(1);
        var reminderDays = gym.ExpiryReminderDays < 0 ? 0 : gym.ExpiryReminderDays;
        var soonLimit = today.AddDays(reminderDays);

        var stats = new DashboardStatsDto { CurrencySymbol = gym.CurrencySymbol };

        // Members by status: at most one row per enum value, so the group is safe to materialise.
        var memberCounts = await _db.Members.AsNoTracking()
            .GroupBy(m => m.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);

        stats.TotalMembers = memberCounts.Sum(x => x.Count);
        stats.ActiveMembers = memberCounts.Where(x => x.Status == MemberStatus.Active).Sum(x => x.Count);
        // Everything that is not Active (Inactive, Suspended, Expired) counts as inactive on the card.
        stats.InactiveMembers = stats.TotalMembers - stats.ActiveMembers;

        var subscriptionCounts = await _db.Subscriptions.AsNoTracking()
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);

        stats.ActiveSubscriptions = subscriptionCounts
            .Where(x => x.Status == SubscriptionStatus.Active).Sum(x => x.Count);
        stats.FrozenSubscriptions = subscriptionCounts
            .Where(x => x.Status == SubscriptionStatus.Frozen).Sum(x => x.Count);
        stats.ExpiredMemberships = subscriptionCounts
            .Where(x => x.Status == SubscriptionStatus.Expired).Sum(x => x.Count);

        stats.ExpiringSoon = await _db.Subscriptions.AsNoTracking()
            .CountAsync(s => s.Status == SubscriptionStatus.Active
                             && s.EndDate >= today
                             && s.EndDate <= soonLimit, ct)
            .ConfigureAwait(false);

        var attendanceToday = await _db.Attendance.AsNoTracking()
            .Where(a => a.AttendanceDate >= today && a.AttendanceDate < tomorrow)
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);

        stats.TodayAttendance = attendanceToday.Sum(x => x.Count);
        stats.CurrentlyInGym = attendanceToday
            .Where(x => x.Status == AttendanceStatus.CheckedIn).Sum(x => x.Count);

        stats.TodayRevenue = await PaidRevenueAsync(today, tomorrow, ct).ConfigureAwait(false);
        stats.MonthRevenue = await PaidRevenueAsync(monthStart, nextMonthStart, ct).ConfigureAwait(false);
        stats.YearRevenue = await PaidRevenueAsync(yearStart, nextYearStart, ct).ConfigureAwait(false);
        var lastMonthRevenue = await PaidRevenueAsync(lastMonthStart, monthStart, ct).ConfigureAwait(false);

        var monthExpenses = await _db.Expenses.AsNoTracking()
            .Where(e => e.ExpenseDate >= monthStart && e.ExpenseDate < nextMonthStart)
            .SumAsync(e => (decimal?)e.Amount, ct).ConfigureAwait(false);
        stats.MonthExpenses = Round(monthExpenses ?? 0m);

        var pending = await _db.Subscriptions.AsNoTracking()
            .Where(s => s.Status != SubscriptionStatus.Cancelled && s.FinalAmount > s.PaidAmount)
            .GroupBy(s => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Amount = g.Sum(x => x.FinalAmount - x.PaidAmount)
            })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        stats.PendingPaymentsCount = pending?.Count ?? 0;
        stats.PendingPaymentsAmount = Round(pending?.Amount ?? 0m);

        var joinCounts = await _db.Members.AsNoTracking()
            .Where(m => m.JoiningDate >= lastMonthStart && m.JoiningDate < nextMonthStart)
            .GroupBy(m => 1)
            .Select(g => new
            {
                Today = g.Count(m => m.JoiningDate >= today && m.JoiningDate < tomorrow),
                ThisMonth = g.Count(m => m.JoiningDate >= monthStart),
                LastMonth = g.Count(m => m.JoiningDate < monthStart)
            })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        stats.NewMembersToday = joinCounts?.Today ?? 0;
        stats.NewMembersThisMonth = joinCounts?.ThisMonth ?? 0;
        var newMembersLastMonth = joinCounts?.LastMonth ?? 0;

        var trainerCounts = await _db.Trainers.AsNoTracking()
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);

        stats.TotalTrainers = trainerCounts.Sum(x => x.Count);
        stats.ActiveTrainers = trainerCounts
            .Where(x => x.Status == TrainerStatus.Active).Sum(x => x.Count);

        var userId = _currentUser.UserId;
        stats.UnreadNotifications = await _db.Notifications.AsNoTracking()
            .CountAsync(n => !n.IsRead && (n.UserId == null || n.UserId == userId), ct)
            .ConfigureAwait(false);

        stats.RevenueGrowthPercent = GrowthPercent(stats.MonthRevenue, lastMonthRevenue);
        stats.MemberGrowthPercent = GrowthPercent(stats.NewMembersThisMonth, newMembersLastMonth);

        return stats;
    }

    // ---------------------------------------------------------------- dashboard

    public async Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var gym = await _settings.GetGymSettingsAsync(ct).ConfigureAwait(false);

        var today = _clock.Today.Date;
        var tomorrow = today.AddDays(1);
        var reminderDays = gym.ExpiryReminderDays < 0 ? 0 : gym.ExpiryReminderDays;
        var soonLimit = today.AddDays(reminderDays);

        // EF Core forbids concurrent operations on one DbContext, so everything runs sequentially.
        var dashboard = new DashboardDto
        {
            Stats = await BuildStatsAsync(gym, ct).ConfigureAwait(false),
            GeneratedAtUtc = _clock.UtcNow
        };

        dashboard.RevenueDaily = await RevenueDailyAsync(today, tomorrow, ct).ConfigureAwait(false);
        dashboard.RevenueWeekly = await RevenueWeeklyAsync(today, tomorrow, ct).ConfigureAwait(false);
        dashboard.RevenueMonthly = await RevenueMonthlyAsync(today, ct).ConfigureAwait(false);
        dashboard.MembershipGrowth = await MembershipGrowthAsync(today, ct).ConfigureAwait(false);
        dashboard.AttendanceTrend = await AttendanceTrendAsync(null, today, tomorrow, ct).ConfigureAwait(false);
        dashboard.PlanDistribution = await PlanDistributionAsync(ct).ConfigureAwait(false);
        dashboard.PaymentMethodDistribution =
            await PaymentMethodDistributionAsync(today, tomorrow, ct).ConfigureAwait(false);

        dashboard.RecentTransactions = await _db.Payments.AsNoTracking()
            .OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.Id)
            .Take(RecentTake)
            .Select(PaymentProjection)
            .ToListAsync(ct).ConfigureAwait(false);

        try
        {
            dashboard.RecentActivities = await _auditQuery
                .GetRecentActivityAsync(RecentTake, ct).ConfigureAwait(false)
                ?? new List<RecentActivityDto>();
        }
        catch (Exception ex)
        {
            // The activity feed is decoration; a failure there must not blank the dashboard.
            _logger.LogWarning(ex, "Recent activity feed could not be loaded for the dashboard.");
            dashboard.RecentActivities = new List<RecentActivityDto>();
        }

        dashboard.ExpiringSoonMembers =
            await ExpiringSoonMembersAsync(today, soonLimit, reminderDays, ct).ConfigureAwait(false);

        return dashboard;
    }

    // ---------------------------------------------------------------- member dashboard

    public async Task<MemberDashboardDto> GetMemberDashboardAsync(int memberId, CancellationToken ct = default)
    {
        var today = _clock.Today.Date;
        var tomorrow = today.AddDays(1);
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var nextMonthStart = monthStart.AddMonths(1);

        var row = await _db.Members.AsNoTracking()
            .Where(m => m.Id == memberId)
            .Select(m => new
            {
                Member = m,
                TrainerName = m.AssignedTrainer != null ? m.AssignedTrainer.FullName : null,
                HasUserAccount = _db.Users.Any(u => u.MemberId == m.Id),
                TotalPaid = m.Payments
                    .Where(p => p.Status == PaymentStatus.Paid
                                || p.Status == PaymentStatus.PartiallyPaid
                                || p.Status == PaymentStatus.PartiallyRefunded)
                    .Sum(p => (decimal?)(p.FinalAmount - p.RefundedAmount)) ?? 0m,
                Outstanding = m.Subscriptions
                    .Where(s => s.Status != SubscriptionStatus.Cancelled && s.FinalAmount > s.PaidAmount)
                    .Sum(s => (decimal?)(s.FinalAmount - s.PaidAmount)) ?? 0m,
                TotalVisits = m.AttendanceRecords.Count,
                VisitsThisMonth = m.AttendanceRecords
                    .Count(a => a.AttendanceDate >= monthStart && a.AttendanceDate < nextMonthStart),
                LastVisit = m.AttendanceRecords.Max(a => (DateTime?)a.AttendanceDate)
            })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new NotFoundAppException("Member", memberId);

        var member = row.Member;

        var active = await _db.Subscriptions.AsNoTracking()
            .Where(s => s.MemberId == memberId && s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.EndDate).ThenByDescending(s => s.Id)
            .Select(SubscriptionProjection)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        var daysRemaining = 0;
        if (active is not null)
        {
            daysRemaining = (active.EndDate.Date - today).Days;
            active.DaysRemaining = daysRemaining;
            active.IsExpiringSoon = daysRemaining is >= 0 and <= 7;
        }

        var dto = new MemberDashboardDto
        {
            Member = new MemberDetailDto
            {
                Id = member.Id,
                MemberCode = member.MemberCode,
                FullName = member.FullName,
                Gender = member.Gender,
                DateOfBirth = member.DateOfBirth,
                Age = member.DateOfBirth.HasValue ? CalculateAge(member.DateOfBirth.Value.Date, today) : null,
                Phone = member.Phone,
                Email = member.Email,
                Address = member.Address,
                City = member.City,
                PostalCode = member.PostalCode,
                EmergencyContactName = member.EmergencyContactName,
                EmergencyContactPhone = member.EmergencyContactPhone,
                EmergencyContactRelation = member.EmergencyContactRelation,
                JoiningDate = member.JoiningDate,
                ProfilePhotoPath = member.ProfilePhotoPath,
                BloodGroup = member.BloodGroup,
                HeightCm = member.HeightCm,
                WeightKg = member.WeightKg,
                Status = member.Status,
                Notes = member.Notes,
                AssignedTrainerId = member.AssignedTrainerId,
                AssignedTrainerName = row.TrainerName,
                HasUserAccount = row.HasUserAccount,
                CreatedAt = member.CreatedAt,
                UpdatedAt = member.UpdatedAt,
                ActiveSubscription = active,
                TotalPaid = Round(row.TotalPaid),
                TotalOutstanding = Round(row.Outstanding),
                TotalVisits = row.TotalVisits,
                LastVisitDate = row.LastVisit
            },
            ActiveSubscription = active,
            DaysRemaining = daysRemaining,
            OutstandingAmount = Round(row.Outstanding),
            VisitsThisMonth = row.VisitsThisMonth,
            TotalVisits = row.TotalVisits,
            LastVisit = row.LastVisit
        };

        dto.RecentAttendance = await _db.Attendance.AsNoTracking()
            .Where(a => a.MemberId == memberId)
            .OrderByDescending(a => a.CheckInTime).ThenByDescending(a => a.Id)
            .Take(RecentTake)
            .Select(AttendanceProjection)
            .ToListAsync(ct).ConfigureAwait(false);

        dto.RecentPayments = await _db.Payments.AsNoTracking()
            .Where(p => p.MemberId == memberId)
            .OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.Id)
            .Take(RecentTake)
            .Select(PaymentProjection)
            .ToListAsync(ct).ConfigureAwait(false);

        dto.ActiveWorkoutPlan = await LoadActiveWorkoutPlanAsync(memberId, ct).ConfigureAwait(false);

        dto.Notifications = await _db.Notifications.AsNoTracking()
            .Where(n => n.MemberId == memberId && !n.IsRead)
            .OrderByDescending(n => n.CreatedAtUtc).ThenByDescending(n => n.Id)
            .Take(RecentTake)
            .Select(NotificationProjection)
            .ToListAsync(ct).ConfigureAwait(false);

        dto.AttendanceTrend = await AttendanceTrendAsync(memberId, today, tomorrow, ct).ConfigureAwait(false);

        return dto;
    }

    private async Task<MemberWorkoutPlanDto?> LoadActiveWorkoutPlanAsync(int memberId, CancellationToken ct)
    {
        var assignment = await _db.MemberWorkoutPlans.AsNoTracking()
            .Where(a => a.MemberId == memberId && a.IsActive)
            .OrderByDescending(a => a.StartDate).ThenByDescending(a => a.Id)
            .Select(a => new MemberWorkoutPlanDto
            {
                Id = a.Id,
                MemberId = a.MemberId,
                MemberName = a.Member != null ? a.Member.FullName : string.Empty,
                WorkoutPlanId = a.WorkoutPlanId,
                WorkoutPlanName = a.WorkoutPlan != null ? a.WorkoutPlan.Name : string.Empty,
                TrainerId = a.TrainerId,
                TrainerName = a.Trainer != null ? a.Trainer.FullName : null,
                StartDate = a.StartDate,
                EndDate = a.EndDate,
                IsActive = a.IsActive,
                Notes = a.Notes
            })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (assignment is null) return null;

        var planId = assignment.WorkoutPlanId;

        var lines = await _db.WorkoutPlanExercises.AsNoTracking()
            .Where(e => e.WorkoutPlanId == planId)
            .OrderBy(e => e.DayOfWeek).ThenBy(e => e.DisplayOrder).ThenBy(e => e.Id)
            .Select(e => new
            {
                e.Id,
                e.ExerciseId,
                ExerciseName = e.Exercise != null ? e.Exercise.Name : string.Empty,
                MuscleGroup = e.Exercise != null ? (MuscleGroup?)e.Exercise.PrimaryMuscleGroup : null,
                e.DayOfWeek,
                e.DisplayOrder,
                e.Sets,
                e.Repetitions,
                e.TargetWeightKg,
                e.RestSeconds,
                e.DurationMinutes,
                e.Notes
            })
            .ToListAsync(ct).ConfigureAwait(false);

        assignment.Exercises = lines.Select(e => new WorkoutPlanExerciseDto
        {
            Id = e.Id,
            ExerciseId = e.ExerciseId,
            ExerciseName = e.ExerciseName,
            MuscleGroupText = e.MuscleGroup?.ToString(),
            DayOfWeek = e.DayOfWeek,
            DisplayOrder = e.DisplayOrder,
            Sets = e.Sets,
            Repetitions = e.Repetitions,
            TargetWeightKg = e.TargetWeightKg,
            RestSeconds = e.RestSeconds,
            DurationMinutes = e.DurationMinutes,
            Notes = e.Notes
        }).ToList();

        return assignment;
    }

    // ---------------------------------------------------------------- series builders

    private async Task<List<ChartSeriesDto>> RevenueDailyAsync(DateTime today, DateTime tomorrow,
        CancellationToken ct)
    {
        var start = today.AddDays(-(TrendDays - 1));

        var rows = await _db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Paid && p.PaymentDate >= start && p.PaymentDate < tomorrow)
            .GroupBy(p => p.PaymentDate.Date)
            .Select(g => new { Day = g.Key, Amount = g.Sum(x => x.FinalAmount - x.RefundedAmount) })
            .ToListAsync(ct).ConfigureAwait(false);

        var byDay = ToDayMap(rows.Select(r => (r.Day, r.Amount)));

        var series = new List<ChartSeriesDto>(TrendDays);
        for (var i = 0; i < TrendDays; i++)
        {
            var day = start.AddDays(i);
            series.Add(new ChartSeriesDto
            {
                Label = day.ToString(DayLabelFormat, CultureInfo.InvariantCulture),
                Value = byDay.TryGetValue(day, out var amount) ? Round(amount) : 0m,
                Date = day
            });
        }

        return series;
    }

    private async Task<List<ChartSeriesDto>> RevenueWeeklyAsync(DateTime today, DateTime tomorrow,
        CancellationToken ct)
    {
        var start = today.AddDays(-(7 * TrendWeeks - 1));

        // At most 84 grouped rows come back; the weekly buckets are folded in memory.
        var rows = await _db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Paid && p.PaymentDate >= start && p.PaymentDate < tomorrow)
            .GroupBy(p => p.PaymentDate.Date)
            .Select(g => new { Day = g.Key, Amount = g.Sum(x => x.FinalAmount - x.RefundedAmount) })
            .ToListAsync(ct).ConfigureAwait(false);

        var byDay = ToDayMap(rows.Select(r => (r.Day, r.Amount)));

        var series = new List<ChartSeriesDto>(TrendWeeks);
        for (var week = 0; week < TrendWeeks; week++)
        {
            var weekStart = start.AddDays(7 * week);
            var total = 0m;
            for (var d = 0; d < 7; d++)
            {
                if (byDay.TryGetValue(weekStart.AddDays(d), out var amount)) total += amount;
            }

            series.Add(new ChartSeriesDto
            {
                Label = $"W{week + 1}",
                Value = Round(total),
                Date = weekStart,
                Category = $"{weekStart.ToString(DayLabelFormat, CultureInfo.InvariantCulture)} - " +
                           $"{weekStart.AddDays(6).ToString(DayLabelFormat, CultureInfo.InvariantCulture)}"
            });
        }

        return series;
    }

    private async Task<List<ChartSeriesDto>> RevenueMonthlyAsync(DateTime today, CancellationToken ct)
    {
        var firstMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-(TrendMonths - 1));
        var limit = new DateTime(today.Year, today.Month, 1).AddMonths(1);

        var rows = await _db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Paid && p.PaymentDate >= firstMonth && p.PaymentDate < limit)
            .GroupBy(p => new { p.PaymentDate.Year, p.PaymentDate.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Amount = g.Sum(x => x.FinalAmount - x.RefundedAmount)
            })
            .ToListAsync(ct).ConfigureAwait(false);

        var byMonth = new Dictionary<DateTime, decimal>();
        foreach (var r in rows) byMonth[new DateTime(r.Year, r.Month, 1)] = r.Amount;

        var series = new List<ChartSeriesDto>(TrendMonths);
        for (var i = 0; i < TrendMonths; i++)
        {
            var month = firstMonth.AddMonths(i);
            series.Add(new ChartSeriesDto
            {
                Label = month.ToString(MonthLabelFormat, CultureInfo.InvariantCulture),
                Value = byMonth.TryGetValue(month, out var amount) ? Round(amount) : 0m,
                Date = month
            });
        }

        return series;
    }

    private async Task<List<ChartSeriesDto>> MembershipGrowthAsync(DateTime today, CancellationToken ct)
    {
        var firstMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-(TrendMonths - 1));
        var limit = new DateTime(today.Year, today.Month, 1).AddMonths(1);

        var joinedBefore = await _db.Members.AsNoTracking()
            .CountAsync(m => m.JoiningDate < firstMonth, ct).ConfigureAwait(false);

        var rows = await _db.Members.AsNoTracking()
            .Where(m => m.JoiningDate >= firstMonth && m.JoiningDate < limit)
            .GroupBy(m => new { m.JoiningDate.Year, m.JoiningDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);

        var byMonth = new Dictionary<DateTime, int>();
        foreach (var r in rows) byMonth[new DateTime(r.Year, r.Month, 1)] = r.Count;

        var series = new List<ChartSeriesDto>(TrendMonths);
        var running = (decimal)joinedBefore;

        for (var i = 0; i < TrendMonths; i++)
        {
            var month = firstMonth.AddMonths(i);
            var added = byMonth.TryGetValue(month, out var count) ? count : 0;
            running += added;

            series.Add(new ChartSeriesDto
            {
                Label = month.ToString(MonthLabelFormat, CultureInfo.InvariantCulture),
                Value = added,
                SecondaryValue = running,
                Date = month
            });
        }

        return series;
    }

    private async Task<List<ChartSeriesDto>> AttendanceTrendAsync(int? memberId, DateTime today,
        DateTime tomorrow, CancellationToken ct)
    {
        var start = today.AddDays(-(TrendDays - 1));

        var query = _db.Attendance.AsNoTracking()
            .Where(a => a.AttendanceDate >= start && a.AttendanceDate < tomorrow);

        if (memberId.HasValue) query = query.Where(a => a.MemberId == memberId.Value);

        var rows = await query
            .GroupBy(a => a.AttendanceDate.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);

        var byDay = new Dictionary<DateTime, int>();
        foreach (var r in rows) byDay[r.Day.Date] = r.Count;

        var series = new List<ChartSeriesDto>(TrendDays);
        for (var i = 0; i < TrendDays; i++)
        {
            var day = start.AddDays(i);
            series.Add(new ChartSeriesDto
            {
                Label = day.ToString(DayLabelFormat, CultureInfo.InvariantCulture),
                Value = byDay.TryGetValue(day, out var count) ? count : 0,
                Date = day
            });
        }

        return series;
    }

    private async Task<List<ChartSeriesDto>> PlanDistributionAsync(CancellationToken ct)
    {
        // Driven from the plan table so the count is a correlated aggregate and the result is
        // bounded by the number of plans.
        var rows = await _db.MembershipPlans.AsNoTracking()
            .Select(p => new
            {
                p.Name,
                Count = p.Subscriptions.Count(s => s.Status == SubscriptionStatus.Active)
            })
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .ToListAsync(ct).ConfigureAwait(false);

        return rows.Select(x => new ChartSeriesDto
        {
            Label = x.Name,
            Category = x.Name,
            Value = x.Count
        }).ToList();
    }

    private async Task<List<ChartSeriesDto>> PaymentMethodDistributionAsync(DateTime today, DateTime tomorrow,
        CancellationToken ct)
    {
        var since = today.AddDays(-(PaymentMethodWindowDays - 1));

        var rows = await _db.PaymentMethods.AsNoTracking()
            .Select(pm => new
            {
                pm.Name,
                Amount = pm.Payments
                    .Where(p => p.Status == PaymentStatus.Paid
                                && p.PaymentDate >= since
                                && p.PaymentDate < tomorrow)
                    .Sum(p => (decimal?)(p.FinalAmount - p.RefundedAmount)) ?? 0m
            })
            .Where(x => x.Amount != 0m)
            .OrderByDescending(x => x.Amount)
            .ToListAsync(ct).ConfigureAwait(false);

        return rows.Select(x => new ChartSeriesDto
        {
            Label = x.Name,
            Category = x.Name,
            Value = Round(x.Amount)
        }).ToList();
    }

    private async Task<List<MemberListDto>> ExpiringSoonMembersAsync(DateTime today, DateTime soonLimit,
        int reminderDays, CancellationToken ct)
    {
        // A member may hold more than one active subscription, so a few extra rows are fetched and
        // de-duplicated by member before the list is trimmed to RecentTake.
        var rows = await _db.Subscriptions.AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Active
                        && s.EndDate >= today
                        && s.EndDate <= soonLimit)
            .OrderBy(s => s.EndDate).ThenBy(s => s.Id)
            .Take(RecentTake * 3)
            .Select(s => new
            {
                s.MemberId,
                MemberCode = s.Member != null ? s.Member.MemberCode : string.Empty,
                FullName = s.Member != null ? s.Member.FullName : string.Empty,
                Gender = s.Member != null ? s.Member.Gender : Gender.Unspecified,
                Phone = s.Member != null ? s.Member.Phone : string.Empty,
                Email = s.Member != null ? s.Member.Email : null,
                JoiningDate = s.Member != null ? s.Member.JoiningDate : s.StartDate,
                MemberStatus = s.Member != null ? s.Member.Status : Domain.Enums.MemberStatus.Active,
                ProfilePhotoPath = s.Member != null ? s.Member.ProfilePhotoPath : null,
                TrainerName = s.Member != null && s.Member.AssignedTrainer != null
                    ? s.Member.AssignedTrainer.FullName
                    : null,
                PlanName = s.MembershipPlan != null ? s.MembershipPlan.Name : null,
                s.EndDate,
                SubscriptionStatus = s.Status,
                Outstanding = s.FinalAmount - s.PaidAmount > 0m ? s.FinalAmount - s.PaidAmount : 0m
            })
            .ToListAsync(ct).ConfigureAwait(false);

        return rows
            .GroupBy(x => x.MemberId)
            .Select(g => g.First())
            .OrderBy(x => x.EndDate)
            .Take(RecentTake)
            .Select(x =>
            {
                var days = (x.EndDate.Date - today).Days;
                return new MemberListDto
                {
                    Id = x.MemberId,
                    MemberCode = x.MemberCode,
                    FullName = x.FullName,
                    Gender = x.Gender,
                    Phone = x.Phone,
                    Email = x.Email,
                    JoiningDate = x.JoiningDate,
                    Status = x.MemberStatus,
                    ProfilePhotoPath = x.ProfilePhotoPath,
                    AssignedTrainerName = x.TrainerName,
                    CurrentPlanName = x.PlanName,
                    SubscriptionEndDate = x.EndDate,
                    DaysRemaining = days,
                    SubscriptionStatus = x.SubscriptionStatus,
                    OutstandingAmount = Round(x.Outstanding),
                    IsExpiringSoon = days >= 0 && days <= reminderDays
                };
            })
            .ToList();
    }

    // ---------------------------------------------------------------- helpers

    private async Task<decimal> PaidRevenueAsync(DateTime fromInclusive, DateTime toExclusive,
        CancellationToken ct)
    {
        var total = await _db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Paid
                        && p.PaymentDate >= fromInclusive
                        && p.PaymentDate < toExclusive)
            .SumAsync(p => (decimal?)(p.FinalAmount - p.RefundedAmount), ct)
            .ConfigureAwait(false);

        return Round(total ?? 0m);
    }

    private static Dictionary<DateTime, decimal> ToDayMap(IEnumerable<(DateTime Day, decimal Amount)> rows)
    {
        var map = new Dictionary<DateTime, decimal>();
        foreach (var (day, amount) in rows) map[day.Date] = amount;
        return map;
    }

    /// <summary>Percentage change, or 0 when the previous period had nothing to compare against.</summary>
    private static decimal GrowthPercent(decimal current, decimal previous) =>
        previous <= 0m ? 0m : Math.Round((current - previous) / previous * 100m, 2, MidpointRounding.AwayFromZero);

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static int CalculateAge(DateTime dateOfBirth, DateTime asOf)
    {
        var age = asOf.Year - dateOfBirth.Year;
        if (dateOfBirth.Date > asOf.AddYears(-age)) age--;
        return age < 0 ? 0 : age;
    }
}
