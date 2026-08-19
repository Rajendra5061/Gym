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
/// Gym equipment inventory: asset tags, purchase cost, condition and the service schedule.
/// Purchase cost is money, so it is decimal and rounded to two places on save.
/// </summary>
public sealed class EquipmentService : IEquipmentService
{
    private readonly GymDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditService _audit;
    private readonly ILogger<EquipmentService> _logger;

    public EquipmentService(
        GymDbContext db,
        IDateTimeProvider clock,
        IAuditService audit,
        ILogger<EquipmentService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private static readonly Expression<Func<Equipment, EquipmentDetailDto>> ToDetailDto = e => new EquipmentDetailDto
    {
        Id = e.Id,
        Name = e.Name,
        Code = e.Code,
        Category = e.Category,
        SerialNumber = e.SerialNumber,
        Manufacturer = e.Manufacturer,
        PurchaseDate = e.PurchaseDate,
        PurchaseCost = e.PurchaseCost,
        Quantity = e.Quantity,
        Condition = e.Condition,
        Location = e.Location,
        WarrantyExpiry = e.WarrantyExpiry,
        LastServicedOn = e.LastServicedOn,
        NextServiceDue = e.NextServiceDue,
        Notes = e.Notes,
        IsActive = e.IsActive,
        IsDeleted = e.IsDeleted,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };

    private static readonly Expression<Func<Equipment, EquipmentListDto>> ToListDto = e => new EquipmentListDto
    {
        Id = e.Id,
        Name = e.Name,
        Code = e.Code,
        Category = e.Category,
        Manufacturer = e.Manufacturer,
        Quantity = e.Quantity,
        Condition = e.Condition,
        Location = e.Location,
        NextServiceDue = e.NextServiceDue,
        IsActive = e.IsActive,
        IsDeleted = e.IsDeleted
    };

    public async Task<PagedResult<EquipmentListDto>> GetPagedAsync(EquipmentQueryDto query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var q = (query.IncludeDeleted ? _db.Equipment.IgnoreQueryFilters() : _db.Equipment.AsQueryable())
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            q = q.Where(e =>
                EF.Functions.Like(e.Name, pattern) ||
                EF.Functions.Like(e.Code, pattern) ||
                EF.Functions.Like(e.Category, pattern) ||
                (e.SerialNumber != null && EF.Functions.Like(e.SerialNumber, pattern)) ||
                (e.Manufacturer != null && EF.Functions.Like(e.Manufacturer, pattern)) ||
                (e.Location != null && EF.Functions.Like(e.Location, pattern)));
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var category = query.Category.Trim();
            q = q.Where(e => e.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(query.Location))
        {
            var location = query.Location.Trim();
            q = q.Where(e => e.Location == location);
        }

        if (query.Condition is not null) q = q.Where(e => e.Condition == query.Condition);
        if (query.IsActive is not null) q = q.Where(e => e.IsActive == query.IsActive);

        if (query.ServiceDue == true)
        {
            var today = _clock.Today.Date;
            q = q.Where(e => e.NextServiceDue != null && e.NextServiceDue <= today);
        }

        var total = await q.CountAsync(ct).ConfigureAwait(false);

        q = (query.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "name" => query.SortDescending
                ? q.OrderByDescending(e => e.Name).ThenByDescending(e => e.Id)
                : q.OrderBy(e => e.Name).ThenBy(e => e.Id),
            "code" => query.SortDescending
                ? q.OrderByDescending(e => e.Code).ThenByDescending(e => e.Id)
                : q.OrderBy(e => e.Code).ThenBy(e => e.Id),
            "category" => query.SortDescending
                ? q.OrderByDescending(e => e.Category).ThenByDescending(e => e.Id)
                : q.OrderBy(e => e.Category).ThenBy(e => e.Id),
            "condition" => query.SortDescending
                ? q.OrderByDescending(e => e.Condition).ThenByDescending(e => e.Id)
                : q.OrderBy(e => e.Condition).ThenBy(e => e.Id),
            "quantity" => query.SortDescending
                ? q.OrderByDescending(e => e.Quantity).ThenByDescending(e => e.Id)
                : q.OrderBy(e => e.Quantity).ThenBy(e => e.Id),
            "nextservicedue" => query.SortDescending
                ? q.OrderByDescending(e => e.NextServiceDue).ThenByDescending(e => e.Id)
                : q.OrderBy(e => e.NextServiceDue).ThenBy(e => e.Id),
            _ => query.SortDescending
                ? q.OrderByDescending(e => e.Name).ThenByDescending(e => e.Id)
                : q.OrderBy(e => e.Name).ThenBy(e => e.Id)
        };

        var items = await q
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(ToListDto)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedResult<EquipmentListDto>(items, total, query.PageNumber, query.PageSize);
    }

    public async Task<EquipmentDetailDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var dto = await _db.Equipment
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e => e.Id == id)
            .Select(ToDetailDto)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return dto ?? throw new NotFoundAppException("Equipment", id);
    }

    public async Task<EquipmentDetailDto> SaveAsync(SaveEquipmentDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var name = Normalize(dto.Name)
                   ?? throw new ValidationAppException(nameof(dto.Name), "An equipment name is required.");

        var category = Normalize(dto.Category)
                       ?? throw new ValidationAppException(nameof(dto.Category), "A category is required.");

        if (dto.Quantity < 1)
            throw new ValidationAppException(nameof(dto.Quantity), "Quantity must be at least one.");

        if (dto.PurchaseCost is < 0m)
            throw new ValidationAppException(nameof(dto.PurchaseCost), "Purchase cost cannot be negative.");

        if (dto.PurchaseDate is not null && dto.PurchaseDate.Value.Date > _clock.Today.Date)
            throw new ValidationAppException(nameof(dto.PurchaseDate), "Purchase date cannot be in the future.");

        if (dto.LastServicedOn is not null && dto.NextServiceDue is not null &&
            dto.NextServiceDue.Value.Date < dto.LastServicedOn.Value.Date)
        {
            throw new ValidationAppException(nameof(dto.NextServiceDue),
                "The next service date cannot be earlier than the last service date.");
        }

        var isNew = dto.Id <= 0;
        var code = Normalize(dto.Code) ?? (isNew
            ? await NextCodeAsync(category, ct).ConfigureAwait(false)
            : throw new ValidationAppException(nameof(dto.Code), "An asset code is required."));

        var duplicate = await _db.Equipment
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(e => e.Code == code && e.Id != dto.Id, ct)
            .ConfigureAwait(false);

        if (duplicate)
            throw new ConflictAppException($"Equipment with the code '{code}' already exists.");

        Equipment entity;
        object? oldValues = null;

        if (isNew)
        {
            entity = new Equipment();
            _db.Equipment.Add(entity);
        }
        else
        {
            entity = await _db.Equipment
                         .FirstOrDefaultAsync(e => e.Id == dto.Id, ct)
                         .ConfigureAwait(false)
                     ?? throw new NotFoundAppException("Equipment", dto.Id);

            oldValues = new
            {
                entity.Code,
                entity.Name,
                entity.Category,
                entity.Quantity,
                entity.Condition,
                entity.Location,
                entity.IsActive
            };
        }

        entity.Name = name;
        entity.Code = code;
        entity.Category = category;
        entity.SerialNumber = Normalize(dto.SerialNumber);
        entity.Manufacturer = Normalize(dto.Manufacturer);
        entity.PurchaseDate = dto.PurchaseDate?.Date;
        entity.PurchaseCost = dto.PurchaseCost is null ? null : Round(dto.PurchaseCost.Value);
        entity.Quantity = dto.Quantity;
        entity.Condition = dto.Condition;
        entity.Location = Normalize(dto.Location);
        entity.WarrantyExpiry = dto.WarrantyExpiry?.Date;
        entity.LastServicedOn = dto.LastServicedOn?.Date;
        entity.NextServiceDue = dto.NextServiceDue?.Date;
        entity.Notes = Normalize(dto.Notes);
        entity.IsActive = dto.IsActive;

        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogWarning(ex, "Duplicate equipment code {Code}.", code);
            throw new ConflictAppException($"Equipment with the code '{code}' already exists.");
        }

        await _audit.LogAsync(isNew ? AuditActions.Create : AuditActions.Update, nameof(Equipment), entity.Id,
            oldValues,
            new { entity.Code, entity.Name, entity.Category, entity.Quantity, entity.Condition },
            $"Equipment {entity.Code} ({entity.Name}) {(isNew ? "added" : "updated")}.",
            ct).ConfigureAwait(false);

        return await GetByIdAsync(entity.Id, ct).ConfigureAwait(false);
    }

    public async Task SoftDeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await _db.Equipment
                         .FirstOrDefaultAsync(e => e.Id == id, ct)
                         .ConfigureAwait(false)
                     ?? throw new NotFoundAppException("Equipment", id);

        _db.Equipment.Remove(entity); // The context turns this into a soft delete.
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.SoftDelete, nameof(Equipment), entity.Id,
            new { entity.Code, entity.Name, entity.Category }, null,
            $"Equipment {entity.Code} moved to the recycle bin.", ct).ConfigureAwait(false);
    }

    public async Task RestoreAsync(int id, CancellationToken ct = default)
    {
        var entity = await _db.Equipment
                         .IgnoreQueryFilters()
                         .FirstOrDefaultAsync(e => e.Id == id, ct)
                         .ConfigureAwait(false)
                     ?? throw new NotFoundAppException("Equipment", id);

        if (!entity.IsDeleted) return;

        var clash = await _db.Equipment
            .AsNoTracking()
            .AnyAsync(e => e.Code == entity.Code && e.Id != entity.Id, ct)
            .ConfigureAwait(false);

        if (clash)
        {
            throw new ConflictAppException(
                $"Another equipment record now uses the code '{entity.Code}', so this one cannot be restored.");
        }

        entity.IsDeleted = false;
        entity.DeletedAt = null;
        entity.DeletedBy = null;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Restore, nameof(Equipment), entity.Id, null,
            new { entity.Code, entity.Name }, $"Equipment {entity.Code} restored from the recycle bin.",
            ct).ConfigureAwait(false);
    }

    public async Task<List<string>> GetCategoriesAsync(CancellationToken ct = default) =>
        await _db.Equipment
            .AsNoTracking()
            .Select(e => e.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    /// <summary>Builds the next <c>EQP-XXX-000</c> asset tag for a category.</summary>
    private async Task<string> NextCodeAsync(string category, CancellationToken ct)
    {
        var letters = new string(category.Where(char.IsLetter).ToArray()).ToUpperInvariant();
        var segment = (letters.Length >= 3 ? letters[..3] : letters.PadRight(3, 'X'));
        var prefix = $"EQP-{segment}-";

        var last = await _db.Equipment
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e => e.Code.StartsWith(prefix))
            .OrderByDescending(e => e.Code)
            .Select(e => e.Code)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        var next = 1;
        if (last is not null && int.TryParse(last[prefix.Length..], out var parsed)) next = parsed + 1;

        return prefix + next.ToString("D3");
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>True for SQL Server unique index / unique constraint violations (2601, 2627).</summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner.GetType().GetProperty("Number")?.GetValue(inner) is int number && number is 2601 or 2627)
                return true;

            if (inner.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
                inner.Message.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
