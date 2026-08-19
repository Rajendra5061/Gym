using FluentValidation;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;

namespace GymManagement.Application.Validators;

public sealed class MembershipPlanValidator : AbstractValidator<MembershipPlanDto>
{
    public MembershipPlanValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Plan name is required.")
            .Length(2, 120).WithMessage("Plan name must be 2 to 120 characters long.");

        RuleFor(x => x.DurationType)
            .IsInEnum().WithMessage("Select a valid duration type.");

        RuleFor(x => x.DurationValue)
            .InclusiveBetween(1, 3650).WithMessage("Duration must be between 1 and 3650.");

        RuleFor(x => x.Price)
            .GreaterThanOrEqualTo(0m).WithMessage("Price cannot be negative.")
            .LessThanOrEqualTo(ValidationRules.MaxMoneyAmount)
                .WithMessage("Price is unrealistically high. Please check the amount.");

        RuleFor(x => x.RegistrationFee)
            .GreaterThanOrEqualTo(0m).WithMessage("Registration fee cannot be negative.")
            .LessThanOrEqualTo(ValidationRules.MaxMoneyAmount)
                .WithMessage("Registration fee is unrealistically high. Please check the amount.");

        RuleFor(x => x.TaxPercent)
            .InclusiveBetween(0m, 100m).WithMessage("Tax must be between 0 and 100 percent.");

        RuleFor(x => x.MaxDiscountPercent)
            .InclusiveBetween(0m, 100m).WithMessage("Maximum discount must be between 0 and 100 percent.");

        RuleFor(x => x.GracePeriodDays)
            .InclusiveBetween(0, 90).WithMessage("Grace period must be between 0 and 90 days.");

        RuleFor(x => x.MaxFreezeDays)
            .InclusiveBetween(0, 365).WithMessage("Maximum freeze days must be between 0 and 365.");

        RuleFor(x => x.SessionLimit)
            .GreaterThan(0).WithMessage("Session limit must be greater than 0, or left empty for unlimited.")
            .When(x => x.SessionLimit.HasValue);

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Select a valid plan status.");

        RuleFor(x => x.Description)
            .MaximumLength(ValidationRules.NotesMaxLength)
                .WithMessage($"Description must not be longer than {ValidationRules.NotesMaxLength} characters.");
    }
}

public sealed class CreateSubscriptionValidator : AbstractValidator<CreateSubscriptionDto>
{
    public CreateSubscriptionValidator()
    {
        RuleFor(x => x.MemberId)
            .GreaterThan(0).WithMessage("Select the member for this subscription.");

        RuleFor(x => x.MembershipPlanId)
            .GreaterThan(0).WithMessage("Select a membership plan.");

        RuleFor(x => x.StartDate)
            .Must(date => date.Date >= DateTime.Today.AddDays(-30) && date.Date <= DateTime.Today.AddDays(90))
            .WithMessage("Start date must be within 30 days in the past and 90 days in the future.");

        RuleFor(x => x.DiscountAmount)
            .GreaterThanOrEqualTo(0m).WithMessage("Discount cannot be negative.")
            .LessThanOrEqualTo(ValidationRules.MaxMoneyAmount)
                .WithMessage("Discount is unrealistically high. Please check the amount.");

        RuleFor(x => x.AssignedTrainerId)
            .GreaterThan(0).WithMessage("The selected trainer is not valid.")
            .When(x => x.AssignedTrainerId.HasValue);

        RuleFor(x => x.Notes)
            .MaximumLength(ValidationRules.NotesMaxLength)
                .WithMessage($"Notes must not be longer than {ValidationRules.NotesMaxLength} characters.");

        RuleFor(x => x.Payment!)
            .SetValidator(new CollectPaymentInlineValidator())
            .When(x => x.Payment is not null);
    }
}

public sealed class RenewSubscriptionValidator : AbstractValidator<RenewSubscriptionDto>
{
    public RenewSubscriptionValidator()
    {
        RuleFor(x => x.MemberId)
            .GreaterThan(0).WithMessage("Select the member whose membership is being renewed.");

        RuleFor(x => x.PreviousSubscriptionId)
            .GreaterThan(0).WithMessage("The selected subscription is not valid.")
            .When(x => x.PreviousSubscriptionId.HasValue);

        RuleFor(x => x.MembershipPlanId)
            .GreaterThan(0).WithMessage("The selected membership plan is not valid.")
            .When(x => x.MembershipPlanId.HasValue);

        RuleFor(x => x.DiscountAmount)
            .GreaterThanOrEqualTo(0m).WithMessage("Discount cannot be negative.")
            .LessThanOrEqualTo(ValidationRules.MaxMoneyAmount)
                .WithMessage("Discount is unrealistically high. Please check the amount.");

        RuleFor(x => x.Notes)
            .MaximumLength(ValidationRules.NotesMaxLength)
                .WithMessage($"Notes must not be longer than {ValidationRules.NotesMaxLength} characters.");

        RuleFor(x => x.Payment!)
            .SetValidator(new CollectPaymentInlineValidator())
            .When(x => x.Payment is not null);
    }
}

