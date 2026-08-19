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
/// Operating expenses (rent, salaries, equipment, utilities…) and their categories. Expenses feed
/// the profit and loss report, so amounts are always decimal and rounded to two places on save.
/// </summary>
public sealed class ExpenseService : IExpenseService
{
    private readonly GymDbContext _db;
    private readonly ICodeGeneratorService _codes;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;
    private readonly ILogger<ExpenseService> _logger;

    public ExpenseService(
        GymDbContext db,
        ICodeGeneratorService codes,
        IDateTimeProvider clock,
        ICurrentUserService currentUser,
        IAuditService audit,
        ILogger<ExpenseService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _codes = codes ?? throw new ArgumentNullException(nameof(codes));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private static readonly Expression<Func<Expense, ExpenseDto>> ToExpenseDto = e => new ExpenseDto
    {
        Id = e.Id,
        ExpenseNumber = e.ExpenseNumber,
        ExpenseCategoryId = e.ExpenseCategoryId,
        CategoryName = e.ExpenseCategory != null ? e.ExpenseCategory.Name : string.Empty,
        Title = e.Title,
        Description = e.Description,
        Amount = e.Amount,
        ExpenseDate = e.ExpenseDate,
        PaymentMethodId = e.PaymentMethodId,
        PaymentMethodName = e.PaymentMethod != null ? e.PaymentMethod.Name : null,
        VendorName = e.VendorName,
        ReferenceNumber = e.ReferenceNumber,
        AttachmentPath = e.AttachmentPath,
        CreatedAt = e.CreatedAt
    };

    public async Task<PagedResult<ExpenseDto>> GetPagedAsync(ExpenseQueryDto query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var source = query.IncludeDeleted
            ? _db.Expenses.IgnoreQueryFilters()
            : _db.Expenses.AsQueryable();

        var q = source.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            q = q.Where(e =>
                EF.Functions.Like(e.ExpenseNumber, pattern) ||
                EF.Functions.Like(e.Title, pattern) ||
                (e.VendorName != null && EF.Functions.Like(e.VendorName, pattern)) ||
                (e.ReferenceNumber != null && EF.Functions.Like(e.ReferenceNumber, pattern)));
        }

        if (query.ExpenseCategoryId is > 0)
            q = q.Where(e => e.ExpenseCategoryId == query.ExpenseCategoryId);

        if (query.FromDate is not null)
        {
            var from = query.FromDate.Value.Date;
            q = q.Where(e => e.ExpenseDate >= from);
        }

        if (query.ToDate is not null)
        {
            var toExclusive = query.ToDate.Value.Date.AddDays(1);
            q = q.Where(e => e.ExpenseDate < toExclusive);
        }

        if (query.MinAmount is not null) q = q.Where(e => e.Amount >= query.MinAmount);
        if (query.MaxAmount is not null) q = q.Where(e => e.Amount <= query.MaxAmount);

        var total = await q.CountAsync(ct).ConfigureAwait(false);

        q = (query.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "amount" => query.SortDescending
                ? q.OrderByDescending(e => e.Amount).ThenByDescending(e => e.Id)
                : q.OrderBy(e => e.Amount).ThenBy(e => e.Id),
            "title" => query.SortDescending
                ? q.OrderByDescending(e => e.Title).ThenByDescending(e => e.Id)
                : q.OrderBy(e => e.Title).ThenBy(e => e.Id),
            "expensedate" => query.SortDescending
                ? q.OrderByDescending(e => e.ExpenseDate).ThenByDescending(e => e.Id)
                : q.OrderBy(e => e.ExpenseDate).ThenBy(e => e.Id),
            _ => q.OrderByDescending(e => e.ExpenseDate).ThenByDescending(e => e.Id)
        };

        var items = await q
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(ToExpenseDto)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        await FillRecordedByNamesAsync(source, items, ct).ConfigureAwait(false);

        return new PagedResult<ExpenseDto>(items, total, query.PageNumber, query.PageSize);
    }

    public async Task<ExpenseDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var dto = await _db.Expenses
            .AsNoTracking()
            .Where(e => e.Id == id)
            .Select(ToExpenseDto)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (dto is null) throw new NotFoundAppException("Expense", id);

