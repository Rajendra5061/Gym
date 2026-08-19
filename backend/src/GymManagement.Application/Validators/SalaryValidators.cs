using FluentValidation;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;

namespace GymManagement.Application.Validators;

public sealed class SaveSalaryPaymentValidator : AbstractValidator<SaveSalaryPaymentDto>
{
    public SaveSalaryPaymentValidator()
    {
        RuleFor(x => x.TrainerId)
            .GreaterThan(0).WithMessage("Select the trainer the salary is for.");

        RuleFor(x => x.PeriodYear)
            .InclusiveBetween(2000, 2100).WithMessage("Year must be between 2000 and 2100.");

        RuleFor(x => x.PeriodMonth)
            .InclusiveBetween(1, 12).WithMessage("Month must be between 1 (January) and 12 (December).");

        RuleFor(x => x.BaseAmount)
            .InclusiveBetween(0m, ValidationRules.MaxMoneyAmount)
                .WithMessage("Base amount is unrealistically high. Please check the amount.");

        RuleFor(x => x.Bonus)
            .GreaterThanOrEqualTo(0m).WithMessage("Bonus cannot be negative.")
            .LessThanOrEqualTo(ValidationRules.MaxMoneyAmount)
                .WithMessage("Bonus is unrealistically high. Please check the amount.");

        RuleFor(x => x.Deduction)
            .GreaterThanOrEqualTo(0m).WithMessage("Deduction cannot be negative.")
            .LessThanOrEqualTo(ValidationRules.MaxMoneyAmount)
                .WithMessage("Deduction is unrealistically high. Please check the amount.");

        RuleFor(x => x.PaymentDate)
            .NotEmpty().WithMessage("A payment date is required.");

        RuleFor(x => x.PaymentMethodId)
            .GreaterThan(0).WithMessage("The selected payment method is not valid.")
            .When(x => x.PaymentMethodId.HasValue);

        RuleFor(x => x.TransactionReference)
            .MaximumLength(ValidationRules.ReferenceMaxLength)
                .WithMessage($"Reference must not be longer than {ValidationRules.ReferenceMaxLength} characters.");

        RuleFor(x => x.Notes)
            .MaximumLength(ValidationRules.NotesMaxLength)
                .WithMessage($"Notes must not be longer than {ValidationRules.NotesMaxLength} characters.");
    }
}
