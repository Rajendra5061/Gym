using System.Linq.Expressions;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Services;

/// <summary>
/// Gym floor check-in / check-out. Enforces the two front-desk rules: a member may only be
/// recorded once per calendar day, and a member whose membership has lapsed may not enter
/// unless an authorised operator overrides it or the gym allows expired check-ins.
/// </summary>
public sealed class AttendanceService : IAttendanceService
{
    private const string EntityName = "Attendance";

    /// <summary>Matches the <c>Notes</c> column width.</summary>
    private const int NotesLength = 512;

    /// <summary>A trend chart never spans more than this many days.</summary>
    private const int MaxTrendDays = 366;

    private static readonly string[] AllowedCheckInMethods =
        { "Manual", "QrCode", "Barcode", "Rfid", "Biometric" };

    private readonly GymDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly ILogger<AttendanceService> _logger;

    public AttendanceService(
        GymDbContext db,
        IDateTimeProvider clock,
        ICurrentUserService currentUser,
        IAuditService audit,
        ISettingsService settings,
        INotificationService notifications,
        ILogger<AttendanceService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Server side projection used by every read on this service.</summary>
    private static readonly Expression<Func<Attendance, AttendanceDto>> ToDto = a => new AttendanceDto
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

    // ---------------------------------------------------------------- check in

    public async Task<AttendanceDto> CheckInAsync(CheckInRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var today = _clock.Today.Date;
        var now = _clock.Now;
        var method = NormalizeCheckInMethod(request.CheckInMethod);
        var member = await ResolveMemberAsync(request, ct).ConfigureAwait(false);

        // Rule 1: one attendance row per member per calendar day.
        var existing = await FindTodaysRowAsync(member.Id, today, ct).ConfigureAwait(false);
        if (existing is not null)
            throw new ConflictAppException(DuplicateMessage(existing.Status, member.FullName));

        // Rule 2: the membership must cover today, unless the visit is explicitly overridden.
        var subscription = await FindUsableSubscriptionAsync(member.Id, today, ct).ConfigureAwait(false);
        string? overrideNote = null;
        var overriddenByOperator = false;

        if (subscription is null)
        {
            var allowedByOperator = request.OverrideExpiredMembership &&
                                    _currentUser.HasPermission(Permissions.AttendanceManage);
            var allowedByPolicy = await AllowExpiredCheckInAsync(ct).ConfigureAwait(false);

            if (!allowedByOperator && !allowedByPolicy)
                throw new BusinessRuleAppException(
                    $"{member.FullName} ({member.MemberCode}) has no membership that is valid today. " +
                    "The membership has expired or is missing and must be renewed before check-in.");

            overriddenByOperator = allowedByOperator;
            overrideNote = allowedByOperator
                ? $"Expired membership override by {DescribeCaller()} on {now:dd MMM yyyy HH:mm}."
                : $"Checked in without a valid membership on {now:dd MMM yyyy HH:mm}; " +
                  "allowed by the AllowExpiredMemberCheckIn gym setting.";
        }

        var entity = new Attendance
        {
            MemberId = member.Id,
            AttendanceDate = today,
            CheckInTime = now,
            Status = AttendanceStatus.CheckedIn,
            CheckInMethod = method,
            SubscriptionId = subscription?.Id,
            Notes = ComposeNotes(request.Notes, overrideNote)
        };

        _db.Attendance.Add(entity);

        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            // The unique index on (MemberId, AttendanceDate) is the last line of defence against a
            // double click or two operators scanning the same member at the same moment.
            _db.Entry(entity).State = EntityState.Detached;
            _logger.LogWarning(ex,
                "Check-in for member {MemberId} on {Date:yyyy-MM-dd} was rejected by the database.",
                member.Id, today);

            var clash = await FindTodaysRowAsync(member.Id, today, ct).ConfigureAwait(false);
            throw new ConflictAppException(DuplicateMessage(clash?.Status, member.FullName));
        }

        if (overrideNote is not null)
            await NotifyExpiredMembershipAsync(member.Id, member.FullName, member.MemberCode,
                entity.Id, overriddenByOperator, ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Create, EntityName, entity.Id,
            newValues: new
            {
                entity.MemberId,
                member.MemberCode,
                entity.AttendanceDate,
                entity.CheckInTime,
                entity.CheckInMethod,
                entity.SubscriptionId,
                ExpiredOverride = overrideNote is not null
            },
            description: $"Check-in for {member.FullName} ({member.MemberCode}) via {method}.",
            ct: ct).ConfigureAwait(false);

        return await GetDtoAsync(entity.Id, ct).ConfigureAwait(false);
    }