public sealed class ChangePlanValidator : AbstractValidator<ChangePlanDto>
{
    public ChangePlanValidator()
    {
        RuleFor(x => x.SubscriptionId)
            .GreaterThan(0).WithMessage("Select the subscription you want to change.");

        RuleFor(x => x.NewMembershipPlanId)
            .GreaterThan(0).WithMessage("Select the new membership plan.");

        RuleFor(x => x.EffectiveDate)
            .Must(date => date.Date >= DateTime.Today.AddDays(-30))
            .WithMessage("Effective date cannot be more than 30 days in the past.");

        RuleFor(x => x.DiscountAmount)
            .GreaterThanOrEqualTo(0m).WithMessage("Discount cannot be negative.")
            .LessThanOrEqualTo(ValidationRules.MaxMoneyAmount)
                .WithMessage("Discount is unrealistically high. Please check the amount.");

        RuleFor(x => x.Reason)
            .MaximumLength(ValidationRules.ReasonMaxLength)
                .WithMessage($"Reason must not be longer than {ValidationRules.ReasonMaxLength} characters.");

        RuleFor(x => x.Payment!)
            .SetValidator(new CollectPaymentInlineValidator())
            .When(x => x.Payment is not null);
    }
}

public sealed class FreezeSubscriptionValidator : AbstractValidator<FreezeSubscriptionDto>
{
    public FreezeSubscriptionValidator()
    {
        RuleFor(x => x.SubscriptionId)
            .GreaterThan(0).WithMessage("Select the subscription you want to freeze.");

        RuleFor(x => x.FreezeStartDate)
            .Must(date => date.Date >= DateTime.Today)
            .WithMessage("Freeze start date cannot be in the past.");

        RuleFor(x => x.FreezeEndDate)
            .Must((dto, endDate) => endDate.Date > dto.FreezeStartDate.Date)
                .WithMessage("Freeze end date must be after the freeze start date.")
            .Must((dto, endDate) => (endDate.Date - dto.FreezeStartDate.Date).TotalDays <= 365)
                .WithMessage("A freeze cannot last longer than 365 days.");

        RuleFor(x => x.Reason)
            .MaximumLength(ValidationRules.ReasonMaxLength)
                .WithMessage($"Reason must not be longer than {ValidationRules.ReasonMaxLength} characters.");
    }
}

public sealed class ResumeSubscriptionValidator : AbstractValidator<ResumeSubscriptionDto>
{
    public ResumeSubscriptionValidator()
    {
        RuleFor(x => x.SubscriptionId)
            .GreaterThan(0).WithMessage("Select the subscription you want to resume.");

        RuleFor(x => x.Remarks)
            .MaximumLength(ValidationRules.ReasonMaxLength)
                .WithMessage($"Remarks must not be longer than {ValidationRules.ReasonMaxLength} characters.");
    }
}

public sealed class CancelSubscriptionValidator : AbstractValidator<CancelSubscriptionDto>
{
    public CancelSubscriptionValidator()
    {
        RuleFor(x => x.SubscriptionId)
            .GreaterThan(0).WithMessage("Select the subscription you want to cancel.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Please give a reason for the cancellation.")
            .Length(ValidationRules.ReasonMinLength, ValidationRules.ReasonMaxLength)
                .WithMessage($"Reason must be {ValidationRules.ReasonMinLength} to {ValidationRules.ReasonMaxLength} characters long.");
    }
}

public sealed class QuoteRequestValidator : AbstractValidator<QuoteRequestDto>
{
    public QuoteRequestValidator()
    {
        RuleFor(x => x.MembershipPlanId)
            .GreaterThan(0).WithMessage("Select a membership plan to price.");

        RuleFor(x => x.DiscountAmount)
            .GreaterThanOrEqualTo(0m).WithMessage("Discount cannot be negative.")
            .LessThanOrEqualTo(ValidationRules.MaxMoneyAmount)
                .WithMessage("Discount is unrealistically high. Please check the amount.");

        RuleFor(x => x.CurrentSubscriptionId)
            .GreaterThan(0).WithMessage("The selected subscription is not valid.")
            .When(x => x.CurrentSubscriptionId.HasValue);
    }
}
