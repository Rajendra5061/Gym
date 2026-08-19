using GymManagement.Domain.Common;
using GymManagement.Domain.Enums;

namespace GymManagement.Domain.Entities;

/// <summary>
/// One webhook delivery from a payment gateway, recorded after its signature was verified.
///
/// This table is the idempotency ledger. <c>(Provider, EventId)</c> carries a unique index, and the
/// row is inserted <b>before</b> the payment is touched, so the database — not application logic —
/// decides which of two simultaneous deliveries of the same event gets to settle the payment. The
/// loser's insert fails with a duplicate-key violation and is answered 200 without changing
/// anything, which is what makes the gateway stop retrying.
///
/// The payload itself is never stored: it may name the payer. Only a SHA-256 digest of the exact
/// bytes that were signature-checked is kept, so a disputed settlement can be tied back to the
/// delivery that caused it.
/// </summary>
public class PaymentGatewayEvent : BaseEntity
{
    /// <summary>Gateway slug taken from the webhook route, e.g. <c>simulator</c>.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The gateway's own id for this event. Unique per provider.</summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>Provider event name, e.g. <c>payment.captured</c>.</summary>
    public string? EventType { get; set; }

    /// <summary>The reference the UPI intent minted and the gateway echoed back.</summary>
    public string? PaymentReference { get; set; }

    /// <summary>Amount the gateway says it collected, in major units.</summary>
    public decimal? Amount { get; set; }

    public string? Currency { get; set; }

    /// <summary>Raw status word from the payload, kept verbatim for support.</summary>
    public string? GatewayStatus { get; set; }

    /// <summary>The gateway's own transaction id / UTR, when the payload carried one.</summary>
    public string? GatewayTransactionId { get; set; }

    /// <summary>What the application did about the event.</summary>
    public PaymentGatewayEventOutcome Outcome { get; set; } = PaymentGatewayEventOutcome.Received;

    /// <summary>The payment this event settled or was matched against, when one was found.</summary>
    public int? PaymentId { get; set; }
    public Payment? Payment { get; set; }

    /// <summary>Lowercase hex SHA-256 of the exact request body whose signature was verified.</summary>
    public string? PayloadDigest { get; set; }

    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAtUtc { get; set; }

    /// <summary>Human-readable note: why it was rejected, what it settled.</summary>
    public string? Detail { get; set; }
}
