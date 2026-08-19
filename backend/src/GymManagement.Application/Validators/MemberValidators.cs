using FluentValidation;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;

namespace GymManagement.Application.Validators;

public sealed class CreateMemberValidator : AbstractValidator<CreateMemberDto>
{
    public CreateMemberValidator() => MemberWriteRules.Apply(this);
}

public sealed class UpdateMemberValidator : AbstractValidator<UpdateMemberDto>
{
    public UpdateMemberValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0).WithMessage("Select the member you want to update.");

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Select a valid member status.");

        MemberWriteRules.Apply(this);
    }
}

public sealed class MemberQueryValidator : AbstractValidator<MemberQueryDto>
{
    public MemberQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(1).WithMessage("Page number must be 1 or higher.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 200).WithMessage("Page size must be between 1 and 200.");

        RuleFor(x => x.ExpiringWithinDays)
            .InclusiveBetween(0, 365).WithMessage("Expiring within days must be between 0 and 365.");

        RuleFor(x => x.JoinedFrom)
            .Must((query, from) => !from.HasValue || !query.JoinedTo.HasValue || from.Value.Date <= query.JoinedTo.Value.Date)
            .WithMessage("Joined from date must be on or before the joined to date.");
    }
}

public sealed class MemberMeasurementValidator : AbstractValidator<MemberMeasurementDto>
{
    public MemberMeasurementValidator()
    {
        RuleFor(x => x.MemberId)
            .GreaterThan(0).WithMessage("Select the member these measurements belong to.");

        RuleFor(x => x.MeasuredOn)
            .Must(date => date.Date <= DateTime.Today)
            .WithMessage("Measured on date cannot be in the future.");

        RuleFor(x => x.WeightKg)
            .InclusiveBetween(10m, 500m).WithMessage("Weight must be between 10 and 500 kg.");

        RuleFor(x => x.HeightCm)
            .InclusiveBetween(50m, 300m).WithMessage("Height must be between 50 and 300 cm.");

        RuleFor(x => x.BodyFatPercent)
            .InclusiveBetween(1m, 70m).WithMessage("Body fat must be between 1 and 70 percent.");

        RuleFor(x => x.MuscleMassKg)
            .InclusiveBetween(1m, 300m).WithMessage("Muscle mass must be between 1 and 300 kg.");

        RuleFor(x => x.ChestCm)
            .InclusiveBetween(30m, 250m).WithMessage("Chest measurement must be between 30 and 250 cm.");

        RuleFor(x => x.WaistCm)
            .InclusiveBetween(30m, 250m).WithMessage("Waist measurement must be between 30 and 250 cm.");

        RuleFor(x => x.HipCm)
            .InclusiveBetween(30m, 250m).WithMessage("Hip measurement must be between 30 and 250 cm.");

        RuleFor(x => x.ArmCm)
            .InclusiveBetween(10m, 100m).WithMessage("Arm measurement must be between 10 and 100 cm.");

        RuleFor(x => x.ThighCm)
            .InclusiveBetween(10m, 150m).WithMessage("Thigh measurement must be between 10 and 150 cm.");

        RuleFor(x => x.Bmi)
            .InclusiveBetween(5m, 100m).WithMessage("BMI must be between 5 and 100.");

        RuleFor(x => x.RecordedByTrainerId)
            .GreaterThan(0).WithMessage("The selected trainer is not valid.")
            .When(x => x.RecordedByTrainerId.HasValue);

        RuleFor(x => x.Notes)
            .MaximumLength(ValidationRules.NotesMaxLength)
                .WithMessage($"Notes must not be longer than {ValidationRules.NotesMaxLength} characters.");

        RuleFor(x => x)
            .Must(HasAtLeastOneValue)
            .WithName("Measurements")
            .WithMessage("Enter at least one measurement before saving.");
    }

    private static bool HasAtLeastOneValue(MemberMeasurementDto dto) =>
        dto.WeightKg.HasValue || dto.HeightCm.HasValue || dto.BodyFatPercent.HasValue ||
        dto.MuscleMassKg.HasValue || dto.ChestCm.HasValue || dto.WaistCm.HasValue ||
        dto.HipCm.HasValue || dto.ArmCm.HasValue || dto.ThighCm.HasValue || dto.Bmi.HasValue;
}

/// <summary>Rules shared by the create and update member requests.</summary>
internal static class MemberWriteRules
{
    private static readonly DateTime EarliestJoiningDate = new(1990, 1, 1);

    internal static void Apply<T>(AbstractValidator<T> validator) where T : CreateMemberDto
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
                .WithMessage("Email address is required when a login account is created for the member.")
            .When(x => x.CreateUserAccount);

        validator.RuleFor(x => x.DateOfBirth)
            .Must(dob => dob!.Value.Date < DateTime.Today)
                .WithMessage("Date of birth must be a date in the past.")
            .Must(dob => ValidationRules.AgeInYears(dob!.Value) is >= 5 and <= 100)
                .WithMessage("Date of birth must give an age between 5 and 100 years.")
            .When(x => x.DateOfBirth.HasValue);

        validator.RuleFor(x => x.JoiningDate)
            .Must(date => date.Date <= DateTime.Today.AddDays(1))
                .WithMessage("Joining date cannot be more than one day in the future.")
            .GreaterThanOrEqualTo(EarliestJoiningDate)
                .WithMessage("Joining date must be on or after 1 January 1990.");

        validator.RuleFor(x => x.HeightCm)
            .InclusiveBetween(50m, 300m).WithMessage("Height must be between 50 and 300 cm.");

        validator.RuleFor(x => x.WeightKg)
            .InclusiveBetween(10m, 500m).WithMessage("Weight must be between 10 and 500 kg.");

        validator.RuleFor(x => x.BloodGroup)
            .Must(ValidationRules.IsBloodGroup)
                .WithMessage($"Blood group must be one of {string.Join(", ", ValidationRules.BloodGroups)}.")
            .When(x => !string.IsNullOrWhiteSpace(x.BloodGroup));

        validator.RuleFor(x => x.EmergencyContactPhone).PhoneNumber();

        validator.RuleFor(x => x.Notes)
            .MaximumLength(ValidationRules.NotesMaxLength)
                .WithMessage($"Notes must not be longer than {ValidationRules.NotesMaxLength} characters.");

        validator.RuleFor(x => x.AssignedTrainerId)
            .GreaterThan(0).WithMessage("The selected trainer is not valid.")
            .When(x => x.AssignedTrainerId.HasValue);
    }
}
