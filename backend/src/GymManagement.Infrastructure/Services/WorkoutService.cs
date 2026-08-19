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
/// Workout plan templates, the assignment of a template to a member (one active plan per member)
/// and the sessions a member actually performed.
/// </summary>
public sealed class WorkoutService : IWorkoutService
{
    private const string PlanEntity = "WorkoutPlan";
    private const string AssignmentEntity = "MemberWorkoutPlan";
    private const string SessionEntity = "WorkoutSession";

    private const int MinDurationWeeks = 1;
    private const int MaxDurationWeeks = 104;
    private const int MinSessionsPerWeek = 1;
    private const int MaxSessionsPerWeek = 14;

    /// <summary>How far in the past an assignment may be back-dated.</summary>
    private const int MaxBackdateDays = 7;

    private readonly GymDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;
    private readonly ILogger<WorkoutService> _logger;

    public WorkoutService(
        GymDbContext db,
        IDateTimeProvider clock,
        ICurrentUserService currentUser,
        IAuditService audit,
        ILogger<WorkoutService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ------------------------------------------------------------ projections

    /// <summary>List projection: the exercise rows are deliberately left out.</summary>
    private static readonly Expression<Func<WorkoutPlan, WorkoutPlanDto>> PlanToDto = p => new WorkoutPlanDto
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        Goal = p.Goal,
        Difficulty = p.Difficulty,
        DurationWeeks = p.DurationWeeks,
        SessionsPerWeek = p.SessionsPerWeek,
        IsActive = p.IsActive,
        ExerciseCount = p.Exercises.Count,
        AssignedMemberCount = p.Assignments.Count(a => a.IsActive)
    };

    private static readonly Expression<Func<MemberWorkoutPlan, MemberWorkoutPlanDto>> AssignmentToDto =
        a => new MemberWorkoutPlanDto
        {
            Id = a.Id,
            MemberId = a.MemberId,
            MemberName = a.Member != null ? a.Member.FullName : string.Empty,
            WorkoutPlanId = a.WorkoutPlanId,
            WorkoutPlanName = a.WorkoutPlan != null ? a.WorkoutPlan.Name : string.Empty,
            TrainerId = a.TrainerId,
            TrainerName = a.Trainer != null ? a.Trainer.FullName : null,
            StartDate = a.StartDate,
            EndDate = a.EndDate,
            IsActive = a.IsActive,
            Notes = a.Notes
        };

    /// <summary>List projection: the exercise lines are loaded only for the detail view.</summary>
    private static readonly Expression<Func<WorkoutSession, WorkoutSessionDto>> SessionToDto = s => new WorkoutSessionDto
    {
        Id = s.Id,
        MemberId = s.MemberId,
        MemberName = s.Member != null ? s.Member.FullName : string.Empty,
        MemberCode = s.Member != null ? s.Member.MemberCode : null,
        TrainerId = s.TrainerId,
        TrainerName = s.Trainer != null ? s.Trainer.FullName : null,
        WorkoutPlanId = s.WorkoutPlanId,
        WorkoutPlanName = s.WorkoutPlan != null ? s.WorkoutPlan.Name : null,
        SessionDate = s.SessionDate,
        StartTime = s.StartTime,
        EndTime = s.EndTime,
        DurationMinutes = s.DurationMinutes,
        CaloriesBurned = s.CaloriesBurned,
        Notes = s.Notes,
        ExerciseCount = s.Exercises.Count,
        TotalVolumeKg = s.Exercises.Sum(x => x.Sets * x.Repetitions * (x.WeightKg ?? 0m))
    };

    // ---------------------------------------------------------- plan templates

    public async Task<PagedResult<WorkoutPlanDto>> GetPlansAsync(WorkoutPlanQueryDto query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var q = _db.WorkoutPlans.AsNoTracking();
        if (query.IncludeDeleted) q = q.IgnoreQueryFilters();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(p => EF.Functions.Like(p.Name, $"%{term}%")
                             || (p.Description != null && EF.Functions.Like(p.Description, $"%{term}%"))
                             || (p.Goal != null && EF.Functions.Like(p.Goal, $"%{term}%")));
        }

