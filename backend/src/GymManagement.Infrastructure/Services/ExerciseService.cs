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
/// The exercise library that workout plans and logged sessions draw from. Names are unique among
/// non-deleted rows and an exercise that a live plan still references may not be removed.
/// </summary>
public sealed class ExerciseService : IExerciseService
{
    private const string EntityName = "Exercise";

    private readonly GymDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditService _audit;
    private readonly ILogger<ExerciseService> _logger;

    public ExerciseService(
        GymDbContext db,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IAuditService audit,
        ILogger<ExerciseService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private static readonly Expression<Func<Exercise, ExerciseDto>> ToDto = e => new ExerciseDto
    {
        Id = e.Id,
        Name = e.Name,
        Category = e.Category,
        PrimaryMuscleGroup = e.PrimaryMuscleGroup,
        SecondaryMuscleGroups = e.SecondaryMuscleGroups,
        Difficulty = e.Difficulty,
        Equipment = e.Equipment,
        Instructions = e.Instructions,
        VideoUrl = e.VideoUrl,
        ImagePath = e.ImagePath,
        CaloriesPerMinute = e.CaloriesPerMinute,
        IsActive = e.IsActive
    };

    public async Task<PagedResult<ExerciseDto>> GetPagedAsync(ExerciseQueryDto query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var q = _db.Exercises.AsNoTracking();
        if (query.IncludeDeleted) q = q.IgnoreQueryFilters();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(e => EF.Functions.Like(e.Name, $"%{term}%")
                             || (e.Equipment != null && EF.Functions.Like(e.Equipment, $"%{term}%"))
                             || (e.Instructions != null && EF.Functions.Like(e.Instructions, $"%{term}%")));
        }

        if (query.Category.HasValue) q = q.Where(e => e.Category == query.Category!.Value);
        if (query.MuscleGroup.HasValue) q = q.Where(e => e.PrimaryMuscleGroup == query.MuscleGroup!.Value);
        if (query.Difficulty.HasValue) q = q.Where(e => e.Difficulty == query.Difficulty!.Value);
        if (query.IsActive.HasValue) q = q.Where(e => e.IsActive == query.IsActive!.Value);

        var total = await q.CountAsync(ct).ConfigureAwait(false);
        if (total == 0)
            return new PagedResult<ExerciseDto>(Array.Empty<ExerciseDto>(), 0, query.PageNumber, query.PageSize);

        var items = await ApplySort(q, query)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(ToDto)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedResult<ExerciseDto>(items, total, query.PageNumber, query.PageSize);
    }

