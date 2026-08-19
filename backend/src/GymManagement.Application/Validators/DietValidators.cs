using FluentValidation;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;

namespace GymManagement.Application.Validators;

public sealed class SaveDietPlanMealValidator : AbstractValidator<SaveDietPlanMealDto>
{
    private const int MacroMax = 10_000;

    public SaveDietPlanMealValidator()
    {
        RuleFor(x => x.MealType)
            .IsInEnum().WithMessage("Select a valid meal type for every meal.");

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Every meal needs a title.")
            .MaximumLength(200).WithMessage("Meal title must not be longer than 200 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(ValidationRules.NotesMaxLength)
                .WithMessage($"Meal description must not be longer than {ValidationRules.NotesMaxLength} characters.");

        RuleFor(x => x.Calories)
            .InclusiveBetween(0, MacroMax).WithMessage($"Calories must be between 0 and {MacroMax}.");

        RuleFor(x => x.ProteinGrams)
            .InclusiveBetween(0, MacroMax).WithMessage($"Protein must be between 0 and {MacroMax} grams.");

        RuleFor(x => x.CarbsGrams)
            .InclusiveBetween(0, MacroMax).WithMessage($"Carbs must be between 0 and {MacroMax} grams.");

        RuleFor(x => x.FatGrams)
            .InclusiveBetween(0, MacroMax).WithMessage($"Fat must be between 0 and {MacroMax} grams.");
    }
}

public sealed class SaveDietPlanValidator : AbstractValidator<SaveDietPlanDto>
{
    public SaveDietPlanValidator()
    {
        RuleFor(x => x.MemberId)
            .GreaterThan(0).WithMessage("Select the member the diet plan is for.");

        RuleFor(x => x.TrainerId)
            .GreaterThan(0).WithMessage("The selected trainer is not valid.")
            .When(x => x.TrainerId.HasValue);

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Diet plan title is required.")
            .Length(3, 200).WithMessage("Diet plan title must be 3 to 200 characters long.");

        RuleFor(x => x.Goal)
            .MaximumLength(256).WithMessage("Goal must not be longer than 256 characters.");

        RuleFor(x => x.Notes)
            .MaximumLength(ValidationRules.NotesMaxLength)
                .WithMessage($"Notes must not be longer than {ValidationRules.NotesMaxLength} characters.");

        RuleFor(x => x.StartDate)
            .NotEmpty().WithMessage("A start date is required.");

        RuleFor(x => x.EndDate)
            .Must((dto, endDate) => endDate!.Value.Date > dto.StartDate.Date)
                .WithMessage("End date must be after the start date.")
            .When(x => x.EndDate.HasValue);

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Select a valid diet plan status.");

        RuleFor(x => x.Meals)
            .NotEmpty().WithMessage("Add at least one meal to the diet plan.");

        RuleForEach(x => x.Meals).SetValidator(new SaveDietPlanMealValidator());
    }
}
