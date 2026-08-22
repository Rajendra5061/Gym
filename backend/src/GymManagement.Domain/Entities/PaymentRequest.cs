using GymManagement.Domain.Common;
using GymManagement.Domain.Enums;

namespace GymManagement.Domain.Entities;

/// <summary>
/// A UPI payment request the front desk sends to a member's phone: an SMS with a public link
/// that opens the member's UPI app of choice, pre-filled to pay the gym's UPI id.
///
/// The row exists so the link can be short and tamper-proof — the SMS carries only an opaque
/// token; the amount, payee and note live here and are served read-only to whoever opens it.
///
/// With a payment gateway configured the request also carries the gateway order created for it
/// and the id of the pending <see cref="Payment"/> the webhook will settle, so the transfer
/// reconciles itself: the member pays, the gateway reports it, and the payment (and any renewal
/// the request was selling) completes with no operator involved. Without a gateway the row
/// behaves exactly as before — a static link the front desk verifies by hand.
/// </summary>
public class PaymentRequest : BaseEntity
{
    /// <summary>Opaque, URL-safe id carried in the SMS link. Unique.</summary>
    public string Token { get; set; } = string.Empty;

    public int MemberId { get; set; }
    public Member? Member { get; set; }

    /// <summary>The subscription the request is against, when it is a due balance. Renewal
    /// requests carry none — the term they sell does not exist until the money arrives.</summary>
    public int? SubscriptionId { get; set; }
    public Subscription? Subscription { get; set; }

    public decimal Amount { get; set; }

    /// <summary>Short human line shown on the pay page and inside the UPI app ("Monthly renewal").</summary>
    public string? Note { get; set; }

    /// <summary>UPI transaction reference (tr), fixed at creation so retries reconcile as one.</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>Whether the SMS actually left; false means the operator shared the link by hand.</summary>
    public bool SmsSent { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// Where the request is in its life. Pending until the gateway settles (Paid) or reports a
    /// failed collection (Failed), or until the validity window lapses (Expired). Time-driven
    /// flips are applied lazily when the link is read, so a Pending row past its window is
    /// already effectively Expired.
    /// </summary>
    public PaymentRequestStatus Status { get; set; } = PaymentRequestStatus.Pending;

    /// <summary>The gateway's order id, when an order was created for this request.</summary>
    public string? OrderId { get; set; }

    /// <summary>Provider slug of the gateway that issued <see cref="OrderId"/>.</summary>
    public string? GatewayProvider { get; set; }

    /// <summary>
    /// QR material for the gateway order. A value starting <c>image:</c> is a hosted QR image
    /// URL; anything else is a raw payload the pay page encodes itself.
    /// </summary>
    public string? QrData { get; set; }

    /// <summary>The gateway's hosted checkout page for the order, when it offers one.</summary>
    public string? PaymentUrl { get; set; }

    /// <summary>
    /// The pending payment created alongside the request — the row the gateway webhook settles.
    /// Null when no UPI payment method existed to record it against, in which case the link
    /// still works as a static collection the front desk verifies by hand.
    /// </summary>
    public int? PaymentId { get; set; }
    public Payment? Payment { get; set; }

    /// <summary>
    /// The plan a RENEWAL request sells. Null means the request collects a due balance instead.
    /// On settlement the plan is sold as a new term through the subscription service.
    /// </summary>
    public int? MembershipPlanId { get; set; }
    public MembershipPlan? MembershipPlan { get; set; }

    /// <summary>When the gateway settled the payment behind this request.</summary>
    public DateTime? PaidAtUtc { get; set; }
}
