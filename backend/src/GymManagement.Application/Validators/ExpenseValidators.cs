using FluentValidation;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;

namespace GymManagement.Application.Validators;

public sealed class SaveExpenseValidator : AbstractValidator<SaveExpenseDto>
{
    private static readonly DateTime EarliestExpenseDate = new(2000, 1, 1);

    public SaveExpenseValidator()
    {
        RuleFor(x => x.ExpenseCategoryId)
            .GreaterThan(0).WithMessage("Select an expense category.");

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Expense title is required.")
            .Length(3, 200).WithMessage("Expense title must be 3 to 200 characters long.");

        RuleFor(x => x.Amount)
            .GreaterThan(0m).WithMessage("Expense amount must be greater than 0.")
            .LessThanOrEqualTo(ValidationRules.MaxMoneyAmount)
                .WithMessage("Expense amount is unrealistically high. Please check the amount.");

        RuleFor(x => x.ExpenseDate)
            .Must(date => date.Date <= DateTime.Today)
                .WithMessage("Expense date cannot be in the future.")
            .GreaterThanOrEqualTo(EarliestExpenseDate)
                .WithMessage("Expense date must be on or after 1 January 2000.");

        RuleFor(x => x.PaymentMethodId)
            .GreaterThan(0).WithMessage("The selected payment method is not valid.")
            .When(x => x.PaymentMethodId.HasValue);

        RuleFor(x => x.VendorName)
            .MaximumLength(200).WithMessage("Vendor name must not be longer than 200 characters.");

        RuleFor(x => x.ReferenceNumber)
            .MaximumLength(ValidationRules.ReferenceMaxLength)
                .WithMessage($"Reference number must not be longer than {ValidationRules.ReferenceMaxLength} characters.");

        RuleFor(x => x.Description)
            .MaximumLength(ValidationRules.NotesMaxLength)
                .WithMessage($"Description must not be longer than {ValidationRules.NotesMaxLength} characters.");
    }
}

public sealed class ExpenseCategoryValidator : AbstractValidator<ExpenseCategoryDto>
{
    public ExpenseCategoryValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Category name is required.")
            .Length(2, 120).WithMessage("Category name must be 2 to 120 characters long.");

        RuleFor(x => x.Description)
            .MaximumLength(512).WithMessage("Description must not be longer than 512 characters.");
    }
}
