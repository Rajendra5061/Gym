namespace GymManagement.Domain.Enums;

/// <summary>What the application did with a verified gateway webhook event.</summary>
public enum PaymentGatewayEventOutcome
{
    /// <summary>Signature verified, claim row written, processing not finished yet.</summary>
    Received = 0,

    /// <summary>The matching payment was moved to Paid by this event.</summary>
    Settled = 1,

    /// <summary>The payment was already Paid; nothing changed.</summary>
    AlreadySettled = 2,

    /// <summary>No live payment carries the reference the gateway quoted.</summary>
    PaymentNotFound = 3,

    /// <summary>The gateway's amount is not the amount that was requested. Never reconciled.</summary>
    AmountMismatch = 4,

    /// <summary>A failure/decline event, recorded against the payment without settling it.</summary>
    FailureRecorded = 5,

    /// <summary>A status the application has no rule for. Recorded and ignored.</summary>
    Ignored = 6,

    /// <summary>Matched a payment that cannot be settled (cancelled, refunded, failed).</summary>
    Rejected = 7
}
