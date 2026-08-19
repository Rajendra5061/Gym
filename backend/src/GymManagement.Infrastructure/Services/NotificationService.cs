using System.Globalization;
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
/// In-app notification centre. Reads are always scoped to the caller (rows addressed to the caller
/// plus broadcast rows with no recipient) and expired rows are hidden.
/// <para>
/// <see cref="NotifyAsync"/> is the helper the rest of the application uses; it is deliberately
/// forgiving so that a failed reminder can never roll back or break the business operation that
/// triggered it. <see cref="GenerateSystemAlertsAsync"/> is idempotent per day through the unique
/// <c>DeduplicationKey</c> index.
/// </para>
/// </summary>
public sealed class NotificationService : INotificationService
{
    // Column widths from NotificationConfiguration.
    private const int TitleLength = 200;
    private const int MessageLength = 2000;
    private const int EntityNameLength = 64;
    private const int DeduplicationKeyLength = 160;

    /// <summary>Safety valve so one bad data day cannot insert an unbounded number of rows.</summary>
    private const int MaxAlertsPerCategory = 500;

    /// <summary>Licence reminders start this many days before the licence or trial ends.</summary>
    private const int LicenseReminderDays = 7;

    private readonly GymDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly ISettingsService _settings;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        GymDbContext db,
        IDateTimeProvider clock,
        ICurrentUserService currentUser,
        ISettingsService settings,
        ILogger<NotificationService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private static readonly Expression<Func<Notification, NotificationDto>> Projection = n => new NotificationDto
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

    // ----------------------------------------------------------------- read

    public async Task<PagedResult<NotificationDto>> GetPagedAsync(NotificationQueryDto query,
        CancellationToken ct = default)
    {
        query ??= new NotificationQueryDto();

        var source = Scoped(query.OnlyMine);

        if (query.Type.HasValue)
            source = source.Where(n => n.Type == query.Type.Value);

        if (query.Severity.HasValue)
            source = source.Where(n => n.Severity == query.Severity.Value);

        if (query.IsRead.HasValue)
            source = source.Where(n => n.IsRead == query.IsRead.Value);

        if (query.MemberId.HasValue)
            source = source.Where(n => n.MemberId == query.MemberId.Value);

        if (query.FromDate.HasValue)
        {
            var from = query.FromDate.Value;
            source = source.Where(n => n.CreatedAtUtc >= from);
        }

        if (query.ToDate.HasValue)
        {
            // Inclusive upper bound: callers pass a date, not an instant.
            var to = query.ToDate.Value.Date.AddDays(1);
            source = source.Where(n => n.CreatedAtUtc < to);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(n =>
                EF.Functions.Like(n.Title, $"%{term}%") ||
                EF.Functions.Like(n.Message, $"%{term}%"));
        }

        var total = await source.CountAsync(ct).ConfigureAwait(false);
        if (total == 0)
            return new PagedResult<NotificationDto>(Array.Empty<NotificationDto>(), 0, query.PageNumber, query.PageSize);

        var items = await source
            .OrderByDescending(n => n.CreatedAtUtc)
            .ThenByDescending(n => n.Id)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(Projection)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedResult<NotificationDto>(items, total, query.PageNumber, query.PageSize);
    }

