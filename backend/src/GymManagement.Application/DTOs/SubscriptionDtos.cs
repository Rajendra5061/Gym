using GymManagement.Application.Common;
using GymManagement.Domain.Enums;

namespace GymManagement.Application.DTOs;

public class MembershipPlanDto
{
    public int Id { get; set; }
    public string PlanCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Features { get; set; }
    public PlanDurationType DurationType { get; set; } = PlanDurationType.Month;
    public string DurationTypeText => DurationType.ToString();
    public int DurationValue { get; set; } = 1;
    public int TotalDays { get; set; }
    public decimal Price { get; set; }
    public decimal? RegistrationFee { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal MaxDiscountPercent { get; set; } = 100m;
    public int GracePeriodDays { get; set; }
    public int MaxFreezeDays { get; set; }
    public int? SessionLimit { get; set; }
    public bool TrainerIncluded { get; set; }
    public int DisplayOrder { get; set; }
    public PlanStatus Status { get; set; } = PlanStatus.Active;
    public string StatusText => Status.ToString();
    public int ActiveSubscriptionCount { get; set; }
}

public class MembershipPlanQueryDto : PagedRequest
{
    public PlanStatus? Status { get; set; }
    public PlanDurationType? DurationType { get; set; }
    public bool IncludeDeleted { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string SubscriptionCode { get; set; } = string.Empty;
    public int MemberId { get; set; }
    public string MemberCode { get; set; } = string.Empty;
    public string MemberName { get; set; } = string.Empty;
    public string? MemberPhone { get; set; }
    public int MembershipPlanId { get; set; }
    public string PlanName { get; set; } = string.Empty;

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal PlanAmount { get; set; }
    public decimal RegistrationFee { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal FinalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal OutstandingAmount { get; set; }

    public PaymentStatus PaymentStatus { get; set; }
    public string PaymentStatusText => PaymentStatus.ToString();
    public SubscriptionStatus Status { get; set; }
    public string StatusText => Status.ToString();

    public int GracePeriodDays { get; set; }
    public bool IsRenewal { get; set; }
    public int? PreviousSubscriptionId { get; set; }
    public DateTime? FreezeStartDate { get; set; }
    public DateTime? FreezeEndDate { get; set; }
    public int FrozenDaysUsed { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public int? AssignedTrainerId { get; set; }
    public string? AssignedTrainerName { get; set; }
    public string? Notes { get; set; }
    public int DaysRemaining { get; set; }
    public bool IsExpiringSoon { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByName { get; set; }
}

/// <summary>
/// Request to create a subscription. Amounts are recalculated on the server from the plan;
/// values supplied by the client are only used for the discount, which is validated against
/// the plan's maximum discount.
/// </summary>
public class CreateSubscriptionDto
{
    public int MemberId { get; set; }
    public int MembershipPlanId { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
    public decimal DiscountAmount { get; set; }
    public bool ChargeRegistrationFee { get; set; } = true;
    public int? AssignedTrainerId { get; set; }
    public string? Notes { get; set; }

    /// <summary>Optional payment collected together with the subscription, in one transaction.</summary>
    public CollectPaymentInlineDto? Payment { get; set; }
}

public class RenewSubscriptionDto
{
    public int MemberId { get; set; }
    /// <summary>Subscription being renewed. When null the member's latest subscription is used.</summary>
    public int? PreviousSubscriptionId { get; set; }
    /// <summary>Plan for the new term. When null the previous plan is reused.</summary>
    public int? MembershipPlanId { get; set; }
    /// <summary>When null the day after the current end date is used.</summary>
    public DateTime? StartDate { get; set; }
    public decimal DiscountAmount { get; set; }
    public int? AssignedTrainerId { get; set; }
    public string? Notes { get; set; }
    public CollectPaymentInlineDto? Payment { get; set; }
}

public class ChangePlanDto
{
    public int SubscriptionId { get; set; }
    public int NewMembershipPlanId { get; set; }
    public DateTime EffectiveDate { get; set; } = DateTime.Today;
    /// <summary>Credit the unused portion of the current plan against the new one.</summary>
    public bool ProrateUnusedAmount { get; set; } = true;
    public decimal DiscountAmount { get; set; }
    public string? Reason { get; set; }
    public CollectPaymentInlineDto? Payment { get; set; }
}

public class FreezeSubscriptionDto
{
    public int SubscriptionId { get; set; }
    public DateTime FreezeStartDate { get; set; } = DateTime.Today;
    public DateTime FreezeEndDate { get; set; } = DateTime.Today.AddDays(7);
    public string? Reason { get; set; }
}

public class ResumeSubscriptionDto
{
    public int SubscriptionId { get; set; }
    public DateTime ResumeDate { get; set; } = DateTime.Today;
    public string? Remarks { get; set; }
}

public class CancelSubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool RefundRemainingAmount { get; set; }
}

public class SubscriptionQueryDto : PagedRequest
{
    public int? MemberId { get; set; }
    public int? MembershipPlanId { get; set; }
    public SubscriptionStatus? Status { get; set; }
    public PaymentStatus? PaymentStatus { get; set; }
    public DateTime? StartFrom { get; set; }
    public DateTime? StartTo { get; set; }
    public DateTime? EndFrom { get; set; }
    public DateTime? EndTo { get; set; }
    public bool? OnlyRenewals { get; set; }
    public bool? OnlyExpiringSoon { get; set; }
    public int ExpiringWithinDays { get; set; } = 7;
    public bool? OnlyWithOutstanding { get; set; }

    /// <summary>Drops rows whose member already holds a later Active/Pending term — i.e. shows
    /// only memberships that will actually lapse, the set the renewal-reminder emails target.</summary>
    public bool? ExcludeRenewed { get; set; }
    public bool IncludeDeleted { get; set; }
}

public class SubscriptionHistoryDto
{
    public int Id { get; set; }
    public int SubscriptionId { get; set; }
    public SubscriptionActionType ActionType { get; set; }
    public string ActionText => ActionType.ToString();
    public DateTime ActionDate { get; set; }
    public SubscriptionStatus? OldStatus { get; set; }
    public SubscriptionStatus? NewStatus { get; set; }
    public DateTime? OldEndDate { get; set; }
    public DateTime? NewEndDate { get; set; }
    public decimal? Amount { get; set; }
    public string? Remarks { get; set; }
    public string? PerformedByName { get; set; }
}

/// <summary>Server-side price breakdown shown before the operator confirms a subscription.</summary>
public class SubscriptionQuoteDto
{
    public int MembershipPlanId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int TotalDays { get; set; }
    public decimal PlanAmount { get; set; }
    public decimal RegistrationFee { get; set; }
    public decimal ProratedCredit { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal MaxAllowedDiscount { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal FinalAmount { get; set; }
}

public class QuoteRequestDto
{
    public int MembershipPlanId { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
    public decimal DiscountAmount { get; set; }
    public bool ChargeRegistrationFee { get; set; } = true;
    /// <summary>Supplied for upgrade/downgrade quotes so unused days can be credited.</summary>
    public int? CurrentSubscriptionId { get; set; }
    public bool ProrateUnusedAmount { get; set; }
}

/// <summary>Result of the scheduled auto-expiry job.</summary>
public class ExpiryProcessingResultDto
{
    public int ExpiredCount { get; set; }
    public int NotificationsCreated { get; set; }
    public int ResumedFromFreeze { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
    public List<string> Messages { get; set; } = new();
}
