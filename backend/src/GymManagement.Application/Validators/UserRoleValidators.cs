using FluentValidation;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;

namespace GymManagement.Application.Validators;

public sealed class CreateUserValidator : AbstractValidator<CreateUserDto>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.UserName)
            .NotEmpty().WithMessage("User name is required.")
            .Length(3, 64).WithMessage("User name must be 3 to 64 characters long.")
            .Matches(ValidationRules.UserNameRegex())
                .WithMessage("User name may only contain letters, digits, dots, underscores and hyphens.");

        RuleFor(x => x.FullName).PersonName();

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email address is required.")
            .MaximumLength(ValidationRules.EmailMaxLength)
                .WithMessage($"Email address must not be longer than {ValidationRules.EmailMaxLength} characters.")
            .EmailAddressOrEmpty();

        RuleFor(x => x.Phone).PhoneNumber();

        RuleFor(x => x.Password)
            .StrongPassword()
            .When(x => !string.IsNullOrEmpty(x.Password));

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Select a valid account status.");

        RuleFor(x => x.RoleIds)
            .NotEmpty().WithMessage("Select at least one role for this user.");

        RuleForEach(x => x.RoleIds)
            .GreaterThan(0).WithMessage("One of the selected roles is not valid.");

        RuleFor(x => x.MemberId)
            .GreaterThan(0).WithMessage("The linked member is not valid.")
            .When(x => x.MemberId.HasValue);

        RuleFor(x => x.TrainerId)
            .GreaterThan(0).WithMessage("The linked trainer is not valid.")
            .When(x => x.TrainerId.HasValue);
    }
}

public sealed class UpdateUserValidator : AbstractValidator<UpdateUserDto>
{
    public UpdateUserValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0).WithMessage("Select the user you want to update.");

        RuleFor(x => x.FullName).PersonName();

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email address is required.")
            .MaximumLength(ValidationRules.EmailMaxLength)
                .WithMessage($"Email address must not be longer than {ValidationRules.EmailMaxLength} characters.")
            .EmailAddressOrEmpty();

        RuleFor(x => x.Phone).PhoneNumber();

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Select a valid account status.");

        RuleFor(x => x.RoleIds)
            .NotEmpty().WithMessage("Select at least one role for this user.");

        RuleForEach(x => x.RoleIds)
            .GreaterThan(0).WithMessage("One of the selected roles is not valid.");
    }
}

public sealed class SaveRoleValidator : AbstractValidator<SaveRoleDto>
{
    public SaveRoleValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThanOrEqualTo(0).WithMessage("The selected role is not valid.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Role name is required.")
            .Length(2, 64).WithMessage("Role name must be 2 to 64 characters long.");

        RuleFor(x => x.Description)
            .MaximumLength(512).WithMessage("Description must not be longer than 512 characters.");
    }
}