        if (query.Difficulty.HasValue) q = q.Where(p => p.Difficulty == query.Difficulty!.Value);
        if (query.IsActive.HasValue) q = q.Where(p => p.IsActive == query.IsActive!.Value);

        var total = await q.CountAsync(ct).ConfigureAwait(false);
        if (total == 0)
            return new PagedResult<WorkoutPlanDto>(Array.Empty<WorkoutPlanDto>(), 0, query.PageNumber, query.PageSize);

        var desc = query.SortDescending;
        q = (query.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "difficulty" => desc
                ? q.OrderByDescending(p => p.Difficulty).ThenBy(p => p.Name)
                : q.OrderBy(p => p.Difficulty).ThenBy(p => p.Name),
            "durationweeks" => desc
                ? q.OrderByDescending(p => p.DurationWeeks).ThenBy(p => p.Name)
                : q.OrderBy(p => p.DurationWeeks).ThenBy(p => p.Name),
            "sessionsperweek" => desc
                ? q.OrderByDescending(p => p.SessionsPerWeek).ThenBy(p => p.Name)
                : q.OrderBy(p => p.SessionsPerWeek).ThenBy(p => p.Name),
            _ => desc
                ? q.OrderByDescending(p => p.Name).ThenByDescending(p => p.Id)
                : q.OrderBy(p => p.Name).ThenBy(p => p.Id)
        };

