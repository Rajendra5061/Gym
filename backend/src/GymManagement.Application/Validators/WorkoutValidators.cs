using FluentValidation;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;

namespace GymManagement.Application.Validators;

public sealed class ExerciseValidator : AbstractValidator<ExerciseDto>
{
    public ExerciseValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Exercise name is required.")
            .Length(2, ValidationRules.NameMaxLength)
                .WithMessage($"Exercise name must be 2 to {ValidationRules.NameMaxLength} characters long.");

        RuleFor(x => x.Category)
            .IsInEnum().WithMessage("Select a valid exercise category.");

        RuleFor(x => x.PrimaryMuscleGroup)
            .IsInEnum().WithMessage("Select a valid primary muscle group.");

        RuleFor(x => x.Difficulty)
            .IsInEnum().WithMessage("Select a valid difficulty level.");

        RuleFor(x => x.CaloriesPerMinute)
            .InclusiveBetween(0m, 100m).WithMessage("Calories per minute must be between 0 and 100.");

        RuleFor(x => x.Instructions)
            .MaximumLength(4000).WithMessage("Instructions must not be longer than 4000 characters.");

        RuleFor(x => x.Equipment)
            .MaximumLength(200).WithMessage("Equipment must not be longer than 200 characters.");
    }
}

public sealed class WorkoutPlanExerciseValidator : AbstractValidator<WorkoutPlanExerciseDto>
{
    public WorkoutPlanExerciseValidator()
    {
        RuleFor(x => x.ExerciseId)
            .GreaterThan(0).WithMessage("Select an exercise for every line in the plan.");

        RuleFor(x => x.Sets)
            .InclusiveBetween(1, 20).WithMessage("Sets must be between 1 and 20.");

        RuleFor(x => x.Repetitions)
            .InclusiveBetween(1, 200).WithMessage("Repetitions must be between 1 and 200.");

        RuleFor(x => x.DayOfWeek)
            .InclusiveBetween(1, 7).WithMessage("Day of week must be between 1 (Monday) and 7 (Sunday).")
            .When(x => x.DayOfWeek.HasValue);

        RuleFor(x => x.TargetWeightKg)
            .InclusiveBetween(0m, 1000m).WithMessage("Target weight must be between 0 and 1000 kg.");

        RuleFor(x => x.RestSeconds)
            .InclusiveBetween(0, 900).WithMessage("Rest must be between 0 and 900 seconds.");

        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(0, 600).WithMessage("Duration must be between 0 and 600 minutes.");

        RuleFor(x => x.Notes)
            .MaximumLength(ValidationRules.NotesMaxLength)
                .WithMessage($"Notes must not be longer than {ValidationRules.NotesMaxLength} characters.");
    }
}

public sealed class WorkoutPlanValidator : AbstractValidator<WorkoutPlanDto>
{
    public WorkoutPlanValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Workout plan name is required.")
            .Length(2, ValidationRules.NameMaxLength)
                .WithMessage($"Workout plan name must be 2 to {ValidationRules.NameMaxLength} characters long.");

        RuleFor(x => x.Difficulty)
            .IsInEnum().WithMessage("Select a valid difficulty level.");

        RuleFor(x => x.DurationWeeks)
            .InclusiveBetween(1, 104).WithMessage("Duration must be between 1 and 104 weeks.");

        RuleFor(x => x.SessionsPerWeek)
            .InclusiveBetween(1, 14).WithMessage("Sessions per week must be between 1 and 14.");

        RuleFor(x => x.Description)
            .MaximumLength(ValidationRules.NotesMaxLength)
                .WithMessage($"Description must not be longer than {ValidationRules.NotesMaxLength} characters.");

        RuleForEach(x => x.Exercises).SetValidator(new WorkoutPlanExerciseValidator());
    }
}

public sealed class AssignWorkoutPlanValidator : AbstractValidator<AssignWorkoutPlanDto>
{
    public AssignWorkoutPlanValidator()
    {
        RuleFor(x => x.MemberId)
            .GreaterThan(0).WithMessage("Select the member the plan is for.");

        RuleFor(x => x.WorkoutPlanId)
            .GreaterThan(0).WithMessage("Select a workout plan to assign.");

        RuleFor(x => x.TrainerId)
            .GreaterThan(0).WithMessage("The selected trainer is not valid.")
            .When(x => x.TrainerId.HasValue);

        RuleFor(x => x.EndDate)
            .Must((dto, endDate) => endDate!.Value.Date > dto.StartDate.Date)
                .WithMessage("End date must be after the start date.")
            .When(x => x.EndDate.HasValue);

        RuleFor(x => x.Notes)
            .MaximumLength(ValidationRules.NotesMaxLength)
                .WithMessage($"Notes must not be longer than {ValidationRules.NotesMaxLength} characters.");
    }
}

public sealed class WorkoutSessionExerciseValidator : AbstractValidator<WorkoutSessionExerciseDto>
{
    public WorkoutSessionExerciseValidator()
    {
        RuleFor(x => x.ExerciseId)
            .GreaterThan(0).WithMessage("Select an exercise for every line in the session.");

        RuleFor(x => x.Sets)
            .InclusiveBetween(0, 50).WithMessage("Sets must be between 0 and 50.");

        RuleFor(x => x.Repetitions)
            .InclusiveBetween(0, 500).WithMessage("Repetitions must be between 0 and 500.");

        RuleFor(x => x.WeightKg)
            .InclusiveBetween(0m, 1000m).WithMessage("Weight must be between 0 and 1000 kg.");

        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(0, 600).WithMessage("Duration must be between 0 and 600 minutes.");

        RuleFor(x => x.CaloriesBurned)
            .InclusiveBetween(0m, 10000m).WithMessage("Calories burned must be between 0 and 10,000.");

        RuleFor(x => x.Notes)
            .MaximumLength(ValidationRules.NotesMaxLength)
                .WithMessage($"Notes must not be longer than {ValidationRules.NotesMaxLength} characters.");
    }
}

public sealed class SaveWorkoutSessionValidator : AbstractValidator<SaveWorkoutSessionDto>
{
    public SaveWorkoutSessionValidator()
    {
        RuleFor(x => x.MemberId)
            .GreaterThan(0).WithMessage("Select the member who trained.");

        RuleFor(x => x.TrainerId)
            .GreaterThan(0).WithMessage("The selected trainer is not valid.")
            .When(x => x.TrainerId.HasValue);

        RuleFor(x => x.WorkoutPlanId)
            .GreaterThan(0).WithMessage("The selected workout plan is not valid.")
            .When(x => x.WorkoutPlanId.HasValue);

        RuleFor(x => x.SessionDate)
            .Must(date => date.Date <= DateTime.Today)
            .WithMessage("Session date cannot be in the future.");

        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(0, 600).WithMessage("Duration must be between 0 and 600 minutes.");

        RuleFor(x => x.EndTime)
            .Must((dto, endTime) => endTime!.Value > dto.StartTime!.Value)
                .WithMessage("End time must be after the start time.")
            .When(x => x.StartTime.HasValue && x.EndTime.HasValue);

        RuleFor(x => x.CaloriesBurned)
            .InclusiveBetween(0m, 10000m).WithMessage("Calories burned must be between 0 and 10,000.");

        RuleFor(x => x.Notes)
            .MaximumLength(ValidationRules.NotesMaxLength)
                .WithMessage($"Notes must not be longer than {ValidationRules.NotesMaxLength} characters.");

        RuleForEach(x => x.Exercises).SetValidator(new WorkoutSessionExerciseValidator());
    }
}