    // --------------------------------------------------------------- check out

    public async Task<AttendanceDto> CheckOutAsync(CheckOutRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var today = _clock.Today.Date;
        var now = _clock.Now;

        Attendance? row = null;

        if (request.AttendanceId is > 0)
            row = await _db.Attendance
                .FirstOrDefaultAsync(a => a.Id == request.AttendanceId!.Value, ct)
                .ConfigureAwait(false);

        if (row is null && request.MemberId is > 0)
            row = await _db.Attendance
                .Where(a => a.MemberId == request.MemberId!.Value
                            && a.AttendanceDate == today
                            && a.Status == AttendanceStatus.CheckedIn)
                .OrderByDescending(a => a.CheckInTime)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

        if (row is null)
            throw new NotFoundAppException(
                "No open check-in was found to close. Supply a valid attendance id, or a member id with an open visit for today.");

        if (row.Status == AttendanceStatus.CheckedOut || row.CheckOutTime is not null)
            throw new ConflictAppException(
                $"This visit was already checked out at {row.CheckOutTime:dd MMM yyyy HH:mm}.");

        var minutes = (int)Math.Floor((now - row.CheckInTime).TotalMinutes);
        if (minutes < 0)
        {
            _logger.LogWarning(
                "Check-out time {CheckOut:o} precedes check-in time {CheckIn:o} for attendance {AttendanceId}; duration stored as 0.",
                now, row.CheckInTime, row.Id);
            minutes = 0;
        }

        row.CheckOutTime = now;
        row.Status = AttendanceStatus.CheckedOut;
        row.DurationMinutes = minutes;
        row.Notes = ComposeNotes(row.Notes, request.Notes);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Update, EntityName, row.Id,
            newValues: new { row.CheckOutTime, Status = nameof(AttendanceStatus.CheckedOut), row.DurationMinutes },
            description: $"Check-out recorded after {minutes} minute(s).",
            ct: ct).ConfigureAwait(false);

        return await GetDtoAsync(row.Id, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ querie

    public async Task<PagedResult<AttendanceDto>> GetPagedAsync(AttendanceQueryDto query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var q = _db.Attendance.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(a => a.Member != null &&
                             (EF.Functions.Like(a.Member.MemberCode, $"%{term}%") ||
                              EF.Functions.Like(a.Member.FullName, $"%{term}%") ||
                              EF.Functions.Like(a.Member.Phone, $"%{term}%")));
        }

        if (query.MemberId is > 0)
            q = q.Where(a => a.MemberId == query.MemberId!.Value);

        if (query.OnlyToday)
        {
            var today = _clock.Today.Date;
            q = q.Where(a => a.AttendanceDate == today);
        }
        else
        {
            if (query.FromDate.HasValue)
            {
                var from = query.FromDate.Value.Date;
                q = q.Where(a => a.AttendanceDate >= from);
            }

            if (query.ToDate.HasValue)
            {
                var to = query.ToDate.Value.Date;
                q = q.Where(a => a.AttendanceDate <= to);
            }
        }

        if (query.Status.HasValue)
            q = q.Where(a => a.Status == query.Status!.Value);

        if (!string.IsNullOrWhiteSpace(query.CheckInMethod))
        {
            var method = query.CheckInMethod.Trim();
            q = q.Where(a => a.CheckInMethod == method);
        }

        var total = await q.CountAsync(ct).ConfigureAwait(false);
        if (total == 0)
            return new PagedResult<AttendanceDto>(Array.Empty<AttendanceDto>(), 0, query.PageNumber, query.PageSize);

        var items = await ApplySort(q, query)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(ToDto)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedResult<AttendanceDto>(items, total, query.PageNumber, query.PageSize);
    }

