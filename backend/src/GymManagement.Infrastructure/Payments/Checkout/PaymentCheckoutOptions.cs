namespace GymManagement.Infrastructure.Payments.Checkout;

/// <summary>
/// Configuration for outbound order creation — the "ask the gateway to collect" half of automatic
/// payments, bound from the <c>Payments:Checkout</c> section. The shape follows <c>SmsOptions</c>:
/// provider selection plus per-provider settings.
///
/// Credentials come from environment variables or user secrets only — for example
/// <c>Payments__Checkout__Razorpay__KeySecret</c> or
/// <c>dotnet user-secrets set "Payments:Checkout:Cashfree:SecretKey" …</c> — and never from the
/// database: <c>GymSettings</c> is readable by every administrator over the API, and an API key
/// kept there would be an API key handed to every admin account.
///
/// This section is deliberately separate from <c>Payments:Gateway</c>, which configures the
/// inbound webhook. A gym can verify webhooks without creating orders (static QR plus
/// reconciliation) or run the Development simulator end to end; the two halves only meet on the
/// payment reference.
/// </summary>
public sealed class PaymentCheckoutOptions
{
    public const string SectionName = "Payments:Checkout";

    /// <summary>
    /// <c>none</c>, <c>simulator</c>, <c>razorpay</c> or <c>cashfree</c>. Left unset it resolves
    /// to <c>simulator</c> in Development and <c>none</c> everywhere else — the resolution is done
    /// by <see cref="PaymentGatewayClientFactory"/>, so order creation is off in production until
    /// somebody deliberately turns it on.
    /// </summary>
    public string? Provider { get; set; }

    public RazorpayCheckoutOptions? Razorpay { get; set; }

    public CashfreeCheckoutOptions? Cashfree { get; set; }

    /// <summary>
    /// How long a created order stays payable, in minutes. The caller derives
    /// <c>GatewayOrderRequest.ExpiresAtUtc</c> from this so the gateway's expiry and the local
    /// payment-request expiry tell the member the same thing.
    /// </summary>
    public int OrderValidMinutes { get; set; } = 30;
}

/// <summary>
/// Razorpay Orders + dynamic QR. <see cref="KeyId"/> / <see cref="KeySecret"/> are the dashboard
/// API key pair; supply them via environment variables or user secrets and never commit them.
/// </summary>
public sealed class RazorpayCheckoutOptions
{
    public string? KeyId { get; set; }

    public string? KeySecret { get; set; }

    /// <summary>Sandbox and live share one host; test keys select test mode.</summary>
    public string BaseUrl { get; set; } = "https://api.razorpay.com";
}

/// <summary>
/// Cashfree Payment Gateway orders. <see cref="AppId"/> / <see cref="SecretKey"/> are the merchant
/// credentials; supply them via environment variables or user secrets and never commit them. The
/// default <see cref="BaseUrl"/> is the sandbox — production is <c>https://api.cashfree.com/pg</c>.
/// </summary>
public sealed class CashfreeCheckoutOptions
{
    public string? AppId { get; set; }

    public string? SecretKey { get; set; }

    public string BaseUrl { get; set; } = "https://sandbox.cashfree.com/pg";

    /// <summary>Cashfree versions its API by date header; this is the one this adapter speaks.</summary>
    public string ApiVersion { get; set; } = "2023-08-01";
}
