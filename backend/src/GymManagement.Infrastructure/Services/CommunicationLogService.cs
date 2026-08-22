using System.Linq.Expressions;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GymManagement.Infrastructure.Services;

/// <summary>
/// Read side of the member-messaging audit trail. Every query is evaluated on the server and
/// tracked by nothing: this service only ever answers "what actually went out?" — the send path
/// (<c>IMemberNotifier</c>) owns the writes.
/// </summary>
public sealed class CommunicationLogService : ICommunicationLogService
{
    private const int MaxMemberTake = 200;

    /// <summary>
    /// Mirrors <c>PaymentService.PaymentRequestValidDays</c>. A pay-link row stores no creation
    /// timestamp of its own — only <c>ExpiresAtUtc</c>, which is always set to creation + this many
    /// days — so the created instant is recovered by subtracting it. Keep the two in step.
    /// </summary>
    private const int PaymentRequestValidDays = 7;

    /// <summary>
    /// Shaped, not final: <see cref="MemberNotificationLog.Kind"/> stays an enum inside the SQL
    /// projection because enum-to-string does not translate, and is rendered by <see cref="ToDto"/>.
    /// </summary>
    private static readonly Expression<Func<MemberNotificationLog, LogRow>> RowProjection =
        l => new LogRow
        {
            Id = l.Id,
            MemberId = l.MemberId,
            MemberName = l.Member != null ? l.Member.FullName : string.Empty,
            MemberCode = l.Member != null ? l.Member.MemberCode : string.Empty,
            Kind = l.Kind,
            Detail = l.Detail,
            SentOnDate = l.SentOnDate,
            CreatedAtUtc = l.CreatedAtUtc,
            EmailSent = l.EmailSent,
            WhatsAppSent = l.WhatsAppSent
        };

    private readonly GymDbContext _db;
    private readonly IDateTimeProvider _clock;

