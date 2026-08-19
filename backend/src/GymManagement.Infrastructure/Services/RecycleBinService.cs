using System.Globalization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Common;
using GymManagement.Domain.Constants;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Services;

/// <summary>
/// The recycle bin over every soft-deletable entity. Reads bypass the global soft-delete filters
/// with <c>IgnoreQueryFilters()</c>; restoring clears the delete stamp and purging is the only place
/// in the application that physically deletes a business row.
/// </summary>
public sealed class RecycleBinService : IRecycleBinService
{
    /// <summary>Typing this exactly is required before anything is permanently deleted.</summary>
    public const string PurgeConfirmation = "PERMANENTLY DELETE";

    private readonly GymDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;
    private readonly ILogger<RecycleBinService> _logger;

    public RecycleBinService(
        GymDbContext db,
        ICurrentUserService currentUser,
        IAuditService audit,
        ILogger<RecycleBinService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Every supported entity, mapped to the three operations the service needs. Keeping the map in
    /// one place is what keeps <see cref="GetPagedAsync"/>, <see cref="RestoreAsync"/> and
    /// <see cref="PurgeAsync"/> short and identical in behaviour across entity types.
    /// </summary>
    private static readonly Dictionary<string, EntitySource> Sources =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Members"] = For<Member>("Members", db => Deleted<Member>(db).Select(m => new DeletedRow
            {
                EntityName = "Members",
                EntityId = m.Id,
                Code = m.MemberCode,
                Name = m.FullName,
                Note = m.Phone,
                DeletedAt = m.DeletedAt,
                DeletedBy = m.DeletedBy
            })),

            ["Trainers"] = For<Trainer>("Trainers", db => Deleted<Trainer>(db).Select(t => new DeletedRow
            {
                EntityName = "Trainers",
                EntityId = t.Id,
                Code = t.TrainerCode,
                Name = t.FullName,
                Note = t.Specialization,
                DeletedAt = t.DeletedAt,
                DeletedBy = t.DeletedBy
            })),

            ["Subscriptions"] = For<Subscription>("Subscriptions", db => Deleted<Subscription>(db).Select(s => new DeletedRow
            {
                EntityName = "Subscriptions",
                EntityId = s.Id,
                Code = s.SubscriptionCode,
                Name = s.Member != null ? s.Member.FullName : null,
                Note = s.MembershipPlan != null ? s.MembershipPlan.Name : null,
                Amount = s.FinalAmount,
                Date = s.EndDate,
                DeletedAt = s.DeletedAt,
                DeletedBy = s.DeletedBy
            })),

            ["Payments"] = For<Payment>("Payments", db => Deleted<Payment>(db).Select(p => new DeletedRow
            {
                EntityName = "Payments",
                EntityId = p.Id,
                Code = p.ReceiptNumber,
                Name = p.Member != null ? p.Member.FullName : null,
                Amount = p.FinalAmount,
                Date = p.PaymentDate,
                DeletedAt = p.DeletedAt,
                DeletedBy = p.DeletedBy
            })),

            ["Exercises"] = For<Exercise>("Exercises", db => Deleted<Exercise>(db).Select(e => new DeletedRow
            {
                EntityName = "Exercises",
                EntityId = e.Id,
                Name = e.Name,
                Note = e.Equipment,
                DeletedAt = e.DeletedAt,
                DeletedBy = e.DeletedBy
            })),

            ["WorkoutPlans"] = For<WorkoutPlan>("WorkoutPlans", db => Deleted<WorkoutPlan>(db).Select(w => new DeletedRow
            {
                EntityName = "WorkoutPlans",
                EntityId = w.Id,
                Name = w.Name,
                Note = w.Goal,
                DeletedAt = w.DeletedAt,
                DeletedBy = w.DeletedBy
            })),

            ["WorkoutSessions"] = For<WorkoutSession>("WorkoutSessions", db => Deleted<WorkoutSession>(db).Select(s => new DeletedRow
            {
                EntityName = "WorkoutSessions",
                EntityId = s.Id,
                Name = s.Member != null ? s.Member.FullName : null,
                Note = s.WorkoutPlan != null ? s.WorkoutPlan.Name : null,
                Date = s.SessionDate,
                DeletedAt = s.DeletedAt,
                DeletedBy = s.DeletedBy
            })),

            ["Expenses"] = For<Expense>("Expenses", db => Deleted<Expense>(db).Select(e => new DeletedRow
            {
                EntityName = "Expenses",
                EntityId = e.Id,
                Code = e.ExpenseNumber,
                Name = e.Title,
                Note = e.VendorName,
                Amount = e.Amount,
                Date = e.ExpenseDate,
                DeletedAt = e.DeletedAt,
                DeletedBy = e.DeletedBy
            })),

            ["Users"] = For<User>("Users", db => Deleted<User>(db).Select(u => new DeletedRow
            {
                EntityName = "Users",
                EntityId = u.Id,
                Code = u.UserName,
                Name = u.FullName,
                Note = u.Email,
                DeletedAt = u.DeletedAt,
                DeletedBy = u.DeletedBy
            })),

            ["MemberDocuments"] = For<MemberDocument>("MemberDocuments", db => Deleted<MemberDocument>(db).Select(d => new DeletedRow
            {
                EntityName = "MemberDocuments",
                EntityId = d.Id,
                Code = d.DocumentType,
                Name = d.FileName,
                Note = d.Member != null ? d.Member.FullName : null,
                DeletedAt = d.DeletedAt,
                DeletedBy = d.DeletedBy
            }))
        };

    // ----------------------------------------------------------------- read

    public async Task<PagedResult<RecycleBinItemDto>> GetPagedAsync(RecycleBinQueryDto query,
        CancellationToken ct = default)
    {
        query ??= new RecycleBinQueryDto();

        var selected = Select(query.EntityName);
        if (selected.Count == 0)
            return new PagedResult<RecycleBinItemDto>(Array.Empty<RecycleBinItemDto>(), 0, query.PageNumber, query.PageSize);

        // Each entity lives in its own table, so the page is assembled from bounded per-entity
        // queries: every one of them only fetches as far as the requested page window reaches.
        var window = query.Skip + query.PageSize;

        var total = 0;
        var candidates = new List<DeletedRow>(Math.Min(window * selected.Count, 4096));

        foreach (var source in selected)
        {
            var rows = Filter(source.Deleted(_db), query);

            total += await rows.CountAsync(ct).ConfigureAwait(false);

            candidates.AddRange(await rows
                .OrderByDescending(r => r.DeletedAt)
                .ThenByDescending(r => r.EntityId)
                .Take(window)
                .ToListAsync(ct)
                .ConfigureAwait(false));
        }

        var page = candidates
            .OrderByDescending(r => r.DeletedAt)
            .ThenBy(r => r.EntityName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(r => r.EntityId)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToList();

        var names = await ResolveUserNamesAsync(page, ct).ConfigureAwait(false);

        var items = page.Select(r => new RecycleBinItemDto
        {
            EntityName = r.EntityName,
            EntityId = r.EntityId,
            DisplayName = Display(r),
            Details = Details(r),
            DeletedAt = r.DeletedAt,
            DeletedBy = r.DeletedBy,
            DeletedByName = r.DeletedBy.HasValue && names.TryGetValue(r.DeletedBy.Value, out var name) ? name : null
        }).ToList();

        return new PagedResult<RecycleBinItemDto>(items, total, query.PageNumber, query.PageSize);
    }

    public async Task<List<LookupDto>> GetEntityTypesAsync(CancellationToken ct = default)
    {
        var result = new List<LookupDto>(Sources.Count);
        var id = 1;

        foreach (var source in Sources.Values.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            var count = await source.CountAsync(_db, ct).ConfigureAwait(false);

            result.Add(new LookupDto
            {
                Id = id++,
                Name = source.Name,
                Code = source.Name,
                Extra = count.ToString(CultureInfo.InvariantCulture),
                IsActive = count > 0
            });
        }

        return result;
    }

    // ---------------------------------------------------------------- write

    public async Task<int> RestoreAsync(RecycleBinActionDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var source = Resolve(dto.EntityName);
        var ids = Ids(dto);
        if (ids.Count == 0) return 0;

        var restored = await source.RestoreAsync(_db, ids, ct).ConfigureAwait(false);

        if (restored > 0)
        {
            await _audit.LogAsync(AuditActions.Restore, source.Name, ids.Count == 1 ? ids[0] : null,
                newValues: new { Ids = ids },
                description: $"Restored {restored} {source.Name} record(s) from the recycle bin: {Describe(ids)}.", ct: ct)
                .ConfigureAwait(false);
        }

        return restored;
    }

    public async Task<int> PurgeAsync(RecycleBinActionDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (!string.Equals(dto.ConfirmationText, PurgeConfirmation, StringComparison.Ordinal))
        {
            throw new ValidationAppException(nameof(dto.ConfirmationText),
                $"Type '{PurgeConfirmation}' exactly to permanently delete these records.");
        }

        if (!_currentUser.HasPermission(Permissions.RecycleBinPurge))
            throw new ForbiddenAppException("You do not have permission to permanently delete records.");

        var source = Resolve(dto.EntityName);
        var ids = Ids(dto);
        if (ids.Count == 0) return 0;

        int purged;

        try
        {
            purged = await source.PurgeAsync(_db, ids, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbUpdateException or SqlException)
        {
            _logger.LogWarning(ex, "Purge of {Entity} {Ids} was blocked by the database.", source.Name, Describe(ids));

            throw new BusinessRuleAppException(
                $"These {source.Name} records are still referenced by other records (payments, subscriptions, " +
                "attendance…). Purge the dependent records first, then try again.");
        }

        if (purged > 0)
        {
            await _audit.LogAsync(AuditActions.Delete, source.Name, ids.Count == 1 ? ids[0] : null,
                oldValues: new { Ids = ids },
                description: $"Permanently deleted {purged} {source.Name} record(s) from the recycle bin: {Describe(ids)}.", ct: ct)
                .ConfigureAwait(false);
        }

        return purged;
    }

    // -------------------------------------------------------------- helpers

    private static IQueryable<T> Deleted<T>(GymDbContext db) where T : class, ISoftDeletable =>
        db.Set<T>().IgnoreQueryFilters().AsNoTracking().Where(x => x.IsDeleted);

    /// <summary>Builds the map entry for one entity type; all three operations share the same shape.</summary>
    private static EntitySource For<T>(string name, Func<GymDbContext, IQueryable<DeletedRow>> projection)
        where T : SoftDeletableEntity => new()
        {
            Name = name,
            Deleted = projection,
            CountAsync = (db, ct) => Deleted<T>(db).CountAsync(ct),

            // Set based updates/deletes: no entity is loaded and the soft-delete filter is bypassed.
            RestoreAsync = (db, ids, ct) => db.Set<T>()
                .IgnoreQueryFilters()
                .Where(x => x.IsDeleted && ids.Contains(x.Id))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.IsDeleted, false)
                    .SetProperty(x => x.DeletedAt, (DateTime?)null)
                    .SetProperty(x => x.DeletedBy, (int?)null), ct),

            PurgeAsync = (db, ids, ct) => db.Set<T>()
                .IgnoreQueryFilters()
                .Where(x => x.IsDeleted && ids.Contains(x.Id))
                .ExecuteDeleteAsync(ct)
        };

    private static List<EntitySource> Select(string? entityName)
    {
        if (string.IsNullOrWhiteSpace(entityName))
            return Sources.Values.ToList();

        return Sources.TryGetValue(entityName.Trim(), out var source)
            ? new List<EntitySource> { source }
            : new List<EntitySource>();
    }

    private static EntitySource Resolve(string? entityName)
    {
        if (!string.IsNullOrWhiteSpace(entityName) && Sources.TryGetValue(entityName.Trim(), out var source))
            return source;

        throw new NotFoundAppException(
            $"'{entityName}' is not a recycle bin entity. Supported entities: {string.Join(", ", Sources.Keys)}.");
    }

    private static IQueryable<DeletedRow> Filter(IQueryable<DeletedRow> rows, RecycleBinQueryDto query)
    {
        if (query.FromDate.HasValue)
        {
            var from = query.FromDate.Value.Date;
            rows = rows.Where(r => r.DeletedAt >= from);
        }

        if (query.ToDate.HasValue)
        {
            // Inclusive upper bound: the filter panel supplies a date, not an instant.
            var to = query.ToDate.Value.Date.AddDays(1);
            rows = rows.Where(r => r.DeletedAt < to);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            rows = rows.Where(r =>
                (r.Code != null && EF.Functions.Like(r.Code, $"%{term}%")) ||
                (r.Name != null && EF.Functions.Like(r.Name, $"%{term}%")) ||
                (r.Note != null && EF.Functions.Like(r.Note, $"%{term}%")));
        }

        return rows;
    }

    private static List<int> Ids(RecycleBinActionDto dto) =>
        dto.EntityIds is null
            ? new List<int>()
            : dto.EntityIds.Where(id => id > 0).Distinct().ToList();

    /// <summary>One extra query resolves the display name of everyone who deleted a row on this page.</summary>
    private async Task<Dictionary<int, string>> ResolveUserNamesAsync(List<DeletedRow> page, CancellationToken ct)
    {
        var userIds = page
            .Where(r => r.DeletedBy.HasValue)
            .Select(r => r.DeletedBy!.Value)
            .Distinct()
            .ToList();

        if (userIds.Count == 0) return new Dictionary<int, string>();

        return await _db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.UserName })
            .ToDictionaryAsync(u => u.Id,
                u => string.IsNullOrWhiteSpace(u.FullName) ? u.UserName : u.FullName, ct)
            .ConfigureAwait(false);
    }

    /// <summary>Human label: the business code and name of the row, whichever of them exist.</summary>
    private static string Display(DeletedRow row)
    {
        var parts = new[] { row.Code, row.Name }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToArray();

        return parts.Length == 0
            ? $"{row.EntityName} #{row.EntityId}"
            : string.Join(" – ", parts);
    }

    /// <summary>Short summary line: amount, the row's own date and any secondary label.</summary>
    private static string? Details(DeletedRow row)
    {
        var parts = new List<string>(3);

        if (!string.IsNullOrWhiteSpace(row.Note)) parts.Add(row.Note!.Trim());
        if (row.Amount.HasValue) parts.Add(row.Amount.Value.ToString("N2", CultureInfo.InvariantCulture));
        if (row.Date.HasValue) parts.Add(row.Date.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture));

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string Describe(List<int> ids) =>
        ids.Count <= 20
            ? string.Join(", ", ids)
            : $"{string.Join(", ", ids.Take(20))} and {ids.Count - 20} more";

    /// <summary>Flat shape every entity projects into so one pipeline can page over all of them.</summary>
    private sealed class DeletedRow
    {
        public string EntityName { get; init; } = string.Empty;
        public int EntityId { get; init; }
        /// <summary>Business code: member code, receipt number, user name.</summary>
        public string? Code { get; init; }
        /// <summary>Human label: member name, plan name, file name.</summary>
        public string? Name { get; init; }
        /// <summary>Secondary label shown in the details column.</summary>
        public string? Note { get; init; }
        public decimal? Amount { get; init; }
        public DateTime? Date { get; init; }
        public DateTime? DeletedAt { get; init; }
        public int? DeletedBy { get; init; }
    }

    /// <summary>One supported entity type and the four operations the recycle bin performs on it.</summary>
    private sealed class EntitySource
    {
        public required string Name { get; init; }
        public required Func<GymDbContext, IQueryable<DeletedRow>> Deleted { get; init; }
        public required Func<GymDbContext, CancellationToken, Task<int>> CountAsync { get; init; }
        public required Func<GymDbContext, List<int>, CancellationToken, Task<int>> RestoreAsync { get; init; }
        public required Func<GymDbContext, List<int>, CancellationToken, Task<int>> PurgeAsync { get; init; }
    }
}