    public async Task<ExerciseDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var dto = await _db.Exercises.AsNoTracking().IgnoreQueryFilters()
            .Where(e => e.Id == id)
            .Select(ToDto)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return dto ?? throw new NotFoundAppException(EntityName, id);
    }

    public async Task<ExerciseDto> CreateAsync(ExerciseDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var name = ValidateAndNormalize(dto);
        await EnsureNameIsFreeAsync(name, null, ct).ConfigureAwait(false);

        var entity = new Exercise();
        Apply(dto, entity, name);

        _db.Exercises.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Create, EntityName, entity.Id,
            newValues: Snapshot(entity),
            description: $"Created exercise '{entity.Name}'.",
            ct: ct).ConfigureAwait(false);

        return await GetByIdAsync(entity.Id, ct).ConfigureAwait(false);
    }

    public async Task<ExerciseDto> UpdateAsync(ExerciseDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (dto.Id <= 0)
            throw new ValidationAppException(nameof(dto.Id), "A valid exercise id is required for an update.");

        var name = ValidateAndNormalize(dto);

        var entity = await _db.Exercises
            .FirstOrDefaultAsync(e => e.Id == dto.Id, ct)
            .ConfigureAwait(false);

        if (entity is null) throw new NotFoundAppException(EntityName, dto.Id);

        await EnsureNameIsFreeAsync(name, entity.Id, ct).ConfigureAwait(false);

        var before = Snapshot(entity);
        Apply(dto, entity, name);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Update, EntityName, entity.Id,
            oldValues: before, newValues: Snapshot(entity),
            description: $"Updated exercise '{entity.Name}'.",
            ct: ct).ConfigureAwait(false);

        return await GetByIdAsync(entity.Id, ct).ConfigureAwait(false);
    }

    public async Task SoftDeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await _db.Exercises
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            .ConfigureAwait(false);

        if (entity is null) throw new NotFoundAppException(EntityName, id);

        // A plan that is still live would be left with a dangling movement.
        var referencingPlans = await _db.WorkoutPlanExercises.AsNoTracking()
            .Where(pe => pe.ExerciseId == id && pe.WorkoutPlan != null && !pe.WorkoutPlan.IsDeleted)
            .Select(pe => pe.WorkoutPlan!.Name)
            .Distinct()
            .Take(5)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (referencingPlans.Count > 0)
            throw new BusinessRuleAppException(
                $"'{entity.Name}' is used by the workout plan(s) {string.Join(", ", referencingPlans)} " +
                "and cannot be deleted. Remove it from those plans first.");

        // The context turns a Remove on a soft-deletable entity into a soft delete.
        _db.Exercises.Remove(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.SoftDelete, EntityName, entity.Id,
            oldValues: Snapshot(entity),
            description: $"Soft deleted exercise '{entity.Name}'.",
            ct: ct).ConfigureAwait(false);
    }

    public async Task RestoreAsync(int id, CancellationToken ct = default)
    {
        var entity = await _db.Exercises.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            .ConfigureAwait(false);

        if (entity is null) throw new NotFoundAppException(EntityName, id);

        if (!entity.IsDeleted)
        {
            _logger.LogDebug("Exercise {ExerciseId} is not deleted; restore is a no-op.", id);
            return;
        }

        // The unique filtered index only covers live rows, so the name may have been taken meanwhile.
        await EnsureNameIsFreeAsync(entity.Name, entity.Id, ct).ConfigureAwait(false);

        entity.IsDeleted = false;
        entity.DeletedAt = null;
        entity.DeletedBy = null;
        entity.UpdatedAt = _clock.UtcNow;
        entity.UpdatedBy = _currentUser.UserId;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Restore, EntityName, entity.Id,
            newValues: Snapshot(entity),
            description: $"Restored exercise '{entity.Name}'.",
            ct: ct).ConfigureAwait(false);
    }

    public async Task<List<LookupDto>> GetLookupAsync(CancellationToken ct = default)
    {
        var rows = await _db.Exercises.AsNoTracking()
            .Where(e => e.IsActive)
            .OrderBy(e => e.Name)
            .Select(e => new { e.Id, e.Name, e.PrimaryMuscleGroup })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // ToString() on the enum cannot be translated, so the text is built client side.
        return rows.Select(e => new LookupDto
        {
            Id = e.Id,
            Name = e.Name,
            Extra = e.PrimaryMuscleGroup.ToString(),
            IsActive = true
        }).ToList();
    }

    // ----------------------------------------------------------------- helpers

    private static IQueryable<Exercise> ApplySort(IQueryable<Exercise> q, ExerciseQueryDto query)
    {
        var desc = query.SortDescending;

        return (query.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "category" => desc
                ? q.OrderByDescending(e => e.Category).ThenBy(e => e.Name)
                : q.OrderBy(e => e.Category).ThenBy(e => e.Name),
            "difficulty" => desc
                ? q.OrderByDescending(e => e.Difficulty).ThenBy(e => e.Name)
                : q.OrderBy(e => e.Difficulty).ThenBy(e => e.Name),
            _ => desc
                ? q.OrderByDescending(e => e.Name).ThenByDescending(e => e.Id)
                : q.OrderBy(e => e.Name).ThenBy(e => e.Id)
        };
    }

    private static string ValidateAndNormalize(ExerciseDto dto)
    {
        var errors = new Dictionary<string, string[]>();

        var name = dto.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            errors[nameof(dto.Name)] = new[] { "The exercise name is required." };
        else if (name.Length > 160)
            errors[nameof(dto.Name)] = new[] { "The exercise name cannot exceed 160 characters." };

        if (dto.CaloriesPerMinute is < 0m)
            errors[nameof(dto.CaloriesPerMinute)] = new[] { "Calories per minute cannot be negative." };

        if (errors.Count > 0) throw new ValidationAppException(errors);

        return name;
    }

    private async Task EnsureNameIsFreeAsync(string name, int? exceptId, CancellationToken ct)
    {
        var clash = await _db.Exercises.AsNoTracking()
            .AnyAsync(e => e.Name == name && (exceptId == null || e.Id != exceptId), ct)
            .ConfigureAwait(false);

        if (clash)
            throw new ConflictAppException($"An exercise named '{name}' already exists.");
    }

    private static void Apply(ExerciseDto dto, Exercise entity, string name)
    {
        entity.Name = name;
        entity.Category = dto.Category;
        entity.PrimaryMuscleGroup = dto.PrimaryMuscleGroup;
        entity.SecondaryMuscleGroups = Trim(dto.SecondaryMuscleGroups);
        entity.Difficulty = dto.Difficulty;
        entity.Equipment = Trim(dto.Equipment);
        entity.Instructions = Trim(dto.Instructions);
        entity.VideoUrl = Trim(dto.VideoUrl);
        entity.ImagePath = Trim(dto.ImagePath);
        entity.CaloriesPerMinute = dto.CaloriesPerMinute;
        entity.IsActive = dto.IsActive;
    }

    private static object Snapshot(Exercise e) => new
    {
        e.Name,
        Category = e.Category.ToString(),
        PrimaryMuscleGroup = e.PrimaryMuscleGroup.ToString(),
        Difficulty = e.Difficulty.ToString(),
        e.Equipment,
        e.CaloriesPerMinute,
        e.IsActive,
        e.IsDeleted
    };

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
