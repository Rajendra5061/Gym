using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GymManagement.Infrastructure.Services;

/// <summary>
/// Read-only views over the audit trail and the login attempt log. The audit trail is append-only:
/// this service never writes, updates or deletes, so every query runs with <c>AsNoTracking()</c>.
/// </summary>
public sealed class AuditQueryService : IAuditQueryService
{
    /// <summary>Upper bound for the dashboard activity feed.</summary>
    private const int MaxRecentActivity = 100;

    private readonly GymDbContext _db;

    public AuditQueryService(GymDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<PagedResult<AuditLogDto>> GetPagedAsync(AuditQueryDto query,
        CancellationToken ct = default)
    {
        query ??= new AuditQueryDto();

        var source = _db.AuditLogs.AsNoTracking();

        if (query.UserId.HasValue)
            source = source.Where(a => a.UserId == query.UserId.Value);

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            var action = query.Action.Trim();
            source = source.Where(a => a.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityName))
        {
            var entity = query.EntityName.Trim();
            source = source.Where(a => a.EntityName == entity);
        }

        if (query.EntityId.HasValue)
            source = source.Where(a => a.EntityId == query.EntityId.Value);

        if (query.FromDate.HasValue)
        {
            var from = query.FromDate.Value;
            source = source.Where(a => a.ChangedAtUtc >= from);
        }

        if (query.ToDate.HasValue)
        {
            // Inclusive upper bound: the filter panel supplies a date, not an instant.
            var to = query.ToDate.Value.Date.AddDays(1);
            source = source.Where(a => a.ChangedAtUtc < to);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(a =>
                EF.Functions.Like(a.Action, $"%{term}%") ||
                EF.Functions.Like(a.EntityName, $"%{term}%") ||
                (a.Description != null && EF.Functions.Like(a.Description, $"%{term}%")) ||
                (a.UserName != null && EF.Functions.Like(a.UserName, $"%{term}%")));
        }

        var total = await source.CountAsync(ct).ConfigureAwait(false);
        if (total == 0)
            return new PagedResult<AuditLogDto>(Array.Empty<AuditLogDto>(), 0, query.PageNumber, query.PageSize);

        var items = await source
            .OrderByDescending(a => a.ChangedAtUtc)
            .ThenByDescending(a => a.Id)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(a => new AuditLogDto
            {
                Id = a.Id,
                UserId = a.UserId,
                UserName = a.UserName,
                Action = a.Action,
                EntityName = a.EntityName,
                EntityId = a.EntityId,
                OldValues = a.OldValues,
                NewValues = a.NewValues,
                Description = a.Description,
                IpAddress = a.IpAddress,
                DeviceInfo = a.DeviceInfo,
                ChangedAtUtc = a.ChangedAtUtc
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedResult<AuditLogDto>(items, total, query.PageNumber, query.PageSize);
    }

    public async Task<PagedResult<LoginAttemptDto>> GetLoginAttemptsAsync(PagedRequest query,
        CancellationToken ct = default)
    {
        query ??= new PagedRequest();

        var source = _db.LoginAttempts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(l =>
                EF.Functions.Like(l.UserNameOrEmail, $"%{term}%") ||
                (l.IpAddress != null && EF.Functions.Like(l.IpAddress, $"%{term}%")));
        }

        var total = await source.CountAsync(ct).ConfigureAwait(false);
        if (total == 0)
            return new PagedResult<LoginAttemptDto>(Array.Empty<LoginAttemptDto>(), 0, query.PageNumber, query.PageSize);

        var items = await source
            .OrderByDescending(l => l.AttemptedAtUtc)
            .ThenByDescending(l => l.Id)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(l => new LoginAttemptDto
            {
                Id = l.Id,
                UserId = l.UserId,
                UserNameOrEmail = l.UserNameOrEmail,
                Result = l.Result,
                AttemptedAtUtc = l.AttemptedAtUtc,
                IpAddress = l.IpAddress,
                DeviceInfo = l.DeviceInfo,
                FailureReason = l.FailureReason
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedResult<LoginAttemptDto>(items, total, query.PageNumber, query.PageSize);
    }

    public async Task<List<RecentActivityDto>> GetRecentActivityAsync(int take = 10,
        CancellationToken ct = default)
    {
        var wanted = Math.Clamp(take, 1, MaxRecentActivity);

        var rows = await _db.AuditLogs
            .AsNoTracking()
            .OrderByDescending(a => a.ChangedAtUtc)
            .ThenByDescending(a => a.Id)
            .Take(wanted)
            .Select(a => new RecentActivityDto
            {
                WhenUtc = a.ChangedAtUtc,
                Action = a.Action,
                EntityName = a.EntityName,
                EntityId = a.EntityId,
                Description = a.Description,
                UserName = a.UserName
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // The icon name is a presentation concern, so it is resolved after materialisation.
        foreach (var row in rows) row.Icon = IconFor(row.Action);

        return rows;
    }

    public Task<List<string>> GetDistinctActionsAsync(CancellationToken ct = default) =>
        _db.AuditLogs
            .AsNoTracking()
            .Select(a => a.Action)
            .Distinct()
            .OrderBy(action => action)
            .ToListAsync(ct);

    public Task<List<string>> GetDistinctEntitiesAsync(CancellationToken ct = default) =>
        _db.AuditLogs
            .AsNoTracking()
            .Select(a => a.EntityName)
            .Distinct()
            .OrderBy(entity => entity)
            .ToListAsync(ct);

    /// <summary>Maps an audit action to the icon key the client renders in the activity feed.</summary>
    private static string IconFor(string? action) => action switch
    {
        AuditActions.Login or AuditActions.Logout or AuditActions.LoginFailed => "login",

        AuditActions.PaymentCreated or AuditActions.PaymentConfirmed
            or AuditActions.PaymentRefunded => "payment",

        AuditActions.SubscriptionCreated or AuditActions.SubscriptionRenewed
            or AuditActions.SubscriptionCancelled or AuditActions.SubscriptionFrozen
            or AuditActions.SubscriptionResumed or AuditActions.SubscriptionChanged => "subscription",

        AuditActions.Create => "add",
        AuditActions.Update or AuditActions.ConfigurationChanged => "edit",
        AuditActions.SoftDelete or AuditActions.Delete => "delete",
        AuditActions.Restore => "restore",

        _ => "activity"
    };
}
