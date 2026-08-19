using System.Text;

namespace GymManagement.Infrastructure.Common;

/// <summary>
/// Spells a money amount using the Indian numbering system (Crore / Lakh / Thousand / Hundred),
/// e.g. <c>1200.50</c> becomes <c>"One Thousand Two Hundred Rupees and Fifty Paise Only"</c>.
/// Used on printed receipts, where the amount in words is the legally meaningful figure.
/// </summary>
public static class NumberToWords
{
    private const decimal Crore = 10_000_000m;
    private const decimal Lakh = 100_000m;
    private const decimal Thousand = 1_000m;
    private const decimal Hundred = 100m;

    private static readonly string[] Units =
    {
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine",
        "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen",
        "Seventeen", "Eighteen", "Nineteen"
    };

    private static readonly string[] Tens =
    {
        "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"
    };

    /// <summary>
    /// Converts <paramref name="amount"/> to words. The value is first rounded to two decimals
    /// (away from zero) so that the words always match the printed figure. Handles zero, negative
    /// values (prefixed with "Minus") and everything up to 99,99,99,999.99 and beyond.
    /// </summary>
    /// <param name="amount">Money amount to spell.</param>
    /// <param name="currencyName">Major unit name, e.g. "Rupees". Omitted when blank.</param>
    /// <param name="fractionName">Minor unit name, e.g. "Paise". Omitted when blank.</param>
    public static string ToWords(decimal amount, string currencyName = "Rupees", string fractionName = "Paise")
    {
        var rounded = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        var isNegative = rounded < 0m;
        rounded = Math.Abs(rounded);

        var whole = decimal.Truncate(rounded);
        var fraction = (int)Math.Round((rounded - whole) * 100m, 0, MidpointRounding.AwayFromZero);

        // 0.999 rounds to 1.00: carry the fraction into the whole part instead of printing "100 Paise".
        if (fraction >= 100)
        {
            whole += 1m;
            fraction = 0;
        }

        var builder = new StringBuilder();

        if (isNegative && (whole > 0m || fraction > 0)) builder.Append("Minus ");

        builder.Append(IntegerToWords(whole));
        AppendUnit(builder, currencyName);

        if (fraction > 0)
        {
            builder.Append(" and ").Append(IntegerToWords(fraction));
            AppendUnit(builder, fractionName);
        }

        builder.Append(" Only");
        return builder.ToString();
    }

    private static void AppendUnit(StringBuilder builder, string? unitName)
    {
        if (string.IsNullOrWhiteSpace(unitName)) return;
        builder.Append(' ').Append(unitName.Trim());
    }

    /// <summary>
    /// Spells a non-negative whole number. The Crore group recurses, so values larger than
    /// 99,99,99,999 still read correctly (e.g. "One Thousand Two Hundred Crore …").
    /// </summary>
    private static string IntegerToWords(decimal value)
    {
        if (value <= 0m) return Units[0];

        var parts = new List<string>(5);
        var remaining = value;

        if (remaining >= Crore)
        {
            var crores = decimal.Truncate(remaining / Crore);
            parts.Add($"{IntegerToWords(crores)} Crore");
            remaining -= crores * Crore;
        }

        // After the crore group is removed the remainder is below 10,000,000,
        // so each of the groups below is guaranteed to be in the range 1..99.
        if (remaining >= Lakh)
        {
            var lakhs = (int)decimal.Truncate(remaining / Lakh);
            parts.Add($"{BelowHundredToWords(lakhs)} Lakh");
            remaining -= lakhs * Lakh;
        }

        if (remaining >= Thousand)
        {
            var thousands = (int)decimal.Truncate(remaining / Thousand);
            parts.Add($"{BelowHundredToWords(thousands)} Thousand");
            remaining -= thousands * Thousand;
        }

        if (remaining >= Hundred)
        {
            var hundreds = (int)decimal.Truncate(remaining / Hundred);
            parts.Add($"{BelowHundredToWords(hundreds)} Hundred");
            remaining -= hundreds * Hundred;
        }

        var rest = (int)decimal.Truncate(remaining);
        if (rest > 0) parts.Add(BelowHundredToWords(rest));

        return string.Join(' ', parts);
    }

    /// <summary>Spells 1..99. Returns an empty string for zero so no stray "Zero" is emitted.</summary>
    private static string BelowHundredToWords(int value)
    {
        if (value <= 0) return string.Empty;
        if (value < 20) return Units[value];

        var tens = Tens[value / 10];
        var unit = value % 10;
        return unit == 0 ? tens : $"{tens} {Units[unit]}";
    }
}
