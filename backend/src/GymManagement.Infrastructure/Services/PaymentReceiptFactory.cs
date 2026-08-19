using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Infrastructure.Common;
using GymManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GymManagement.Infrastructure.Services;

/// <summary>
/// Builds the printable receipt model for a payment.
///
/// It lives outside <see cref="PaymentService"/> so that the receipt mailer can build the same
/// model without depending on the payment service — which depends on the mailer in turn, and would
/// otherwise close a dependency cycle. Both call in here, so the printed receipt, the emailed
/// receipt and the JSON receipt are always assembled from one query.
/// </summary>
internal static class PaymentReceiptFactory
{
    /// <summary>Builds the receipt, or returns null when no such payment is visible.</summary>
    public static async Task<PaymentReceiptDto?> TryBuildAsync(
        GymDbContext db, ISettingsService settings, int paymentId, CancellationToken ct)
    {
        var row = await db.Payments
            .AsNoTracking()
            .Where(p => p.Id == paymentId)
            .Select(p => new
            {
                Payment = p,
                MemberCode = p.Member != null ? p.Member.MemberCode : string.Empty,
                MemberName = p.Member != null ? p.Member.FullName : string.Empty,
                MemberPhone = p.Member != null ? p.Member.Phone : null,

                // The subscription's plan wins where there is one; the plan recorded directly on
                // the payment covers money collected without a subscription being created.
                SubscriptionPlanName = p.Subscription != null && p.Subscription.MembershipPlan != null
                    ? p.Subscription.MembershipPlan.Name
                    : null,
                RecordedPlanName = p.MembershipPlan != null ? p.MembershipPlan.Name : null,

                StartDate = p.Subscription != null ? p.Subscription.StartDate : (DateTime?)null,
                EndDate = p.Subscription != null ? p.Subscription.EndDate : (DateTime?)null,
                SubscriptionFinal = p.Subscription != null ? p.Subscription.FinalAmount : 0m,
                SubscriptionPaid = p.Subscription != null ? p.Subscription.PaidAmount : 0m,
                HasSubscription = p.Subscription != null,
                IsRenewal = p.Subscription != null && p.Subscription.IsRenewal,
                MethodName = p.PaymentMethod != null ? p.PaymentMethod.Name : string.Empty,
                CollectedByName = p.CollectedByUser != null ? p.CollectedByUser.FullName : null
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (row is null) return null;

        var gym = await settings.GetGymSettingsAsync(ct).ConfigureAwait(false);
        var payment = row.Payment;

        var isRupees = string.Equals(gym.CurrencyCode, "INR", StringComparison.OrdinalIgnoreCase);
        var currencyName = isRupees ? "Rupees" : gym.CurrencyCode;
        var fractionName = isRupees ? "Paise" : "Cents";

        return new PaymentReceiptDto
        {
            ReceiptNumber = payment.ReceiptNumber,
            PaymentDate = payment.PaymentDate,

            GymName = gym.GymName,
            GymAddress = ComposeAddress(gym),
            GymPhone = gym.Phone,
            GymEmail = gym.Email,
            GymLogoPath = gym.LogoPath,
            TaxNumber = gym.TaxNumber,
            CurrencySymbol = string.IsNullOrWhiteSpace(gym.CurrencySymbol) ? "₹" : gym.CurrencySymbol,
            FooterText = gym.ReceiptFooterText,

            MemberCode = row.MemberCode,
            MemberName = row.MemberName,
            MemberPhone = row.MemberPhone,
            // A membership sold for the first time reads differently from one continued, and the
            // subscription already records which it was.
            Purpose = !row.HasSubscription
                ? ReceiptPurpose.Payment
                : row.IsRenewal ? ReceiptPurpose.Renewal : ReceiptPurpose.NewMembership,


            PlanName = string.IsNullOrWhiteSpace(row.SubscriptionPlanName)
                ? row.RecordedPlanName
                : row.SubscriptionPlanName,
            SubscriptionStartDate = row.StartDate,
            SubscriptionEndDate = row.EndDate,

            Amount = payment.Amount,
            DiscountAmount = payment.DiscountAmount,
            TaxAmount = payment.TaxAmount,
            FinalAmount = payment.FinalAmount,
            PaidAmount = row.HasSubscription
                ? row.SubscriptionPaid
                : payment.FinalAmount - payment.RefundedAmount,
            OutstandingAmount = row.HasSubscription
                ? Round(Math.Max(0m, row.SubscriptionFinal - row.SubscriptionPaid))
                : 0m,
            AmountInWords = NumberToWords.ToWords(payment.FinalAmount, currencyName, fractionName),

            PaymentMethodName = row.MethodName,
            TransactionReference = payment.TransactionReference,
            StatusText = payment.Status.ToString(),
            CollectedByName = row.CollectedByName,
            Notes = payment.Notes
        };
    }

    /// <summary>File name for a downloaded or attached receipt PDF.</summary>
    public static string PdfFileName(string? receiptNumber) =>
        $"Receipt_{SanitizeFileToken(receiptNumber)}.pdf";

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string? ComposeAddress(GymSettingsDto gym)
    {
        var parts = new[] { gym.Address, gym.City, gym.State, gym.PostalCode, gym.Country }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim());

        var address = string.Join(", ", parts);
        return string.IsNullOrWhiteSpace(address) ? null : address;
    }

    private static string SanitizeFileToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Payment";

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "Payment" : cleaned;
    }
}
