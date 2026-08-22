namespace GymManagement.Application.Interfaces;

/// <summary>
/// What the application asks a checkout gateway for: one collect order for one payment.
///
/// <see cref="Reference"/> is the join key to everything that already exists. It is the same
/// reference the UPI intent minted (<c>PaymentRequest.Reference</c>, stamped onto
/// <c>Payment.TransactionReference</c>), it travels to the gateway inside the order, and the
/// gateway quotes it back in the webhook — which is how <c>PaymentWebhookService</c> finds the
/// payment to settle. Creating an order therefore moves no money and changes no payment; it only
/// gives the gateway a ledger entry that the existing settlement engine can later match by this
/// reference, amount first.
/// </summary>
public sealed record GatewayOrderRequest(
    string Reference, decimal Amount, string Currency, string Description,
    string? CustomerName, string? CustomerPhone, DateTime ExpiresAtUtc);

/// <summary>
/// The gateway's answer to an order request.
///
/// <see cref="QrData"/> is something to put in front of the member, and it comes in exactly two
/// forms distinguished by one convention: a value beginning <c>image:</c> is a gateway-hosted QR
/// image URL (Razorpay serves its dynamic QRs as hosted PNGs) for the client to display as-is;
/// any other value is a raw payload — typically a <c>upi://pay?…</c> intent — for the client to
/// encode into a QR itself. <see cref="PaymentUrl"/> is a gateway-hosted checkout link, for
/// providers that render their own payment page instead of handing back a QR.
///
/// <see cref="Created"/> false with a <see cref="Detail"/> means the order was deliberately not
/// placed — no provider configured — and the manual flow carries on exactly as before.
/// </summary>
public sealed record GatewayOrderResult(
    bool Created, string? OrderId, string? QrData, string? PaymentUrl, string? Detail)
{
    public static GatewayOrderResult Skipped(string detail) => new(false, null, null, null, detail);
}

/// <summary>
/// The outbound half of automatic payment collection: asks a provider to open an order / dynamic
/// QR for one payment. The inbound half — the signed webhook that reports the money arrived — is
/// <see cref="IPaymentWebhookService"/>, and the two halves meet only on the payment reference.
///
/// Implementations are configuration-driven and provider-shaped (simulator in Development, a real
/// gateway when credentials are supplied, a null client otherwise). With <see cref="IsEnabled"/>
/// false the caller skips order creation entirely and the static-UPI manual flow is untouched.
/// </summary>
public interface IPaymentGatewayClient
{
    bool IsEnabled { get; }
    string Provider { get; }
    Task<GatewayOrderResult> CreateOrderAsync(GatewayOrderRequest request, CancellationToken ct = default);
}
