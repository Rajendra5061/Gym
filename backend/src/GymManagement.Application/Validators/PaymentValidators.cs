using FluentValidation;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;

namespace GymManagement.Application.Validators;

public sealed class PaymentMethodValidator : AbstractValidator<PaymentMethodDto>
{
    public PaymentMethodValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Payment method code is required.")
            .Length(2, 32).WithMessage("Payment method code must be 2 to 32 characters long.")
            .Matches(ValidationRules.UpperCaseCodeRegex())
                .WithMessage("Payment method code may only contain upper-case letters, digits and underscores.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Payment method name is required.")
            .Length(2, 64).WithMessage("Payment method name must be 2 to 64 characters long.");
    }
}

public sealed class CollectPaymentInlineValidator : AbstractValidator<CollectPaymentInlineDto>
{
    public CollectPaymentInlineValidator()
    {
        RuleFor(x => x.PaymentMethodId)
            .GreaterThan(0).WithMessage("Select a payment method.");

        RuleFor(x => x.Amount)
            .GreaterThan(0m).WithMessage("Payment amount must be greater than 0.")
            .LessThanOrEqualTo(ValidationRules.MaxMoneyAmount)
                .WithMessage("Payment amount is unrealistically high. Please check the amount.");

        RuleFor(x => x.TransactionReference)
            .MaximumLength(ValidationRules.ReferenceMaxLength)
                .WithMessage($"Transaction reference must not be longer than {ValidationRules.ReferenceMaxLength} characters.");

        RuleFor(x => x.Notes)
            .MaximumLength(ValidationRules.NotesMaxLength)
                .WithMessage($"Notes must not be longer than {ValidationRules.NotesMaxLength} characters.");
    }
}

public sealed class CreatePaymentValidator : AbstractValidator<CreatePaymentDto>
{
    public CreatePaymentValidator()
    {
        RuleFor(x => x.MemberId)
            .GreaterThan(0).WithMessage("Select the member who is paying.");

        RuleFor(x => x.SubscriptionId)
            .GreaterThan(0).WithMessage("The selected subscription is not valid.")
            .When(x => x.SubscriptionId.HasValue);

        RuleFor(x => x.PaymentMethodId)
            .GreaterThan(0).WithMessage("Select a payment method.");

        RuleFor(x => x.Amount)
            .GreaterThan(0m).WithMessage("Payment amount must be greater than 0.")
            .LessThanOrEqualTo(ValidationRules.MaxMoneyAmount)
                .WithMessage("Payment amount is unrealistically high. Please check the amount.");

        RuleFor(x => x.DiscountAmount)
            .GreaterThanOrEqualTo(0m).WithMessage("Discount cannot be negative.")
            .Must((dto, discount) => discount <= dto.Amount)
                .WithMessage("Discount cannot be greater than the payment amount.");

        RuleFor(x => x.TaxAmount)
            .GreaterThanOrEqualTo(0m).WithMessage("Tax cannot be negative.");

        RuleFor(x => x.PaymentDate)
            .Must(date => date!.Value.Date <= DateTime.Today.AddDays(1))
                .WithMessage("Payment date cannot be more than one day in the future.")
            .When(x => x.PaymentDate.HasValue);

        RuleFor(x => x.TransactionReference)
            .MaximumLength(ValidationRules.ReferenceMaxLength)
                .WithMessage($"Transaction reference must not be longer than {ValidationRules.ReferenceMaxLength} characters.");

        RuleFor(x => x.Notes)
            .MaximumLength(ValidationRules.NotesMaxLength)
                .WithMessage($"Notes must not be longer than {ValidationRules.NotesMaxLength} characters.");
    }
}

public sealed class ConfirmPaymentValidator : AbstractValidator<ConfirmPaymentDto>
{
    public ConfirmPaymentValidator()
    {
        RuleFor(x => x.PaymentId)
            .GreaterThan(0).WithMessage("Select the payment you want to confirm.");

        RuleFor(x => x.TransactionReference)
            .MaximumLength(ValidationRules.ReferenceMaxLength)
                .WithMessage($"Transaction reference must not be longer than {ValidationRules.ReferenceMaxLength} characters.");

        RuleFor(x => x.Remarks)
            .MaximumLength(ValidationRules.ReasonMaxLength)
                .WithMessage($"Remarks must not be longer than {ValidationRules.ReasonMaxLength} characters.");
    }
}

public sealed class CreateRefundValidator : AbstractValidator<CreateRefundDto>
{
    public CreateRefundValidator()
    {
        RuleFor(x => x.PaymentId)
            .GreaterThan(0).WithMessage("Select the payment you want to refund.");

        RuleFor(x => x.Amount)
            .GreaterThan(0m).WithMessage("Refund amount must be greater than 0.")
            .LessThanOrEqualTo(ValidationRules.MaxMoneyAmount)
                .WithMessage("Refund amount is unrealistically high. Please check the amount.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Please give a reason for the refund.")
            .Length(ValidationRules.ReasonMinLength, ValidationRules.ReasonMaxLength)
                .WithMessage($"Reason must be {ValidationRules.ReasonMinLength} to {ValidationRules.ReasonMaxLength} characters long.");

        RuleFor(x => x.TransactionReference)
            .MaximumLength(ValidationRules.ReferenceMaxLength)
                .WithMessage($"Transaction reference must not be longer than {ValidationRules.ReferenceMaxLength} characters.");
    }
}

public sealed class ApproveRefundValidator : AbstractValidator<ApproveRefundDto>
{
    public ApproveRefundValidator()
    {
        RuleFor(x => x.RefundId)
            .GreaterThan(0).WithMessage("Select the refund request you want to review.");

        RuleFor(x => x.Remarks)
            .MaximumLength(ValidationRules.ReasonMaxLength)
                .WithMessage($"Remarks must not be longer than {ValidationRules.ReasonMaxLength} characters.");
    }
}

public sealed class UpiPaymentRequestValidator : AbstractValidator<UpiPaymentRequestDto>
{
    public UpiPaymentRequestValidator()
    {
        RuleFor(x => x.MemberId)
            .GreaterThan(0).WithMessage("Select the member who is paying.");

        RuleFor(x => x.Amount)
            .GreaterThan(0m).WithMessage("Payment amount must be greater than 0.")
            .LessThanOrEqualTo(ValidationRules.MaxMoneyAmount)
                .WithMessage("Payment amount is unrealistically high. Please check the amount.");

        RuleFor(x => x.SubscriptionId)
            .GreaterThan(0).WithMessage("The selected subscription is not valid.")
            .When(x => x.SubscriptionId.HasValue);

        RuleFor(x => x.Notes)
            .MaximumLength(ValidationRules.NotesMaxLength)
                .WithMessage($"Notes must not be longer than {ValidationRules.NotesMaxLength} characters.");
    }
}