    public async Task<NotificationCountsDto> GetCountsAsync(CancellationToken ct = default)
    {
        // One round trip: the four counters are aggregated over the same scoped set.
        var counts = await Scoped(onlyMine: true)
            .GroupBy(_ => 1)
            .Select(g => new NotificationCountsDto
            {
                Total = g.Count(),
                Unread = g.Count(n => !n.IsRead),
                Critical = g.Count(n => n.Severity == NotificationSeverity.Critical),
                Warning = g.Count(n => n.Severity == NotificationSeverity.Warning)
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return counts ?? new NotificationCountsDto();
    }

    // ---------------------------------------------------------------- write

    public async Task<NotificationDto> CreateAsync(CreateNotificationDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new ValidationAppException(nameof(dto.Title), "Title is required.");

        if (string.IsNullOrWhiteSpace(dto.Message))
            throw new ValidationAppException(nameof(dto.Message), "Message is required.");

        var entity = new Notification
        {
            Type = dto.Type,
            Severity = dto.Severity,
            Title = Truncate(dto.Title.Trim(), TitleLength)!,
            Message = Truncate(dto.Message.Trim(), MessageLength)!,
            UserId = dto.UserId,
            MemberId = dto.MemberId,
            EntityName = Truncate(Clean(dto.EntityName), EntityNameLength),
            EntityId = dto.EntityId,
            IsRead = false,
            CreatedAtUtc = _clock.UtcNow,
            ExpiresAtUtc = dto.ExpiresAtUtc
        };

        _db.Notifications.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return await MapAsync(entity, ct).ConfigureAwait(false);
    }

    public async Task MarkAsReadAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        var idList = ids?.Where(id => id > 0).Distinct().ToList() ?? new List<int>();
        if (idList.Count == 0) return;

        var now = _clock.UtcNow;

        await Scope(_db.Notifications, onlyMine: true)
            .Where(n => idList.Contains(n.Id) && !n.IsRead)
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(n => n.IsRead, true)
                    .SetProperty(n => n.ReadAtUtc, now), ct)
            .ConfigureAwait(false);
    }