    public async Task<AttendanceSummaryDto> GetSummaryAsync(DateTime? date, CancellationToken ct = default)
    {
        var day = (date ?? _clock.Today).Date;
        var q = _db.Attendance.AsNoTracking().Where(a => a.AttendanceDate == day);

        var byStatus = await q
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var hourly = await q
            .GroupBy(a => a.CheckInTime.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var average = await q
            .Where(a => a.Status == AttendanceStatus.CheckedOut && a.DurationMinutes != null)
            .Select(a => (double?)a.DurationMinutes)
            .AverageAsync(ct)
            .ConfigureAwait(false);

        var summary = new AttendanceSummaryDto
        {
            Date = day,
            TotalCheckIns = byStatus.Sum(x => x.Count),
            CurrentlyInGym = byStatus.Where(x => x.Status == AttendanceStatus.CheckedIn).Sum(x => x.Count),
            CheckedOut = byStatus.Where(x => x.Status == AttendanceStatus.CheckedOut).Sum(x => x.Count),
            AverageDurationMinutes = average.HasValue ? Math.Round(average.Value, 1) : 0d,
            HourlyBreakdown = hourly
                .OrderBy(x => x.Hour)
                .Select(x => new HourlyAttendanceDto { Hour = x.Hour, Count = x.Count })
                .ToList()
        };

        summary.PeakHour = summary.HourlyBreakdown.Count == 0
            ? 0
            : summary.HourlyBreakdown.OrderByDescending(h => h.Count).ThenBy(h => h.Hour).First().Hour;

        return summary;
    }

    public async Task<List<AttendanceDto>> GetCurrentlyInGymAsync(CancellationToken ct = default)
    {
        var today = _clock.Today.Date;

        return await _db.Attendance.AsNoTracking()
            .Where(a => a.AttendanceDate == today && a.Status == AttendanceStatus.CheckedIn)
            .OrderBy(a => a.CheckInTime)
            .Select(ToDto)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task DeleteAsync(int attendanceId, CancellationToken ct = default)
    {
        var row = await _db.Attendance
            .Include(a => a.Member)
            .FirstOrDefaultAsync(a => a.Id == attendanceId, ct)
            .ConfigureAwait(false);

        if (row is null) throw new NotFoundAppException(EntityName, attendanceId);

        var memberCode = row.Member?.MemberCode;
        var memberName = row.Member?.FullName;
        var snapshot = new
        {
            row.MemberId,
            MemberCode = memberCode,
            row.AttendanceDate,
            row.CheckInTime,
            row.CheckOutTime,
            Status = row.Status.ToString(),
            row.DurationMinutes,
            row.CheckInMethod
        };

        // Attendance is not soft deletable: the row leaves the database for good.
        _db.Attendance.Remove(row);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Delete, EntityName, attendanceId,
            oldValues: snapshot,
            description: $"Deleted attendance for {memberName ?? "member #" + row.MemberId} " +
                         $"({memberCode ?? "n/a"}) dated {row.AttendanceDate:dd MMM yyyy}.",
            ct: ct).ConfigureAwait(false);
    }

    public async Task<List<ChartSeriesDto>> GetTrendAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var start = from.Date;
        var end = to.Date;

        if (end < start)
            throw new ValidationAppException(nameof(to), "The end date cannot be earlier than the start date.");

        // Keep the chart (and the in-memory gap filling below) bounded.
        if ((end - start).Days + 1 > MaxTrendDays)
            end = start.AddDays(MaxTrendDays - 1);

        var counts = await _db.Attendance.AsNoTracking()
            .Where(a => a.AttendanceDate >= start && a.AttendanceDate <= end)
            .GroupBy(a => a.AttendanceDate)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var lookup = counts.ToDictionary(x => x.Date.Date, x => x.Count);
        var series = new List<ChartSeriesDto>();

        for (var day = start; day <= end; day = day.AddDays(1))
        {
            series.Add(new ChartSeriesDto
            {
                Label = day.ToString("dd MMM"),
                Date = day,
                Value = lookup.TryGetValue(day, out var count) ? count : 0
            });
        }

        return series;
    }