        var items = await q
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(PlanToDto)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedResult<WorkoutPlanDto>(items, total, query.PageNumber, query.PageSize);
    }

    public async Task<WorkoutPlanDto> GetPlanByIdAsync(int id, CancellationToken ct = default)
    {
        var dto = await _db.WorkoutPlans.AsNoTracking().IgnoreQueryFilters()
            .Where(p => p.Id == id)
            .Select(PlanToDto)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (dto is null) throw new NotFoundAppException(PlanEntity, id);

        dto.Exercises = await LoadPlanExercisesAsync(id, ct).ConfigureAwait(false);
        return dto;
    }

    public async Task<WorkoutPlanDto> SavePlanAsync(WorkoutPlanDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var name = ValidatePlan(dto);
        var lines = dto.Exercises ?? new List<WorkoutPlanExerciseDto>();
        await EnsureExercisesExistAsync(lines.Select(l => l.ExerciseId), ct).ConfigureAwait(false);
        await EnsurePlanNameIsFreeAsync(name, dto.Id > 0 ? dto.Id : null, ct).ConfigureAwait(false);

        var isNew = dto.Id <= 0;
        WorkoutPlan plan;
        object? before = null;

        await using var tx = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        if (isNew)
        {
            plan = new WorkoutPlan();
            ApplyPlan(dto, plan, name);
            _db.WorkoutPlans.Add(plan);
        }
        else
        {
            plan = await _db.WorkoutPlans.FirstOrDefaultAsync(p => p.Id == dto.Id, ct).ConfigureAwait(false)
                   ?? throw new NotFoundAppException(PlanEntity, dto.Id);
            before = PlanSnapshot(plan);
            ApplyPlan(dto, plan, name);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        // The child rows are replaced wholesale: the incoming order is the new DisplayOrder.
        if (!isNew)
        {
            var existing = await _db.WorkoutPlanExercises
                .Where(pe => pe.WorkoutPlanId == plan.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (existing.Count > 0) _db.WorkoutPlanExercises.RemoveRange(existing);
        }

        var order = 1;
        foreach (var line in lines)
        {
            _db.WorkoutPlanExercises.Add(new WorkoutPlanExercise
            {
                WorkoutPlanId = plan.Id,
                ExerciseId = line.ExerciseId,
                DayOfWeek = line.DayOfWeek,
                DisplayOrder = order++,
                Sets = line.Sets,
                Repetitions = line.Repetitions,
                TargetWeightKg = line.TargetWeightKg,
                RestSeconds = line.RestSeconds,
                DurationMinutes = line.DurationMinutes,
                Notes = Trim(line.Notes)
            });
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(isNew ? AuditActions.Create : AuditActions.Update, PlanEntity, plan.Id,
            oldValues: before, newValues: PlanSnapshot(plan),
            description: $"{(isNew ? "Created" : "Updated")} workout plan '{plan.Name}' with {lines.Count} exercise(s).",
            ct: ct).ConfigureAwait(false);

        return await GetPlanByIdAsync(plan.Id, ct).ConfigureAwait(false);
    }

    public async Task SoftDeletePlanAsync(int id, CancellationToken ct = default)
    {
        var plan = await _db.WorkoutPlans.FirstOrDefaultAsync(p => p.Id == id, ct).ConfigureAwait(false);
        if (plan is null) throw new NotFoundAppException(PlanEntity, id);

        var activeAssignments = await _db.MemberWorkoutPlans.AsNoTracking()
            .CountAsync(a => a.WorkoutPlanId == id && a.IsActive, ct)
            .ConfigureAwait(false);

        if (activeAssignments > 0)
            throw new BusinessRuleAppException(
                $"'{plan.Name}' is still assigned to {activeAssignments} member(s). " +
                "Deactivate those assignments before deleting the plan.");

        _db.WorkoutPlans.Remove(plan);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.SoftDelete, PlanEntity, plan.Id,
            oldValues: PlanSnapshot(plan),
            description: $"Soft deleted workout plan '{plan.Name}'.",
            ct: ct).ConfigureAwait(false);
    }

    public async Task RestorePlanAsync(int id, CancellationToken ct = default)
    {
        var plan = await _db.WorkoutPlans.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            .ConfigureAwait(false);

        if (plan is null) throw new NotFoundAppException(PlanEntity, id);

        if (!plan.IsDeleted)
        {
            _logger.LogDebug("Workout plan {PlanId} is not deleted; restore is a no-op.", id);
            return;
        }

        await EnsurePlanNameIsFreeAsync(plan.Name, plan.Id, ct).ConfigureAwait(false);

        plan.IsDeleted = false;
        plan.DeletedAt = null;
        plan.DeletedBy = null;
        plan.UpdatedAt = _clock.UtcNow;
        plan.UpdatedBy = _currentUser.UserId;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Restore, PlanEntity, plan.Id,
            newValues: PlanSnapshot(plan),
            description: $"Restored workout plan '{plan.Name}'.",
            ct: ct).ConfigureAwait(false);
    }

    public async Task<List<LookupDto>> GetPlanLookupAsync(CancellationToken ct = default)
    {
        var rows = await _db.WorkoutPlans.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.Difficulty, p.DurationWeeks })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.Select(p => new LookupDto
        {
            Id = p.Id,
            Name = p.Name,
            Extra = $"{p.Difficulty} · {p.DurationWeeks} week(s)",
            IsActive = true
        }).ToList();
    }

    // ------------------------------------------------------------- assignments

    public async Task<MemberWorkoutPlanDto> AssignPlanAsync(AssignWorkoutPlanDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var today = _clock.Today.Date;
        var errors = new Dictionary<string, string[]>();

        var startDate = dto.StartDate.Date;
        if (startDate < today.AddDays(-MaxBackdateDays))
            errors[nameof(dto.StartDate)] = new[]
                { $"The start date cannot be more than {MaxBackdateDays} days in the past." };

        DateTime? endDate = dto.EndDate?.Date;
        if (endDate.HasValue && endDate.Value <= startDate)
            errors[nameof(dto.EndDate)] = new[] { "The end date must be after the start date." };

        if (errors.Count > 0) throw new ValidationAppException(errors);

        var memberName = await _db.Members.AsNoTracking()
            .Where(m => m.Id == dto.MemberId)
            .Select(m => m.FullName)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (memberName is null) throw new NotFoundAppException("Member", dto.MemberId);

        var planName = await _db.WorkoutPlans.AsNoTracking()
            .Where(p => p.Id == dto.WorkoutPlanId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (planName is null) throw new NotFoundAppException(PlanEntity, dto.WorkoutPlanId);

        if (dto.TrainerId is > 0)
            await EnsureTrainerIsActiveAsync(dto.TrainerId.Value, ct).ConfigureAwait(false);

        await using var tx = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        // A member follows exactly one plan at a time: everything else is stood down.
        var superseded = await _db.MemberWorkoutPlans
            .Where(a => a.MemberId == dto.MemberId && a.IsActive)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var old in superseded)
        {
            old.IsActive = false;
            if (old.EndDate is null || old.EndDate > today) old.EndDate = today;
        }

        var assignment = new MemberWorkoutPlan
        {
            MemberId = dto.MemberId,
            WorkoutPlanId = dto.WorkoutPlanId,
            TrainerId = dto.TrainerId is > 0 ? dto.TrainerId : null,
            StartDate = startDate,
            EndDate = endDate,
            IsActive = true,
            Notes = Trim(dto.Notes)
        };

        _db.MemberWorkoutPlans.Add(assignment);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Create, AssignmentEntity, assignment.Id,
            newValues: new
            {
                assignment.MemberId,
                assignment.WorkoutPlanId,
                assignment.TrainerId,
                assignment.StartDate,
                assignment.EndDate
            },
            description: $"Assigned plan '{planName}' to {memberName}" +
                         (superseded.Count > 0 ? $"; deactivated {superseded.Count} earlier assignment(s)." : "."),
            ct: ct).ConfigureAwait(false);

        return await GetAssignmentDtoAsync(assignment.Id, withExercises: true, ct).ConfigureAwait(false);
    }

    public async Task<List<MemberWorkoutPlanDto>> GetMemberPlansAsync(int memberId, bool onlyActive = false,
        CancellationToken ct = default)
    {
        var q = _db.MemberWorkoutPlans.AsNoTracking().Where(a => a.MemberId == memberId);
        if (onlyActive) q = q.Where(a => a.IsActive);

        var assignments = await q
            .OrderByDescending(a => a.StartDate)
            .ThenByDescending(a => a.Id)
            .Select(AssignmentToDto)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // The exercise lines belong to the plan template, so each distinct plan is loaded once and
        // shared across the assignments that reference it.
        foreach (var planId in assignments.Select(a => a.WorkoutPlanId).Distinct())
        {
            var exercises = await LoadPlanExercisesAsync(planId, ct).ConfigureAwait(false);
            foreach (var assignment in assignments.Where(a => a.WorkoutPlanId == planId))
                assignment.Exercises = exercises;
        }

        return assignments;
    }

    public async Task DeactivateMemberPlanAsync(int memberWorkoutPlanId, CancellationToken ct = default)
    {
        var assignment = await _db.MemberWorkoutPlans
            .FirstOrDefaultAsync(a => a.Id == memberWorkoutPlanId, ct)
            .ConfigureAwait(false);

        if (assignment is null) throw new NotFoundAppException(AssignmentEntity, memberWorkoutPlanId);

        if (!assignment.IsActive)
        {
            _logger.LogDebug("Member workout plan {AssignmentId} is already inactive.", memberWorkoutPlanId);
            return;
        }

        var today = _clock.Today.Date;
        assignment.IsActive = false;
        if (assignment.EndDate is null || assignment.EndDate > today) assignment.EndDate = today;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Deactivate, AssignmentEntity, assignment.Id,
            newValues: new { assignment.IsActive, assignment.EndDate },
            description: $"Deactivated workout plan assignment #{assignment.Id}.",
            ct: ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- sessions

    public async Task<PagedResult<WorkoutSessionDto>> GetSessionsAsync(WorkoutSessionQueryDto query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var q = _db.WorkoutSessions.AsNoTracking();
        if (query.IncludeDeleted) q = q.IgnoreQueryFilters();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(s =>
                (s.Member != null && (EF.Functions.Like(s.Member.FullName, $"%{term}%")
                                      || EF.Functions.Like(s.Member.MemberCode, $"%{term}%")))
                || (s.Trainer != null && (EF.Functions.Like(s.Trainer.FullName, $"%{term}%")
                                          || EF.Functions.Like(s.Trainer.TrainerCode, $"%{term}%"))));
        }

        if (query.MemberId is > 0) q = q.Where(s => s.MemberId == query.MemberId!.Value);
        if (query.TrainerId is > 0) q = q.Where(s => s.TrainerId == query.TrainerId!.Value);
        if (query.WorkoutPlanId is > 0) q = q.Where(s => s.WorkoutPlanId == query.WorkoutPlanId!.Value);

        if (query.FromDate.HasValue)
        {
            var from = query.FromDate.Value.Date;
            q = q.Where(s => s.SessionDate >= from);
        }

        if (query.ToDate.HasValue)
        {
            var to = query.ToDate.Value.Date;
            q = q.Where(s => s.SessionDate <= to);
        }

        var total = await q.CountAsync(ct).ConfigureAwait(false);
        if (total == 0)
            return new PagedResult<WorkoutSessionDto>(Array.Empty<WorkoutSessionDto>(), 0, query.PageNumber, query.PageSize);

        var desc = query.SortDescending;
        q = (query.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "membername" => desc
                ? q.OrderByDescending(s => s.Member!.FullName).ThenByDescending(s => s.SessionDate)
                : q.OrderBy(s => s.Member!.FullName).ThenByDescending(s => s.SessionDate),
            "durationminutes" => desc
                ? q.OrderByDescending(s => s.DurationMinutes).ThenByDescending(s => s.Id)
                : q.OrderBy(s => s.DurationMinutes).ThenBy(s => s.Id),
            "caloriesburned" => desc
                ? q.OrderByDescending(s => s.CaloriesBurned).ThenByDescending(s => s.Id)
                : q.OrderBy(s => s.CaloriesBurned).ThenBy(s => s.Id),
            "sessiondate" => desc
                ? q.OrderByDescending(s => s.SessionDate).ThenByDescending(s => s.Id)
                : q.OrderBy(s => s.SessionDate).ThenBy(s => s.Id),
            // Most recent workout first.
            _ => q.OrderByDescending(s => s.SessionDate).ThenByDescending(s => s.Id)
        };

        var items = await q
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(SessionToDto)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedResult<WorkoutSessionDto>(items, total, query.PageNumber, query.PageSize);
    }

    public async Task<WorkoutSessionDto> GetSessionByIdAsync(int id, CancellationToken ct = default)
    {
        var dto = await _db.WorkoutSessions.AsNoTracking().IgnoreQueryFilters()
            .Where(s => s.Id == id)
            .Select(SessionToDto)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (dto is null) throw new NotFoundAppException(SessionEntity, id);

        dto.Exercises = await _db.WorkoutSessionExercises.AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.WorkoutSessionId == id)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new WorkoutSessionExerciseDto
            {
                Id = x.Id,
                ExerciseId = x.ExerciseId,
                ExerciseName = x.Exercise != null ? x.Exercise.Name : string.Empty,
                DisplayOrder = x.DisplayOrder,
                Sets = x.Sets,
                Repetitions = x.Repetitions,
                WeightKg = x.WeightKg,
                DurationMinutes = x.DurationMinutes,
                CaloriesBurned = x.CaloriesBurned,
                Notes = x.Notes
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return dto;
    }

    public async Task<WorkoutSessionDto> SaveSessionAsync(SaveWorkoutSessionDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var today = _clock.Today.Date;
        var lines = dto.Exercises ?? new List<WorkoutSessionExerciseDto>();
        ValidateSession(dto, lines, today);

        var memberExists = await _db.Members.AsNoTracking()
            .AnyAsync(m => m.Id == dto.MemberId, ct)
            .ConfigureAwait(false);

        if (!memberExists) throw new NotFoundAppException("Member", dto.MemberId);

        if (dto.TrainerId is > 0)
            await EnsureTrainerIsActiveAsync(dto.TrainerId.Value, ct).ConfigureAwait(false);

        if (dto.WorkoutPlanId is > 0)
        {
            var planExists = await _db.WorkoutPlans.AsNoTracking()
                .AnyAsync(p => p.Id == dto.WorkoutPlanId!.Value, ct)
                .ConfigureAwait(false);

            if (!planExists) throw new NotFoundAppException(PlanEntity, dto.WorkoutPlanId!.Value);
        }

        var calorieRates = await LoadCalorieRatesAsync(lines.Select(l => l.ExerciseId), ct).ConfigureAwait(false);

        var duration = dto.DurationMinutes ?? DeriveDuration(dto.StartTime, dto.EndTime);
        var calories = dto.CaloriesBurned ?? EstimateCalories(lines, calorieRates);

        var isNew = dto.Id <= 0;
        WorkoutSession session;
        object? before = null;

        await using var tx = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        if (isNew)
        {
            session = new WorkoutSession();
        }
        else
        {
            session = await _db.WorkoutSessions.FirstOrDefaultAsync(s => s.Id == dto.Id, ct).ConfigureAwait(false)
                      ?? throw new NotFoundAppException(SessionEntity, dto.Id);
            before = SessionSnapshot(session);
        }

        session.MemberId = dto.MemberId;
        session.TrainerId = dto.TrainerId is > 0 ? dto.TrainerId : null;
        session.WorkoutPlanId = dto.WorkoutPlanId is > 0 ? dto.WorkoutPlanId : null;
        session.SessionDate = dto.SessionDate.Date;
        session.StartTime = dto.StartTime;
        session.EndTime = dto.EndTime;
        session.DurationMinutes = duration;
        session.CaloriesBurned = calories;
        session.Notes = Trim(dto.Notes);

        if (isNew) _db.WorkoutSessions.Add(session);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (!isNew)
        {
            var existing = await _db.WorkoutSessionExercises
                .Where(x => x.WorkoutSessionId == session.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (existing.Count > 0) _db.WorkoutSessionExercises.RemoveRange(existing);
        }

        var order = 1;
        foreach (var line in lines)
        {
            _db.WorkoutSessionExercises.Add(new WorkoutSessionExercise
            {
                WorkoutSessionId = session.Id,
                ExerciseId = line.ExerciseId,
                DisplayOrder = order++,
                Sets = line.Sets,
                Repetitions = line.Repetitions,
                WeightKg = line.WeightKg,
                DurationMinutes = line.DurationMinutes,
                CaloriesBurned = line.CaloriesBurned,
                Notes = Trim(line.Notes)
            });
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(isNew ? AuditActions.Create : AuditActions.Update, SessionEntity, session.Id,
            oldValues: before, newValues: SessionSnapshot(session),
            description: $"{(isNew ? "Logged" : "Updated")} workout session on " +
                         $"{session.SessionDate:dd MMM yyyy} with {lines.Count} exercise(s).",
            ct: ct).ConfigureAwait(false);

        return await GetSessionByIdAsync(session.Id, ct).ConfigureAwait(false);
    }

    public async Task SoftDeleteSessionAsync(int id, CancellationToken ct = default)
    {
        var session = await _db.WorkoutSessions.FirstOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false);
        if (session is null) throw new NotFoundAppException(SessionEntity, id);

        _db.WorkoutSessions.Remove(session);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.SoftDelete, SessionEntity, session.Id,
            oldValues: SessionSnapshot(session),
            description: $"Soft deleted workout session dated {session.SessionDate:dd MMM yyyy}.",
            ct: ct).ConfigureAwait(false);
    }

    public async Task RestoreSessionAsync(int id, CancellationToken ct = default)
    {
        var session = await _db.WorkoutSessions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            .ConfigureAwait(false);

        if (session is null) throw new NotFoundAppException(SessionEntity, id);

        if (!session.IsDeleted)
        {
            _logger.LogDebug("Workout session {SessionId} is not deleted; restore is a no-op.", id);
            return;
        }

        session.IsDeleted = false;
        session.DeletedAt = null;
        session.DeletedBy = null;
        session.UpdatedAt = _clock.UtcNow;
        session.UpdatedBy = _currentUser.UserId;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Restore, SessionEntity, session.Id,
            newValues: SessionSnapshot(session),
            description: $"Restored workout session dated {session.SessionDate:dd MMM yyyy}.",
            ct: ct).ConfigureAwait(false);
    }

    // ----------------------------------------------------------------- helpers

    private async Task<List<WorkoutPlanExerciseDto>> LoadPlanExercisesAsync(int planId, CancellationToken ct)
    {
        var rows = await _db.WorkoutPlanExercises.AsNoTracking().IgnoreQueryFilters()
            .Where(pe => pe.WorkoutPlanId == planId)
            .OrderBy(pe => pe.DayOfWeek)
            .ThenBy(pe => pe.DisplayOrder)
            .Select(pe => new
            {
                pe.Id,
                pe.ExerciseId,
                ExerciseName = pe.Exercise != null ? pe.Exercise.Name : string.Empty,
                MuscleGroup = pe.Exercise != null ? pe.Exercise.PrimaryMuscleGroup : (MuscleGroup?)null,
                pe.DayOfWeek,
                pe.DisplayOrder,
                pe.Sets,
                pe.Repetitions,
                pe.TargetWeightKg,
                pe.RestSeconds,
                pe.DurationMinutes,
                pe.Notes
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Enum.ToString() has no SQL translation, so the display text is composed here.
        return rows.Select(r => new WorkoutPlanExerciseDto
        {
            Id = r.Id,
            ExerciseId = r.ExerciseId,
            ExerciseName = r.ExerciseName,
            MuscleGroupText = r.MuscleGroup?.ToString(),
            DayOfWeek = r.DayOfWeek,
            DisplayOrder = r.DisplayOrder,
            Sets = r.Sets,
            Repetitions = r.Repetitions,
            TargetWeightKg = r.TargetWeightKg,
            RestSeconds = r.RestSeconds,
            DurationMinutes = r.DurationMinutes,
            Notes = r.Notes
        }).ToList();
    }

    private async Task<MemberWorkoutPlanDto> GetAssignmentDtoAsync(int id, bool withExercises, CancellationToken ct)
    {
        var dto = await _db.MemberWorkoutPlans.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(AssignmentToDto)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (dto is null) throw new NotFoundAppException(AssignmentEntity, id);

        if (withExercises)
            dto.Exercises = await LoadPlanExercisesAsync(dto.WorkoutPlanId, ct).ConfigureAwait(false);

        return dto;
    }

    private static string ValidatePlan(WorkoutPlanDto dto)
    {
        var errors = new Dictionary<string, string[]>();

        var name = dto.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            errors[nameof(dto.Name)] = new[] { "The plan name is required." };
        else if (name.Length > 160)
            errors[nameof(dto.Name)] = new[] { "The plan name cannot exceed 160 characters." };

        if (dto.DurationWeeks is < MinDurationWeeks or > MaxDurationWeeks)
            errors[nameof(dto.DurationWeeks)] = new[]
                { $"The duration must be between {MinDurationWeeks} and {MaxDurationWeeks} weeks." };

        if (dto.SessionsPerWeek is < MinSessionsPerWeek or > MaxSessionsPerWeek)
            errors[nameof(dto.SessionsPerWeek)] = new[]
                { $"Sessions per week must be between {MinSessionsPerWeek} and {MaxSessionsPerWeek}." };

        var lines = dto.Exercises ?? new List<WorkoutPlanExerciseDto>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            if (line.ExerciseId <= 0)
                errors[$"Exercises[{i}].ExerciseId"] = new[] { "An exercise must be selected." };

            if (line.DayOfWeek.HasValue && (line.DayOfWeek.Value < 1 || line.DayOfWeek.Value > 7))
                errors[$"Exercises[{i}].DayOfWeek"] = new[] { "The day of the week must be between 1 (Monday) and 7 (Sunday)." };

            if (line.Sets < 1)
                errors[$"Exercises[{i}].Sets"] = new[] { "At least one set is required." };

            if (line.Repetitions < 1)
                errors[$"Exercises[{i}].Repetitions"] = new[] { "At least one repetition is required." };
        }

        if (errors.Count > 0) throw new ValidationAppException(errors);

        return name;
    }

    private static void ValidateSession(SaveWorkoutSessionDto dto, List<WorkoutSessionExerciseDto> lines, DateTime today)
    {
        var errors = new Dictionary<string, string[]>();

        if (dto.MemberId <= 0)
            errors[nameof(dto.MemberId)] = new[] { "A member must be selected." };

        if (dto.SessionDate.Date > today)
            errors[nameof(dto.SessionDate)] = new[] { "A workout session cannot be logged for a future date." };

        if (dto.DurationMinutes is < 0)
            errors[nameof(dto.DurationMinutes)] = new[] { "The duration cannot be negative." };

        if (dto.CaloriesBurned is < 0m)
            errors[nameof(dto.CaloriesBurned)] = new[] { "Calories burned cannot be negative." };

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            if (line.ExerciseId <= 0)
                errors[$"Exercises[{i}].ExerciseId"] = new[] { "An exercise must be selected." };

            if (line.Sets < 0)
                errors[$"Exercises[{i}].Sets"] = new[] { "The number of sets cannot be negative." };

            if (line.Repetitions < 0)
                errors[$"Exercises[{i}].Repetitions"] = new[] { "The number of repetitions cannot be negative." };
        }

        if (errors.Count > 0) throw new ValidationAppException(errors);
    }

    private async Task EnsurePlanNameIsFreeAsync(string name, int? exceptId, CancellationToken ct)
    {
        var clash = await _db.WorkoutPlans.AsNoTracking()
            .AnyAsync(p => p.Name == name && (exceptId == null || p.Id != exceptId), ct)
            .ConfigureAwait(false);

        if (clash)
            throw new ConflictAppException($"A workout plan named '{name}' already exists.");
    }

    private async Task EnsureExercisesExistAsync(IEnumerable<int> exerciseIds, CancellationToken ct)
    {
        var ids = exerciseIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0) return;

        var found = await _db.Exercises.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .Select(e => e.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var missing = ids.Except(found).ToList();
        if (missing.Count > 0)
            throw new ValidationAppException("Exercises",
                $"Unknown or deleted exercise id(s): {string.Join(", ", missing)}.");
    }

    /// <summary>Validates the exercise ids and returns their calories-per-minute rates in one pass.</summary>
    private async Task<Dictionary<int, decimal?>> LoadCalorieRatesAsync(IEnumerable<int> exerciseIds, CancellationToken ct)
    {
        var ids = exerciseIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, decimal?>();

        var rows = await _db.Exercises.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.CaloriesPerMinute })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var missing = ids.Except(rows.Select(r => r.Id)).ToList();
        if (missing.Count > 0)
            throw new ValidationAppException("Exercises",
                $"Unknown or deleted exercise id(s): {string.Join(", ", missing)}.");

        return rows.ToDictionary(r => r.Id, r => r.CaloriesPerMinute);
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

    /// <summary>Whole minutes between the two clock times; a wrap past midnight counts as the next day.</summary>
    private static int? DeriveDuration(TimeSpan? start, TimeSpan? end)
    {
        if (start is null || end is null) return null;

        var span = end.Value - start.Value;
        if (span < TimeSpan.Zero) span += TimeSpan.FromDays(1);

        var minutes = (int)Math.Floor(span.TotalMinutes);
        return minutes < 0 ? 0 : minutes;
    }

    /// <summary>
    /// Sums <c>CaloriesPerMinute × minutes</c> over the lines that carry both values.
    /// Returns null when nothing can be estimated so the caller keeps an empty field.
    /// </summary>
    private static decimal? EstimateCalories(List<WorkoutSessionExerciseDto> lines, Dictionary<int, decimal?> rates)
    {
        decimal? total = null;

        foreach (var line in lines)
        {
            if (line.DurationMinutes is not > 0) continue;
            if (!rates.TryGetValue(line.ExerciseId, out var rate) || rate is not > 0m) continue;

            total = (total ?? 0m) + rate.Value * line.DurationMinutes.Value;
        }

        return total.HasValue ? Math.Round(total.Value, 2, MidpointRounding.AwayFromZero) : null;
    }

    private static void ApplyPlan(WorkoutPlanDto dto, WorkoutPlan plan, string name)
    {
        plan.Name = name;
        plan.Description = Trim(dto.Description);
        plan.Goal = Trim(dto.Goal);
        plan.Difficulty = dto.Difficulty;
        plan.DurationWeeks = dto.DurationWeeks;
        plan.SessionsPerWeek = dto.SessionsPerWeek;
        plan.IsActive = dto.IsActive;
    }

    private static object PlanSnapshot(WorkoutPlan p) => new
    {
        p.Name,
        p.Goal,
        Difficulty = p.Difficulty.ToString(),
        p.DurationWeeks,
        p.SessionsPerWeek,
        p.IsActive,
        p.IsDeleted
    };

    private static object SessionSnapshot(WorkoutSession s) => new
    {
        s.MemberId,
        s.TrainerId,
        s.WorkoutPlanId,
        s.SessionDate,
        s.StartTime,
        s.EndTime,
        s.DurationMinutes,
        s.CaloriesBurned,
        s.IsDeleted
    };

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