    public async Task MarkAllAsReadAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        await Scope(_db.Notifications, onlyMine: true)
            .Where(n => !n.IsRead)
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(n => n.IsRead, true)
                    .SetProperty(n => n.ReadAtUtc, now), ct)
            .ConfigureAwait(false);
    }

    /// <summary>Notifications are not soft-deletable, so dismissing one removes the row.</summary>
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var affected = await _db.Notifications
            .Where(n => n.Id == id)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        if (affected == 0)
            throw new NotFoundAppException(nameof(Notification), id);
    }

    public async Task NotifyAsync(NotificationType type, NotificationSeverity severity,
        string title, string message, int? memberId = null, int? userId = null,
        string? entityName = null, int? entityId = null, string? deduplicationKey = null,
        CancellationToken ct = default)
    {
        Notification? entity = null;

        try
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
            {
                _logger.LogWarning("Skipped a notification with an empty title or message. Type={Type}", type);
                return;
            }

            var key = Truncate(Clean(deduplicationKey), DeduplicationKeyLength);

            if (key is not null)
            {
                var exists = await _db.Notifications
                    .AsNoTracking()
                    .AnyAsync(n => n.DeduplicationKey == key, ct)
                    .ConfigureAwait(false);

                if (exists) return;
            }

            entity = new Notification
            {
                Type = type,
                Severity = severity,
                Title = Truncate(title.Trim(), TitleLength)!,
                Message = Truncate(message.Trim(), MessageLength)!,
                UserId = userId,
                MemberId = memberId,
                EntityName = Truncate(Clean(entityName), EntityNameLength),
                EntityId = entityId,
                CreatedAtUtc = _clock.UtcNow,
                DeduplicationKey = key
            };

            _db.Notifications.Add(entity);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            // Most likely the unique filtered index on DeduplicationKey: another caller (or another
            // run of the alert job) already raised this notification, which is exactly what we want.
            Detach(entity);
            _logger.LogDebug(ex, "Notification insert was rejected by the database. Key={Key}", deduplicationKey);
        }
        catch (Exception ex)
        {
            Detach(entity);
            _logger.LogError(ex, "Failed to raise notification. Type={Type} Title={Title}", type, title);
        }
    }

    // -------------------------------------------------------- system alerts

    public async Task<int> GenerateSystemAlertsAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var today = now.Date;
        var stamp = today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        var gym = await _settings.GetGymSettingsAsync(ct).ConfigureAwait(false);
        var reminderDays = gym.ExpiryReminderDays > 0 ? gym.ExpiryReminderDays : 7;
        var horizon = today.AddDays(reminderDays);

        var pending = new List<Notification>();

        pending.AddRange(await BuildExpiringSoonAsync(today, horizon, stamp, now, ct).ConfigureAwait(false));
        pending.AddRange(await BuildExpiredAsync(today, stamp, now, ct).ConfigureAwait(false));
        pending.AddRange(await BuildPaymentPendingAsync(stamp, now, gym.CurrencySymbol, ct).ConfigureAwait(false));

        var licenseAlert = await BuildLicenseAlertAsync(now, stamp, ct).ConfigureAwait(false);
        if (licenseAlert is not null) pending.Add(licenseAlert);

        if (pending.Count == 0) return 0;

        // One query resolves which keys already exist; the same hash set then removes duplicates
        // inside this batch, so the unique index is never hit in the normal case.
        var keys = pending.Select(p => p.DeduplicationKey!).ToList();

        var taken = await _db.Notifications
            .AsNoTracking()
            .Where(n => n.DeduplicationKey != null && keys.Contains(n.DeduplicationKey))
            .Select(n => n.DeduplicationKey!)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var seen = new HashSet<string>(taken, StringComparer.Ordinal);
        var fresh = pending.Where(p => seen.Add(p.DeduplicationKey!)).ToList();

        if (fresh.Count == 0) return 0;

        return await PersistAsync(fresh, ct).ConfigureAwait(false);
    }

    private async Task<List<Notification>> BuildExpiringSoonAsync(DateTime today, DateTime horizon,
        string stamp, DateTime now, CancellationToken ct)
    {
        var rows = await _db.Subscriptions
            .AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Active
                        && s.EndDate.Date >= today
                        && s.EndDate.Date <= horizon)
            .OrderBy(s => s.EndDate)
            .Take(MaxAlertsPerCategory)
            .Select(AlertRow.Projection)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        WarnIfTruncated(rows.Count, NotificationType.MembershipExpiringSoon);

        return rows.Select(r =>
        {
            var days = (r.EndDate.Date - today).Days;
            return new Notification
            {
                Type = NotificationType.MembershipExpiringSoon,
                Severity = NotificationSeverity.Warning,
                Title = days <= 0 ? "Membership expires today" : $"Membership expires in {days} day(s)",
                Message = Truncate(
                    $"{Describe(r)} ends on {r.EndDate:dd MMM yyyy} (subscription {r.SubscriptionCode}).",
                    MessageLength)!,
                MemberId = r.MemberId,
                EntityName = nameof(Subscription),
                EntityId = r.SubscriptionId,
                CreatedAtUtc = now,
                DeduplicationKey = $"expiring:{r.SubscriptionId}:{stamp}"
            };
        }).ToList();
    }

    private async Task<List<Notification>> BuildExpiredAsync(DateTime today, string stamp,
        DateTime now, CancellationToken ct)
    {
        // Effective end date = EndDate + grace period. Rows still flagged Active are overdue for the
        // nightly status sweep; rows flagged Expired are only reported on the day they lapsed so old
        // history does not generate alerts forever.
        var rows = await _db.Subscriptions
            .AsNoTracking()
            .Where(s => (s.Status == SubscriptionStatus.Active
                         && s.EndDate.AddDays(s.GracePeriodDays).Date < today)
                        || (s.Status == SubscriptionStatus.Expired
                            && s.EndDate.AddDays(s.GracePeriodDays).Date == today))
            .OrderByDescending(s => s.EndDate)
            .Take(MaxAlertsPerCategory)
            .Select(AlertRow.Projection)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        WarnIfTruncated(rows.Count, NotificationType.MembershipExpired);

        return rows.Select(r => new Notification
        {
            Type = NotificationType.MembershipExpired,
            Severity = NotificationSeverity.Critical,
            Title = "Membership expired",
            Message = Truncate(
                $"{Describe(r)} expired on {r.EndDate.AddDays(r.GracePeriodDays):dd MMM yyyy} " +
                $"(subscription {r.SubscriptionCode}). Renew it to restore access.",
                MessageLength)!,
            MemberId = r.MemberId,
            EntityName = nameof(Subscription),
            EntityId = r.SubscriptionId,
            CreatedAtUtc = now,
            DeduplicationKey = $"expired:{r.SubscriptionId}:{stamp}"
        }).ToList();
    }

    private async Task<List<Notification>> BuildPaymentPendingAsync(string stamp, DateTime now,
        string currencySymbol, CancellationToken ct)
    {
        var rows = await _db.Subscriptions
            .AsNoTracking()
            .Where(s => s.Status != SubscriptionStatus.Cancelled && s.FinalAmount - s.PaidAmount > 0m)
            .OrderByDescending(s => s.FinalAmount - s.PaidAmount)
            .Take(MaxAlertsPerCategory)
            .Select(AlertRow.Projection)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        WarnIfTruncated(rows.Count, NotificationType.PaymentPending);

        return rows.Select(r => new Notification
        {
            Type = NotificationType.PaymentPending,
            Severity = NotificationSeverity.Warning,
            Title = "Payment pending",
            Message = Truncate(
                $"{Describe(r)} has an outstanding balance of {currencySymbol}{r.Outstanding.ToString("N2", CultureInfo.InvariantCulture)} " +
                $"on subscription {r.SubscriptionCode}.",
                MessageLength)!,
            MemberId = r.MemberId,
            EntityName = nameof(Subscription),
            EntityId = r.SubscriptionId,
            CreatedAtUtc = now,
            DeduplicationKey = $"pending:{r.SubscriptionId}:{stamp}"
        }).ToList();
    }

    private async Task<Notification?> BuildLicenseAlertAsync(DateTime now, string stamp, CancellationToken ct)
    {
        var license = await _db.Licenses
            .AsNoTracking()
            .OrderBy(l => l.Id)
            .Select(l => new { l.Id, l.Status, l.IsTrial, l.ExpiryDateUtc })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (license?.ExpiryDateUtc is null) return null;
        if (license.Status is LicenseStatus.NotActivated or LicenseStatus.Revoked) return null;

        var days = (int)Math.Ceiling((license.ExpiryDateUtc.Value - now).TotalDays);
        if (days > LicenseReminderDays) return null;

        var subject = license.IsTrial ? "trial" : "licence";

        return new Notification
        {
            Type = NotificationType.LicenseExpiringSoon,
            Severity = NotificationSeverity.Warning,
            Title = days <= 0 ? $"The {subject} has expired" : $"The {subject} expires in {days} day(s)",
            Message = Truncate(
                $"This installation's {subject} ends on {license.ExpiryDateUtc.Value:dd MMM yyyy}. " +
                "Activate a licence key to keep using the system.",
                MessageLength)!,
            EntityName = nameof(License),
            EntityId = license.Id,
            CreatedAtUtc = now,
            DeduplicationKey = $"license:{stamp}"
        };
    }

    /// <summary>
    /// Saves the batch. A unique-index clash means a concurrent run already wrote some of these rows,
    /// so the batch is retried one row at a time and the losers are dropped.
    /// </summary>
    private async Task<int> PersistAsync(List<Notification> batch, CancellationToken ct)
    {
        try
        {
            _db.Notifications.AddRange(batch);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return batch.Count;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Batch insert of {Count} system alerts failed; retrying individually.", batch.Count);

            foreach (var row in batch) Detach(row);

            var created = 0;

            foreach (var row in batch)
            {
                try
                {
                    _db.Notifications.Add(row);
                    await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                    created++;
                }
                catch (DbUpdateException single)
                {
                    Detach(row);
                    _logger.LogDebug(single, "System alert {Key} was already present.", row.DeduplicationKey);
                }
            }

            return created;
        }
    }

    // -------------------------------------------------------------- helpers

    /// <summary>Rows the caller may see: addressed to them or broadcast, and not yet expired.</summary>
    private IQueryable<Notification> Scoped(bool onlyMine) =>
        Scope(_db.Notifications.AsNoTracking(), onlyMine);

    private IQueryable<Notification> Scope(IQueryable<Notification> source, bool onlyMine)
    {
        var now = _clock.UtcNow;
        var unexpired = (IQueryable<Notification> q) =>
            q.Where(n => n.ExpiresAtUtc == null || n.ExpiresAtUtc > now);

        // A member sees only rows addressed to them, enforced here rather than trusting the
        // caller to pass OnlyMine. Without this a member who simply omitted the flag received
        // every notification in the gym — other people's payment amounts and names included —
        // because `notifications.view` was satisfied and nothing else narrowed the query.
        // Broadcasts (UserId == null) are staff announcements and are withheld from members too.
        var memberId = _currentUser.MemberId;
        if (memberId.HasValue && !_currentUser.HasPermission(Permissions.NotificationsManage))
            return unexpired(source.Where(n => n.MemberId == memberId.Value));

        // A null UserId is a broadcast to all staff, so it is always visible.
        if (onlyMine)
        {
            var userId = _currentUser.UserId;
            source = source.Where(n => n.UserId == null || n.UserId == userId);
        }

        return unexpired(source);
    }

    private async Task<NotificationDto> MapAsync(Notification entity, CancellationToken ct)
    {
        string? memberName = null;

        if (entity.MemberId.HasValue)
        {
            memberName = await _db.Members
                .AsNoTracking()
                .Where(m => m.Id == entity.MemberId.Value)
                .Select(m => m.FullName)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
        }

        return new NotificationDto
        {
            Id = entity.Id,
            Type = entity.Type,
            Severity = entity.Severity,
            Title = entity.Title,
            Message = entity.Message,
            UserId = entity.UserId,
            MemberId = entity.MemberId,
            MemberName = memberName,
            EntityName = entity.EntityName,
            EntityId = entity.EntityId,
            IsRead = entity.IsRead,
            ReadAtUtc = entity.ReadAtUtc,
            CreatedAtUtc = entity.CreatedAtUtc,
            ExpiresAtUtc = entity.ExpiresAtUtc
        };
    }

    private void Detach(Notification? entity)
    {
        if (entity is null) return;

        // Leaving a failed insert in the change tracker would make the caller's next SaveChanges fail.
        var entry = _db.Entry(entity);
        if (entry.State != EntityState.Detached) entry.State = EntityState.Detached;
    }

    private void WarnIfTruncated(int count, NotificationType type)
    {
        if (count < MaxAlertsPerCategory) return;

        _logger.LogWarning(
            "System alert generation hit the per-category cap of {Cap} for {Type}; the rest will be picked up on the next run.",
            MaxAlertsPerCategory, type);
    }

    private static string Describe(AlertRow row) =>
        string.IsNullOrWhiteSpace(row.MemberCode) ? row.MemberName : $"{row.MemberCode} – {row.MemberName}";

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];

    /// <summary>Flat subscription row the three membership alerts are built from.</summary>
    private sealed class AlertRow
    {
        public int SubscriptionId { get; init; }
        public string SubscriptionCode { get; init; } = string.Empty;
        public int MemberId { get; init; }
        public string MemberName { get; init; } = string.Empty;
        public string? MemberCode { get; init; }
        public DateTime EndDate { get; init; }
        public int GracePeriodDays { get; init; }
        public decimal Outstanding { get; init; }

        public static readonly Expression<Func<Subscription, AlertRow>> Projection = s => new AlertRow
        {
            SubscriptionId = s.Id,
            SubscriptionCode = s.SubscriptionCode,
            MemberId = s.MemberId,
            MemberName = s.Member != null ? s.Member.FullName : string.Empty,
            MemberCode = s.Member != null ? s.Member.MemberCode : null,
            EndDate = s.EndDate,
            GracePeriodDays = s.GracePeriodDays,
            Outstanding = s.FinalAmount - s.PaidAmount
        };
    }
}
