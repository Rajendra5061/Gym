using FluentValidation;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;

namespace GymManagement.Application.Validators;

public sealed class CreateTrainerValidator : AbstractValidator<CreateTrainerDto>
{
    public CreateTrainerValidator() => TrainerWriteRules.Apply(this);
}

public sealed class UpdateTrainerValidator : AbstractValidator<UpdateTrainerDto>
{
    public UpdateTrainerValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0).WithMessage("Select the trainer you want to update.");

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Select a valid trainer status.");

        TrainerWriteRules.Apply(this);
    }
}

public sealed class AssignTrainerValidator : AbstractValidator<AssignTrainerDto>
{
    public AssignTrainerValidator()
    {
        RuleFor(x => x.TrainerId)
            .GreaterThan(0).WithMessage("Select the trainer to assign.");

        RuleFor(x => x.MemberIds)
            .NotEmpty().WithMessage("Select at least one member to assign to this trainer.");

        RuleForEach(x => x.MemberIds)
            .GreaterThan(0).WithMessage("One of the selected members is not valid.");

        RuleFor(x => x.MemberIds)
            .Must(ids => ids is null || ids.Count == ids.Distinct().Count())
            .WithMessage("The same member has been selected more than once.");
    }
}

/// <summary>Rules shared by the create and update trainer requests.</summary>
internal static class TrainerWriteRules
{
    internal static void Apply<T>(AbstractValidator<T> validator) where T : CreateTrainerDto
    {
        validator.RuleFor(x => x.FullName).PersonName();

        validator.RuleFor(x => x.Gender)
            .IsInEnum().WithMessage("Select a valid gender.");

        validator.RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Phone number is required.")
            .PhoneNumber();

        validator.RuleFor(x => x.Email)
            .MaximumLength(ValidationRules.EmailMaxLength)
                .WithMessage($"Email address must not be longer than {ValidationRules.EmailMaxLength} characters.")
            .EmailAddressOrEmpty();

        validator.RuleFor(x => x.Email)
            .NotEmpty()
                .WithMessage("Email address is required when a login account is created for the trainer.")
            .When(x => x.CreateUserAccount);

        validator.RuleFor(x => x.ExperienceYears)
            .InclusiveBetween(0, 70).WithMessage("Experience must be between 0 and 70 years.");

        validator.RuleFor(x => x.MonthlySalary)
            .GreaterThanOrEqualTo(0m).WithMessage("Monthly salary cannot be negative.")
            .LessThanOrEqualTo(ValidationRules.MaxMoneyAmount)
                .WithMessage("Monthly salary is unrealistically high. Please check the amount.");

        validator.RuleFor(x => x.CommissionPercent)
            .InclusiveBetween(0m, 100m).WithMessage("Commission must be between 0 and 100 percent.");

        validator.RuleFor(x => x.JoiningDate)
            .Must(date => date.Date <= DateTime.Today)
            .WithMessage("Joining date cannot be in the future.");

        validator.RuleFor(x => x.Specialization)
            .MaximumLength(200).WithMessage("Specialization must not be longer than 200 characters.");

        validator.RuleFor(x => x.Notes)
            .MaximumLength(ValidationRules.NotesMaxLength)
                .WithMessage($"Notes must not be longer than {ValidationRules.NotesMaxLength} characters.");
    }
}
