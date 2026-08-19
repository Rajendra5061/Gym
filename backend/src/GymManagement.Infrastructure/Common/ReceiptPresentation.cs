using System.Globalization;
using System.Text;
using GymManagement.Application.DTOs;

namespace GymManagement.Infrastructure.Common;

/// <summary>
/// Wording and number formatting shared by the printed receipt and the emailed one, so the two
/// always say the same thing. Money is formatted one way only; a status is described in words
/// rather than by colour, because the PDF has to survive a black and white printer.
/// </summary>
internal static class ReceiptPresentation
{
    public const string DateFormat = "dd MMM yyyy";
    public const string DateTimeFormat = "dd MMM yyyy, HH:mm";

    /// <summary>What the amount on this receipt was for.</summary>
    public static string LineItemDescription(PaymentReceiptDto receipt) =>
        string.IsNullOrWhiteSpace(receipt.PlanName)
            ? "Membership payment"
            : $"{receipt.PlanName!.Trim()} — membership fee";

    /// <summary>
    /// <c>₹ 3,304.00</c>. One space after the symbol keeps a long amount from crowding it, and the
    /// invariant culture keeps the grouping identical wherever the server happens to run.
    /// </summary>
    public static string Money(decimal value, string? currency)
    {
        var amount = Math.Round(value, 2, MidpointRounding.AwayFromZero)
            .ToString("N2", CultureInfo.InvariantCulture);

        return string.IsNullOrWhiteSpace(currency) ? amount : $"{currency.Trim()} {amount}";
    }

    /// <summary>A negative line (a discount) written as a deduction rather than as a minus sign.</summary>
    public static string MoneyDeducted(decimal value, string? currency) =>
        value <= 0m ? Money(0m, currency) : "- " + Money(value, currency);

    public static string MoneyAdded(decimal value, string? currency) =>
        value <= 0m ? Money(0m, currency) : "+ " + Money(value, currency);

    public static string Date(DateTime? value) =>
        value.HasValue ? value.Value.ToString(DateFormat, CultureInfo.InvariantCulture) : "—";

    public static string DateTimeStamp(DateTime value) =>
        value == default ? "—" : value.ToString(DateTimeFormat, CultureInfo.InvariantCulture);

    /// <summary>The subscription term, or an em dash when the payment is not tied to one.</summary>
    public static string Term(PaymentReceiptDto receipt) =>
        receipt.SubscriptionStartDate.HasValue || receipt.SubscriptionEndDate.HasValue
            ? $"{Date(receipt.SubscriptionStartDate)} to {Date(receipt.SubscriptionEndDate)}"
            : "—";

    /// <summary>Turns <c>AwaitingConfirmation</c> into <c>Awaiting confirmation</c>.</summary>
    public static string Humanise(string? pascalCase)
    {
        if (string.IsNullOrWhiteSpace(pascalCase)) return "—";

        var text = pascalCase.Trim();
        var builder = new StringBuilder(text.Length + 6);

        for (var i = 0; i < text.Length; i++)
        {
            if (i > 0 && char.IsUpper(text[i]) && !char.IsUpper(text[i - 1]))
            {
                builder.Append(' ').Append(char.ToLowerInvariant(text[i]));
                continue;
            }

            builder.Append(text[i]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The paid / outstanding verdict, as a short headline plus a sentence. Both the PDF and the
    /// email show it as text inside a bordered box so that a greyscale print still reads correctly.
    /// </summary>
    public static (string Headline, string Detail) Status(PaymentReceiptDto receipt)
    {
        var status = (receipt.StatusText ?? string.Empty).Trim();
        var currency = receipt.CurrencySymbol;

        if (status.Equals("Refunded", StringComparison.OrdinalIgnoreCase))
            return ("REFUNDED", "This payment has been refunded in full.");

        if (status.Equals("PartiallyRefunded", StringComparison.OrdinalIgnoreCase))
            return ("PARTIALLY REFUNDED", "Part of this payment has been refunded.");

        if (!status.Equals("Paid", StringComparison.OrdinalIgnoreCase))
        {
            return ($"PAYMENT {Humanise(status).ToUpperInvariant()}",
                "This receipt is provisional until the payment has been confirmed by the gym.");
        }

        if (receipt.OutstandingAmount > 0m)
        {
            return ("PART PAYMENT RECEIVED",
                $"A balance of {Money(receipt.OutstandingAmount, currency)} remains outstanding on this membership.");
        }

        return ("PAID IN FULL", "No balance is outstanding on this membership. Thank you.");
    }

    /// <summary>Joins the parts that are actually present, so no separator is ever left dangling.</summary>
    public static string Join(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

    public static string OrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
}
