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
/// Diet plans written for members. On update the meal lines are replaced wholesale, mirroring how
/// workout plan exercises are saved. List queries by a trainer-linked account that cannot see the
/// whole trainer roster are always scoped to that trainer's own plans.
/// </summary>
public sealed class DietPlanService : IDietPlanService
{
    private const string Entity = "DietPlan";

    private readonly GymDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;
    private readonly ILogger<DietPlanService> _logger;

    /// <summary>Optional so tests construct the service without messaging; null sends nothing.</summary>
    private readonly IMemberNotifier? _memberNotifier;

    public DietPlanService(
        GymDbContext db,
        IDateTimeProvider clock,
        ICurrentUserService currentUser,
        IAuditService audit,
        ILogger<DietPlanService> logger,
        IMemberNotifier? memberNotifier = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _memberNotifier = memberNotifier;
    }

    private static readonly Expression<Func<DietPlan, DietPlanDto>> ToDto = p => new DietPlanDto
    {
        Id = p.Id,
        MemberId = p.MemberId,
        MemberName = p.Member != null ? p.Member.FullName : string.Empty,
        TrainerId = p.TrainerId,
        TrainerName = p.Trainer != null ? p.Trainer.FullName : null,
        Title = p.Title,
        Goal = p.Goal,
        Notes = p.Notes,
        StartDate = p.StartDate,
        EndDate = p.EndDate,
        Status = p.Status,
        CreatedAt = p.CreatedAt,
        Meals = p.Meals
            .OrderBy(m => m.DisplayOrder)
            .Select(m => new DietPlanMealDto
            {
                Id = m.Id,
                MealType = m.MealType,
                Title = m.Title,
                Description = m.Description,
                Calories = m.Calories,
                ProteinGrams = m.ProteinGrams,
                CarbsGrams = m.CarbsGrams,
                FatGrams = m.FatGrams,
                DisplayOrder = m.DisplayOrder
            })
            .ToList()
    };

    public async Task<PagedResult<DietPlanDto>> GetPagedAsync(DietPlanQueryDto query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // A trainer-linked caller without the trainers.view permission only ever sees their own
        // plans, whatever filter the client sent. The claim comes from the validated JWT.
        var ownTrainerId = _currentUser.TrainerId;
        if (ownTrainerId is > 0 && !_currentUser.HasPermission(Permissions.TrainersView))
            query.TrainerId = ownTrainerId;

        var q = _db.DietPlans.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(p =>
                EF.Functions.Like(p.Title, $"%{term}%")
                || (p.Goal != null && EF.Functions.Like(p.Goal, $"%{term}%"))
                || (p.Member != null && (EF.Functions.Like(p.Member.FullName, $"%{term}%")
                                         || EF.Functions.Like(p.Member.MemberCode, $"%{term}%"))));
        }

        if (query.MemberId is > 0) q = q.Where(p => p.MemberId == query.MemberId!.Value);
        if (query.TrainerId is > 0) q = q.Where(p => p.TrainerId == query.TrainerId!.Value);
        if (query.Status.HasValue) q = q.Where(p => p.Status == query.Status!.Value);

        var total = await q.CountAsync(ct).ConfigureAwait(false);
        if (total == 0)
            return new PagedResult<DietPlanDto>(Array.Empty<DietPlanDto>(), 0, query.PageNumber, query.PageSize);

        var desc = query.SortDescending;
        q = (query.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "title" => desc
                ? q.OrderByDescending(p => p.Title).ThenByDescending(p => p.Id)
                : q.OrderBy(p => p.Title).ThenBy(p => p.Id),
            "membername" => desc
                ? q.OrderByDescending(p => p.Member!.FullName).ThenByDescending(p => p.Id)
                : q.OrderBy(p => p.Member!.FullName).ThenBy(p => p.Id),
            "status" => desc
                ? q.OrderByDescending(p => p.Status).ThenByDescending(p => p.StartDate)
                : q.OrderBy(p => p.Status).ThenByDescending(p => p.StartDate),
            "startdate" => desc
                ? q.OrderByDescending(p => p.StartDate).ThenByDescending(p => p.Id)
                : q.OrderBy(p => p.StartDate).ThenBy(p => p.Id),
            _ => q.OrderByDescending(p => p.StartDate).ThenByDescending(p => p.Id)
        };

