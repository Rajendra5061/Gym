using FluentValidation;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;

namespace GymManagement.Application.Validators;

public sealed class CreateNotificationValidator : AbstractValidator<CreateNotificationDto>
{
    public CreateNotificationValidator()
    {
        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Select a valid notification type.");

        RuleFor(x => x.Severity)
            .IsInEnum().WithMessage("Select a valid notification severity.");

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Notification title is required.")
            .Length(3, 200).WithMessage("Notification title must be 3 to 200 characters long.");

        RuleFor(x => x.Message)
            .NotEmpty().WithMessage("Notification message is required.")
            .Length(3, ValidationRules.NotesMaxLength)
                .WithMessage($"Notification message must be 3 to {ValidationRules.NotesMaxLength} characters long.");

        RuleFor(x => x.UserId)
            .GreaterThan(0).WithMessage("The selected user is not valid.")
            .When(x => x.UserId.HasValue);

        RuleFor(x => x.MemberId)
            .GreaterThan(0).WithMessage("The selected member is not valid.")
            .When(x => x.MemberId.HasValue);
    }
}

public sealed class GymSettingsValidator : AbstractValidator<GymSettingsDto>
{
    public GymSettingsValidator()
    {
        RuleFor(x => x.GymName)
            .NotEmpty().WithMessage("Gym name is required.")
            .Length(2, 200).WithMessage("Gym name must be 2 to 200 characters long.");

        RuleFor(x => x.Email)
            .MaximumLength(ValidationRules.EmailMaxLength)
                .WithMessage($"Email address must not be longer than {ValidationRules.EmailMaxLength} characters.")
            .EmailAddressOrEmpty();

        RuleFor(x => x.Phone).PhoneNumber();

        RuleFor(x => x.CurrencyCode)
            .NotEmpty().WithMessage("Currency code is required.")
            .Length(1, 8).WithMessage("Currency code must be 1 to 8 characters long.");

        RuleFor(x => x.CurrencySymbol)
            .NotEmpty().WithMessage("Currency symbol is required.")
            .Length(1, 8).WithMessage("Currency symbol must be 1 to 8 characters long.");

        RuleFor(x => x.ExpiryReminderDays)
            .InclusiveBetween(0, 90).WithMessage("Expiry reminder must be between 0 and 90 days.");

        RuleFor(x => x.DefaultGracePeriodDays)
            .InclusiveBetween(0, 90).WithMessage("Default grace period must be between 0 and 90 days.");

        RuleFor(x => x.MaxFailedLoginAttempts)
            .InclusiveBetween(1, 20).WithMessage("Maximum failed login attempts must be between 1 and 20.");

        RuleFor(x => x.LockoutMinutes)
            .InclusiveBetween(1, 1440).WithMessage("Lockout duration must be between 1 and 1440 minutes.");

        RuleFor(x => x.ClosingTime)
            .Must((settings, closingTime) => closingTime > settings.OpeningTime)
            .WithMessage("Closing time must be later than the opening time.");

        RuleFor(x => x.UpiId)
            .Matches(ValidationRules.UpiIdRegex())
                .WithMessage("UPI ID must look like name@handle, for example mygym@examplebank.")
            .When(x => !string.IsNullOrWhiteSpace(x.UpiId));

        RuleFor(x => x.ReceiptFooterText)
            .MaximumLength(ValidationRules.NotesMaxLength)
                .WithMessage($"Receipt footer text must not be longer than {ValidationRules.NotesMaxLength} characters.");
    }
}

public sealed class SystemSettingValidator : AbstractValidator<SystemSettingDto>
{
    public SystemSettingValidator()
    {
        RuleFor(x => x.Key)
            .NotEmpty().WithMessage("Setting key is required.")
            .Length(2, 120).WithMessage("Setting key must be 2 to 120 characters long.");

        RuleFor(x => x.Value)
            .MaximumLength(ValidationRules.NotesMaxLength)
                .WithMessage($"Setting value must not be longer than {ValidationRules.NotesMaxLength} characters.");
    }
}

public sealed class ActivateLicenseValidator : AbstractValidator<ActivateLicenseDto>
{
    public ActivateLicenseValidator()
    {
        RuleFor(x => x.LicenseKey)
            .NotEmpty().WithMessage("License key is required.")
            .Length(8, 256).WithMessage("License key must be 8 to 256 characters long.");

        RuleFor(x => x.CustomerName)
            .MaximumLength(200).WithMessage("Customer name must not be longer than 200 characters.");
    }
}

public sealed class StartTrialValidator : AbstractValidator<StartTrialDto>
{
    public StartTrialValidator()
    {
        RuleFor(x => x.CustomerName)
            .NotEmpty().WithMessage("Customer name is required.")
            .Length(2, 200).WithMessage("Customer name must be 2 to 200 characters long.");

        RuleFor(x => x.GymIdentifier)
            .MaximumLength(120).WithMessage("Gym identifier must not be longer than 120 characters.");
    }
}

public sealed class CreateBackupValidator : AbstractValidator<CreateBackupDto>
{
    public CreateBackupValidator()
    {
        RuleFor(x => x.TargetFolder)
            .Must(IsRootedPath)
                .WithMessage("Target folder must be a full path, for example D:\\GymBackups.")
            .When(x => !string.IsNullOrWhiteSpace(x.TargetFolder));

        RuleFor(x => x.Notes)
            .MaximumLength(ValidationRules.ReasonMaxLength)
                .WithMessage($"Notes must not be longer than {ValidationRules.ReasonMaxLength} characters.");
    }

    private static bool IsRootedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;

        try
        {
            return Path.IsPathRooted(path.Trim()) &&
                   path.IndexOfAny(Path.GetInvalidPathChars()) < 0;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

public sealed class RestoreBackupValidator : AbstractValidator<RestoreBackupDto>
{
    public RestoreBackupValidator()
    {
        RuleFor(x => x.BackupId)
            .GreaterThan(0).WithMessage("Select the backup you want to restore.");

        RuleFor(x => x.ConfirmationText)
            .NotEmpty().WithMessage("Type the database name to confirm that you want to restore this backup.")
            .MaximumLength(200).WithMessage("Confirmation text must not be longer than 200 characters.");
    }
}

public sealed class RecycleBinActionValidator : AbstractValidator<RecycleBinActionDto>
{
    public RecycleBinActionValidator()
    {
        RuleFor(x => x.EntityName)
            .NotEmpty().WithMessage("Select the type of record you want to act on.")
            .MaximumLength(120).WithMessage("Record type must not be longer than 120 characters.");

        RuleFor(x => x.EntityIds)
            .NotEmpty().WithMessage("Select at least one record from the recycle bin.");

        RuleForEach(x => x.EntityIds)
            .GreaterThan(0).WithMessage("One of the selected records is not valid.");
    }
}

public sealed class SendTestEmailValidator : AbstractValidator<SendTestEmailDto>
{
    public SendTestEmailValidator()
    {
        RuleFor(x => x.ToAddress)
            .NotEmpty().WithMessage("Enter the address to send the test message to.")
            .MaximumLength(ValidationRules.EmailMaxLength)
                .WithMessage($"Email address must not be longer than {ValidationRules.EmailMaxLength} characters.")
            .EmailAddressOrEmpty();
    }
}
