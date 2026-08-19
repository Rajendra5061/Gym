using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using GymManagement.Application;
using GymManagement.Application.DTOs;
using GymManagement.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GymManagement.UnitTests.Validators;

/// <summary>
/// The FluentValidation rules the API applies before any service is reached. Validators are
/// resolved through <c>AddApplication()</c> so the test proves the DI wiring as well as the rules.
/// Covers cases M-04, M-05, A-15, A-16, A-17, S-04 (input side), S-06, Y-04 and N-10.
/// </summary>
public class ValidatorTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public ValidatorTests()
    {
        _provider = new ServiceCollection().AddApplication().BuildServiceProvider();
        _scope = _provider.CreateScope();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
        GC.SuppressFinalize(this);
    }

    private ValidationResult Validate<T>(T instance) =>
        _scope.ServiceProvider.GetRequiredService<IValidator<T>>().Validate(instance);

    private static IEnumerable<string> FailedProperties(ValidationResult result) =>
        result.Errors.Select(e => e.PropertyName).Distinct();

    // ------------------------------------------------------------ members

    private static CreateMemberDto ValidMember() => new()
    {
        FullName = "Asha Menon",
        Gender = Gender.Female,
        Phone = "9876543210",
        Email = "asha.menon@example.com",
        DateOfBirth = new DateTime(1995, 4, 12),
        JoiningDate = DateTime.Today
    };

    [Fact(DisplayName = "A fully populated CreateMemberDto passes validation")]
    public void CreateMember_WhenValid_Passes()
    {
        var result = Validate(ValidMember());

        result.IsValid.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact(DisplayName = "A member with no optional details still passes")]
    public void CreateMember_WithOnlyTheRequiredFields_Passes()
    {
        var dto = new CreateMemberDto { FullName = "Ravi Kumar", Phone = "9876543210" };

        Validate(dto).IsValid.Should().BeTrue();
    }

    [Fact(DisplayName = "M-05 A date of birth in the future fails on DateOfBirth")]
    public void CreateMember_WithAFutureDateOfBirth_FailsOnDateOfBirth()
    {
        var dto = ValidMember();
        dto.DateOfBirth = DateTime.Today.AddDays(1);

        var result = Validate(dto);

        result.IsValid.Should().BeFalse();
        FailedProperties(result).Should().Contain(nameof(CreateMemberDto.DateOfBirth));
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("in the past"));
    }

    [Fact(DisplayName = "M-05 A member under 5 years old fails on DateOfBirth")]
    public void CreateMember_WithAnUnderFiveAge_FailsOnDateOfBirth()
    {
        var dto = ValidMember();
        dto.DateOfBirth = DateTime.Today.AddYears(-3);

        var result = Validate(dto);

        result.IsValid.Should().BeFalse();
        FailedProperties(result).Should().Contain(nameof(CreateMemberDto.DateOfBirth));
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("between 5 and 100"));
    }

    [Theory(DisplayName = "M-05 A malformed phone number fails on Phone")]
    [InlineData("12345")]
    [InlineData("not-a-phone")]
    [InlineData("98765abc43")]
    [InlineData("+")]
    public void CreateMember_WithABadPhone_FailsOnPhone(string phone)
    {
        var dto = ValidMember();
        dto.Phone = phone;

        var result = Validate(dto);

        result.IsValid.Should().BeFalse();
        FailedProperties(result).Should().Contain(nameof(CreateMemberDto.Phone));
    }

    [Fact(DisplayName = "A missing full name fails on FullName")]
    public void CreateMember_WithoutAFullName_FailsOnFullName()
    {
        var dto = ValidMember();
        dto.FullName = string.Empty;

        var result = Validate(dto);

        result.IsValid.Should().BeFalse();
        FailedProperties(result).Should().Contain(nameof(CreateMemberDto.FullName));
    }

    [Fact(DisplayName = "M-04 CreateUserAccount without an email fails on Email")]
    public void CreateMember_WithAccountButNoEmail_FailsOnEmail()
    {
        var dto = ValidMember();
        dto.Email = null;
        dto.CreateUserAccount = true;

        var result = Validate(dto);

        result.IsValid.Should().BeFalse();
        FailedProperties(result).Should().Contain(nameof(CreateMemberDto.Email));
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("login account"));
    }

    [Fact(DisplayName = "M-04 CreateUserAccount with an email passes")]
    public void CreateMember_WithAccountAndEmail_Passes()
    {
        var dto = ValidMember();
        dto.CreateUserAccount = true;

        Validate(dto).IsValid.Should().BeTrue();
    }

    [Fact(DisplayName = "A malformed email address fails on Email")]
    public void CreateMember_WithAMalformedEmail_FailsOnEmail()
    {
        var dto = ValidMember();
        dto.Email = "not-an-email";

        var result = Validate(dto);

        result.IsValid.Should().BeFalse();
        FailedProperties(result).Should().Contain(nameof(CreateMemberDto.Email));
    }

    // ----------------------------------------------------- change password

    private static ChangePasswordRequestDto ValidChangePassword() => new()
    {
        CurrentPassword = "OldPassw0rd",
        NewPassword = "NewPassw0rd",
        ConfirmPassword = "NewPassw0rd"
    };

    [Fact(DisplayName = "A-14 A well formed change-password request passes")]
    public void ChangePassword_WhenValid_Passes()
    {
        Validate(ValidChangePassword()).IsValid.Should().BeTrue();
    }

    [Theory(DisplayName = "A-15 A weak new password fails on NewPassword")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("short1")]
    [InlineData("nodigitshere")]
    [InlineData("12345678")]
    [InlineData(" Passw0rd ")]
    public void ChangePassword_WithAWeakNewPassword_FailsOnNewPassword(string newPassword)
    {
        var dto = ValidChangePassword();
        dto.NewPassword = newPassword;
        dto.ConfirmPassword = newPassword;

        var result = Validate(dto);

        result.IsValid.Should().BeFalse();
        FailedProperties(result).Should().Contain(nameof(ChangePasswordRequestDto.NewPassword));
    }

    [Fact(DisplayName = "A-16 A confirmation that does not match fails on ConfirmPassword")]
    public void ChangePassword_WithAMismatchedConfirmation_FailsOnConfirmPassword()
    {
        var dto = ValidChangePassword();
        dto.ConfirmPassword = "SomethingElse1";

        var result = Validate(dto);

        result.IsValid.Should().BeFalse();
        FailedProperties(result).Should().Contain(nameof(ChangePasswordRequestDto.ConfirmPassword));
    }

    [Fact(DisplayName = "A-17 A new password equal to the current one fails on NewPassword")]
    public void ChangePassword_WithAnUnchangedPassword_FailsOnNewPassword()
    {
        var dto = new ChangePasswordRequestDto
        {
            CurrentPassword = "SamePassw0rd",
            NewPassword = "SamePassw0rd",
            ConfirmPassword = "SamePassw0rd"
        };

        var result = Validate(dto);

        result.IsValid.Should().BeFalse();
        FailedProperties(result).Should().Contain(nameof(ChangePasswordRequestDto.NewPassword));
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("different"));
    }

    [Fact(DisplayName = "A missing current password fails on CurrentPassword")]
    public void ChangePassword_WithoutTheCurrentPassword_FailsOnCurrentPassword()
    {
        var dto = ValidChangePassword();
        dto.CurrentPassword = string.Empty;

        var result = Validate(dto);

        result.IsValid.Should().BeFalse();
        FailedProperties(result).Should().Contain(nameof(ChangePasswordRequestDto.CurrentPassword));
    }

    // ------------------------------------------------------ subscriptions

    private static CreateSubscriptionDto ValidSubscription() => new()
    {
        MemberId = 1,
        MembershipPlanId = 1,
        StartDate = DateTime.Today,
        DiscountAmount = 200m,
        ChargeRegistrationFee = true
    };

    [Fact(DisplayName = "A well formed CreateSubscriptionDto passes")]
    public void CreateSubscription_WhenValid_Passes()
    {
        Validate(ValidSubscription()).IsValid.Should().BeTrue();
    }

    [Fact(DisplayName = "S-04 A negative discount fails on DiscountAmount")]
    public void CreateSubscription_WithANegativeDiscount_FailsOnDiscountAmount()
    {
        var dto = ValidSubscription();
        dto.DiscountAmount = -1m;

        var result = Validate(dto);

        result.IsValid.Should().BeFalse();
        FailedProperties(result).Should().Contain(nameof(CreateSubscriptionDto.DiscountAmount));
    }

    [Fact(DisplayName = "S-06 A start date more than 90 days ahead fails on StartDate")]
    public void CreateSubscription_WithAFarFutureStartDate_FailsOnStartDate()
    {
        var dto = ValidSubscription();
        dto.StartDate = DateTime.Today.AddDays(120);

        var result = Validate(dto);

        result.IsValid.Should().BeFalse();
        FailedProperties(result).Should().Contain(nameof(CreateSubscriptionDto.StartDate));
    }

    [Fact(DisplayName = "S-06 A start date more than 30 days in the past fails on StartDate")]
    public void CreateSubscription_WithAFarPastStartDate_FailsOnStartDate()
    {
        var dto = ValidSubscription();
        dto.StartDate = DateTime.Today.AddDays(-45);

        var result = Validate(dto);

        result.IsValid.Should().BeFalse();
        FailedProperties(result).Should().Contain(nameof(CreateSubscriptionDto.StartDate));
    }

    [Fact(DisplayName = "S-06 A start date at the edges of the allowed window passes")]
    public void CreateSubscription_AtTheEdgesOfTheWindow_Passes()
    {
        var backdated = ValidSubscription();
        backdated.StartDate = DateTime.Today.AddDays(-30);
        Validate(backdated).IsValid.Should().BeTrue();

        var presold = ValidSubscription();
        presold.StartDate = DateTime.Today.AddDays(90);
        Validate(presold).IsValid.Should().BeTrue();
    }

    [Fact(DisplayName = "An inline payment on a subscription is validated under the Payment prefix")]
    public void CreateSubscription_WithAnInvalidInlinePayment_FailsOnThePaymentProperty()
    {
        var dto = ValidSubscription();
        dto.Payment = new CollectPaymentInlineDto { PaymentMethodId = 0, Amount = 0m };

        var result = Validate(dto);

        result.IsValid.Should().BeFalse();
        FailedProperties(result).Should().Contain("Payment.Amount");
        FailedProperties(result).Should().Contain("Payment.PaymentMethodId");
    }

    // ---------------------------------------------------------- cancelling

    [Theory(DisplayName = "S-16 A blank cancellation reason fails on Reason")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void CancelSubscription_WithABlankReason_FailsOnReason(string? reason)
    {
        var dto = new CancelSubscriptionDto { SubscriptionId = 1, Reason = reason! };

        var result = Validate(dto);

        result.IsValid.Should().BeFalse();
        FailedProperties(result).Should().Contain(nameof(CancelSubscriptionDto.Reason));
    }

    [Fact(DisplayName = "S-15 A cancellation with a real reason passes")]
    public void CancelSubscription_WithAReason_Passes()
    {
        var dto = new CancelSubscriptionDto { SubscriptionId = 1, Reason = "Member relocated to another city." };

        Validate(dto).IsValid.Should().BeTrue();
    }

    // ------------------------------------------------------------ payments

    private static CreatePaymentDto ValidPayment() => new()
    {
        MemberId = 1,
        PaymentMethodId = 1,
        Amount = 1000m,
        DiscountAmount = 0m,
        TaxAmount = 0m
    };

    [Fact(DisplayName = "A well formed CreatePaymentDto passes")]
    public void CreatePayment_WhenValid_Passes()
    {
        Validate(ValidPayment()).IsValid.Should().BeTrue();
    }

    [Theory(DisplayName = "Y-04 A zero or negative amount fails on Amount")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1000)]
    public void CreatePayment_WithANonPositiveAmount_FailsOnAmount(int amount)
    {
        var dto = ValidPayment();
        dto.Amount = amount;

        var result = Validate(dto);

        result.IsValid.Should().BeFalse();
        FailedProperties(result).Should().Contain(nameof(CreatePaymentDto.Amount));
    }

    [Fact(DisplayName = "A discount larger than the amount fails on DiscountAmount")]
    public void CreatePayment_WithADiscountAboveTheAmount_FailsOnDiscountAmount()
    {
        var dto = ValidPayment();
        dto.Amount = 100m;
        dto.DiscountAmount = 150m;

        var result = Validate(dto);

        result.IsValid.Should().BeFalse();
        FailedProperties(result).Should().Contain(nameof(CreatePaymentDto.DiscountAmount));
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("greater than the payment amount"));
    }

    [Fact(DisplayName = "A negative discount fails on DiscountAmount")]
    public void CreatePayment_WithANegativeDiscount_FailsOnDiscountAmount()
    {
        var dto = ValidPayment();
        dto.DiscountAmount = -5m;

        var result = Validate(dto);

        result.IsValid.Should().BeFalse();
        FailedProperties(result).Should().Contain(nameof(CreatePaymentDto.DiscountAmount));
    }

    // ---------------------------------------------------------- attendance

    [Fact(DisplayName = "N-10 An unrecognised check-in method fails on CheckInMethod")]
    public void CheckIn_WithAnUnknownMethod_FailsOnCheckInMethod()
    {
        var dto = new CheckInRequestDto { MemberId = 1, CheckInMethod = "Telepathy" };

        var result = Validate(dto);

        result.IsValid.Should().BeFalse();
        FailedProperties(result).Should().Contain(nameof(CheckInRequestDto.CheckInMethod));
    }

    [Theory(DisplayName = "N-10 Every supported check-in method is accepted")]
    [InlineData("Manual")]
    [InlineData("QrCode")]
    [InlineData("Barcode")]
    [InlineData("Rfid")]
    [InlineData("Biometric")]
    [InlineData("  qrcode  ")]
    public void CheckIn_WithASupportedMethod_Passes(string method)
    {
        var dto = new CheckInRequestDto { MemberId = 1, CheckInMethod = method };

        Validate(dto).IsValid.Should().BeTrue();
    }

    [Fact(DisplayName = "A check-in with neither a member id nor a code fails on Member")]
    public void CheckIn_WithoutAMemberIdentifier_FailsOnMember()
    {
        var dto = new CheckInRequestDto();

        var result = Validate(dto);

        result.IsValid.Should().BeFalse();
        FailedProperties(result).Should().Contain("Member");
    }

    // ------------------------------------------------------------ settings

    private static GymSettingsDto ValidSettings() => new()
    {
        GymName = "Iron Temple",
        CurrencyCode = "INR",
        CurrencySymbol = "₹",
        OpeningTime = new TimeSpan(6, 0, 0),
        ClosingTime = new TimeSpan(22, 0, 0),
        ExpiryReminderDays = 7,
        DefaultGracePeriodDays = 3,
        MaxFailedLoginAttempts = 5,
        LockoutMinutes = 15
    };

    [Fact(DisplayName = "G-01 A well formed gym settings payload passes")]
    public void GymSettings_WhenValid_Passes()
    {
        Validate(ValidSettings()).IsValid.Should().BeTrue();
    }

    [Fact(DisplayName = "G-02 A closing time before the opening time fails on ClosingTime")]
    public void GymSettings_WithClosingBeforeOpening_FailsOnClosingTime()
    {
        var dto = ValidSettings();
        dto.OpeningTime = new TimeSpan(22, 0, 0);
        dto.ClosingTime = new TimeSpan(6, 0, 0);

        var result = Validate(dto);

        result.IsValid.Should().BeFalse();
        FailedProperties(result).Should().Contain(nameof(GymSettingsDto.ClosingTime));
    }

    [Theory(DisplayName = "G-02 A malformed UPI id fails on UpiId")]
    [InlineData("no-at-sign")]
    [InlineData("@handle")]
    [InlineData("name@")]
    [InlineData("a@b")]
    public void GymSettings_WithAMalformedUpiId_FailsOnUpiId(string upiId)
    {
        var dto = ValidSettings();
        dto.UpiId = upiId;

        var result = Validate(dto);

        result.IsValid.Should().BeFalse();
        FailedProperties(result).Should().Contain(nameof(GymSettingsDto.UpiId));
    }

    [Fact(DisplayName = "G-02 A well formed UPI id is accepted")]
    public void GymSettings_WithAValidUpiId_Passes()
    {
        var dto = ValidSettings();
        dto.UpiId = "mygym@examplebank";

        Validate(dto).IsValid.Should().BeTrue();
    }
}
