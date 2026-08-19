using GymManagement.Application.Common;
using GymManagement.Domain.Enums;

namespace GymManagement.Application.DTOs;

public class ExerciseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ExerciseCategory Category { get; set; }
    public string CategoryText => Category.ToString();
    public MuscleGroup PrimaryMuscleGroup { get; set; }
    public string PrimaryMuscleGroupText => PrimaryMuscleGroup.ToString();
    public string? SecondaryMuscleGroups { get; set; }
    public DifficultyLevel Difficulty { get; set; }
    public string DifficultyText => Difficulty.ToString();
    public string? Equipment { get; set; }
    public string? Instructions { get; set; }
    public string? VideoUrl { get; set; }
    public string? ImagePath { get; set; }
    public decimal? CaloriesPerMinute { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ExerciseQueryDto : PagedRequest
{
    public ExerciseCategory? Category { get; set; }
    public MuscleGroup? MuscleGroup { get; set; }
    public DifficultyLevel? Difficulty { get; set; }
    public bool? IsActive { get; set; }
    public bool IncludeDeleted { get; set; }
}

public class WorkoutPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Goal { get; set; }
    public DifficultyLevel Difficulty { get; set; }
    public string DifficultyText => Difficulty.ToString();
    public int DurationWeeks { get; set; } = 4;
    public int SessionsPerWeek { get; set; } = 3;
    public bool IsActive { get; set; } = true;
    public int ExerciseCount { get; set; }
    public int AssignedMemberCount { get; set; }
    public List<WorkoutPlanExerciseDto> Exercises { get; set; } = new();
}

public class WorkoutPlanExerciseDto
{
    public int Id { get; set; }
    public int ExerciseId { get; set; }
    public string ExerciseName { get; set; } = string.Empty;
    public string? MuscleGroupText { get; set; }
    public int? DayOfWeek { get; set; }
    public int DisplayOrder { get; set; }
    public int Sets { get; set; } = 3;
    public int Repetitions { get; set; } = 10;
    public decimal? TargetWeightKg { get; set; }
    public int? RestSeconds { get; set; }
    public int? DurationMinutes { get; set; }
    public string? Notes { get; set; }
}

public class WorkoutPlanQueryDto : PagedRequest
{
    public DifficultyLevel? Difficulty { get; set; }
    public bool? IsActive { get; set; }
    public bool IncludeDeleted { get; set; }
}

public class AssignWorkoutPlanDto
{
    public int MemberId { get; set; }
    public int WorkoutPlanId { get; set; }
    public int? TrainerId { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime? EndDate { get; set; }
    public string? Notes { get; set; }
}

public class MemberWorkoutPlanDto
{
    public int Id { get; set; }
    public int MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public int WorkoutPlanId { get; set; }
    public string WorkoutPlanName { get; set; } = string.Empty;
    public int? TrainerId { get; set; }
    public string? TrainerName { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; }
    public string? Notes { get; set; }
    public List<WorkoutPlanExerciseDto> Exercises { get; set; } = new();
}

public class WorkoutSessionDto
{
    public int Id { get; set; }
    public int MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public string? MemberCode { get; set; }
    public int? TrainerId { get; set; }
    public string? TrainerName { get; set; }
    public int? WorkoutPlanId { get; set; }
    public string? WorkoutPlanName { get; set; }
    public DateTime SessionDate { get; set; } = DateTime.Today;
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public int? DurationMinutes { get; set; }
    public decimal? CaloriesBurned { get; set; }
    public string? Notes { get; set; }
    public int ExerciseCount { get; set; }
    public decimal TotalVolumeKg { get; set; }
    public List<WorkoutSessionExerciseDto> Exercises { get; set; } = new();
}

public class WorkoutSessionExerciseDto
{
    public int Id { get; set; }
    public int ExerciseId { get; set; }
    public string ExerciseName { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public int Sets { get; set; }
    public int Repetitions { get; set; }
    public decimal? WeightKg { get; set; }
    public int? DurationMinutes { get; set; }
    public decimal? CaloriesBurned { get; set; }
    public string? Notes { get; set; }
}

public class SaveWorkoutSessionDto
{
    public int Id { get; set; }
    public int MemberId { get; set; }
    public int? TrainerId { get; set; }
    public int? WorkoutPlanId { get; set; }
    public DateTime SessionDate { get; set; } = DateTime.Today;
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public int? DurationMinutes { get; set; }
    public decimal? CaloriesBurned { get; set; }
    public string? Notes { get; set; }
    public List<WorkoutSessionExerciseDto> Exercises { get; set; } = new();
}

public class WorkoutSessionQueryDto : PagedRequest
{
    public int? MemberId { get; set; }
    public int? TrainerId { get; set; }
    public int? WorkoutPlanId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public bool IncludeDeleted { get; set; }
}

/// <summary>One point on a member progress chart.</summary>
public class ProgressPointDto
{
    public DateTime Date { get; set; }
    public decimal Value { get; set; }
    public string? Label { get; set; }
}

public class MemberProgressDto
{
    public int MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public List<ProgressPointDto> Weight { get; set; } = new();
    public List<ProgressPointDto> BodyFat { get; set; } = new();
    public List<ProgressPointDto> Bmi { get; set; } = new();
    public List<ProgressPointDto> SessionVolume { get; set; } = new();
    public List<ProgressPointDto> CaloriesBurned { get; set; } = new();
}
