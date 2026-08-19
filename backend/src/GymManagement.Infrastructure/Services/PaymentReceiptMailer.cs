using GymManagement.Application.Common;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Services;

/// <summary>
/// Emails a payment receipt to the member it belongs to.
///
/// Three rules govern everything here:
/// <list type="number">
///   <item>
///     <b>The money comes first.</b> This runs only after the payment transaction has committed,
///     and <see cref="SendReceiptAsync"/> never throws. A dead mail server, an unwritable folder or
///     a malformed address is logged and dropped; the payment is already recorded either way.
///   </item>
///   <item>
///     <b>At most one receipt per payment.</b> <c>Payment.ReceiptEmailedAtUtc</c> is read before
///     sending and stamped after a successful send, so replaying the collection flow — a retried
///     request, a create-then-confirm sequence, a restore — cannot email the member twice.
///   </item>
///   <item>
///     <b>Nothing sensitive travels.</b> The message is rendered from the receipt model, which
///     carries amounts, the payment method name and the operator-supplied transaction reference.
///     No card number, CVV, UPI PIN or password exists anywhere in that model, and none is read
///     here. Payer VPA is deliberately left out as well.
///   </item>
/// </list>
/// </summary>
public sealed class PaymentReceiptMailer : IPaymentReceiptMailer
{
    /// <summary>
    /// Statuses whose receipt is worth sending. An unconfirmed payment gets its receipt when it is
    /// confirmed, so the member is never emailed a receipt for money the gym has not accepted yet.
    /// </summary>
    private static readonly PaymentStatus[] SendableStatuses =
    {
        PaymentStatus.Paid, PaymentStatus.PartiallyRefunded
    };

    private readonly GymDbContext _db;
    private readonly ISettingsService _settings;
    private readonly IPdfExportService _pdf;
    private readonly IReceiptEmailBuilder _content;
    private readonly IEmailSender _email;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<PaymentReceiptMailer> _logger;

    public PaymentReceiptMailer(
        GymDbContext db,
        ISettingsService settings,
        IPdfExportService pdf,
        IReceiptEmailBuilder content,
        IEmailSender email,
        IDateTimeProvider clock,
        ILogger<PaymentReceiptMailer> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _pdf = pdf ?? throw new ArgumentNullException(nameof(pdf));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ReceiptEmailOutcome> SendReceiptAsync(int paymentId, CancellationToken ct = default)
    {
        // Cheapest check first: with no provider configured there is no reason to touch the
        // database or render a PDF.
        if (!_email.IsEnabled) return ReceiptEmailOutcome.Disabled;

        try
        {
            var payment = await _db.Payments
                .FirstOrDefaultAsync(p => p.Id == paymentId, ct)
                .ConfigureAwait(false);

            if (payment is null)
            {
                _logger.LogDebug("No receipt emailed: payment {PaymentId} no longer exists.", paymentId);
                return ReceiptEmailOutcome.PaymentNotFound;
            }

            // The send-once guard. Read before any work is done, so a repeat call costs one query.
            if (payment.ReceiptEmailedAtUtc.HasValue)
            {
                _logger.LogDebug(
                    "Receipt for payment {PaymentId} ({Receipt}) was already emailed at {SentAt:u}; not sending again.",
                    payment.Id, payment.ReceiptNumber, payment.ReceiptEmailedAtUtc.Value);

                return ReceiptEmailOutcome.AlreadySent;
            }

            if (!SendableStatuses.Contains(payment.Status))
            {
                _logger.LogDebug(
                    "No receipt emailed for payment {PaymentId}: status is {Status}; it will go out on confirmation.",
                    payment.Id, payment.Status);

                return ReceiptEmailOutcome.NotSettled;
            }

            var recipient = await _db.Members
                .AsNoTracking()
                .Where(m => m.Id == payment.MemberId)
                .Select(m => m.Email)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(recipient))
            {
                _logger.LogDebug(
                    "No receipt emailed for payment {PaymentId}: member {MemberId} has no email address on file.",
                    payment.Id, payment.MemberId);

                return ReceiptEmailOutcome.NoRecipient;
            }

            var receipt = await PaymentReceiptFactory
                .TryBuildAsync(_db, _settings, payment.Id, ct)
                .ConfigureAwait(false);

            if (receipt is null) return ReceiptEmailOutcome.PaymentNotFound;

            var rendered = _content.Build(receipt);

            var message = new EmailMessage
            {
                To = { recipient.Trim() },
                Subject = rendered.Subject,
                HtmlBody = rendered.HtmlBody,
                TextBody = rendered.TextBody,
                Attachments =
                {
                    new EmailAttachment(
                        PaymentReceiptFactory.PdfFileName(receipt.ReceiptNumber),
                        "application/pdf",
                        _pdf.ExportReceipt(receipt))
                }
            };

            var result = await _email.SendAsync(message, ct).ConfigureAwait(false);

            if (!result.WasSent)
            {
                // Nothing left the building, so the payment is left unstamped and a later attempt
                // may still succeed.
                _logger.LogInformation(
                    "Receipt for payment {PaymentId} was not sent by provider {Provider}: {Detail}",
                    payment.Id, result.Provider, result.Detail);

                return ReceiptEmailOutcome.Failed;
            }

            payment.ReceiptEmailedAtUtc = _clock.UtcNow;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Receipt {Receipt} for payment {PaymentId} emailed to {Recipient} via {Provider}.",
                payment.ReceiptNumber, payment.Id, Mask(recipient), result.Provider);

            return ReceiptEmailOutcome.Sent;
        }
        catch (Exception ex)
        {
            // Deliberately catches everything, cancellation included. The payment has committed;
            // nothing that happens while sending a receipt may be allowed to surface as a failed
            // request or to leave the caller thinking the money was not taken.
            _logger.LogWarning(ex,
                "Could not email the receipt for payment {PaymentId}. The payment itself is unaffected.",
                paymentId);

            return ReceiptEmailOutcome.Failed;
        }
    }

    /// <summary>Same masking the senders use, kept local so this class needs nothing from Messaging.</summary>
    private static string Mask(string address)
    {
        var at = address.IndexOf('@');
        return at <= 0 ? "***" : $"{address[0]}***{address[at..]}";
    }
}