    public CommunicationLogService(GymDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<PagedResult<CommunicationLogDto>> GetPagedAsync(
        CommunicationQueryDto query, CancellationToken ct = default)
    {
        query ??= new CommunicationQueryDto();

        var source = _db.MemberNotificationLogs.AsNoTracking();

        // A filter value that names nothing matches nothing: dropping it silently would return
        // exactly the rows the caller asked to exclude.
        if (!string.IsNullOrWhiteSpace(query.Kind))
        {
            if (!Enum.TryParse<MemberNotificationKind>(query.Kind.Trim(), ignoreCase: true, out var kind))
                return PagedResult<CommunicationLogDto>.Empty(query.PageSize);

            source = source.Where(l => l.Kind == kind);
        }

        if (!string.IsNullOrWhiteSpace(query.Channel))
        {
            switch (query.Channel.Trim().ToLowerInvariant())
            {
                case "email":
                    source = source.Where(l => l.EmailSent);
                    break;
                case "whatsapp":
                    source = source.Where(l => l.WhatsAppSent);
                    break;
                default:
                    return PagedResult<CommunicationLogDto>.Empty(query.PageSize);
            }
        }

        if (query.MemberId.HasValue)
            source = source.Where(l => l.MemberId == query.MemberId.Value);

        // SentOnDate is a local calendar date, so both bounds are inclusive dates, not instants.
        if (query.From.HasValue)
        {
            var from = query.From.Value.Date;
            source = source.Where(l => l.SentOnDate >= from);
        }

        if (query.To.HasValue)
        {
            var to = query.To.Value.Date;
            source = source.Where(l => l.SentOnDate <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(l =>
                (l.Member != null && (l.Member.FullName.Contains(term) || l.Member.MemberCode.Contains(term))) ||
                (l.Detail != null && l.Detail.Contains(term)));
        }

        var total = await source.CountAsync(ct).ConfigureAwait(false);
        if (total == 0)
            return new PagedResult<CommunicationLogDto>(
                Array.Empty<CommunicationLogDto>(), 0, query.PageNumber, query.PageSize);

        var rows = await source
            .OrderByDescending(l => l.CreatedAtUtc)
            .ThenByDescending(l => l.Id)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(RowProjection)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedResult<CommunicationLogDto>(
            rows.Select(ToDto).ToList(), total, query.PageNumber, query.PageSize);
    }

    public async Task<List<CommunicationLogDto>> GetForMemberAsync(
        int memberId, int take = 50, CancellationToken ct = default)
    {
        var limit = take switch { < 1 => 50, > MaxMemberTake => MaxMemberTake, _ => take };

        var rows = await _db.MemberNotificationLogs.AsNoTracking()
            .Where(l => l.MemberId == memberId)
            .OrderByDescending(l => l.CreatedAtUtc)
            .ThenByDescending(l => l.Id)
            .Take(limit)
            .Select(RowProjection)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.Select(ToDto).ToList();
    }

    public async Task<CommunicationUsageDto> GetUsageAsync(CancellationToken ct = default)
    {
        var today = _clock.Today.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var nextMonth = monthStart.AddMonths(1);

        var month = _db.MemberNotificationLogs.AsNoTracking()
            .Where(l => l.SentOnDate >= monthStart && l.SentOnDate < nextMonth);

        // One round trip for the four counters, aggregated over the same month window.
        var counters = await month
            .GroupBy(_ => 1)
            .Select(g => new
            {
                MonthEmails = g.Count(l => l.EmailSent),
                MonthWhatsApp = g.Count(l => l.WhatsAppSent),
                TodayEmails = g.Count(l => l.EmailSent && l.SentOnDate == today),
                TodayWhatsApp = g.Count(l => l.WhatsAppSent && l.SentOnDate == today)
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        // Distinct members reached goes out separately: COUNT(DISTINCT ...) inside a grouped
        // aggregate does not translate reliably, and a wrong number here is worse than a round trip.
        var membersReached = await month
            .Where(l => l.EmailSent || l.WhatsAppSent)
            .Select(l => l.MemberId)
            .Distinct()
            .CountAsync(ct)
            .ConfigureAwait(false);

        return new CommunicationUsageDto
        {
            MonthEmails = counters?.MonthEmails ?? 0,
            MonthWhatsApp = counters?.MonthWhatsApp ?? 0,
            TodayEmails = counters?.TodayEmails ?? 0,
            TodayWhatsApp = counters?.TodayWhatsApp ?? 0,
            MonthMembersReached = membersReached
        };
    }

    public async Task<MessageTrackingDto> GetTrackingAsync(CancellationToken ct = default)
    {
        var today = _clock.Today.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var nextMonth = monthStart.AddMonths(1);

        // The UTC-stamped tables (pay links, in-app notifications) are windowed on the same local
        // calendar as the date-stamped ones by shifting the local day boundaries into UTC with the
        // clock's own offset — one consistent rule instead of a per-row timezone conversion that
        // would not translate to SQL.
        var utcOffset = _clock.Now - _clock.UtcNow;
        var todayStartUtc = today - utcOffset;
        var tomorrowStartUtc = todayStartUtc.AddDays(1);
        var monthStartUtc = monthStart - utcOffset;
        var nextMonthUtc = nextMonth - utcOffset;

        var occasions = await _db.MemberNotificationLogs.AsNoTracking()
            .Where(l => l.SentOnDate >= monthStart && l.SentOnDate < nextMonth)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                ThisMonth = g.Count(),
                Today = g.Count(l => l.SentOnDate == today),
                Email = g.Count(l => l.EmailSent),
                WhatsApp = g.Count(l => l.WhatsAppSent)
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        var reminders = await _db.ExpiryReminderEmails.AsNoTracking()
            .Where(r => r.SentOnDate >= monthStart && r.SentOnDate < nextMonth)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                ThisMonth = g.Count(),
                Today = g.Count(r => r.SentOnDate == today),
                Email = g.Count(r => r.EmailSent),
                Sms = g.Count(r => r.SmsSent)
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        // A pay-link row stores no creation timestamp; ExpiresAtUtc is always creation plus the
        // validity window, so the window is applied to ExpiresAtUtc shifted by that many days.
        var payLinkMonthStart = monthStartUtc.AddDays(PaymentRequestValidDays);
        var payLinkNextMonth = nextMonthUtc.AddDays(PaymentRequestValidDays);
        var payLinkTodayStart = todayStartUtc.AddDays(PaymentRequestValidDays);
        var payLinkTomorrowStart = tomorrowStartUtc.AddDays(PaymentRequestValidDays);

        var payLinks = await _db.PaymentRequests.AsNoTracking()
            .Where(p => p.ExpiresAtUtc >= payLinkMonthStart && p.ExpiresAtUtc < payLinkNextMonth)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                ThisMonth = g.Count(),
                Today = g.Count(p => p.ExpiresAtUtc >= payLinkTodayStart && p.ExpiresAtUtc < payLinkTomorrowStart),
                Sms = g.Count(p => p.SmsSent)
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        var inApp = await _db.Notifications.AsNoTracking()
            .Where(n => n.CreatedAtUtc >= monthStartUtc && n.CreatedAtUtc < nextMonthUtc)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                ThisMonth = g.Count(),
                Today = g.Count(n => n.CreatedAtUtc >= todayStartUtc && n.CreatedAtUtc < tomorrowStartUtc)
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        var streams = new List<TrackingStreamDto>
        {
            new()
            {
                Stream = "Occasions",
                Description = "Receipts, diet plans, streaks, birthdays and festival wishes",
                Today = occasions?.Today ?? 0,
                ThisMonth = occasions?.ThisMonth ?? 0,
                Email = occasions?.Email ?? 0,
                WhatsApp = occasions?.WhatsApp ?? 0
            },
            new()
            {
                Stream = "RenewalReminders",
                Description = "Daily membership-expiry reminders",
                Today = reminders?.Today ?? 0,
                ThisMonth = reminders?.ThisMonth ?? 0,
                Email = reminders?.Email ?? 0,
                Sms = reminders?.Sms ?? 0
            },
            new()
            {
                Stream = "PayLinks",
                Description = "Tap-to-pay UPI links texted to members (email copies not counted)",
                Today = payLinks?.Today ?? 0,
                ThisMonth = payLinks?.ThisMonth ?? 0,
                Sms = payLinks?.Sms ?? 0
            },
            new()
            {
                Stream = "InApp",
                Description = "In-app notifications shown in the bell",
                Today = inApp?.Today ?? 0,
                ThisMonth = inApp?.ThisMonth ?? 0,
                InApp = inApp?.ThisMonth ?? 0
            }
        };

        return new MessageTrackingDto
        {
            Streams = streams,
            TodayTotal = streams.Sum(s => s.Today),
            MonthTotal = streams.Sum(s => s.ThisMonth)
        };
    }

    // ---------------------------------------------------------------- helpers

    private static CommunicationLogDto ToDto(LogRow r) => new()
    {
        Id = r.Id,
        MemberId = r.MemberId,
        MemberName = r.MemberName,
        MemberCode = r.MemberCode,
        Kind = r.Kind.ToString(),
        Detail = r.Detail,
        SentOnDate = r.SentOnDate,
        CreatedAtUtc = r.CreatedAtUtc,
        EmailSent = r.EmailSent,
        WhatsAppSent = r.WhatsAppSent
    };

    /// <summary>SQL-side shape of one log row; the enum is stringified in memory, not in SQL.</summary>
    private sealed class LogRow
    {
        public int Id { get; set; }
        public int MemberId { get; set; }
        public string MemberName { get; set; } = string.Empty;
        public string MemberCode { get; set; } = string.Empty;
        public MemberNotificationKind Kind { get; set; }
        public string? Detail { get; set; }
        public DateTime SentOnDate { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public bool EmailSent { get; set; }
        public bool WhatsAppSent { get; set; }
    }
}
