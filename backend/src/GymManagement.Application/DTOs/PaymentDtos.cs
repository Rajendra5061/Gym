using GymManagement.Application.Common;
using GymManagement.Domain.Enums;

namespace GymManagement.Application.DTOs;

public class PaymentMethodDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool RequiresReference { get; set; }
    public bool SupportsQrCode { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public class PaymentDto
{
    public int Id { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public int MemberId { get; set; }
    public string MemberCode { get; set; } = string.Empty;
    public string MemberName { get; set; } = string.Empty;
    public string? MemberPhone { get; set; }
    public int? SubscriptionId { get; set; }
    public string? SubscriptionCode { get; set; }

    /// <summary>
    /// The plan the payment was for. Taken from the linked subscription when there is one — that
    /// remains authoritative — and otherwise from the plan recorded directly on the payment, so a
    /// payment collected without a subscription still shows what it was for.
    /// </summary>
    public string? PlanName { get; set; }

    /// <summary>The plan recorded directly on the payment, when one was chosen.</summary>
    public int? MembershipPlanId { get; set; }

    public decimal Amount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal FinalAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal NetAmount => FinalAmount - RefundedAmount;

    public int PaymentMethodId { get; set; }
    public string PaymentMethodName { get; set; } = string.Empty;
    public string? TransactionReference { get; set; }
    public string? PayerVpa { get; set; }

    public DateTime PaymentDate { get; set; }
    public PaymentStatus Status { get; set; }
    public string StatusText => Status.ToString();
    public int? CollectedByUserId { get; set; }
    public string? CollectedByName { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the receipt was emailed to the member, or null when it never was (no address on file,
    /// no mail provider configured, or the send failed). Doubles as the send-once guard.
    /// </summary>
    public DateTime? ReceiptEmailedAtUtc { get; set; }

    public bool ReceiptEmailed => ReceiptEmailedAtUtc.HasValue;
}

/// <summary>Payment details supplied inline with a subscription create/renew request.</summary>
public class CollectPaymentInlineDto
{
    public int PaymentMethodId { get; set; }
    /// <summary>Amount tendered now. May be less than the total for a part payment.</summary>
    public decimal Amount { get; set; }
    public string? TransactionReference { get; set; }
    public string? PayerVpa { get; set; }
    public DateTime? PaymentDate { get; set; }
    public string? Notes { get; set; }
    /// <summary>
    /// For UPI-style methods the payment is created as AwaitingConfirmation unless this is set,
    /// in which case staff assert that the transfer was verified.
    /// </summary>
    public bool MarkConfirmed { get; set; } = true;
}

public class CreatePaymentDto
{
    public int MemberId { get; set; }
    public int? SubscriptionId { get; set; }

    /// <summary>
    /// Optional membership plan the money is being collected for. Recording it does not create or
    /// attach a subscription; it only says what the payment was for, so the payments list and the
    /// receipt can show a plan name.
    /// </summary>
    public int? MembershipPlanId { get; set; }

    public decimal Amount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public int PaymentMethodId { get; set; }
    public string? TransactionReference { get; set; }
    public string? PayerVpa { get; set; }
    public DateTime? PaymentDate { get; set; }
    public string? Notes { get; set; }
    public bool MarkConfirmed { get; set; } = true;
}

public class ConfirmPaymentDto
{
    public int PaymentId { get; set; }
    public string? TransactionReference { get; set; }
    public string? Remarks { get; set; }
}

public class PaymentQueryDto : PagedRequest
{
    public int? MemberId { get; set; }
    public int? SubscriptionId { get; set; }
    public int? PaymentMethodId { get; set; }
    public PaymentStatus? Status { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public int? CollectedByUserId { get; set; }
    public bool OnlyToday { get; set; }
    public bool IncludeDeleted { get; set; }
}

public class CreateRefundDto
{
    public int PaymentId { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? RefundMethod { get; set; }
    public string? TransactionReference { get; set; }
    public string? Remarks { get; set; }
    /// <summary>An operator with payments.refund may approve at creation time.</summary>
    public bool ApproveImmediately { get; set; }
}

public class PaymentRefundDto
{
    public int Id { get; set; }
    public string RefundNumber { get; set; } = string.Empty;
    public int PaymentId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public int MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public RefundStatus Status { get; set; }
    public string StatusText => Status.ToString();
    public DateTime RequestedAt { get; set; }
    public string? RequestedByName { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedByName { get; set; }
    public string? RefundMethod { get; set; }
    public string? TransactionReference { get; set; }
    public string? Remarks { get; set; }
}

public class ApproveRefundDto
{
    public int RefundId { get; set; }
    public bool Approve { get; set; }
    public string? Remarks { get; set; }
}

/// <summary>What a receipt is covering, which decides the wording of the covering email.</summary>
public enum ReceiptPurpose
{
    /// <summary>Money taken without a membership being sold — a balance, a day pass, an extra.</summary>
    Payment = 0,

    /// <summary>A membership sold for the first time on this subscription.</summary>
    NewMembership = 1,

    /// <summary>A membership continued for another term.</summary>
    Renewal = 2,
}

/// <summary>Printable receipt model. Rendered to PDF by the API and previewed in the client.</summary>
public class PaymentReceiptDto
{
    public string ReceiptNumber { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }

    public string GymName { get; set; } = string.Empty;
    public string? GymAddress { get; set; }
    public string? GymPhone { get; set; }
    public string? GymEmail { get; set; }
    public string? GymLogoPath { get; set; }
    public string? TaxNumber { get; set; }
    public string CurrencySymbol { get; set; } = "₹";
    public string? FooterText { get; set; }

    public string MemberCode { get; set; } = string.Empty;
    public string MemberName { get; set; } = string.Empty;
    public string? MemberPhone { get; set; }

    public string? PlanName { get; set; }
    public DateTime? SubscriptionStartDate { get; set; }
    public DateTime? SubscriptionEndDate { get; set; }

    /// <summary>
    /// What this receipt is for, so the covering email can say "welcome" to a new member and
    /// "renewed" to a returning one instead of the same neutral wording for both.
    /// </summary>
    public ReceiptPurpose Purpose { get; set; } = ReceiptPurpose.Payment;

    public decimal Amount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal FinalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public string AmountInWords { get; set; } = string.Empty;

    public string PaymentMethodName { get; set; } = string.Empty;
    public string? TransactionReference { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public string? CollectedByName { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// The rendered receipt email: subject plus the HTML body and its plain-text alternative.
/// Carries no card number, CVV, UPI PIN or password — the source
/// <see cref="PaymentReceiptDto"/> holds none of those in the first place.
/// </summary>
public sealed record ReceiptEmailContent(string Subject, string HtmlBody, string TextBody);

/// <summary>Data needed to render the UPI collection screen.</summary>
public class UpiPaymentRequestDto
{
    public int MemberId { get; set; }
    public int? SubscriptionId { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
}

public class UpiPaymentIntentDto
{
    public int MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public int? SubscriptionId { get; set; }
    public decimal Amount { get; set; }
    public string CurrencySymbol { get; set; } = "₹";
    public string? UpiId { get; set; }
    public string? PayeeName { get; set; }
    /// <summary>Standard UPI deep link (<c>upi://pay?…</c>) that the client renders as a QR code.</summary>
    public string? UpiDeepLink { get; set; }
    /// <summary>Path to the gym's static QR image, when one is configured instead.</summary>
    public string? QrImagePath { get; set; }
    /// <summary>Reference the operator should quote when reconciling the transaction.</summary>
    public string PaymentReference { get; set; } = string.Empty;
    /// <summary>
    /// True when no payment gateway is configured, so the transaction must be verified manually
    /// before the payment is marked successful.
    /// </summary>
    public bool RequiresManualVerification { get; set; } = true;
    public string Instructions { get; set; } = string.Empty;
}

public class OutstandingBalanceDto
{
    public int MemberId { get; set; }
    public string MemberCode { get; set; } = string.Empty;
    public string MemberName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public int SubscriptionId { get; set; }
    public string SubscriptionCode { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal FinalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public int DaysOverdue { get; set; }
}
