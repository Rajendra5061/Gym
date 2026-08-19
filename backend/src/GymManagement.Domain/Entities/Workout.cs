using GymManagement.Domain.Common;
using GymManagement.Domain.Enums;

namespace GymManagement.Domain.Entities;

public class Exercise : SoftDeletableEntity
{
    public string Name { get; set; } = string.Empty;
    public ExerciseCategory Category { get; set; } = ExerciseCategory.Strength;
    public MuscleGroup PrimaryMuscleGroup { get; set; } = MuscleGroup.FullBody;
    public string? SecondaryMuscleGroups { get; set; }
    public DifficultyLevel Difficulty { get; set; } = DifficultyLevel.Beginner;
    public string? Equipment { get; set; }
    public string? Instructions { get; set; }
    public string? VideoUrl { get; set; }
    public string? ImagePath { get; set; }
    /// <summary>Approximate calories burned per minute, used to estimate session calories.</summary>
    public decimal? CaloriesPerMinute { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<WorkoutPlanExercise> PlanExercises { get; set; } = new List<WorkoutPlanExercise>();
}

/// <summary>A reusable workout template that can be assigned to many members.</summary>
public class WorkoutPlan : SoftDeletableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Goal { get; set; }
    public DifficultyLevel Difficulty { get; set; } = DifficultyLevel.Beginner;
    public int DurationWeeks { get; set; } = 4;
    public int SessionsPerWeek { get; set; } = 3;
    public bool IsActive { get; set; } = true;

    public ICollection<WorkoutPlanExercise> Exercises { get; set; } = new List<WorkoutPlanExercise>();
    public ICollection<MemberWorkoutPlan> Assignments { get; set; } = new List<MemberWorkoutPlan>();
}

public class WorkoutPlanExercise : BaseEntity
{
    public int WorkoutPlanId { get; set; }
    public WorkoutPlan? WorkoutPlan { get; set; }
    public int ExerciseId { get; set; }
    public Exercise? Exercise { get; set; }

    /// <summary>1 = Monday … 7 = Sunday. Null means "any day of the week".</summary>
    public int? DayOfWeek { get; set; }
    public int DisplayOrder { get; set; }
    public int Sets { get; set; } = 3;
    public int Repetitions { get; set; } = 10;
    public decimal? TargetWeightKg { get; set; }
    public int? RestSeconds { get; set; }
    public int? DurationMinutes { get; set; }
    public string? Notes { get; set; }
}

public class MemberWorkoutPlan : AuditableEntity
{
    public int MemberId { get; set; }
    public Member? Member { get; set; }
    public int WorkoutPlanId { get; set; }
    public WorkoutPlan? WorkoutPlan { get; set; }
    public int? TrainerId { get; set; }
    public Trainer? Trainer { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}

public class WorkoutSession : SoftDeletableEntity
{
    public int MemberId { get; set; }
    public Member? Member { get; set; }
    public int? TrainerId { get; set; }
    public Trainer? Trainer { get; set; }
    public int? WorkoutPlanId { get; set; }
    public WorkoutPlan? WorkoutPlan { get; set; }

    public DateTime SessionDate { get; set; } = DateTime.Today;
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public int? DurationMinutes { get; set; }
    public decimal? CaloriesBurned { get; set; }
    public string? Notes { get; set; }

    public ICollection<WorkoutSessionExercise> Exercises { get; set; } = new List<WorkoutSessionExercise>();
}

public class WorkoutSessionExercise : BaseEntity
{
    public int WorkoutSessionId { get; set; }
    public WorkoutSession? WorkoutSession { get; set; }
    public int ExerciseId { get; set; }
    public Exercise? Exercise { get; set; }

    public int DisplayOrder { get; set; }
    public int Sets { get; set; }
    public int Repetitions { get; set; }
    public decimal? WeightKg { get; set; }
    public int? DurationMinutes { get; set; }
    public decimal? CaloriesBurned { get; set; }
    public string? Notes { get; set; }
}
