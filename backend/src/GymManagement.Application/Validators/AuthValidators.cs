using FluentValidation;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;

namespace GymManagement.Application.Validators;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequestDto>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.UserNameOrEmail)
            .NotEmpty().WithMessage("Enter your user name or email address.")
            .MaximumLength(ValidationRules.NameMaxLength)
                .WithMessage($"User name or email must not be longer than {ValidationRules.NameMaxLength} characters.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Enter your password.");
    }
}

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequestDto>
{
    public ForgotPasswordRequestValidator()
    {
        RuleFor(x => x.UserNameOrEmail)
            .NotEmpty().WithMessage("Enter the user name or email address of the account.")
            .MaximumLength(ValidationRules.NameMaxLength)
                .WithMessage($"User name or email must not be longer than {ValidationRules.NameMaxLength} characters.");
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequestDto>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Enter your current password.");

        RuleFor(x => x.NewPassword)
            .StrongPassword();

        RuleFor(x => x.NewPassword)
            .NotEqual(x => x.CurrentPassword)
                .WithMessage("New password must be different from your current password.")
            .When(x => !string.IsNullOrEmpty(x.NewPassword) && !string.IsNullOrEmpty(x.CurrentPassword));

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("Re-enter the new password to confirm it.")
            .Equal(x => x.NewPassword)
                .WithMessage("Confirm password does not match the new password.");
    }
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequestDto>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.UserNameOrEmail)
            .NotEmpty().WithMessage("Enter the user name or email address of the account.")
            .MaximumLength(ValidationRules.NameMaxLength)
                .WithMessage($"User name or email must not be longer than {ValidationRules.NameMaxLength} characters.");

        RuleFor(x => x.ResetToken)
            .NotEmpty().WithMessage("Enter the reset code that was issued for this account.")
            .MaximumLength(512).WithMessage("The reset code is not valid.");

        RuleFor(x => x.NewPassword)
            .StrongPassword();

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("Re-enter the new password to confirm it.")
            .Equal(x => x.NewPassword)
                .WithMessage("Confirm password does not match the new password.");
    }
}
