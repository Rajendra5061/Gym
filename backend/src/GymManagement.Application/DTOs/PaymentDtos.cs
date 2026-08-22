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
    /// The plan the payment was for. Taken from the linked subscription when there is one â that
    /// remains authoritative â and otherwise from the plan recorded directly on the payment, so a
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
    /// <summary>Money taken without a membership being sold â a balance, a day pass, an extra.</summary>
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
    public string CurrencySymbol { get; set; } = "â¹";
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
/// Carries no card number, CVV, UPI PIN or password â the source
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
    public string CurrencySymbol { get; set; } = "â¹";
    public string? UpiId { get; set; }
    public string? PayeeName { get; set; }
    /// <summary>Standard UPI deep link (<c>upi://pay?â¦</c>) that the client renders as a QR code.</summary>
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

/// <summary>Asks the server to text a member a pay-by-UPI link.</summary>
public class CreatePaymentRequestDto
{
    public int MemberId { get; set; }
    public int? SubscriptionId { get; set; }
    public decimal Amount { get; set; }

    /// <summary>Short line shown on the pay page and in the UPI app ("Monthly renewal").</summary>
    public string? Note { get; set; }

    /// <summary>
    /// The plan a RENEWAL request sells. When set, the request targets the member's NEXT term:
    /// the amount defaults to the plan's price when none is given, no subscription is bound
    /// (the renewed term does not exist until the money arrives), and a gateway settlement
    /// renews the membership automatically. Leave null for a due-balance request.
    /// </summary>
    public int? MembershipPlanId { get; set; }
}

/// <summary>What the operator gets back after sending a payment request.</summary>
public class PaymentRequestCreatedDto
{
    public string Token { get; set; } = string.Empty;
    /// <summary>The public link the message carries; share it by hand if no channel could go.</summary>
    public string Link { get; set; } = string.Empty;
    public bool SmsSent { get; set; }
    public bool EmailSent { get; set; }
    public string Detail { get; set; } = string.Empty;

    /// <summary>The member's contact points, so the screen can offer hand-share routes (WhatsApp).</summary>
    public string? MemberPhone { get; set; }
    public string? MemberEmail { get; set; }

    /// <summary>The exact message the SMS carries — reused verbatim for a WhatsApp share.</summary>
    public string MessageText { get; set; } = string.Empty;
}

/// <summary>Everything the anonymous pay page needs to render and launch a UPI app.</summary>
public class PublicPaymentRequestDto
{
    public string GymName { get; set; } = string.Empty;
    public string MemberFirstName { get; set; } = string.Empty;
    public string? PlanName { get; set; }
    public decimal Amount { get; set; }
    public string CurrencySymbol { get; set; } = "₹";
    public string? Note { get; set; }
    public string UpiId { get; set; } = string.Empty;
    public string PayeeName { get; set; } = string.Empty;
    /// <summary>Standard <c>upi://pay?…</c> link; the page derives the app-specific schemes from it.</summary>
    public string UpiDeepLink { get; set; } = string.Empty;
    public bool Expired { get; set; }

    /// <summary>One of <c>pending</c> | <c>paid</c> | <c>failed</c> | <c>expired</c>.</summary>
    public string Status { get; set; } = "pending";

    /// <summary>The gateway's order id, when an order backs this request.</summary>
    public string? OrderId { get; set; }

    /// <summary>
    /// QR material for the gateway order. A value starting <c>image:</c> is a hosted QR image
    /// URL; anything else is a raw payload the page encodes itself.
    /// </summary>
    public string? QrData { get; set; }

    /// <summary>The gateway's hosted checkout page, when it offers one.</summary>
    public string? PaymentUrl { get; set; }

    /// <summary>True when a gateway settles this link automatically, so the page should poll.</summary>
    public bool GatewayEnabled { get; set; }

    /// <summary>The collection reference, shown on screen as the payment id.</summary>
    public string PaymentCode { get; set; } = string.Empty;
}

/// <summary>
/// Live outcome of a pay link, polled by the public pay page every few seconds while it waits
/// for the gateway. Carries an outcome word and receipt facts — never money credentials.
/// </summary>
public class PublicPaymentRequestStatusDto
{
    /// <summary>One of <c>pending</c> | <c>paid</c> | <c>failed</c> | <c>expired</c>.</summary>
    public string Status { get; set; } = "pending";

    /// <summary>Receipt number of the settled payment, once there is one.</summary>
    public string? ReceiptNumber { get; set; }

    /// <summary>The gateway's own transaction id / UTR, when its webhook carried one.</summary>
    public string? GatewayTransactionId { get; set; }

    public DateTime? PaidAtUtc { get; set; }

    /// <summary>End date of the membership term the settled payment credited, when one did.</summary>
    public DateTime? ValidUntil { get; set; }
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
