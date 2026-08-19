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
/// Trainer salary payments. Recording a payment also writes the matching operating expense into
/// the "Salaries" category in the same transaction, so the profit and loss report picks it up
/// without double entry. Deleting a salary row deliberately leaves that expense in place.
/// </summary>
public sealed class SalaryPaymentService : ISalaryPaymentService
{
    private const string Entity = "SalaryPayment";
    private const string SalariesCategoryName = "Salaries";

    private readonly GymDbContext _db;
    private readonly ICodeGeneratorService _codes;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;
    private readonly ILogger<SalaryPaymentService> _logger;

    public SalaryPaymentService(
        GymDbContext db,
        ICodeGeneratorService codes,
        IDateTimeProvider clock,
        ICurrentUserService currentUser,
        IAuditService audit,
        ILogger<SalaryPaymentService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _codes = codes ?? throw new ArgumentNullException(nameof(codes));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private static readonly Expression<Func<SalaryPayment, SalaryPaymentDto>> ToDto = s => new SalaryPaymentDto
    {
        Id = s.Id,
        TrainerId = s.TrainerId,
        TrainerName = s.Trainer != null ? s.Trainer.FullName : string.Empty,
        PeriodYear = s.PeriodYear,
        PeriodMonth = s.PeriodMonth,
        BaseAmount = s.BaseAmount,
        Bonus = s.Bonus,
        Deduction = s.Deduction,
        NetAmount = s.NetAmount,
        PaymentDate = s.PaymentDate,
        PaymentMethodId = s.PaymentMethodId,
        PaymentMethodName = s.PaymentMethod != null ? s.PaymentMethod.Name : null,
        TransactionReference = s.TransactionReference,
        Notes = s.Notes,
        CreatedAt = s.CreatedAt
    };

    public async Task<PagedResult<SalaryPaymentDto>> GetPagedAsync(SalaryQueryDto query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var q = _db.SalaryPayments.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(s =>
                (s.Trainer != null && (EF.Functions.Like(s.Trainer.FullName, $"%{term}%")
                                       || EF.Functions.Like(s.Trainer.TrainerCode, $"%{term}%")))
                || (s.TransactionReference != null && EF.Functions.Like(s.TransactionReference, $"%{term}%")));
        }

        if (query.Year is > 0) q = q.Where(s => s.PeriodYear == query.Year!.Value);
        if (query.Month is >= 1 and <= 12) q = q.Where(s => s.PeriodMonth == query.Month!.Value);
        if (query.TrainerId is > 0) q = q.Where(s => s.TrainerId == query.TrainerId!.Value);

        var total = await q.CountAsync(ct).ConfigureAwait(false);
        if (total == 0)
            return new PagedResult<SalaryPaymentDto>(Array.Empty<SalaryPaymentDto>(), 0, query.PageNumber, query.PageSize);

        var desc = query.SortDescending;
        q = (query.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "trainername" => desc
                ? q.OrderByDescending(s => s.Trainer!.FullName).ThenByDescending(s => s.Id)
                : q.OrderBy(s => s.Trainer!.FullName).ThenBy(s => s.Id),
            "netamount" => desc
                ? q.OrderByDescending(s => s.NetAmount).ThenByDescending(s => s.Id)
                : q.OrderBy(s => s.NetAmount).ThenBy(s => s.Id),
            "paymentdate" => desc
                ? q.OrderByDescending(s => s.PaymentDate).ThenByDescending(s => s.Id)
                : q.OrderBy(s => s.PaymentDate).ThenBy(s => s.Id),
            // Most recent period first.
            _ => q.OrderByDescending(s => s.PeriodYear)
                .ThenByDescending(s => s.PeriodMonth)
                .ThenBy(s => s.Trainer!.FullName)
        };

        var items = await q
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(ToDto)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedResult<SalaryPaymentDto>(items, total, query.PageNumber, query.PageSize);
    }