        var items = await q
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(ToDto)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedResult<DietPlanDto>(items, total, query.PageNumber, query.PageSize);
    }

    public async Task<DietPlanDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var dto = await _db.DietPlans.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(ToDto)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return dto ?? throw new NotFoundAppException(Entity, id);
    }

    public async Task<List<DietPlanDto>> GetMemberPlansAsync(int memberId, CancellationToken ct = default)
    {
        var memberExists = await _db.Members.AsNoTracking()
            .AnyAsync(m => m.Id == memberId, ct)
            .ConfigureAwait(false);

        if (!memberExists) throw new NotFoundAppException("Member", memberId);

        return await _db.DietPlans.AsNoTracking()
            .Where(p => p.MemberId == memberId)
            .OrderByDescending(p => p.Status == DietPlanStatus.Active)
            .ThenByDescending(p => p.StartDate)
            .ThenByDescending(p => p.Id)
            .Select(ToDto)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<DietPlanDto> SaveAsync(SaveDietPlanDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var title = Trim(dto.Title)
                    ?? throw new ValidationAppException(nameof(dto.Title), "A diet plan title is required.");

        var meals = dto.Meals ?? new List<SaveDietPlanMealDto>();
        if (meals.Count == 0)
            throw new ValidationAppException(nameof(dto.Meals), "Add at least one meal to the diet plan.");

        if (dto.EndDate.HasValue && dto.EndDate.Value.Date <= dto.StartDate.Date)
            throw new ValidationAppException(nameof(dto.EndDate), "The end date must be after the start date.");

        var memberName = await _db.Members.AsNoTracking()
            .Where(m => m.Id == dto.MemberId)
            .Select(m => m.FullName)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (memberName is null) throw new NotFoundAppException("Member", dto.MemberId);

        if (dto.TrainerId is > 0)
            await EnsureTrainerIsActiveAsync(dto.TrainerId.Value, ct).ConfigureAwait(false);

        var isNew = dto.Id <= 0;
        DietPlan plan;
        object? before = null;

        await using var tx = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        if (isNew)
        {
            plan = new DietPlan();
            Apply(dto, plan, title);
            _db.DietPlans.Add(plan);
        }
        else
        {
            plan = await _db.DietPlans.FirstOrDefaultAsync(p => p.Id == dto.Id, ct).ConfigureAwait(false)
                   ?? throw new NotFoundAppException(Entity, dto.Id);
            before = Snapshot(plan);
            Apply(dto, plan, title);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        // The meal lines are replaced wholesale: the incoming order is the new DisplayOrder.
        if (!isNew)
        {
            var existing = await _db.DietPlanMeals
                .Where(m => m.DietPlanId == plan.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (existing.Count > 0) _db.DietPlanMeals.RemoveRange(existing);
        }

        var order = 1;
        foreach (var meal in meals)
        {
            var mealTitle = Trim(meal.Title)
                            ?? throw new ValidationAppException("Meals", "Every meal needs a title.");

            _db.DietPlanMeals.Add(new DietPlanMeal
            {
                DietPlanId = plan.Id,
                MealType = meal.MealType,
                Title = mealTitle,
                Description = Trim(meal.Description),
                Calories = meal.Calories,
                ProteinGrams = meal.ProteinGrams,
                CarbsGrams = meal.CarbsGrams,
                FatGrams = meal.FatGrams,
                DisplayOrder = order++
            });
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(isNew ? AuditActions.Create : AuditActions.Update, Entity, plan.Id,
            oldValues: before, newValues: Snapshot(plan),
            description: $"{(isNew ? "Created" : "Updated")} diet plan '{plan.Title}' for {memberName} " +
                         $"with {meals.Count} meal(s).",
            ct: ct).ConfigureAwait(false);

        // Tell the member their plan is ready. After the commit, and never allowed to turn a
        // saved plan into a failed request — the notifier promises not to throw and is wrapped anyway.
        if (_memberNotifier is not null)
        {
            try
            {
                await _memberNotifier.NotifyDietPlanAsync(plan.Id, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Diet plan notification for plan {PlanId} failed.", plan.Id);
            }
        }

        return await GetByIdAsync(plan.Id, ct).ConfigureAwait(false);
    }

    public async Task SoftDeleteAsync(int id, CancellationToken ct = default)
    {
        var plan = await _db.DietPlans.FirstOrDefaultAsync(p => p.Id == id, ct).ConfigureAwait(false);
        if (plan is null) throw new NotFoundAppException(Entity, id);

        _db.DietPlans.Remove(plan); // The context turns this into a soft delete.
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.SoftDelete, Entity, plan.Id,
            oldValues: Snapshot(plan),
            description: $"Soft deleted diet plan '{plan.Title}'.",
            ct: ct).ConfigureAwait(false);
    }

    // ----------------------------------------------------------------- helpers

    private void Apply(SaveDietPlanDto dto, DietPlan plan, string title)
    {
        plan.MemberId = dto.MemberId;
        plan.TrainerId = dto.TrainerId is > 0 ? dto.TrainerId : null;
        plan.Title = title;
        plan.Goal = Trim(dto.Goal);
        plan.Notes = Trim(dto.Notes);
        plan.StartDate = dto.StartDate.Date;
        plan.EndDate = dto.EndDate?.Date;
        plan.Status = Enum.IsDefined(dto.Status) ? dto.Status : DietPlanStatus.Active;
    }

    private async Task EnsureTrainerIsActiveAsync(int trainerId, CancellationToken ct)
    {
        var trainer = await _db.Trainers.AsNoTracking()
            .Where(t => t.Id == trainerId)
            .Select(t => new { t.FullName, t.Status })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (trainer is null) throw new NotFoundAppException("Trainer", trainerId);

        if (trainer.Status != TrainerStatus.Active)
            throw new BusinessRuleAppException(
                $"Trainer {trainer.FullName} is {trainer.Status} and cannot be assigned.");
    }

    private static object Snapshot(DietPlan p) => new
    {
        p.MemberId,
        p.TrainerId,
        p.Title,
        p.Goal,
        p.StartDate,
        p.EndDate,
        Status = p.Status.ToString(),
        p.IsDeleted
    };

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
