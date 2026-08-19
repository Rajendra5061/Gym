using FluentValidation;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;

namespace GymManagement.Application.Validators;

public sealed class CheckInRequestValidator : AbstractValidator<CheckInRequestDto>
{
    public CheckInRequestValidator()
    {
        RuleFor(x => x)
            .Must(HasMemberIdentifier)
            .WithName("Member")
            .WithMessage("Select a member or scan / enter a member code before checking in.");

        RuleFor(x => x.MemberId)
            .GreaterThan(0).WithMessage("The selected member is not valid.")
            .When(x => x.MemberId.HasValue);

        RuleFor(x => x.MemberCode)
            .MaximumLength(64).WithMessage("Member code must not be longer than 64 characters.");

        RuleFor(x => x.CheckInMethod)
            .NotEmpty().WithMessage("Check-in method is required.")
            .Must(method => ValidationRules.IsCheckInMethod(method))
                .WithMessage($"Check-in method must be one of {ValidationRules.CheckInMethodList}.");

        RuleFor(x => x.Notes)
            .MaximumLength(ValidationRules.NotesMaxLength)
                .WithMessage($"Notes must not be longer than {ValidationRules.NotesMaxLength} characters.");
    }

    private static bool HasMemberIdentifier(CheckInRequestDto dto) =>
        dto.MemberId is > 0 || !string.IsNullOrWhiteSpace(dto.MemberCode);
}

public sealed class CheckOutRequestValidator : AbstractValidator<CheckOutRequestDto>
{
    public CheckOutRequestValidator()
    {
        RuleFor(x => x)
            .Must(dto => dto.AttendanceId is > 0 || dto.MemberId is > 0)
            .WithName("Check-out")
            .WithMessage("Select the check-in record or the member you want to check out.");

        RuleFor(x => x.Notes)
            .MaximumLength(ValidationRules.NotesMaxLength)
                .WithMessage($"Notes must not be longer than {ValidationRules.NotesMaxLength} characters.");
    }
}