        await FillRecordedByNamesAsync(_db.Expenses, new[] { dto }, ct).ConfigureAwait(false);
        return dto;
    }

    public async Task<ExpenseDto> SaveAsync(SaveExpenseDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var title = Normalize(dto.Title)
                    ?? throw new ValidationAppException(nameof(dto.Title), "An expense title is required.");

        if (dto.Amount <= 0m)
            throw new ValidationAppException(nameof(dto.Amount), "Amount must be greater than zero.");

        if (dto.ExpenseDate.Date > _clock.Today.Date)
            throw new ValidationAppException(nameof(dto.ExpenseDate), "An expense cannot be dated in the future.");

        var category = await _db.ExpenseCategories
                           .AsNoTracking()
                           .FirstOrDefaultAsync(c => c.Id == dto.ExpenseCategoryId, ct)
                           .ConfigureAwait(false)
                       ?? throw new NotFoundAppException("ExpenseCategory", dto.ExpenseCategoryId);

        if (!category.IsActive)
            throw new BusinessRuleAppException($"Expense category '{category.Name}' is inactive.");

        if (dto.PaymentMethodId is > 0)
        {
            var methodExists = await _db.PaymentMethods
                .AsNoTracking()
                .AnyAsync(m => m.Id == dto.PaymentMethodId, ct)
                .ConfigureAwait(false);

            if (!methodExists)
                throw new NotFoundAppException("PaymentMethod", dto.PaymentMethodId.Value);
        }

        var isNew = dto.Id <= 0;
        Expense entity;
        object? oldValues = null;

        if (isNew)
        {
            entity = new Expense
            {
                ExpenseNumber = await _codes.NextExpenseNumberAsync(ct).ConfigureAwait(false),
                RecordedByUserId = _currentUser.UserId
            };
            _db.Expenses.Add(entity);
        }
        else
        {
            entity = await _db.Expenses
                         .FirstOrDefaultAsync(e => e.Id == dto.Id, ct)
                         .ConfigureAwait(false)
                     ?? throw new NotFoundAppException("Expense", dto.Id);

            oldValues = new
            {
                entity.ExpenseCategoryId,
                entity.Title,
                entity.Amount,
                entity.ExpenseDate,
                entity.VendorName,
                entity.ReferenceNumber
            };
        }

        entity.ExpenseCategoryId = category.Id;
        entity.Title = title;
        entity.Description = Normalize(dto.Description);
        entity.Amount = Round(dto.Amount);
        entity.ExpenseDate = dto.ExpenseDate.Date;
        entity.PaymentMethodId = dto.PaymentMethodId is > 0 ? dto.PaymentMethodId : null;
        entity.VendorName = Normalize(dto.VendorName);
        entity.ReferenceNumber = Normalize(dto.ReferenceNumber);
        entity.AttachmentPath = Normalize(dto.AttachmentPath);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(isNew ? AuditActions.Create : AuditActions.Update, nameof(Expense), entity.Id,
            oldValues,
            new { entity.ExpenseNumber, entity.ExpenseCategoryId, entity.Title, entity.Amount, entity.ExpenseDate },
            $"Expense {entity.ExpenseNumber} ({entity.Title}) of {entity.Amount:0.00} " +
            $"{(isNew ? "recorded" : "updated")}.",
            ct).ConfigureAwait(false);

        return await GetByIdAsync(entity.Id, ct).ConfigureAwait(false);
    }

    public async Task SoftDeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await _db.Expenses
                         .FirstOrDefaultAsync(e => e.Id == id, ct)
                         .ConfigureAwait(false)
                     ?? throw new NotFoundAppException("Expense", id);

        _db.Expenses.Remove(entity); // The context turns this into a soft delete.
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.SoftDelete, nameof(Expense), entity.Id,
            new { entity.ExpenseNumber, entity.Title, entity.Amount }, null,
            $"Expense {entity.ExpenseNumber} moved to the recycle bin.", ct).ConfigureAwait(false);
    }

    public async Task RestoreAsync(int id, CancellationToken ct = default)
    {
        var entity = await _db.Expenses
                         .IgnoreQueryFilters()
                         .FirstOrDefaultAsync(e => e.Id == id, ct)
                         .ConfigureAwait(false)
                     ?? throw new NotFoundAppException("Expense", id);

        if (!entity.IsDeleted) return;

        entity.IsDeleted = false;
        entity.DeletedAt = null;
        entity.DeletedBy = null;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Restore, nameof(Expense), entity.Id, null,
            new { entity.ExpenseNumber, entity.Title, entity.Amount },
            $"Expense {entity.ExpenseNumber} restored from the recycle bin.", ct).ConfigureAwait(false);
    }

    public async Task<List<ExpenseCategoryDto>> GetCategoriesAsync(CancellationToken ct = default) =>
        await _db.ExpenseCategories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new ExpenseCategoryDto
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                IsActive = c.IsActive,
                // The soft-delete filter also applies to this navigation, so deleted expenses
                // are excluded from both aggregates.
                ExpenseCount = c.Expenses.Count(),
                TotalAmount = c.Expenses.Sum(e => (decimal?)e.Amount) ?? 0m
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<ExpenseCategoryDto> SaveCategoryAsync(ExpenseCategoryDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var name = Normalize(dto.Name)
                   ?? throw new ValidationAppException(nameof(dto.Name), "A category name is required.");

        var duplicate = await _db.ExpenseCategories
            .AsNoTracking()
            .AnyAsync(c => c.Name == name && c.Id != dto.Id, ct)
            .ConfigureAwait(false);

        if (duplicate)
            throw new ConflictAppException($"An expense category named '{name}' already exists.");

        var isNew = dto.Id <= 0;
        ExpenseCategory entity;

        if (isNew)
        {
            entity = new ExpenseCategory();
            _db.ExpenseCategories.Add(entity);
        }
        else
        {
            entity = await _db.ExpenseCategories
                         .FirstOrDefaultAsync(c => c.Id == dto.Id, ct)
                         .ConfigureAwait(false)
                     ?? throw new NotFoundAppException("ExpenseCategory", dto.Id);
        }

        entity.Name = name;
        entity.Description = Normalize(dto.Description);
        entity.IsActive = dto.IsActive;

        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogWarning(ex, "Duplicate expense category name '{Name}'.", name);
            throw new ConflictAppException($"An expense category named '{name}' already exists.");
        }

        await _audit.LogAsync(AuditActions.ConfigurationChanged, nameof(ExpenseCategory), entity.Id, null,
            new { entity.Name, entity.IsActive },
            $"Expense category '{entity.Name}' {(isNew ? "created" : "updated")}.", ct).ConfigureAwait(false);

        var totals = await _db.Expenses
            .AsNoTracking()
            .Where(e => e.ExpenseCategoryId == entity.Id)
            .GroupBy(e => e.ExpenseCategoryId)
            .Select(g => new { Count = g.Count(), Total = g.Sum(e => e.Amount) })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return new ExpenseCategoryDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            IsActive = entity.IsActive,
            ExpenseCount = totals?.Count ?? 0,
            TotalAmount = totals?.Total ?? 0m
        };
    }

    /// <summary>
    /// Resolves the recording user's display name for a page of expenses with one extra query,
    /// since <see cref="Expense"/> has no navigation to <see cref="User"/>.
    /// </summary>
    private async Task FillRecordedByNamesAsync(IQueryable<Expense> source, IReadOnlyCollection<ExpenseDto> items,
        CancellationToken ct)
    {
        if (items.Count == 0) return;

        var ids = items.Select(i => i.Id).ToList();

        var pairs = await source
            .AsNoTracking()
            .Where(e => ids.Contains(e.Id) && e.RecordedByUserId != null)
            .Select(e => new
            {
                e.Id,
                UserName = _db.Users
                    .Where(u => u.Id == e.RecordedByUserId)
                    .Select(u => u.FullName)
                    .FirstOrDefault()
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var byId = pairs.ToDictionary(p => p.Id, p => p.UserName);

        foreach (var item in items)
        {
            if (byId.TryGetValue(item.Id, out var name)) item.RecordedByName = name;
        }
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