    // ----------------------------------------------------------------- helpers

    private static IQueryable<Attendance> ApplySort(IQueryable<Attendance> q, AttendanceQueryDto query)
    {
        var desc = query.SortDescending;

        return (query.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "attendancedate" => desc
                ? q.OrderByDescending(a => a.AttendanceDate).ThenByDescending(a => a.CheckInTime)
                : q.OrderBy(a => a.AttendanceDate).ThenBy(a => a.CheckInTime),
            "membername" => desc
                ? q.OrderByDescending(a => a.Member!.FullName).ThenByDescending(a => a.CheckInTime)
                : q.OrderBy(a => a.Member!.FullName).ThenBy(a => a.CheckInTime),
            "durationminutes" => desc
                ? q.OrderByDescending(a => a.DurationMinutes).ThenByDescending(a => a.Id)
                : q.OrderBy(a => a.DurationMinutes).ThenBy(a => a.Id),
            "checkintime" => desc
                ? q.OrderByDescending(a => a.CheckInTime).ThenByDescending(a => a.Id)
                : q.OrderBy(a => a.CheckInTime).ThenBy(a => a.Id),
            // Newest visit first is what the front desk wants to see.
            _ => q.OrderByDescending(a => a.CheckInTime).ThenByDescending(a => a.Id)
        };
    }

    private async Task<MemberSnapshot> ResolveMemberAsync(CheckInRequestDto request, CancellationToken ct)
    {
        var byId = request.MemberId is > 0;
        var code = request.MemberCode?.Trim();
        var byCode = !string.IsNullOrWhiteSpace(code);

        if (!byId && !byCode)
            throw new ValidationAppException(nameof(request.MemberId),
                "Either a member id or a member code is required to record a check-in.");

        // Soft deleted members are looked up too so the caller gets a precise reason.
        var q = _db.Members.IgnoreQueryFilters().AsNoTracking();
        q = byId
            ? q.Where(m => m.Id == request.MemberId!.Value)
            : q.Where(m => m.MemberCode == code);

        var member = await q
            .Select(m => new MemberSnapshot(m.Id, m.MemberCode, m.FullName, m.Status, m.IsDeleted))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (member is null)
            throw new NotFoundAppException("Member", byId ? (object)request.MemberId!.Value : code!);

        if (member.IsDeleted)
            throw new BusinessRuleAppException(
                $"Member {member.FullName} ({member.MemberCode}) has been deleted and cannot check in. " +
                "Restore the member first.");

        if (member.Status == MemberStatus.Suspended)
            throw new BusinessRuleAppException(
                $"Member {member.FullName} ({member.MemberCode}) is suspended and may not enter the gym.");

        return member;
    }

    private Task<TodaysRow?> FindTodaysRowAsync(int memberId, DateTime today, CancellationToken ct) =>
        _db.Attendance.AsNoTracking()
            .Where(a => a.MemberId == memberId && a.AttendanceDate == today)
            .Select(a => new TodaysRow(a.Id, a.Status))
            .FirstOrDefaultAsync(ct);

    private Task<UsableSubscription?> FindUsableSubscriptionAsync(int memberId, DateTime today, CancellationToken ct) =>
        _db.Subscriptions.AsNoTracking()
            // today <= EndDate + GracePeriodDays. Expressed with AddDays rather than
            // EF.Functions.DateDiffDay because DateDiffDay is a SQL Server only function: it
            // translates to DATEADD(day, …) on SQL Server exactly as before, but it also evaluates
            // under providers that have no SQL translation, which keeps the check-in path testable.
            // Both StartDate and EndDate are `date` columns, so the two forms are equivalent.
            .Where(s => s.MemberId == memberId
                        && s.Status == SubscriptionStatus.Active
                        && s.StartDate <= today
                        && today <= s.EndDate.AddDays(s.GracePeriodDays))
            .OrderByDescending(s => s.EndDate)
            .ThenByDescending(s => s.Id)
            .Select(s => new UsableSubscription(s.Id, s.EndDate))
            .FirstOrDefaultAsync(ct);

