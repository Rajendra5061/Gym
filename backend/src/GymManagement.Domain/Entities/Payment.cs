using GymManagement.Domain.Common;
using GymManagement.Domain.Enums;

namespace GymManagement.Domain.Entities;

/// <summary>Configurable tender type: Cash, UPI, Card, Bank Transfer, Cheque, Wallet…</summary>
public class PaymentMethod : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>True when the method needs a transaction / reference number.</summary>
    public bool RequiresReference { get; set; }
    /// <summary>True for UPI style flows that show a QR code and await confirmation.</summary>
    public bool SupportsQrCode { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

public class Payment : SoftDeletableEntity
{
    /// <summary>Unique, gapless receipt number, e.g. <c>RCP-2026-000123</c>.</summary>
    public string ReceiptNumber { get; set; } = string.Empty;

    public int MemberId { get; set; }
    public Member? Member { get; set; }
    public int? SubscriptionId { get; set; }
    public Subscription? Subscription { get; set; }

    /// <summary>
    /// The membership plan the money was collected for, recorded directly on the payment.
    /// Optional and independent of <see cref="SubscriptionId"/>: money can be taken against a plan
    /// from the Record Payment screen without a subscription being created, and a payment need not
    /// relate to a plan at all. Where a subscription exists, its plan stays the authoritative one.
    /// </summary>
    public int? MembershipPlanId { get; set; }
    public MembershipPlan? MembershipPlan { get; set; }

    public decimal Amount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal FinalAmount { get; set; }
    public decimal RefundedAmount { get; set; }

    public int PaymentMethodId { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }

    /// <summary>
    /// UPI / card / bank reference supplied by the payer. Card numbers, CVV and UPI PINs are
    /// never accepted or stored by this application.
    /// </summary>
    public string? TransactionReference { get; set; }
    public string? PayerVpa { get; set; }

    public DateTime PaymentDate { get; set; } = DateTime.Now;
    public PaymentStatus Status { get; set; } = PaymentStatus.Paid;

    public int? CollectedByUserId { get; set; }
    public User? CollectedByUser { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public int? ConfirmedByUserId { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// Stamped the moment the receipt email is accepted by the mail provider, and never cleared.
    /// This is the send-once guard: the mailer refuses to send while it holds a value, so replaying
    /// the collection flow for the same payment cannot email the member twice.
    /// </summary>
    public DateTime? ReceiptEmailedAtUtc { get; set; }

    public ICollection<PaymentRefund> Refunds { get; set; } = new List<PaymentRefund>();

    public decimal RefundableAmount => Math.Max(0m, FinalAmount - RefundedAmount);
}

public class PaymentRefund : AuditableEntity
{
    public string RefundNumber { get; set; } = string.Empty;
    public int PaymentId { get; set; }
    public Payment? Payment { get; set; }

    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public RefundStatus Status { get; set; } = RefundStatus.Pending;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public int? RequestedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public int? ApprovedByUserId { get; set; }
    public string? RefundMethod { get; set; }
    public string? TransactionReference { get; set; }
    public string? Remarks { get; set; }
}

public class ExpenseCategory : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
}

public class Expense : SoftDeletableEntity
{
    public string ExpenseNumber { get; set; } = string.Empty;
    public int ExpenseCategoryId { get; set; }
    public ExpenseCategory? ExpenseCategory { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public DateTime ExpenseDate { get; set; } = DateTime.Today;
    public int? PaymentMethodId { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public string? VendorName { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? AttachmentPath { get; set; }
    public int? RecordedByUserId { get; set; }
}
