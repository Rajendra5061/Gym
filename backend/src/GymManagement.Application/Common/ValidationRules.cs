using System.Text.RegularExpressions;
using FluentValidation;

namespace GymManagement.Application.Common;

/// <summary>
/// Shared input limits, patterns and reusable FluentValidation rules used by every validator.
/// Messages are written for the person filling in the form, not for a developer.
/// </summary>
public static partial class ValidationRules
{
    // ---------------------------------------------------------------- limits

    public const int PhoneMinLength = 7;
    public const int PhoneMaxLength = 24;
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 128;
    public const int NameMinLength = 2;
    public const int NameMaxLength = 160;
    public const int NotesMaxLength = 2000;
    public const int EmailMaxLength = 200;
    public const int ReasonMinLength = 5;
    public const int ReasonMaxLength = 512;
    public const int ReferenceMaxLength = 128;

    /// <summary>Upper bound for a single money amount, so a typo cannot post a huge transaction.</summary>
    public const decimal MaxMoneyAmount = 10_000_000m;

    public static readonly IReadOnlyList<string> BloodGroups =
        new[] { "A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-" };

    public static readonly IReadOnlyList<string> CheckInMethods =
        new[] { "Manual", "QrCode", "Barcode", "Rfid", "Biometric" };

    // --------------------------------------------------------------- patterns

    /// <summary>Digits, spaces, brackets and hyphens with an optional leading plus.</summary>
    [GeneratedRegex(@"^[+]?[0-9 ()\-]{7,24}$", RegexOptions.CultureInvariant)]
    public static partial Regex PhoneRegex();

    /// <summary>File name without any path separator or character the file system rejects.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 ._\-()]{0,199}$", RegexOptions.CultureInvariant)]
    public static partial Regex SafeFileNameRegex();

    /// <summary>Letters (any alphabet), spaces, apostrophes, hyphens and periods.</summary>
    [GeneratedRegex(@"^[\p{L}\p{M}][\p{L}\p{M} .'\-]*$", RegexOptions.CultureInvariant)]
    public static partial Regex PersonNameRegex();

    /// <summary>Login name: letters, digits, dot, underscore and hyphen.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9._\-]+$", RegexOptions.CultureInvariant)]
    public static partial Regex UserNameRegex();

    /// <summary>Short machine code: upper-case letters, digits and underscore.</summary>
    [GeneratedRegex(@"^[A-Z0-9_]+$", RegexOptions.CultureInvariant)]
    public static partial Regex UpperCaseCodeRegex();

    /// <summary>UPI virtual payment address, for example <c>gym.name@bankhandle</c>.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9._\-]{2,64}@[A-Za-z][A-Za-z0-9.\-]{1,32}$", RegexOptions.CultureInvariant)]
    public static partial Regex UpiIdRegex();

    // --------------------------------------------------------------- helpers

    public static bool IsBloodGroup(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        BloodGroups.Any(group => string.Equals(group, value.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool IsCheckInMethod(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        CheckInMethods.Any(method => string.Equals(method, value.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Comma separated list of the accepted check-in methods, for use in messages.</summary>
    public static string CheckInMethodList => string.Join(", ", CheckInMethods);

    public static bool IsValidPhone(string? value) =>
        !string.IsNullOrWhiteSpace(value) && PhoneRegex().IsMatch(value.Trim());

    /// <summary>Deliberately permissive: enough to catch typing mistakes without rejecting real addresses.</summary>
    public static bool IsValidEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var candidate = value.Trim();
        if (candidate.Length > EmailMaxLength) return false;
        if (candidate.Any(char.IsWhiteSpace)) return false;

        var at = candidate.IndexOf('@');
        if (at <= 0 || at != candidate.LastIndexOf('@') || at == candidate.Length - 1) return false;

        var domain = candidate[(at + 1)..];
        var dot = domain.IndexOf('.');
        return dot > 0 && dot < domain.Length - 1 && !domain.StartsWith('.') && !domain.EndsWith('.');
    }

    /// <summary>Age in completed years on <paramref name="asOf"/> (today when omitted).</summary>
    public static int AgeInYears(DateTime dateOfBirth, DateTime? asOf = null)
    {
        var on = (asOf ?? DateTime.Today).Date;
        var born = dateOfBirth.Date;
        var age = on.Year - born.Year;
        if (born > on.AddYears(-age)) age--;
        return age;
    }

    // ------------------------------------------------------- reusable rules
    // The rules below are deliberately declared in a nullable-oblivious region so that a single
    // definition can be applied to both `string` and `string?` properties without warnings.
#nullable disable

    /// <summary>Accepts an empty value; combine with <c>NotEmpty()</c> when the phone is mandatory.</summary>
    public static IRuleBuilderOptions<T, string> PhoneNumber<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        ruleBuilder
            .Must(value => string.IsNullOrWhiteSpace(value) || PhoneRegex().IsMatch(value.Trim()))
            .WithMessage(
                $"'{{PropertyName}}' must be {PhoneMinLength} to {PhoneMaxLength} characters and may only " +
                "contain digits, spaces, brackets, hyphens and a leading '+'.");

    /// <summary>Validates the format only when a value was entered.</summary>
    public static IRuleBuilderOptions<T, string> EmailAddressOrEmpty<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        ruleBuilder
            .Must(value => string.IsNullOrWhiteSpace(value) || IsValidEmail(value))
            .WithMessage("'{PropertyName}' must be a valid email address, for example name@example.com.");

    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        ruleBuilder
            .NotEmpty()
                .WithMessage("'{PropertyName}' is required.")
            .MinimumLength(PasswordMinLength)
                .WithMessage($"'{{PropertyName}}' must be at least {PasswordMinLength} characters long.")
            .MaximumLength(PasswordMaxLength)
                .WithMessage($"'{{PropertyName}}' must not be longer than {PasswordMaxLength} characters.")
            .Must(value => value != null && value.Any(char.IsLetter))
                .WithMessage("'{PropertyName}' must contain at least one letter.")
            .Must(value => value != null && value.Any(char.IsDigit))
                .WithMessage("'{PropertyName}' must contain at least one digit.")
            .Must(value => value == null || value.Length == value.Trim().Length)
                .WithMessage("'{PropertyName}' must not begin or end with a space.");

    public static IRuleBuilderOptions<T, string> PersonName<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        ruleBuilder
            .NotEmpty()
                .WithMessage("'{PropertyName}' is required.")
            .MinimumLength(NameMinLength)
                .WithMessage($"'{{PropertyName}}' must be at least {NameMinLength} characters long.")
            .MaximumLength(NameMaxLength)
                .WithMessage($"'{{PropertyName}}' must not be longer than {NameMaxLength} characters.")
            .Must(value => string.IsNullOrWhiteSpace(value) || PersonNameRegex().IsMatch(value.Trim()))
                .WithMessage("'{PropertyName}' may only contain letters, spaces, apostrophes, hyphens and periods.");

#nullable restore
}
