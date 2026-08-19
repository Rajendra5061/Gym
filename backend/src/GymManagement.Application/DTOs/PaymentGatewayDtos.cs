namespace GymManagement.Application.DTOs;

/// <summary>
/// A webhook delivery as it arrives at the API: the provider slug from the route, the exact bytes
/// of the request body, and the headers the signature check needs.
///
/// <see cref="RawBody"/> is the untouched body. Signature verification is defined over those bytes,
/// so the API reads them before any model binding can re-serialise, re-order or re-encode the JSON.
/// </summary>
public sealed record PaymentWebhookRequestDto(
    string Provider,
    byte[] RawBody,
    string? Signature,
    string? Timestamp,
    string? SourceIp);

/// <summary>What happened to a webhook delivery. Maps one-to-one onto the HTTP status returned.</summary>
public enum PaymentWebhookStatus
{
    /// <summary>No gateway is configured for this provider. The endpoint is inert.</summary>
    NotConfigured = 0,

    /// <summary>Missing, malformed or mismatched signature. Nothing was read or written.</summary>
    InvalidSignature = 1,

    /// <summary>The signed timestamp is outside the tolerance window — a replay of an old capture.</summary>
    StaleEvent = 2,

    /// <summary>The signature verified but the payload is not usable.</summary>
    Malformed = 3,

    /// <summary>This event id was already processed. Nothing changed.</summary>
    Duplicate = 4,

    /// <summary>The payment moved from AwaitingConfirmation to Paid.</summary>
    Settled = 5,

    /// <summary>The payment was already Paid. Nothing changed.</summary>
    AlreadySettled = 6,

    /// <summary>A failure event, recorded against the payment without settling it.</summary>
    FailureRecorded = 7,

    /// <summary>No live payment carries the quoted reference. Recorded for a human to look at.</summary>
    PaymentNotFound = 8,

    /// <summary>The gateway's amount is not what was requested. Refused, never reconciled.</summary>
    AmountMismatch = 9,

    /// <summary>Verified, but the payment cannot be settled (cancelled, refunded, failed).</summary>
    Rejected = 10,

    /// <summary>A verified event the application has no rule for. Recorded and ignored.</summary>
    Ignored = 11
}

/// <summary>The API's answer to a webhook delivery. Carries nothing the gateway did not send.</summary>
public sealed class PaymentWebhookResultDto
{
    public PaymentWebhookStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>The gateway's event id, echoed so its delivery log can be correlated.</summary>
    public string? EventId { get; set; }

    public string? PaymentReference { get; set; }
    public int? PaymentId { get; set; }
    public string? ReceiptNumber { get; set; }

    /// <summary>True when this exact event had already been processed.</summary>
    public bool Duplicate { get; set; }
}

/// <summary>A verified instruction from a gateway to settle one payment.</summary>
public sealed class GatewaySettlementDto
{
    /// <summary>Gateway slug, recorded on the audit entry in place of a user.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The gateway's event id, for the audit trail.</summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>The reference the UPI intent minted, quoted back by the gateway.</summary>
    public string PaymentReference { get; set; } = string.Empty;

    /// <summary>What the gateway says it collected. Must equal the payment's final amount.</summary>
    public decimal Amount { get; set; }

    public string? GatewayTransactionId { get; set; }
    public string? PayerVpa { get; set; }
}

/// <summary>Outcome of <c>IPaymentService.SettleFromGatewayAsync</c>.</summary>
public enum GatewaySettlementStatus
{
    Settled = 0,
    AlreadySettled = 1,
    PaymentNotFound = 2,
    AmountMismatch = 3,

    /// <summary>The payment exists but is in a status that cannot be settled.</summary>
    NotSettleable = 4
}

public sealed class GatewaySettlementResultDto
{
    public GatewaySettlementStatus Status { get; set; }
    public int? PaymentId { get; set; }
    public string? ReceiptNumber { get; set; }
    public decimal? ExpectedAmount { get; set; }
    public string? Detail { get; set; }
}

/// <summary>
/// Whether automatic reconciliation is wired up, and where the gateway should be told to post.
///
/// The signing secret is deliberately not a member of this type and never will be:
/// <see cref="SigningSecretConfigured"/> reports only that one is present, which is all an operator
/// needs to see on a screen. Everything here is read from configuration — appsettings, environment
/// variables or user secrets — and never from the gym settings table, which any administrator can
/// read back over the API.
/// </summary>
public sealed class PaymentGatewayStatusDto
{
    /// <summary>True only when the feature is on <i>and</i> a usable secret resolved.</summary>
    public bool IsConfigured { get; set; }

    /// <summary>The pinned provider slug, or null when the secret serves whatever slug arrives.</summary>
    public string? Provider { get; set; }

    /// <summary><c>Test</c> / <c>Live</c>, when the operator has said which.</summary>
    public string? Mode { get; set; }

    /// <summary>Absolute URL to hand the provider. Composed by the API from the request it answered.</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>Server-relative path, for when the public origin is not knowable from here.</summary>
    public string? WebhookPath { get; set; }

    public bool SigningSecretConfigured { get; set; }

    /// <summary>Mirrors <see cref="UpiPaymentIntentDto.RequiresManualVerification"/>.</summary>
    public bool RequiresManualVerification { get; set; }

    /// <summary>When a delivery was last accepted, so an operator can see the gateway is alive.</summary>
    public DateTime? LastEventAtUtc { get; set; }

    /// <summary>One sentence describing the state, for a screen to show without composing its own.</summary>
    public string? Message { get; set; }
}