    private async Task<bool> AllowExpiredCheckInAsync(CancellationToken ct)
    {
        try
        {
            var settings = await _settings.GetGymSettingsAsync(ct).ConfigureAwait(false);
            return settings.AllowExpiredMemberCheckIn;
        }
        catch (Exception ex)
        {
            // A missing/unreadable settings row must not silently let expired members in.
            _logger.LogWarning(ex,
                "Could not read the gym settings while evaluating the expired check-in policy; assuming it is not allowed.");
            return false;
        }
    }

    private async Task NotifyExpiredMembershipAsync(int memberId, string memberName, string memberCode,
        int attendanceId, bool overriddenByOperator, CancellationToken ct)
    {
        try
        {
            var reason = overriddenByOperator
                ? $"Overridden by {DescribeCaller()}."
                : "Allowed by the expired check-in gym setting.";

            await _notifications.NotifyAsync(
                NotificationType.MembershipExpired,
                NotificationSeverity.Warning,
                "Check-in without a valid membership",
                $"{memberName} ({memberCode}) checked in without an active membership. {reason} " +
                "Please follow up on the renewal.",
                memberId: memberId,
                entityName: EntityName,
                entityId: attendanceId,
                deduplicationKey: $"expired-checkin:{memberId}:{_clock.Today:yyyy-MM-dd}",
                ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to raise the expired-membership notification for member {MemberId}.", memberId);
        }
    }

    private Task<AttendanceDto> GetDtoAsync(int id, CancellationToken ct) =>
        _db.Attendance.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(ToDto)
            .FirstAsync(ct);

    private string DescribeCaller()
    {
        var name = _currentUser.FullName ?? _currentUser.UserName;
        if (string.IsNullOrWhiteSpace(name))
            name = _currentUser.UserId is { } id ? $"user #{id}" : "an unidentified operator";
        return name;
    }

    private static string DuplicateMessage(AttendanceStatus? status, string memberName) =>
        status == AttendanceStatus.CheckedIn
            ? $"{memberName} is already checked in and is inside the gym. Record a check-out first."
            : $"Attendance for {memberName} is already recorded for today.";

    private static string NormalizeCheckInMethod(string? method)
    {
        if (string.IsNullOrWhiteSpace(method)) return "Manual";

        var trimmed = method.Trim();
        var match = Array.Find(AllowedCheckInMethods,
            m => string.Equals(m, trimmed, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            throw new ValidationAppException(nameof(CheckInRequestDto.CheckInMethod),
                $"'{trimmed}' is not a supported check-in method. Allowed values: {string.Join(", ", AllowedCheckInMethods)}.");

        return match;
    }

    /// <summary>Appends <paramref name="addition"/> to <paramref name="existing"/> within the column width.</summary>
    private static string? ComposeNotes(string? existing, string? addition)
    {
        var left = existing?.Trim();
        var right = addition?.Trim();

        var combined = (string.IsNullOrEmpty(left), string.IsNullOrEmpty(right)) switch
        {
            (true, true) => null,
            (true, false) => right,
            (false, true) => left,
            _ => $"{left} | {right}"
        };

        if (combined is null) return null;
        return combined.Length <= NotesLength ? combined : combined[..NotesLength];
    }

    private sealed record MemberSnapshot(int Id, string MemberCode, string FullName, MemberStatus Status, bool IsDeleted);

    private sealed record TodaysRow(int Id, AttendanceStatus Status);

    private sealed record UsableSubscription(int Id, DateTime EndDate);
}
