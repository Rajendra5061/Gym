using FluentAssertions;
using GymManagement.Infrastructure.Common;
using Xunit;

namespace GymManagement.UnitTests.Common;

/// <summary>
/// The Indian-numbering amount-in-words renderer printed on every receipt (supports case Y-12).
/// </summary>
public class NumberToWordsTests
{
    [Theory(DisplayName = "Y-12 ToWords renders the amount in Indian numbering with rupees and paise")]
    [InlineData(0, "Zero Rupees Only")]
    [InlineData(1, "One Rupee Only")]
    [InlineData(0.50, "Zero Rupees and Fifty Paise Only")]
    [InlineData(1200.50, "One Thousand Two Hundred Rupees and Fifty Paise Only")]
    [InlineData(100000, "One Lakh Rupees Only")]
    [InlineData(10000000, "One Crore Rupees Only")]
    [InlineData(3304.00, "Three Thousand Three Hundred Four Rupees Only")]
    [InlineData(2500, "Two Thousand Five Hundred Rupees Only")]
    [InlineData(19, "Nineteen Rupees Only")]
    [InlineData(20, "Twenty Rupees Only")]
    [InlineData(99, "Ninety Nine Rupees Only")]
    [InlineData(101, "One Hundred One Rupees Only")]
    public void ToWords_RendersTheExpectedText(decimal amount, string expected)
    {
        NumberToWords.ToWords(amount).Should().Be(expected);
    }

    [Theory(DisplayName = "Y-12 Paise are rounded away from zero to two decimal places")]
    [InlineData(1200.505, "One Thousand Two Hundred Rupees and Fifty One Paise Only")]
    [InlineData(1200.504, "One Thousand Two Hundred Rupees and Fifty Paise Only")]
    [InlineData(0.005, "Zero Rupees and One Paisa Only")]
    [InlineData(0.004, "Zero Rupees Only")]
    public void ToWords_RoundsThePaiseAwayFromZero(decimal amount, string expected)
    {
        NumberToWords.ToWords(amount).Should().Be(expected);
    }

    [Fact(DisplayName = "Y-12 A fraction that rounds up to a whole rupee carries into the rupee part")]
    public void ToWords_FractionRoundingUpToOne_CarriesIntoTheWholePart()
    {
        NumberToWords.ToWords(0.999m).Should().Be("One Rupee Only");
    }

    [Fact(DisplayName = "Y-12 A fraction that rounds up never prints 'Hundred Paise'")]
    public void ToWords_FractionRoundingUp_NeverPrintsAnInvalidPaiseValue()
    {
        NumberToWords.ToWords(1200.999m).Should().Be("One Thousand Two Hundred One Rupees Only");
    }

    [Fact(DisplayName = "A crore amount combines every Indian numbering group")]
    public void ToWords_LargeMixedAmount_CombinesEveryGroup()
    {
        NumberToWords.ToWords(12345678.90m)
            .Should().Be("One Crore Twenty Three Lakh Forty Five Thousand Six Hundred Seventy Eight Rupees and Ninety Paise Only");
    }

    [Fact(DisplayName = "A negative amount is prefixed with Minus")]
    public void ToWords_NegativeAmount_IsPrefixedWithMinus()
    {
        NumberToWords.ToWords(-5.25m).Should().Be("Minus Five Rupees and Twenty Five Paise Only");
    }

    [Fact(DisplayName = "A negative amount that rounds to zero is not prefixed with Minus")]
    public void ToWords_NegativeAmountRoundingToZero_HasNoMinus()
    {
        NumberToWords.ToWords(-0.004m).Should().Be("Zero Rupees Only");
    }

    [Fact(DisplayName = "The currency and fraction names are configurable for non-INR gyms")]
    public void ToWords_WithCustomCurrencyNames_UsesThem()
    {
        NumberToWords.ToWords(12.34m, "Dollars", "Cents")
            .Should().Be("Twelve Dollars and Thirty Four Cents Only");
    }

    [Fact(DisplayName = "Blank currency names are dropped rather than producing double spaces")]
    public void ToWords_WithBlankCurrencyNames_OmitsTheUnit()
    {
        NumberToWords.ToWords(5m, string.Empty, string.Empty).Should().Be("Five Only");
    }

    [Fact(DisplayName = "The output never contains a double space")]
    public void ToWords_NeverContainsADoubleSpace()
    {
        foreach (var amount in new[] { 0m, 1m, 100m, 1000m, 100000m, 10000000m, 1200.50m, 12345678.90m })
            NumberToWords.ToWords(amount).Should().NotContain("  ", $"amount {amount} must render cleanly");
    }
}
