using System.Globalization;
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
/// Generic, strictly read-only reporting engine. Every report returns the same shape-agnostic
/// <see cref="ReportResultDto"/> — column metadata plus dictionary rows — so a single client-side
/// DataGrid renders all of them and the Excel/PDF exporters need no report specific code.
/// All filtering, aggregation and paging happens in SQL; enum text and derived day counts are
/// formatted after materialisation because they do not translate to SQL.
/// </summary>
public sealed class ReportService : IReportService
{
    /// <summary>
    /// Page size used by the exports. <see cref="PagedRequest.PageSize"/> clamps itself to 200,
    /// so the effective size is threaded through the engine instead of being set on the request.
    /// </summary>
    private const int ExportPageSize = 100_000;

    private const string DateLabelFormat = "dd MMM yyyy";
    private const string DayLabelFormat = "dd MMM";
    private const string MonthLabelFormat = "MMM yy";
    private const string ExcelContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string PdfContentType = "application/pdf";

    private readonly GymDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly ISettingsService _settings;
    private readonly IAuditQueryService _auditQuery;
    private readonly IExcelExportService _excel;
    private readonly IPdfExportService _pdf;
    private readonly ILogger<ReportService> _logger;

    public ReportService(
        GymDbContext db,
        IDateTimeProvider clock,
        ICurrentUserService currentUser,
        ISettingsService settings,
        IAuditQueryService auditQuery,
        IExcelExportService excel,
        IPdfExportService pdf,
        ILogger<ReportService> logger)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _settings = settings;
        _auditQuery = auditQuery;
        _excel = excel;
        _pdf = pdf;
        _logger = logger;
    }

    // ---------------------------------------------------------------- engine

    public Task<ReportResultDto> RunAsync(ReportRequestDto request, CancellationToken ct = default) =>
        RunCoreAsync(request, null, ct);

    private async Task<ReportResultDto> RunCoreAsync(ReportRequestDto request, int? exportPageSize,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var gym = await _settings.GetGymSettingsAsync(ct).ConfigureAwait(false);
        var (from, to) = ResolveRange(request);

        var pageSize = exportPageSize ?? request.PageSize;
        var pageNumber = exportPageSize.HasValue ? 1 : request.PageNumber;
        var context = new ReportContext(request, gym, from, to, pageSize, pageNumber);

        var report = new ReportResultDto
        {
            ReportType = request.ReportType,
            Title = request.ReportType.ToString(),
            FromDate = from,
            ToDate = to,
            GeneratedAtUtc = _clock.UtcNow,
            GeneratedByName = _currentUser.FullName,
            CurrencySymbol = string.IsNullOrWhiteSpace(gym.CurrencySymbol) ? "₹" : gym.CurrencySymbol,
            PageNumber = pageNumber,
            PageSize = pageSize
        };

        switch (request.ReportType)
        {
            case ReportType.MemberList:
            case ReportType.ActiveMembers:
            case ReportType.ExpiredMembers:
            case ReportType.NewRegistrations:
                await MemberReportAsync(report, context, ct).ConfigureAwait(false);
                break;

            case ReportType.SubscriptionReport:
                await SubscriptionReportAsync(report, context, ct).ConfigureAwait(false);
                break;

            case ReportType.RenewalReport:
                await RenewalReportAsync(report, context, ct).ConfigureAwait(false);
                break;

            case ReportType.AttendanceReport:
                await AttendanceReportAsync(report, context, ct).ConfigureAwait(false);
                break;

            case ReportType.DailyPaymentReport:
                await DailyPaymentReportAsync(report, context, ct).ConfigureAwait(false);
                break;

            case ReportType.MonthlyPaymentReport:
                await MonthlyPaymentReportAsync(report, context, ct).ConfigureAwait(false);
                break;

            case ReportType.RevenueReport:
                await RevenueReportAsync(report, context, ct).ConfigureAwait(false);
                break;

            case ReportType.OutstandingPaymentReport:
                await OutstandingPaymentReportAsync(report, context, ct).ConfigureAwait(false);
                break;

            case ReportType.TrainerReport:
                await TrainerReportAsync(report, context, ct).ConfigureAwait(false);
                break;

            case ReportType.WorkoutActivityReport:
                await WorkoutActivityReportAsync(report, context, ct).ConfigureAwait(false);
                break;

            case ReportType.AuditReport:
                await AuditReportAsync(report, context, ct).ConfigureAwait(false);
                break;

            case ReportType.ExpenseReport:
                await ExpenseReportAsync(report, context, ct).ConfigureAwait(false);
                break;

            case ReportType.ProfitAndLossReport:
                await ProfitAndLossReportAsync(report, context, ct).ConfigureAwait(false);
                break;

            default:
                throw new NotFoundAppException(
                    $"Report type '{(int)request.ReportType}' is not a known report.");
        }

        return report;
    }

    // ---------------------------------------------------------------- members

    private async Task MemberReportAsync(ReportResultDto report, ReportContext ctx, CancellationToken ct)
    {
        var today = _clock.Today.Date;
        var type = ctx.Request.ReportType;

        report.Title = type switch
        {
            ReportType.ActiveMembers => "Active Members",
            ReportType.ExpiredMembers => "Expired Memberships",
            ReportType.NewRegistrations => "New Registrations",
            _ => "Member List"
        };

        var query = _db.Members.AsNoTracking();

        switch (type)
        {
            case ReportType.ActiveMembers:
                query = query.Where(m => m.Status == MemberStatus.Active);
                break;

            case ReportType.ExpiredMembers:
                // No subscription that still runs today: never subscribed, or the last term ended.
                query = query.Where(m => !m.Subscriptions.Any(s => s.EndDate >= today));
                break;

            case ReportType.NewRegistrations:
                query = query.Where(m => m.JoiningDate >= ctx.FromInclusive && m.JoiningDate < ctx.ToExclusive);
                break;
        }

        if (ctx.Request.Status.HasValue)
        {
            var status = (MemberStatus)ctx.Request.Status.Value;
            query = query.Where(m => m.Status == status);
        }

        if (ctx.Request.TrainerId.HasValue)
        {
            var trainerId = ctx.Request.TrainerId.Value;
            query = query.Where(m => m.AssignedTrainerId == trainerId);
        }

        if (ctx.Request.MembershipPlanId.HasValue)
        {
            var planId = ctx.Request.MembershipPlanId.Value;
            query = query.Where(m => m.Subscriptions.Any(s => s.MembershipPlanId == planId));
        }

        if (ctx.Request.MemberId.HasValue)
        {
            var memberId = ctx.Request.MemberId.Value;
            query = query.Where(m => m.Id == memberId);
        }

        if (!string.IsNullOrWhiteSpace(ctx.Request.Search))
        {
            var term = ctx.Request.Search.Trim();
            query = query.Where(m => m.MemberCode.Contains(term)
                                     || m.FullName.Contains(term)
                                     || m.Phone.Contains(term)
                                     || (m.Email != null && m.Email.Contains(term)));
        }

        report.Columns.AddRange(new[]
        {
            new ReportColumnDto("code", "Member Code"),
            new ReportColumnDto("name", "Member Name"),
            new ReportColumnDto("gender", "Gender"),
            new ReportColumnDto("phone", "Phone"),
            new ReportColumnDto("email", "Email"),
            new ReportColumnDto("joiningDate", "Joined On", "date"),
            new ReportColumnDto("status", "Status"),
            new ReportColumnDto("plan", "Current Plan"),
            new ReportColumnDto("endDate", "Valid Till", "date"),
            new ReportColumnDto("daysRemaining", "Days Left", "int"),
            new ReportColumnDto("outstanding", "Outstanding", "currency", true)
        });

        report.TotalCount = await query.CountAsync(ct).ConfigureAwait(false);

        var outstandingTotal = await query
            .SelectMany(m => m.Subscriptions)
            .Where(s => s.Status != SubscriptionStatus.Cancelled && s.FinalAmount > s.PaidAmount)
            .SumAsync(s => (decimal?)(s.FinalAmount - s.PaidAmount), ct)
            .ConfigureAwait(false);

        report.Totals["Total Outstanding"] = Round(outstandingTotal ?? 0m);
        report.Subtitle = $"{report.TotalCount} member(s)"
                          + (type == ReportType.NewRegistrations
                              ? $" registered between {Label(ctx.From)} and {Label(ctx.To)}"
                              : string.Empty);

        var rows = await query
            .OrderBy(m => m.FullName).ThenBy(m => m.Id)
            .Skip(ctx.Skip).Take(ctx.PageSize)
            .Select(m => new
            {
                m.MemberCode,
                m.FullName,
                m.Gender,
                m.Phone,
                m.Email,
                m.JoiningDate,
                m.Status,
                PlanName = m.Subscriptions
                    .OrderByDescending(s => s.Status == SubscriptionStatus.Active)
                    .ThenByDescending(s => s.EndDate)
                    .ThenByDescending(s => s.Id)
                    .Select(s => s.MembershipPlan != null ? s.MembershipPlan.Name : null)
                    .FirstOrDefault(),
                EndDate = m.Subscriptions
                    .OrderByDescending(s => s.Status == SubscriptionStatus.Active)
                    .ThenByDescending(s => s.EndDate)
                    .ThenByDescending(s => s.Id)
                    .Select(s => (DateTime?)s.EndDate)
                    .FirstOrDefault(),
                Outstanding = m.Subscriptions
                    .Where(s => s.Status != SubscriptionStatus.Cancelled && s.FinalAmount > s.PaidAmount)
                    .Sum(s => (decimal?)(s.FinalAmount - s.PaidAmount)) ?? 0m
            })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var r in rows)
        {
            report.Rows.Add(new Dictionary<string, object?>
            {
                ["code"] = r.MemberCode,
                ["name"] = r.FullName,
                ["gender"] = r.Gender.ToString(),
                ["phone"] = r.Phone,
                ["email"] = r.Email,
                ["joiningDate"] = r.JoiningDate,
                ["status"] = r.Status.ToString(),
                ["plan"] = r.PlanName,
                ["endDate"] = r.EndDate,
                ["daysRemaining"] = r.EndDate.HasValue ? (int?)(r.EndDate.Value.Date - today).Days : null,
                ["outstanding"] = Round(r.Outstanding)
            });
        }
    }

    // ---------------------------------------------------------------- subscriptions

    private async Task SubscriptionReportAsync(ReportResultDto report, ReportContext ctx, CancellationToken ct)
    {
        report.Title = "Subscription Report";

        var query = FilteredSubscriptions(ctx)
            .Where(s => s.StartDate >= ctx.FromInclusive && s.StartDate < ctx.ToExclusive);

        report.Columns.AddRange(new[]
        {
            new ReportColumnDto("code", "Subscription"),
            new ReportColumnDto("member", "Member"),
            new ReportColumnDto("plan", "Plan"),
            new ReportColumnDto("startDate", "Start", "date"),
            new ReportColumnDto("endDate", "End", "date"),
            new ReportColumnDto("final", "Amount", "currency", true),
            new ReportColumnDto("paid", "Paid", "currency", true),
            new ReportColumnDto("outstanding", "Outstanding", "currency", true),
            new ReportColumnDto("paymentStatus", "Payment"),
            new ReportColumnDto("status", "Status")
        });

        report.TotalCount = await query.CountAsync(ct).ConfigureAwait(false);

        var totals = await query
            .GroupBy(s => 1)
            .Select(g => new
            {
                Final = g.Sum(x => x.FinalAmount),
                Paid = g.Sum(x => x.PaidAmount)
            })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        var finalTotal = totals?.Final ?? 0m;
        var paidTotal = totals?.Paid ?? 0m;

        report.Totals["Total Billed"] = Round(finalTotal);
        report.Totals["Total Collected"] = Round(paidTotal);
        report.Totals["Total Outstanding"] = Round(finalTotal - paidTotal);
        report.Subtitle = $"{report.TotalCount} subscription(s) starting between " +
                          $"{Label(ctx.From)} and {Label(ctx.To)}";

        var rows = await query
            .OrderByDescending(s => s.StartDate).ThenByDescending(s => s.Id)
            .Skip(ctx.Skip).Take(ctx.PageSize)
            .Select(s => new
            {
                s.SubscriptionCode,
                MemberName = s.Member != null ? s.Member.FullName : string.Empty,
                MemberCode = s.Member != null ? s.Member.MemberCode : string.Empty,
                PlanName = s.MembershipPlan != null ? s.MembershipPlan.Name : null,
                s.StartDate,
                s.EndDate,
                s.FinalAmount,
                s.PaidAmount,
                s.PaymentStatus,
                s.Status
            })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var r in rows)
        {
            report.Rows.Add(new Dictionary<string, object?>
            {
                ["code"] = r.SubscriptionCode,
                ["member"] = $"{r.MemberName} ({r.MemberCode})",
                ["plan"] = r.PlanName,
                ["startDate"] = r.StartDate,
                ["endDate"] = r.EndDate,
                ["final"] = Round(r.FinalAmount),
                ["paid"] = Round(r.PaidAmount),
                ["outstanding"] = Round(Math.Max(0m, r.FinalAmount - r.PaidAmount)),
                ["paymentStatus"] = r.PaymentStatus.ToString(),
                ["status"] = r.Status.ToString()
            });
        }

        report.Chart = await PlanCountChartAsync(query, ct).ConfigureAwait(false);
    }

    private async Task RenewalReportAsync(ReportResultDto report, ReportContext ctx, CancellationToken ct)
    {
        report.Title = "Renewal Report";

        var query = FilteredSubscriptions(ctx)
            .Where(s => s.IsRenewal
                        && s.StartDate >= ctx.FromInclusive
                        && s.StartDate < ctx.ToExclusive);

        report.Columns.AddRange(new[]
        {
            new ReportColumnDto("code", "Subscription"),
            new ReportColumnDto("member", "Member"),
            new ReportColumnDto("plan", "Plan"),
            new ReportColumnDto("previousEnd", "Previous End", "date"),
            new ReportColumnDto("startDate", "Renewed From", "date"),
            new ReportColumnDto("gapDays", "Gap (days)", "int"),
            new ReportColumnDto("endDate", "New End", "date"),
            new ReportColumnDto("final", "Amount", "currency", true),
            new ReportColumnDto("paid", "Paid", "currency", true),
            new ReportColumnDto("status", "Status")
        });

        report.TotalCount = await query.CountAsync(ct).ConfigureAwait(false);

        var totals = await query
            .GroupBy(s => 1)
            .Select(g => new { Final = g.Sum(x => x.FinalAmount), Paid = g.Sum(x => x.PaidAmount) })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        report.Totals["Total Renewal Value"] = Round(totals?.Final ?? 0m);
        report.Totals["Total Collected"] = Round(totals?.Paid ?? 0m);
        report.Subtitle = $"{report.TotalCount} renewal(s) between {Label(ctx.From)} and {Label(ctx.To)}";

        var rows = await query
            .OrderByDescending(s => s.StartDate).ThenByDescending(s => s.Id)
            .Skip(ctx.Skip).Take(ctx.PageSize)
            .Select(s => new
            {
                s.SubscriptionCode,
                MemberName = s.Member != null ? s.Member.FullName : string.Empty,
                MemberCode = s.Member != null ? s.Member.MemberCode : string.Empty,
                PlanName = s.MembershipPlan != null ? s.MembershipPlan.Name : null,
                PreviousEnd = s.PreviousSubscription != null ? (DateTime?)s.PreviousSubscription.EndDate : null,
                s.StartDate,
                s.EndDate,
                s.FinalAmount,
                s.PaidAmount,
                s.Status
            })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var r in rows)
        {
            // 0 means the new term started the day after the old one ended (no lapse).
            int? gap = r.PreviousEnd.HasValue
                ? (r.StartDate.Date - r.PreviousEnd.Value.Date).Days - 1
                : null;

            report.Rows.Add(new Dictionary<string, object?>
            {
                ["code"] = r.SubscriptionCode,
                ["member"] = $"{r.MemberName} ({r.MemberCode})",
                ["plan"] = r.PlanName,
                ["previousEnd"] = r.PreviousEnd,
                ["startDate"] = r.StartDate,
                ["gapDays"] = gap,
                ["endDate"] = r.EndDate,
                ["final"] = Round(r.FinalAmount),
                ["paid"] = Round(r.PaidAmount),
                ["status"] = r.Status.ToString()
            });
        }

        report.Chart = await PlanCountChartAsync(query, ct).ConfigureAwait(false);
    }

    private IQueryable<Subscription> FilteredSubscriptions(ReportContext ctx)
    {
        var query = _db.Subscriptions.AsNoTracking();

        if (ctx.Request.MemberId.HasValue)
        {
            var memberId = ctx.Request.MemberId.Value;
            query = query.Where(s => s.MemberId == memberId);
        }

        if (ctx.Request.MembershipPlanId.HasValue)
        {
            var planId = ctx.Request.MembershipPlanId.Value;
            query = query.Where(s => s.MembershipPlanId == planId);
        }

        if (ctx.Request.TrainerId.HasValue)
        {
            var trainerId = ctx.Request.TrainerId.Value;
            query = query.Where(s => s.AssignedTrainerId == trainerId);
        }

        if (ctx.Request.Status.HasValue)
        {
            var status = (SubscriptionStatus)ctx.Request.Status.Value;
            query = query.Where(s => s.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(ctx.Request.Search))
        {
            var term = ctx.Request.Search.Trim();
            query = query.Where(s => s.SubscriptionCode.Contains(term)
                                     || (s.Member != null && (s.Member.FullName.Contains(term)
                                                              || s.Member.MemberCode.Contains(term))));
        }

        return query;
    }

    // ---------------------------------------------------------------- attendance

    private async Task AttendanceReportAsync(ReportResultDto report, ReportContext ctx, CancellationToken ct)
    {
        report.Title = "Attendance Report";

        var query = _db.Attendance.AsNoTracking()
            .Where(a => a.AttendanceDate >= ctx.FromInclusive && a.AttendanceDate < ctx.ToExclusive);

        if (ctx.Request.MemberId.HasValue)
        {
            var memberId = ctx.Request.MemberId.Value;
            query = query.Where(a => a.MemberId == memberId);
        }

        if (ctx.Request.Status.HasValue)
        {
            var status = (AttendanceStatus)ctx.Request.Status.Value;
            query = query.Where(a => a.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(ctx.Request.Search))
        {
            var term = ctx.Request.Search.Trim();
            query = query.Where(a => a.Member != null && (a.Member.FullName.Contains(term)
                                                          || a.Member.MemberCode.Contains(term)));
        }

        report.Columns.AddRange(new[]
        {
            new ReportColumnDto("memberCode", "Member Code"),
            new ReportColumnDto("member", "Member"),
            new ReportColumnDto("date", "Date", "date"),
            new ReportColumnDto("checkIn", "Check In", "datetime"),
            new ReportColumnDto("checkOut", "Check Out", "datetime"),
            new ReportColumnDto("duration", "Minutes", "int", true),
            new ReportColumnDto("method", "Method"),
            new ReportColumnDto("status", "Status")
        });

        report.TotalCount = await query.CountAsync(ct).ConfigureAwait(false);

        var minutes = await query.SumAsync(a => (int?)a.DurationMinutes, ct).ConfigureAwait(false) ?? 0;
        report.Totals["Total Check-ins"] = report.TotalCount;
        report.Totals["Total Minutes"] = minutes;
        report.Subtitle = $"{report.TotalCount} visit(s) between {Label(ctx.From)} and {Label(ctx.To)}";

        var rows = await query
            .OrderByDescending(a => a.CheckInTime).ThenByDescending(a => a.Id)
            .Skip(ctx.Skip).Take(ctx.PageSize)
            .Select(a => new
            {
                MemberCode = a.Member != null ? a.Member.MemberCode : string.Empty,
                MemberName = a.Member != null ? a.Member.FullName : string.Empty,
                a.AttendanceDate,
                a.CheckInTime,
                a.CheckOutTime,
                a.DurationMinutes,
                a.CheckInMethod,
                a.Status
            })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var r in rows)
        {
            report.Rows.Add(new Dictionary<string, object?>
            {
                ["memberCode"] = r.MemberCode,
                ["member"] = r.MemberName,
                ["date"] = r.AttendanceDate,
                ["checkIn"] = r.CheckInTime,
                ["checkOut"] = r.CheckOutTime,
                ["duration"] = r.DurationMinutes,
                ["method"] = r.CheckInMethod,
                ["status"] = r.Status == AttendanceStatus.CheckedIn ? "Checked In" : "Checked Out"
            });
        }

        var perDay = await query
            .GroupBy(a => a.AttendanceDate.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .OrderBy(x => x.Day)
            .ToListAsync(ct).ConfigureAwait(false);

        report.Chart = perDay.Select(x => new ChartSeriesDto
        {
            Label = x.Day.ToString(DayLabelFormat, CultureInfo.InvariantCulture),
            Value = x.Count,
            Date = x.Day
        }).ToList();
    }

    // ---------------------------------------------------------------- payments

    private async Task DailyPaymentReportAsync(ReportResultDto report, ReportContext ctx, CancellationToken ct)
    {
        var day = (ctx.Request.FromDate ?? _clock.Today).Date;
        var next = day.AddDays(1);

        report.Title = "Daily Payment Report";
        report.FromDate = day;
        report.ToDate = day;

        var query = FilteredPayments(ctx.Request, day, next);

        report.Columns.AddRange(new[]
        {
            new ReportColumnDto("receipt", "Receipt No"),
            new ReportColumnDto("time", "Time", "datetime"),
            new ReportColumnDto("member", "Member"),
            new ReportColumnDto("plan", "Plan"),
            new ReportColumnDto("method", "Method"),
            new ReportColumnDto("amount", "Amount", "currency", true),
            new ReportColumnDto("discount", "Discount", "currency", true),
            new ReportColumnDto("tax", "Tax", "currency", true),
            new ReportColumnDto("final", "Total", "currency", true),
            new ReportColumnDto("refunded", "Refunded", "currency", true),
            new ReportColumnDto("net", "Net", "currency", true),
            new ReportColumnDto("status", "Status"),
            new ReportColumnDto("collectedBy", "Collected By")
        });

        report.TotalCount = await query.CountAsync(ct).ConfigureAwait(false);

        var totals = await query
            .GroupBy(p => 1)
            .Select(g => new
            {
                Final = g.Sum(x => x.FinalAmount),
                Refunded = g.Sum(x => x.RefundedAmount)
            })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        var finalTotal = totals?.Final ?? 0m;
        var refundTotal = totals?.Refunded ?? 0m;

        report.Totals["Total Collected"] = Round(finalTotal);
        report.Totals["Total Refunded"] = Round(refundTotal);
        report.Totals["Net Collection"] = Round(finalTotal - refundTotal);
        report.Subtitle = $"{report.TotalCount} payment(s) on {Label(day)}";

        var rows = await query
            .OrderBy(p => p.PaymentDate).ThenBy(p => p.Id)
            .Skip(ctx.Skip).Take(ctx.PageSize)
            .Select(p => new
            {
                p.ReceiptNumber,
                p.PaymentDate,
                MemberName = p.Member != null ? p.Member.FullName : string.Empty,
                MemberCode = p.Member != null ? p.Member.MemberCode : string.Empty,
                PlanName = p.Subscription != null && p.Subscription.MembershipPlan != null
                    ? p.Subscription.MembershipPlan.Name
                    : null,
                MethodName = p.PaymentMethod != null ? p.PaymentMethod.Name : null,
                p.Amount,
                p.DiscountAmount,
                p.TaxAmount,
                p.FinalAmount,
                p.RefundedAmount,
                p.Status,
                CollectedBy = p.CollectedByUser != null ? p.CollectedByUser.FullName : null
            })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var r in rows)
        {
            report.Rows.Add(new Dictionary<string, object?>
            {
                ["receipt"] = r.ReceiptNumber,
                ["time"] = r.PaymentDate,
                ["member"] = $"{r.MemberName} ({r.MemberCode})",
                ["plan"] = r.PlanName,
                ["method"] = r.MethodName,
                ["amount"] = Round(r.Amount),
                ["discount"] = Round(r.DiscountAmount),
                ["tax"] = Round(r.TaxAmount),
                ["final"] = Round(r.FinalAmount),
                ["refunded"] = Round(r.RefundedAmount),
                ["net"] = Round(r.FinalAmount - r.RefundedAmount),
                ["status"] = r.Status.ToString(),
                ["collectedBy"] = r.CollectedBy
            });
        }

        report.Chart = await MethodAmountChartAsync(query, ct).ConfigureAwait(false);
    }

    private async Task MonthlyPaymentReportAsync(ReportResultDto report, ReportContext ctx, CancellationToken ct)
    {
        var anchor = (ctx.Request.FromDate ?? _clock.Today).Date;
        var monthStart = new DateTime(anchor.Year, anchor.Month, 1);
        var nextMonth = monthStart.AddMonths(1);
        var days = (nextMonth - monthStart).Days;

        report.Title = "Monthly Payment Report";
        report.FromDate = monthStart;
        report.ToDate = nextMonth.AddDays(-1);

        var query = FilteredPayments(ctx.Request, monthStart, nextMonth);

        report.Columns.AddRange(new[]
        {
            new ReportColumnDto("date", "Date", "date"),
            new ReportColumnDto("weekday", "Day"),
            new ReportColumnDto("count", "Payments", "int", true),
            new ReportColumnDto("gross", "Gross", "currency", true),
            new ReportColumnDto("refunded", "Refunded", "currency", true),
            new ReportColumnDto("amount", "Net Collected", "currency", true)
        });

        var grouped = await query
            .GroupBy(p => p.PaymentDate.Date)
            .Select(g => new
            {
                Day = g.Key,
                Count = g.Count(),
                Gross = g.Sum(x => x.FinalAmount),
                Refunded = g.Sum(x => x.RefundedAmount)
            })
            .ToListAsync(ct).ConfigureAwait(false);

        var byDay = grouped.ToDictionary(x => x.Day.Date);

        var buckets = new List<(DateTime Day, int Count, decimal Gross, decimal Refunded)>(days);
        for (var i = 0; i < days; i++)
        {
            var day = monthStart.AddDays(i);
            if (byDay.TryGetValue(day, out var hit))
                buckets.Add((day, hit.Count, hit.Gross, hit.Refunded));
            else
                buckets.Add((day, 0, 0m, 0m));
        }

        report.TotalCount = buckets.Count;
        report.Totals["Total Payments"] = buckets.Sum(b => b.Count);
        report.Totals["Total Collected"] = Round(buckets.Sum(b => b.Gross));
        report.Totals["Total Refunded"] = Round(buckets.Sum(b => b.Refunded));
        report.Totals["Net Collection"] = Round(buckets.Sum(b => b.Gross - b.Refunded));
        report.Subtitle = $"Daily collection for {monthStart.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}";

        foreach (var b in buckets.Skip(ctx.Skip).Take(ctx.PageSize))
        {
            report.Rows.Add(new Dictionary<string, object?>
            {
                ["date"] = b.Day,
                ["weekday"] = b.Day.ToString("ddd", CultureInfo.InvariantCulture),
                ["count"] = b.Count,
                ["gross"] = Round(b.Gross),
                ["refunded"] = Round(b.Refunded),
                ["amount"] = Round(b.Gross - b.Refunded)
            });
        }

        report.Chart = buckets.Select(b => new ChartSeriesDto
        {
            Label = b.Day.ToString(DayLabelFormat, CultureInfo.InvariantCulture),
            Value = Round(b.Gross - b.Refunded),
            SecondaryValue = b.Count,
            Date = b.Day
        }).ToList();
    }

    private async Task RevenueReportAsync(ReportResultDto report, ReportContext ctx, CancellationToken ct)
    {
        var groupBy = NormalizeGroupBy(ctx.Request.GroupBy);

        report.Title = $"Revenue Report ({groupBy})";

        var query = FilteredPayments(ctx.Request, ctx.FromInclusive, ctx.ToExclusive);

        report.Columns.AddRange(new[]
        {
            new ReportColumnDto("period", "Period"),
            new ReportColumnDto("periodStart", "From", "date"),
            new ReportColumnDto("periodEnd", "To", "date"),
            new ReportColumnDto("count", "Payments", "int", true),
            new ReportColumnDto("gross", "Gross Revenue", "currency", true),
            new ReportColumnDto("refunds", "Refunds", "currency", true),
            new ReportColumnDto("net", "Net Revenue", "currency", true)
        });

        var buckets = groupBy switch
        {
            "Month" => await MonthBucketsAsync(query, ctx, ct).ConfigureAwait(false),
            "Week" => await WeekBucketsAsync(query, ctx, ct).ConfigureAwait(false),
            _ => await DayBucketsAsync(query, ctx, ct).ConfigureAwait(false)
        };

        report.TotalCount = buckets.Count;
        report.Totals["Total Payments"] = buckets.Sum(b => b.Count);
        report.Totals["Gross Revenue"] = Round(buckets.Sum(b => b.Gross));
        report.Totals["Total Refunds"] = Round(buckets.Sum(b => b.Refunds));
        report.Totals["Net Revenue"] = Round(buckets.Sum(b => b.Gross - b.Refunds));
        report.Subtitle = $"{groupBy}ly revenue between {Label(ctx.From)} and {Label(ctx.To)}";

        foreach (var b in buckets.Skip(ctx.Skip).Take(ctx.PageSize))
        {
            report.Rows.Add(new Dictionary<string, object?>
            {
                ["period"] = b.Label,
                ["periodStart"] = b.Start,
                ["periodEnd"] = b.End,
                ["count"] = b.Count,
                ["gross"] = Round(b.Gross),
                ["refunds"] = Round(b.Refunds),
                ["net"] = Round(b.Gross - b.Refunds)
            });
        }

        report.Chart = buckets.Select(b => new ChartSeriesDto
        {
            Label = b.Label,
            Value = Round(b.Gross - b.Refunds),
            SecondaryValue = Round(b.Gross),
            Date = b.Start
        }).ToList();
    }

    private async Task OutstandingPaymentReportAsync(ReportResultDto report, ReportContext ctx,
        CancellationToken ct)
    {
        var today = _clock.Today.Date;

        report.Title = "Outstanding Payments";

        // A live receivables snapshot, so it is deliberately not restricted to the date range.
        var query = FilteredSubscriptions(ctx)
            .Where(s => s.Status != SubscriptionStatus.Cancelled && s.FinalAmount > s.PaidAmount);

        report.Columns.AddRange(new[]
        {
            new ReportColumnDto("memberCode", "Member Code"),
            new ReportColumnDto("member", "Member"),
            new ReportColumnDto("phone", "Phone"),
            new ReportColumnDto("plan", "Plan"),
            new ReportColumnDto("endDate", "Valid Till", "date"),
            new ReportColumnDto("final", "Billed", "currency", true),
            new ReportColumnDto("paid", "Paid", "currency", true),
            new ReportColumnDto("outstanding", "Outstanding", "currency", true),
            new ReportColumnDto("daysOverdue", "Days Overdue", "int"),
            new ReportColumnDto("status", "Status")
        });

        report.TotalCount = await query.CountAsync(ct).ConfigureAwait(false);

        var totals = await query
            .GroupBy(s => 1)
            .Select(g => new { Final = g.Sum(x => x.FinalAmount), Paid = g.Sum(x => x.PaidAmount) })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        var finalTotal = totals?.Final ?? 0m;
        var paidTotal = totals?.Paid ?? 0m;

        report.Totals["Total Billed"] = Round(finalTotal);
        report.Totals["Total Collected"] = Round(paidTotal);
        report.Totals["Total Outstanding"] = Round(finalTotal - paidTotal);
        report.Subtitle = $"{report.TotalCount} subscription(s) with a balance due";

        // Ordering by the end date ascending is the same as days overdue descending.
        var rows = await query
            .OrderBy(s => s.EndDate).ThenBy(s => s.Id)
            .Skip(ctx.Skip).Take(ctx.PageSize)
            .Select(s => new
            {
                MemberCode = s.Member != null ? s.Member.MemberCode : string.Empty,
                MemberName = s.Member != null ? s.Member.FullName : string.Empty,
                Phone = s.Member != null ? s.Member.Phone : null,
                PlanName = s.MembershipPlan != null ? s.MembershipPlan.Name : null,
                s.EndDate,
                s.FinalAmount,
                s.PaidAmount,
                s.Status
            })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var r in rows)
        {
            var overdue = (today - r.EndDate.Date).Days;

            report.Rows.Add(new Dictionary<string, object?>
            {
                ["memberCode"] = r.MemberCode,
                ["member"] = r.MemberName,
                ["phone"] = r.Phone,
                ["plan"] = r.PlanName,
                ["endDate"] = r.EndDate,
                ["final"] = Round(r.FinalAmount),
                ["paid"] = Round(r.PaidAmount),
                ["outstanding"] = Round(r.FinalAmount - r.PaidAmount),
                ["daysOverdue"] = overdue > 0 ? overdue : 0,
                ["status"] = r.Status.ToString()
            });
        }

        var perPlan = await query
            .GroupBy(s => s.MembershipPlan!.Name)
            .Select(g => new { Label = g.Key, Amount = g.Sum(x => x.FinalAmount - x.PaidAmount) })
            .OrderByDescending(x => x.Amount)
            .ToListAsync(ct).ConfigureAwait(false);

        report.Chart = perPlan.Select(x => new ChartSeriesDto
        {
            Label = string.IsNullOrWhiteSpace(x.Label) ? "Unassigned" : x.Label,
            Category = x.Label,
            Value = Round(x.Amount)
        }).ToList();
    }

    // ---------------------------------------------------------------- trainers & workouts

    private async Task TrainerReportAsync(ReportResultDto report, ReportContext ctx, CancellationToken ct)
    {
        report.Title = "Trainer Report";

        var query = _db.Trainers.AsNoTracking();

        if (ctx.Request.TrainerId.HasValue)
        {
            var trainerId = ctx.Request.TrainerId.Value;
            query = query.Where(t => t.Id == trainerId);
        }

        if (ctx.Request.Status.HasValue)
        {
            var status = (TrainerStatus)ctx.Request.Status.Value;
            query = query.Where(t => t.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(ctx.Request.Search))
        {
            var term = ctx.Request.Search.Trim();
            query = query.Where(t => t.TrainerCode.Contains(term) || t.FullName.Contains(term));
        }

        report.Columns.AddRange(new[]
        {
            new ReportColumnDto("code", "Trainer Code"),
            new ReportColumnDto("trainer", "Trainer"),
            new ReportColumnDto("specialization", "Specialization"),
            new ReportColumnDto("status", "Status"),
            new ReportColumnDto("assignedMembers", "Assigned Members", "int", true),
            new ReportColumnDto("sessions", "Sessions", "int", true),
            new ReportColumnDto("membersTrained", "Members Trained", "int", true),
            new ReportColumnDto("minutes", "Total Minutes", "int", true)
        });

        report.TotalCount = await query.CountAsync(ct).ConfigureAwait(false);

        var rows = await query
            .OrderBy(t => t.FullName).ThenBy(t => t.Id)
            .Skip(ctx.Skip).Take(ctx.PageSize)
            .Select(t => new
            {
                t.Id,
                t.TrainerCode,
                t.FullName,
                t.Specialization,
                t.Status,
                AssignedMembers = t.AssignedMembers.Count
            })
            .ToListAsync(ct).ConfigureAwait(false);

        // One grouped aggregate for the whole period; bounded by the number of trainers.
        var sessionStats = await _db.WorkoutSessions.AsNoTracking()
            .Where(s => s.TrainerId != null
                        && s.SessionDate >= ctx.FromInclusive
                        && s.SessionDate < ctx.ToExclusive)
            .GroupBy(s => s.TrainerId!.Value)
            .Select(g => new
            {
                TrainerId = g.Key,
                Sessions = g.Count(),
                MembersTrained = g.Select(x => x.MemberId).Distinct().Count(),
                Minutes = g.Sum(x => (int?)x.DurationMinutes) ?? 0
            })
            .ToListAsync(ct).ConfigureAwait(false);

        var statsById = sessionStats.ToDictionary(x => x.TrainerId);
        var chart = new List<ChartSeriesDto>();

        foreach (var r in rows)
        {
            statsById.TryGetValue(r.Id, out var stats);
            var sessions = stats?.Sessions ?? 0;
            var minutes = stats?.Minutes ?? 0;

            if (sessions > 0)
                chart.Add(new ChartSeriesDto { Label = r.FullName, Category = "Sessions", Value = sessions });

            report.Rows.Add(new Dictionary<string, object?>
            {
                ["code"] = r.TrainerCode,
                ["trainer"] = r.FullName,
                ["specialization"] = r.Specialization,
                ["status"] = r.Status.ToString(),
                ["assignedMembers"] = r.AssignedMembers,
                ["sessions"] = sessions,
                ["membersTrained"] = stats?.MembersTrained ?? 0,
                ["minutes"] = minutes
            });
        }

        report.Totals["Total Sessions"] = sessionStats.Sum(x => x.Sessions);
        report.Totals["Total Minutes"] = sessionStats.Sum(x => x.Minutes);
        report.Subtitle = $"{report.TotalCount} trainer(s); sessions counted between " +
                          $"{Label(ctx.From)} and {Label(ctx.To)}";

        report.Chart = chart.OrderByDescending(c => c.Value).ToList();
    }

    private async Task WorkoutActivityReportAsync(ReportResultDto report, ReportContext ctx,
        CancellationToken ct)
    {
        report.Title = "Workout Activity Report";

        var query = _db.WorkoutSessions.AsNoTracking()
            .Where(s => s.SessionDate >= ctx.FromInclusive && s.SessionDate < ctx.ToExclusive);

        if (ctx.Request.MemberId.HasValue)
        {
            var memberId = ctx.Request.MemberId.Value;
            query = query.Where(s => s.MemberId == memberId);
        }

        if (ctx.Request.TrainerId.HasValue)
        {
            var trainerId = ctx.Request.TrainerId.Value;
            query = query.Where(s => s.TrainerId == trainerId);
        }

        if (!string.IsNullOrWhiteSpace(ctx.Request.Search))
        {
            var term = ctx.Request.Search.Trim();
            query = query.Where(s => s.Member != null && (s.Member.FullName.Contains(term)
                                                          || s.Member.MemberCode.Contains(term)));
        }

        report.Columns.AddRange(new[]
        {
            new ReportColumnDto("date", "Session Date", "date"),
            new ReportColumnDto("member", "Member"),
            new ReportColumnDto("trainer", "Trainer"),
            new ReportColumnDto("plan", "Workout Plan"),
            new ReportColumnDto("exercises", "Exercises", "int", true),
            new ReportColumnDto("volume", "Volume (kg)", "decimal", true),
            new ReportColumnDto("duration", "Minutes", "int", true),
            new ReportColumnDto("calories", "Calories", "decimal", true)
        });

        report.TotalCount = await query.CountAsync(ct).ConfigureAwait(false);

        var totals = await query
            .GroupBy(s => 1)
            .Select(g => new
            {
                Minutes = g.Sum(x => (int?)x.DurationMinutes) ?? 0,
                Calories = g.Sum(x => (decimal?)x.CaloriesBurned) ?? 0m
            })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        report.Totals["Total Sessions"] = report.TotalCount;
        report.Totals["Total Minutes"] = totals?.Minutes ?? 0;
        report.Totals["Total Calories"] = Round(totals?.Calories ?? 0m);
        report.Subtitle = $"{report.TotalCount} session(s) between {Label(ctx.From)} and {Label(ctx.To)}";

        var rows = await query
            .OrderByDescending(s => s.SessionDate).ThenByDescending(s => s.Id)
            .Skip(ctx.Skip).Take(ctx.PageSize)
            .Select(s => new
            {
                s.SessionDate,
                MemberName = s.Member != null ? s.Member.FullName : string.Empty,
                MemberCode = s.Member != null ? s.Member.MemberCode : string.Empty,
                TrainerName = s.Trainer != null ? s.Trainer.FullName : null,
                PlanName = s.WorkoutPlan != null ? s.WorkoutPlan.Name : null,
                ExerciseCount = s.Exercises.Count,
                Volume = s.Exercises
                    .Sum(e => (decimal?)(e.Sets * e.Repetitions * (e.WeightKg ?? 0m))) ?? 0m,
                s.DurationMinutes,
                s.CaloriesBurned
            })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var r in rows)
        {
            report.Rows.Add(new Dictionary<string, object?>
            {
                ["date"] = r.SessionDate,
                ["member"] = $"{r.MemberName} ({r.MemberCode})",
                ["trainer"] = r.TrainerName,
                ["plan"] = r.PlanName,
                ["exercises"] = r.ExerciseCount,
                ["volume"] = Round(r.Volume),
                ["duration"] = r.DurationMinutes,
                ["calories"] = r.CaloriesBurned.HasValue ? Round(r.CaloriesBurned.Value) : null
            });
        }

        var perDay = await query
            .GroupBy(s => s.SessionDate.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .OrderBy(x => x.Day)
            .ToListAsync(ct).ConfigureAwait(false);

        report.Chart = perDay.Select(x => new ChartSeriesDto
        {
            Label = x.Day.ToString(DayLabelFormat, CultureInfo.InvariantCulture),
            Value = x.Count,
            Date = x.Day
        }).ToList();
    }

    // ---------------------------------------------------------------- audit & expenses

    private async Task AuditReportAsync(ReportResultDto report, ReportContext ctx, CancellationToken ct)
    {
        report.Title = "Audit Trail";

        var query = _db.AuditLogs.AsNoTracking()
            .Where(a => a.ChangedAtUtc >= ctx.FromInclusive && a.ChangedAtUtc < ctx.ToExclusive);

        if (ctx.Request.UserId.HasValue)
        {
            var userId = ctx.Request.UserId.Value;
            query = query.Where(a => a.UserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(ctx.Request.Search))
        {
            var term = ctx.Request.Search.Trim();
            query = query.Where(a => a.Action.Contains(term)
                                     || a.EntityName.Contains(term)
                                     || (a.UserName != null && a.UserName.Contains(term))
                                     || (a.Description != null && a.Description.Contains(term)));
        }

        report.Columns.AddRange(new[]
        {
            new ReportColumnDto("timestamp", "When", "datetime"),
            new ReportColumnDto("user", "User"),
            new ReportColumnDto("action", "Action"),
            new ReportColumnDto("entity", "Entity"),
            new ReportColumnDto("entityId", "Entity Id", "int"),
            new ReportColumnDto("description", "Description"),
            new ReportColumnDto("ip", "IP Address")
        });

        report.TotalCount = await query.CountAsync(ct).ConfigureAwait(false);
        report.Totals["Total Entries"] = report.TotalCount;
        report.Subtitle = $"{report.TotalCount} audit entry(s) between {Label(ctx.From)} and {Label(ctx.To)}";

        var rows = await query
            .OrderByDescending(a => a.ChangedAtUtc).ThenByDescending(a => a.Id)
            .Skip(ctx.Skip).Take(ctx.PageSize)
            .Select(a => new
            {
                a.ChangedAtUtc,
                a.UserName,
                a.Action,
                a.EntityName,
                a.EntityId,
                a.Description,
                a.IpAddress
            })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var r in rows)
        {
            report.Rows.Add(new Dictionary<string, object?>
            {
                ["timestamp"] = r.ChangedAtUtc,
                ["user"] = r.UserName,
                ["action"] = r.Action,
                ["entity"] = r.EntityName,
                ["entityId"] = r.EntityId,
                ["description"] = r.Description,
                ["ip"] = r.IpAddress
            });
        }

        var perAction = await query
            .GroupBy(a => a.Action)
            .Select(g => new { Label = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync(ct).ConfigureAwait(false);

        report.Chart = perAction.Select(x => new ChartSeriesDto
        {
            Label = string.IsNullOrWhiteSpace(x.Label) ? "Unknown" : x.Label,
            Category = x.Label,
            Value = x.Count
        }).ToList();
    }

    private async Task ExpenseReportAsync(ReportResultDto report, ReportContext ctx, CancellationToken ct)
    {
        report.Title = "Expense Report";

        var query = _db.Expenses.AsNoTracking()
            .Where(e => e.ExpenseDate >= ctx.FromInclusive && e.ExpenseDate < ctx.ToExclusive);

        if (ctx.Request.PaymentMethodId.HasValue)
        {
            var methodId = ctx.Request.PaymentMethodId.Value;
            query = query.Where(e => e.PaymentMethodId == methodId);
        }

        if (ctx.Request.Status.HasValue)
        {
            // The generic Status slot carries the expense category for this report.
            var categoryId = ctx.Request.Status.Value;
            query = query.Where(e => e.ExpenseCategoryId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(ctx.Request.Search))
        {
            var term = ctx.Request.Search.Trim();
            query = query.Where(e => e.ExpenseNumber.Contains(term)
                                     || e.Title.Contains(term)
                                     || (e.VendorName != null && e.VendorName.Contains(term)));
        }

        report.Columns.AddRange(new[]
        {
            new ReportColumnDto("number", "Expense No"),
            new ReportColumnDto("date", "Date", "date"),
            new ReportColumnDto("category", "Category"),
            new ReportColumnDto("title", "Title"),
            new ReportColumnDto("vendor", "Vendor"),
            new ReportColumnDto("amount", "Amount", "currency", true),
            new ReportColumnDto("method", "Paid Via"),
            new ReportColumnDto("reference", "Reference")
        });

        report.TotalCount = await query.CountAsync(ct).ConfigureAwait(false);

        var amountTotal = await query.SumAsync(e => (decimal?)e.Amount, ct).ConfigureAwait(false) ?? 0m;
        report.Totals["Total Expenses"] = Round(amountTotal);
        report.Subtitle = $"{report.TotalCount} expense(s) between {Label(ctx.From)} and {Label(ctx.To)}";

        var rows = await query
            .OrderByDescending(e => e.ExpenseDate).ThenByDescending(e => e.Id)
            .Skip(ctx.Skip).Take(ctx.PageSize)
            .Select(e => new
            {
                e.ExpenseNumber,
                e.ExpenseDate,
                CategoryName = e.ExpenseCategory != null ? e.ExpenseCategory.Name : null,
                e.Title,
                e.VendorName,
                e.Amount,
                MethodName = e.PaymentMethod != null ? e.PaymentMethod.Name : null,
                e.ReferenceNumber
            })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var r in rows)
        {
            report.Rows.Add(new Dictionary<string, object?>
            {
                ["number"] = r.ExpenseNumber,
                ["date"] = r.ExpenseDate,
                ["category"] = r.CategoryName,
                ["title"] = r.Title,
                ["vendor"] = r.VendorName,
                ["amount"] = Round(r.Amount),
                ["method"] = r.MethodName,
                ["reference"] = r.ReferenceNumber
            });
        }

        var perCategory = await query
            .GroupBy(e => e.ExpenseCategory!.Name)
            .Select(g => new { Label = g.Key, Amount = g.Sum(x => x.Amount) })
            .OrderByDescending(x => x.Amount)
            .ToListAsync(ct).ConfigureAwait(false);

        report.Chart = perCategory.Select(x => new ChartSeriesDto
        {
            Label = string.IsNullOrWhiteSpace(x.Label) ? "Uncategorised" : x.Label,
            Category = x.Label,
            Value = Round(x.Amount)
        }).ToList();
    }

    // ---------------------------------------------------------------- profit & loss

    private async Task ProfitAndLossReportAsync(ReportResultDto report, ReportContext ctx, CancellationToken ct)
    {
        report.Title = "Profit and Loss";

        report.Columns.AddRange(new[]
        {
            new ReportColumnDto("month", "Month"),
            new ReportColumnDto("monthStart", "From", "date"),
            new ReportColumnDto("revenue", "Revenue", "currency", true),
            new ReportColumnDto("refunds", "Refunds", "currency", true),
            new ReportColumnDto("netRevenue", "Net Revenue", "currency", true),
            new ReportColumnDto("expenses", "Expenses", "currency", true),
            new ReportColumnDto("profit", "Profit", "currency", true)
        });

        var months = await MonthlyProfitAsync(ctx.FromInclusive, ctx.ToExclusive, ct).ConfigureAwait(false);

        report.TotalCount = months.Count;
        report.Totals["Total Revenue"] = Round(months.Sum(m => m.Revenue));
        report.Totals["Total Refunds"] = Round(months.Sum(m => m.Refunds));
        report.Totals["Net Revenue"] = Round(months.Sum(m => m.Revenue - m.Refunds));
        report.Totals["Total Expenses"] = Round(months.Sum(m => m.Expenses));
        report.Totals["Net Profit"] = Round(months.Sum(m => m.Revenue - m.Refunds - m.Expenses));
        report.Subtitle = $"Month by month result between {Label(ctx.From)} and {Label(ctx.To)}";

        foreach (var m in months.Skip(ctx.Skip).Take(ctx.PageSize))
        {
            report.Rows.Add(new Dictionary<string, object?>
            {
                ["month"] = m.Label,
                ["monthStart"] = m.Start,
                ["revenue"] = Round(m.Revenue),
                ["refunds"] = Round(m.Refunds),
                ["netRevenue"] = Round(m.Revenue - m.Refunds),
                ["expenses"] = Round(m.Expenses),
                ["profit"] = Round(m.Revenue - m.Refunds - m.Expenses)
            });
        }

        report.Chart = months.Select(m => new ChartSeriesDto
        {
            Label = m.Label,
            Value = Round(m.Revenue - m.Refunds - m.Expenses),
            SecondaryValue = Round(m.Revenue - m.Refunds),
            Date = m.Start
        }).ToList();
    }

    public async Task<ProfitLossDto> GetProfitAndLossAsync(DateTime from, DateTime to,
        CancellationToken ct = default)
    {
        var fromDate = from.Date;
        var toDate = to.Date;
        if (toDate < fromDate) (fromDate, toDate) = (toDate, fromDate);
        var toExclusive = toDate.AddDays(1);

        var gym = await _settings.GetGymSettingsAsync(ct).ConfigureAwait(false);

        var dto = new ProfitLossDto
        {
            FromDate = fromDate,
            ToDate = toDate,
            CurrencySymbol = string.IsNullOrWhiteSpace(gym.CurrencySymbol) ? "₹" : gym.CurrencySymbol
        };

        var revenue = await _db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Paid
                        && p.PaymentDate >= fromDate
                        && p.PaymentDate < toExclusive)
            .GroupBy(p => 1)
            .Select(g => new
            {
                Gross = g.Sum(x => x.FinalAmount),
                Refunded = g.Sum(x => x.RefundedAmount)
            })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        dto.TotalRevenue = Round(revenue?.Gross ?? 0m);
        dto.TotalRefunds = Round(revenue?.Refunded ?? 0m);

        var expenses = await _db.Expenses.AsNoTracking()
            .Where(e => e.ExpenseDate >= fromDate && e.ExpenseDate < toExclusive)
            .SumAsync(e => (decimal?)e.Amount, ct).ConfigureAwait(false);
        dto.TotalExpenses = Round(expenses ?? 0m);

        var receivables = await _db.Subscriptions.AsNoTracking()
            .Where(s => s.Status != SubscriptionStatus.Cancelled && s.FinalAmount > s.PaidAmount)
            .SumAsync(s => (decimal?)(s.FinalAmount - s.PaidAmount), ct).ConfigureAwait(false);
        dto.OutstandingReceivables = Round(receivables ?? 0m);

        var byPlan = await _db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Paid
                        && p.PaymentDate >= fromDate
                        && p.PaymentDate < toExclusive)
            .GroupBy(p => p.Subscription!.MembershipPlan!.Name)
            .Select(g => new { Label = g.Key, Amount = g.Sum(x => x.FinalAmount - x.RefundedAmount) })
            .OrderByDescending(x => x.Amount)
            .ToListAsync(ct).ConfigureAwait(false);

        dto.RevenueByPlan = byPlan.Select(x => new ChartSeriesDto
        {
            Label = string.IsNullOrWhiteSpace(x.Label) ? "Unallocated" : x.Label,
            Category = x.Label,
            Value = Round(x.Amount)
        }).ToList();

        var byMethod = await _db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Paid
                        && p.PaymentDate >= fromDate
                        && p.PaymentDate < toExclusive)
            .GroupBy(p => p.PaymentMethod!.Name)
            .Select(g => new { Label = g.Key, Amount = g.Sum(x => x.FinalAmount - x.RefundedAmount) })
            .OrderByDescending(x => x.Amount)
            .ToListAsync(ct).ConfigureAwait(false);

        dto.RevenueByMethod = byMethod.Select(x => new ChartSeriesDto
        {
            Label = string.IsNullOrWhiteSpace(x.Label) ? "Unknown" : x.Label,
            Category = x.Label,
            Value = Round(x.Amount)
        }).ToList();

        var byCategory = await _db.Expenses.AsNoTracking()
            .Where(e => e.ExpenseDate >= fromDate && e.ExpenseDate < toExclusive)
            .GroupBy(e => e.ExpenseCategory!.Name)
            .Select(g => new { Label = g.Key, Amount = g.Sum(x => x.Amount) })
            .OrderByDescending(x => x.Amount)
            .ToListAsync(ct).ConfigureAwait(false);

        dto.ExpensesByCategory = byCategory.Select(x => new ChartSeriesDto
        {
            Label = string.IsNullOrWhiteSpace(x.Label) ? "Uncategorised" : x.Label,
            Category = x.Label,
            Value = Round(x.Amount)
        }).ToList();

        var months = await MonthlyProfitAsync(fromDate, toExclusive, ct).ConfigureAwait(false);

        dto.MonthlyTrend = months.Select(m => new ChartSeriesDto
        {
            Label = m.Label,
            Value = Round(m.Revenue - m.Refunds - m.Expenses),
            SecondaryValue = Round(m.Revenue - m.Refunds),
            Date = m.Start
        }).ToList();

        return dto;
    }

    /// <summary>Revenue, refunds and expenses per calendar month, zero-filled across the range.</summary>
    private async Task<List<MonthResult>> MonthlyProfitAsync(DateTime fromInclusive, DateTime toExclusive,
        CancellationToken ct)
    {
        var firstMonth = new DateTime(fromInclusive.Year, fromInclusive.Month, 1);
        var lastDay = toExclusive.AddDays(-1);
        var lastMonth = new DateTime(lastDay.Year, lastDay.Month, 1);

        var revenueRows = await _db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Paid
                        && p.PaymentDate >= fromInclusive
                        && p.PaymentDate < toExclusive)
            .GroupBy(p => new { p.PaymentDate.Year, p.PaymentDate.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Gross = g.Sum(x => x.FinalAmount),
                Refunded = g.Sum(x => x.RefundedAmount)
            })
            .ToListAsync(ct).ConfigureAwait(false);

        var expenseRows = await _db.Expenses.AsNoTracking()
            .Where(e => e.ExpenseDate >= fromInclusive && e.ExpenseDate < toExclusive)
            .GroupBy(e => new { e.ExpenseDate.Year, e.ExpenseDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Amount = g.Sum(x => x.Amount) })
            .ToListAsync(ct).ConfigureAwait(false);

        var revenueByMonth = new Dictionary<DateTime, (decimal Gross, decimal Refunded)>();
        foreach (var r in revenueRows)
            revenueByMonth[new DateTime(r.Year, r.Month, 1)] = (r.Gross, r.Refunded);

        var expenseByMonth = new Dictionary<DateTime, decimal>();
        foreach (var r in expenseRows)
            expenseByMonth[new DateTime(r.Year, r.Month, 1)] = r.Amount;

        var results = new List<MonthResult>();
        for (var month = firstMonth; month <= lastMonth; month = month.AddMonths(1))
        {
            revenueByMonth.TryGetValue(month, out var revenue);
            expenseByMonth.TryGetValue(month, out var expense);

            results.Add(new MonthResult(
                month.ToString(MonthLabelFormat, CultureInfo.InvariantCulture),
                month,
                revenue.Gross,
                revenue.Refunded,
                expense));
        }

        return results;
    }

    // ---------------------------------------------------------------- exports

    public async Task<FileExportDto> ExportExcelAsync(ReportRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var report = await RunCoreAsync(request, ExportPageSize, ct).ConfigureAwait(false);
        var bytes = _excel.Export(report);

        _logger.LogInformation("Excel export produced for {ReportType} with {RowCount} row(s).",
            request.ReportType, report.Rows.Count);

        return new FileExportDto
        {
            FileName = BuildFileName(request.ReportType, "xlsx"),
            ContentType = ExcelContentType,
            Content = bytes
        };
    }

    public async Task<FileExportDto> ExportPdfAsync(ReportRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var report = await RunCoreAsync(request, ExportPageSize, ct).ConfigureAwait(false);
        var gym = await _settings.GetGymSettingsAsync(ct).ConfigureAwait(false);
        var bytes = _pdf.ExportReport(report, gym);

        _logger.LogInformation("PDF export produced for {ReportType} with {RowCount} row(s).",
            request.ReportType, report.Rows.Count);

        return new FileExportDto
        {
            FileName = BuildFileName(request.ReportType, "pdf"),
            ContentType = PdfContentType,
            Content = bytes
        };
    }

    private string BuildFileName(ReportType reportType, string extension)
    {
        var stamp = _clock.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        return $"{Sanitize(reportType.ToString())}_{stamp}.{extension}";
    }

    /// <summary>Strips anything that is not safe in a file name on Windows or Linux.</summary>
    private static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Report";

        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim()
            .Select(ch => invalid.Contains(ch) || ch is ' ' or '.' ? '_' : ch)
            .ToArray();

        var cleaned = new string(chars).Trim('_');
        return cleaned.Length == 0 ? "Report" : cleaned;
    }

    // ---------------------------------------------------------------- shared query helpers

    private IQueryable<Payment> FilteredPayments(ReportRequestDto request, DateTime fromInclusive,
        DateTime toExclusive)
    {
        var query = _db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Paid
                        && p.PaymentDate >= fromInclusive
                        && p.PaymentDate < toExclusive);

        if (request.MemberId.HasValue)
        {
            var memberId = request.MemberId.Value;
            query = query.Where(p => p.MemberId == memberId);
        }

        if (request.PaymentMethodId.HasValue)
        {
            var methodId = request.PaymentMethodId.Value;
            query = query.Where(p => p.PaymentMethodId == methodId);
        }

        if (request.UserId.HasValue)
        {
            var userId = request.UserId.Value;
            query = query.Where(p => p.CollectedByUserId == userId);
        }

        if (request.MembershipPlanId.HasValue)
        {
            var planId = request.MembershipPlanId.Value;
            query = query.Where(p => p.Subscription != null && p.Subscription.MembershipPlanId == planId);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(p => p.ReceiptNumber.Contains(term)
                                     || (p.Member != null && (p.Member.FullName.Contains(term)
                                                              || p.Member.MemberCode.Contains(term))));
        }

        return query;
    }

    private static async Task<List<ChartSeriesDto>> PlanCountChartAsync(IQueryable<Subscription> query,
        CancellationToken ct)
    {
        var rows = await query
            .GroupBy(s => s.MembershipPlan!.Name)
            .Select(g => new { Label = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync(ct).ConfigureAwait(false);

        return rows.Select(x => new ChartSeriesDto
        {
            Label = string.IsNullOrWhiteSpace(x.Label) ? "Unassigned" : x.Label,
            Category = x.Label,
            Value = x.Count
        }).ToList();
    }

    private static async Task<List<ChartSeriesDto>> MethodAmountChartAsync(IQueryable<Payment> query,
        CancellationToken ct)
    {
        var rows = await query
            .GroupBy(p => p.PaymentMethod!.Name)
            .Select(g => new { Label = g.Key, Amount = g.Sum(x => x.FinalAmount - x.RefundedAmount) })
            .OrderByDescending(x => x.Amount)
            .ToListAsync(ct).ConfigureAwait(false);

        return rows.Select(x => new ChartSeriesDto
        {
            Label = string.IsNullOrWhiteSpace(x.Label) ? "Unknown" : x.Label,
            Category = x.Label,
            Value = Round(x.Amount)
        }).ToList();
    }

    private static async Task<List<RevenueBucket>> DayBucketsAsync(IQueryable<Payment> query,
        ReportContext ctx, CancellationToken ct)
    {
        var rows = await query
            .GroupBy(p => p.PaymentDate.Date)
            .Select(g => new
            {
                Day = g.Key,
                Count = g.Count(),
                Gross = g.Sum(x => x.FinalAmount),
                Refunded = g.Sum(x => x.RefundedAmount)
            })
            .ToListAsync(ct).ConfigureAwait(false);

        var byDay = rows.ToDictionary(x => x.Day.Date);
        var buckets = new List<RevenueBucket>();

        for (var day = ctx.FromInclusive; day < ctx.ToExclusive; day = day.AddDays(1))
        {
            byDay.TryGetValue(day, out var hit);
            buckets.Add(new RevenueBucket(
                day.ToString(DayLabelFormat, CultureInfo.InvariantCulture),
                day, day,
                hit?.Count ?? 0,
                hit?.Gross ?? 0m,
                hit?.Refunded ?? 0m));
        }

        return buckets;
    }

    private static async Task<List<RevenueBucket>> WeekBucketsAsync(IQueryable<Payment> query,
        ReportContext ctx, CancellationToken ct)
    {
        var days = await DayBucketsAsync(query, ctx, ct).ConfigureAwait(false);
        var buckets = new List<RevenueBucket>();

        // Weeks are aligned to the start of the requested range so the labels stay stable.
        for (var index = 0; index * 7 < days.Count; index++)
        {
            var slice = days.Skip(index * 7).Take(7).ToList();
            if (slice.Count == 0) break;

            buckets.Add(new RevenueBucket(
                $"W{index + 1}",
                slice[0].Start,
                slice[^1].End,
                slice.Sum(s => s.Count),
                slice.Sum(s => s.Gross),
                slice.Sum(s => s.Refunds)));
        }

        return buckets;
    }

    private static async Task<List<RevenueBucket>> MonthBucketsAsync(IQueryable<Payment> query,
        ReportContext ctx, CancellationToken ct)
    {
        var rows = await query
            .GroupBy(p => new { p.PaymentDate.Year, p.PaymentDate.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Count = g.Count(),
                Gross = g.Sum(x => x.FinalAmount),
                Refunded = g.Sum(x => x.RefundedAmount)
            })
            .ToListAsync(ct).ConfigureAwait(false);

        var byMonth = rows.ToDictionary(x => new DateTime(x.Year, x.Month, 1));

        var firstMonth = new DateTime(ctx.FromInclusive.Year, ctx.FromInclusive.Month, 1);
        var lastDay = ctx.ToExclusive.AddDays(-1);
        var lastMonth = new DateTime(lastDay.Year, lastDay.Month, 1);

        var buckets = new List<RevenueBucket>();
        for (var month = firstMonth; month <= lastMonth; month = month.AddMonths(1))
        {
            byMonth.TryGetValue(month, out var hit);
            buckets.Add(new RevenueBucket(
                month.ToString(MonthLabelFormat, CultureInfo.InvariantCulture),
                month,
                month.AddMonths(1).AddDays(-1),
                hit?.Count ?? 0,
                hit?.Gross ?? 0m,
                hit?.Refunded ?? 0m));
        }

        return buckets;
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Defaults to the current calendar month when the caller supplied no range.</summary>
    private (DateTime From, DateTime To) ResolveRange(ReportRequestDto request)
    {
        var today = _clock.Today.Date;
        DateTime from;
        DateTime to;

        if (request.FromDate is null && request.ToDate is null)
        {
            from = new DateTime(today.Year, today.Month, 1);
            to = from.AddMonths(1).AddDays(-1);
        }
        else
        {
            var anchor = (request.ToDate ?? today).Date;
            from = request.FromDate?.Date ?? new DateTime(anchor.Year, anchor.Month, 1);
            to = request.ToDate?.Date ?? today;
        }

        if (to < from) (from, to) = (to, from);
        return (from, to);
    }

    private static string NormalizeGroupBy(string? groupBy) =>
        (groupBy ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "month" => "Month",
            "week" => "Week",
            _ => "Day"
        };

    private static string Label(DateTime value) =>
        value.ToString(DateLabelFormat, CultureInfo.InvariantCulture);

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Resolved request state shared by every report builder.</summary>
    private sealed record ReportContext(
        ReportRequestDto Request,
        GymSettingsDto Gym,
        DateTime From,
        DateTime To,
        int PageSize,
        int PageNumber)
    {
        public DateTime FromInclusive => From.Date;
        public DateTime ToExclusive => To.Date.AddDays(1);
        public int Skip => (PageNumber - 1) * PageSize;
    }

    private sealed record RevenueBucket(string Label, DateTime Start, DateTime End, int Count,
        decimal Gross, decimal Refunds);

    private sealed record MonthResult(string Label, DateTime Start, decimal Revenue, decimal Refunds,
        decimal Expenses);
}
