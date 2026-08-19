using FluentAssertions;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Domain.Entities;
using GymManagement.Infrastructure.Data;
using GymManagement.Infrastructure.Services;
using GymManagement.UnitTests.TestBase;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GymManagement.UnitTests.Services;

/// <summary>
/// Case S-01. The server is the only authority on money, so every figure a quote returns is pinned
/// down here: the exact arithmetic, the discount ceiling, the tax rate, the term length and the
/// guarantee that nothing is ever computed in floating point.
/// </summary>
public class SubscriptionPricingTests : IAsyncLifetime
{
    private readonly FixedClock _clock = new();
    private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Admin();
    private readonly StubSettingsService _settings = new();
    private readonly RecordingAuditService _audit = new();
    private readonly RecordingNotificationService _notifications = new();
    private readonly RecordingReceiptMailer _receiptMailer = new();

    private GymDbContext _db = null!;
    private ReferenceData _data = null!;
    private SubscriptionService _service = null!;

    public async Task InitializeAsync()
    {
        _db = InMemoryDbContextFactory.Create(_currentUser, _clock);
        _data = await InMemoryDbContextFactory.SeedReferenceDataAsync(_db);

        _service = new SubscriptionService(_db, new SequentialCodeGenerator(), _clock, _currentUser,
            _audit, _settings, _notifications, _receiptMailer, NullLogger<SubscriptionService>.Instance);
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    private QuoteRequestDto Request(int? planId = null, decimal discount = 200m,
        bool chargeRegistrationFee = true, DateTime? startDate = null) => new()
    {
        MembershipPlanId = planId ?? _data.MonthlyPlanId,
        StartDate = startDate ?? _clock.Today,
        DiscountAmount = discount,
        ChargeRegistrationFee = chargeRegistrationFee
    };

    // ------------------------------------------------------ the arithmetic

    [Fact(DisplayName = "S-01 Plan 2500 + registration 500 - discount 200, taxed at 18%, totals 3304.00")]
    public async Task GetQuote_ForTheMonthlyPlan_ProducesTheVerifiedTotal()
    {
        var quote = await _service.GetQuoteAsync(Request());

        quote.PlanAmount.Should().Be(2500.00m);
        quote.RegistrationFee.Should().Be(500.00m);
        quote.DiscountAmount.Should().Be(200.00m);
        quote.TaxPercent.Should().Be(18.00m);
        quote.TaxAmount.Should().Be(504.00m);
        quote.FinalAmount.Should().Be(3304.00m);
    }

    [Fact(DisplayName = "S-01 The final amount equals plan + fee - discount + tax")]
    public async Task GetQuote_FinalAmountIsInternallyConsistent()
    {
        var quote = await _service.GetQuoteAsync(Request());

        quote.FinalAmount.Should().Be(
            quote.PlanAmount + quote.RegistrationFee - quote.DiscountAmount + quote.TaxAmount);
    }

    [Fact(DisplayName = "S-01 With no discount the total is 3000 plus 540 tax")]
    public async Task GetQuote_WithoutADiscount_TaxesTheFullAmount()
    {
        var quote = await _service.GetQuoteAsync(Request(discount: 0m));

        quote.DiscountAmount.Should().Be(0m);
        quote.TaxAmount.Should().Be(540.00m);
        quote.FinalAmount.Should().Be(3540.00m);
    }

    [Fact(DisplayName = "S-01 ChargeRegistrationFee = false leaves the fee out of the total")]
    public async Task GetQuote_WithoutTheRegistrationFee_ExcludesIt()
    {
        var quote = await _service.GetQuoteAsync(Request(chargeRegistrationFee: false));

        quote.RegistrationFee.Should().Be(0m);
        // 2500 - 200 = 2300, tax 18% = 414
        quote.TaxAmount.Should().Be(414.00m);
        quote.FinalAmount.Should().Be(2714.00m);
    }

    [Fact(DisplayName = "S-01 A plan with a zero tax rate produces no tax")]
    public async Task GetQuote_ForAZeroTaxPlan_ProducesNoTax()
    {
        var quote = await _service.GetQuoteAsync(Request(planId: _data.ZeroTaxPlanId));

        quote.TaxPercent.Should().Be(0m);
        quote.TaxAmount.Should().Be(0m);
        quote.FinalAmount.Should().Be(2800.00m);
    }

    [Fact(DisplayName = "S-01 A negative discount is treated as no discount rather than adding money")]
    public async Task GetQuote_WithANegativeDiscount_TreatsItAsZero()
    {
        var quote = await _service.GetQuoteAsync(Request(discount: -500m));

        quote.DiscountAmount.Should().Be(0m);
        quote.FinalAmount.Should().Be(3540.00m);
    }

    // ------------------------------------------------------ discount ceiling

    [Fact(DisplayName = "S-01 The quote reports the plan's maximum allowed discount")]
    public async Task GetQuote_ReportsTheMaximumAllowedDiscount()
    {
        var quote = await _service.GetQuoteAsync(Request());

        // 15% of (2500 + 500)
        quote.MaxAllowedDiscount.Should().Be(450.00m);
    }

    [Fact(DisplayName = "S-04 A discount above the plan's maximum is rejected")]
    public async Task GetQuote_WithAnExcessDiscount_ThrowsValidationAppException()
    {
        var act = () => _service.GetQuoteAsync(Request(discount: 451m));

        var error = await act.Should().ThrowAsync<ValidationAppException>();
        error.Which.Errors.Should().ContainKey("DiscountAmount");
        error.Which.Errors["DiscountAmount"].Should().Contain(m => m.Contains("450.00"));
    }

    [Fact(DisplayName = "S-04 A discount exactly at the maximum is accepted")]
    public async Task GetQuote_WithTheMaximumDiscount_IsAccepted()
    {
        var quote = await _service.GetQuoteAsync(Request(discount: 450m));

        quote.DiscountAmount.Should().Be(450.00m);
        // 3000 - 450 = 2550, tax 18% = 459
        quote.FinalAmount.Should().Be(3009.00m);
    }

    [Fact(DisplayName = "S-04 The ceiling shrinks when the registration fee is not charged")]
    public async Task GetQuote_WithoutTheRegistrationFee_LowersTheDiscountCeiling()
    {
        var quote = await _service.GetQuoteAsync(Request(discount: 0m, chargeRegistrationFee: false));

        // 15% of 2500 only
        quote.MaxAllowedDiscount.Should().Be(375.00m);
    }

    // --------------------------------------------------------- term dates

    [Fact(DisplayName = "S-01 The quoted end date is the plan's own CalculateEndDate")]
    public async Task GetQuote_EndDateMatchesThePlanCalculation()
    {
        var plan = await _db.MembershipPlans.AsNoTracking()
            .FirstAsync(p => p.Id == _data.MonthlyPlanId);
        var startDate = new DateTime(2026, 8, 17);

        var quote = await _service.GetQuoteAsync(Request(startDate: startDate));

        quote.StartDate.Should().Be(startDate);
        quote.EndDate.Should().Be(plan.CalculateEndDate(startDate).Date);
        quote.EndDate.Should().Be(new DateTime(2026, 9, 16));
        quote.TotalDays.Should().Be(plan.TotalDays);
    }

    [Fact(DisplayName = "S-01 An annual plan quote ends the day before the anniversary")]
    public async Task GetQuote_ForAnAnnualPlan_UsesTheYearlyEndDate()
    {
        var quote = await _service.GetQuoteAsync(
            Request(planId: _data.AnnualPlanId, discount: 0m, startDate: new DateTime(2026, 8, 17)));

        quote.EndDate.Should().Be(new DateTime(2027, 8, 16));
        quote.TotalDays.Should().Be(365);
    }

    [Fact(DisplayName = "S-01 The quoted start date is normalised to midnight")]
    public async Task GetQuote_NormalisesTheStartDate()
    {
        var quote = await _service.GetQuoteAsync(Request(startDate: new DateTime(2026, 8, 17, 14, 25, 33)));

        quote.StartDate.Should().Be(new DateTime(2026, 8, 17));
    }

    // ------------------------------------------------------- money hygiene

    [Fact(DisplayName = "S-01 Every quoted amount is a decimal, never a float or a double")]
    public async Task GetQuote_EveryAmountIsADecimal()
    {
        var quote = await _service.GetQuoteAsync(Request());

        foreach (var (name, value) in Amounts(quote))
            value.Should().BeOfType(typeof(decimal), $"{name} must be exact money");
    }

    [Fact(DisplayName = "S-01 Every quoted amount has at most two decimal places")]
    public async Task GetQuote_EveryAmountIsRoundedToTwoDecimalPlaces()
    {
        // A 7.5% tax on an odd base is the sort of rate that produces long fractions.
        var oddPlan = new MembershipPlan
        {
            PlanCode = "PLN-ODD",
            Name = "Odd rate",
            DurationType = GymManagement.Domain.Enums.PlanDurationType.Month,
            DurationValue = 1,
            Price = 3333.33m,
            RegistrationFee = 111.11m,
            TaxPercent = 7.5m,
            MaxDiscountPercent = 100m
        };
        _db.MembershipPlans.Add(oddPlan);
        await _db.SaveChangesAsync();

        var quote = await _service.GetQuoteAsync(Request(planId: oddPlan.Id, discount: 123.45m));

        foreach (var (name, value) in Amounts(quote))
            decimal.Round(value, 2).Should().Be(value, $"{name} must be rounded to 2dp, but was {value}");
    }

    [Fact(DisplayName = "S-01 Tax is rounded away from zero at the half paise")]
    public async Task GetQuote_RoundsTaxAwayFromZero()
    {
        var plan = new MembershipPlan
        {
            PlanCode = "PLN-ROUND",
            Name = "Rounding",
            DurationType = GymManagement.Domain.Enums.PlanDurationType.Month,
            DurationValue = 1,
            Price = 100.10m,
            RegistrationFee = 0m,
            TaxPercent = 5m,
            MaxDiscountPercent = 100m
        };
        _db.MembershipPlans.Add(plan);
        await _db.SaveChangesAsync();

        var quote = await _service.GetQuoteAsync(Request(planId: plan.Id, discount: 0m));

        // 100.10 * 5% = 5.005 -> 5.01 away from zero
        quote.TaxAmount.Should().Be(5.01m);
        quote.FinalAmount.Should().Be(105.11m);
    }

    [Fact(DisplayName = "S-01 No quoted amount is ever negative")]
    public async Task GetQuote_NeverProducesANegativeAmount()
    {
        var quote = await _service.GetQuoteAsync(Request(discount: 450m));

        foreach (var (name, value) in Amounts(quote))
            value.Should().BeGreaterThanOrEqualTo(0m, $"{name} must never go negative");
    }

    // ------------------------------------------------------------- lookups

    [Fact(DisplayName = "S-01 The quote echoes the plan it priced")]
    public async Task GetQuote_EchoesThePlan()
    {
        var quote = await _service.GetQuoteAsync(Request());

        quote.MembershipPlanId.Should().Be(_data.MonthlyPlanId);
        quote.PlanName.Should().Be("Monthly");
    }

    [Fact(DisplayName = "Quoting an unknown plan is a not-found error")]
    public async Task GetQuote_ForAnUnknownPlan_ThrowsNotFound()
    {
        var act = () => _service.GetQuoteAsync(Request(planId: 999_999));

        await act.Should().ThrowAsync<NotFoundAppException>();
    }

    [Fact(DisplayName = "A quote never writes anything to the database")]
    public async Task GetQuote_IsSideEffectFree()
    {
        await _service.GetQuoteAsync(Request());

        (await _db.Subscriptions.CountAsync()).Should().Be(0);
        (await _db.Payments.CountAsync()).Should().Be(0);
        _audit.Entries.Should().BeEmpty();
        _notifications.Raised.Should().BeEmpty();
    }

    private static IEnumerable<(string Name, decimal Value)> Amounts(SubscriptionQuoteDto quote) =>
        new (string, decimal)[]
        {
            (nameof(quote.PlanAmount), quote.PlanAmount),
            (nameof(quote.RegistrationFee), quote.RegistrationFee),
            (nameof(quote.ProratedCredit), quote.ProratedCredit),
            (nameof(quote.DiscountAmount), quote.DiscountAmount),
            (nameof(quote.MaxAllowedDiscount), quote.MaxAllowedDiscount),
            (nameof(quote.TaxPercent), quote.TaxPercent),
            (nameof(quote.TaxAmount), quote.TaxAmount),
            (nameof(quote.FinalAmount), quote.FinalAmount)
        };
}