    public async Task<SalarySummaryDto> GetSummaryAsync(int? year, CancellationToken ct = default)
    {
        var summaryYear = year is >= 2000 and <= 2100 ? year.Value : _clock.Today.Year;

        var rows = await _db.SalaryPayments.AsNoTracking()
            .Where(s => s.PeriodYear == summaryYear)
            .GroupBy(s => s.PeriodMonth)
            .Select(g => new { Month = g.Key, TotalNet = g.Sum(s => s.NetAmount), Payments = g.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var byMonth = rows.ToDictionary(r => r.Month);

        var summary = new SalarySummaryDto { Year = summaryYear };
        for (var month = 1; month <= 12; month++)
        {
            summary.Months.Add(byMonth.TryGetValue(month, out var row)
                ? new SalaryMonthTotalDto { Month = month, TotalNet = row.TotalNet, Payments = row.Payments }
                : new SalaryMonthTotalDto { Month = month });
        }

        summary.TotalYear = rows.Sum(r => r.TotalNet);

        summary.PerTrainer = await _db.SalaryPayments.AsNoTracking()
            .Where(s => s.PeriodYear == summaryYear)
            .GroupBy(s => new
            {
                s.TrainerId,
                TrainerName = s.Trainer != null ? s.Trainer.FullName : string.Empty,
                MonthlySalary = s.Trainer != null ? s.Trainer.MonthlySalary : null
            })
            .Select(g => new SalaryTrainerTotalDto
            {
                TrainerId = g.Key.TrainerId,
                TrainerName = g.Key.TrainerName,
                MonthlySalary = g.Key.MonthlySalary,
                PaidCount = g.Count(),
                TotalNet = g.Sum(s => s.NetAmount)
            })
            .OrderBy(t => t.TrainerName)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return summary;
    }

    public async Task<SalaryPaymentDto> CreateAsync(SaveSalaryPaymentDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.PeriodYear is < 2000 or > 2100)
            throw new ValidationAppException(nameof(dto.PeriodYear), "Year must be between 2000 and 2100.");

        if (dto.PeriodMonth is < 1 or > 12)
            throw new ValidationAppException(nameof(dto.PeriodMonth), "Month must be between 1 and 12.");

        if (dto.BaseAmount < 0m || dto.Bonus < 0m || dto.Deduction < 0m)
            throw new ValidationAppException(nameof(dto.BaseAmount), "Amounts cannot be negative.");

        var netAmount = Round(dto.BaseAmount + dto.Bonus - dto.Deduction);
        if (netAmount < 0m)
            throw new ValidationAppException(nameof(dto.Deduction),
                "The deduction cannot exceed the base amount plus bonus.");

        var trainer = await _db.Trainers.AsNoTracking()
                          .Where(t => t.Id == dto.TrainerId)
                          .Select(t => new { t.Id, t.FullName })
                          .FirstOrDefaultAsync(ct)
                          .ConfigureAwait(false)
                      ?? throw new NotFoundAppException("Trainer", dto.TrainerId);

        if (dto.PaymentMethodId is > 0)
        {
            var methodExists = await _db.PaymentMethods.AsNoTracking()
                .AnyAsync(m => m.Id == dto.PaymentMethodId, ct)
                .ConfigureAwait(false);

            if (!methodExists) throw new NotFoundAppException("PaymentMethod", dto.PaymentMethodId.Value);
        }

        var periodLabel = $"{dto.PeriodMonth:00}/{dto.PeriodYear.ToString(CultureInfo.InvariantCulture)}";

        // One live row per trainer and period. The filtered unique index backs this up under
        // concurrency; the explicit check gives the friendlier message.
        var duplicate = await _db.SalaryPayments.AsNoTracking()
            .AnyAsync(s => s.TrainerId == dto.TrainerId
                           && s.PeriodYear == dto.PeriodYear
                           && s.PeriodMonth == dto.PeriodMonth, ct)
            .ConfigureAwait(false);

        if (duplicate)
            throw new ConflictAppException(
                $"A salary payment for {trainer.FullName} for {periodLabel} has already been recorded.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var payment = new SalaryPayment
        {
            TrainerId = trainer.Id,
            PeriodYear = dto.PeriodYear,
            PeriodMonth = dto.PeriodMonth,
            BaseAmount = Round(dto.BaseAmount),
            Bonus = Round(dto.Bonus),
            Deduction = Round(dto.Deduction),
            NetAmount = netAmount,
            PaymentDate = dto.PaymentDate.Date,
            PaymentMethodId = dto.PaymentMethodId is > 0 ? dto.PaymentMethodId : null,
            TransactionReference = Trim(dto.TransactionReference),
            Notes = Trim(dto.Notes)
        };

        _db.SalaryPayments.Add(payment);

        var expense = new Expense
        {
            ExpenseNumber = await _codes.NextExpenseNumberAsync(ct).ConfigureAwait(false),
            ExpenseCategoryId = await GetOrCreateSalariesCategoryIdAsync(ct).ConfigureAwait(false),
            Title = $"Salary — {trainer.FullName} — {periodLabel}",
            Description = $"Recorded automatically with the salary payment for {periodLabel}.",
            Amount = netAmount,
            ExpenseDate = payment.PaymentDate,
            PaymentMethodId = payment.PaymentMethodId,
            ReferenceNumber = payment.TransactionReference,
            RecordedByUserId = _currentUser.UserId
        };

        _db.Expenses.Add(expense);

        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogWarning(ex, "Concurrent duplicate salary payment for trainer {TrainerId} {Period}.",
                trainer.Id, periodLabel);
            throw new ConflictAppException(
                $"A salary payment for {trainer.FullName} for {periodLabel} has already been recorded.");
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Create, Entity, payment.Id,
            newValues: new
            {
                payment.TrainerId,
                payment.PeriodYear,
                payment.PeriodMonth,
                payment.BaseAmount,
                payment.Bonus,
                payment.Deduction,
                payment.NetAmount,
                ExpenseNumber = expense.ExpenseNumber
            },
            description: $"Recorded salary of {payment.NetAmount:0.00} for {trainer.FullName} ({periodLabel}); " +
                         $"expense {expense.ExpenseNumber} written to '{SalariesCategoryName}'.",
            ct: ct).ConfigureAwait(false);

        var result = await _db.SalaryPayments.AsNoTracking()
            .Where(s => s.Id == payment.Id)
            .Select(ToDto)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return result ?? throw new NotFoundAppException(Entity, payment.Id);
    }

    public async Task SoftDeleteAsync(int id, CancellationToken ct = default)
    {
        var payment = await _db.SalaryPayments.FirstOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false)
                      ?? throw new NotFoundAppException(Entity, id);

        _db.SalaryPayments.Remove(payment); // The context turns this into a soft delete.
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.SoftDelete, Entity, payment.Id,
            oldValues: new { payment.TrainerId, payment.PeriodYear, payment.PeriodMonth, payment.NetAmount },
            description: $"Salary payment #{payment.Id} for {payment.PeriodMonth:00}/{payment.PeriodYear} " +
                         "moved to the recycle bin. The matching expense record was kept.",
            ct: ct).ConfigureAwait(false);
    }

    // ----------------------------------------------------------------- helpers

    private async Task<int> GetOrCreateSalariesCategoryIdAsync(CancellationToken ct)
    {
        var categoryId = await _db.ExpenseCategories.AsNoTracking()
            .Where(c => c.Name == SalariesCategoryName)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (categoryId is > 0) return categoryId.Value;

        var category = new ExpenseCategory
        {
            Name = SalariesCategoryName,
            Description = "Trainer and staff salaries.",
            IsActive = true
        };

        _db.ExpenseCategories.Add(category);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Created the '{Category}' expense category for salary payments.", SalariesCategoryName);

        return category.Id;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string? Trim(string? value) =>
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
