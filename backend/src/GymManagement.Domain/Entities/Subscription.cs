using GymManagement.Domain.Common;
using GymManagement.Domain.Enums;

namespace GymManagement.Domain.Entities;

public class MembershipPlan : SoftDeletableEntity
{
    public string PlanCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Newline separated feature list rendered on the plans screen.</summary>
    public string? Features { get; set; }

    public PlanDurationType DurationType { get; set; } = PlanDurationType.Month;
    /// <summary>Number of <see cref="DurationType"/> units, e.g. 3 months.</summary>
    public int DurationValue { get; set; } = 1;

    public decimal Price { get; set; }
    public decimal? RegistrationFee { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal MaxDiscountPercent { get; set; } = 100m;

    /// <summary>Days after expiry during which the member may still check in.</summary>
    public int GracePeriodDays { get; set; }
    /// <summary>Maximum days the member may freeze the subscription for.</summary>
    public int MaxFreezeDays { get; set; }
    public int? SessionLimit { get; set; }
    public bool TrainerIncluded { get; set; }
    public int DisplayOrder { get; set; }
    public PlanStatus Status { get; set; } = PlanStatus.Active;

    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();

    /// <summary>Total days this plan covers. Custom plans use <see cref="DurationValue"/> directly as days.</summary>
    public int TotalDays => DurationType switch
    {
        PlanDurationType.Day => DurationValue,
        PlanDurationType.Week => DurationValue * 7,
        PlanDurationType.Month => DurationValue * 30,
        PlanDurationType.Quarter => DurationValue * 90,
        PlanDurationType.HalfYear => DurationValue * 182,
        PlanDurationType.Year => DurationValue * 365,
        PlanDurationType.Custom => DurationValue,
        _ => DurationValue
    };

    /// <summary>Advances <paramref name="startDate"/> by the plan length. End date is inclusive.</summary>
    public DateTime CalculateEndDate(DateTime startDate) => DurationType switch
    {
        PlanDurationType.Month => startDate.AddMonths(DurationValue).AddDays(-1),
        PlanDurationType.Quarter => startDate.AddMonths(3 * DurationValue).AddDays(-1),
        PlanDurationType.HalfYear => startDate.AddMonths(6 * DurationValue).AddDays(-1),
        PlanDurationType.Year => startDate.AddYears(DurationValue).AddDays(-1),
        PlanDurationType.Week => startDate.AddDays(7 * DurationValue - 1),
        _ => startDate.AddDays(TotalDays - 1)
    };
}

public class Subscription : SoftDeletableEntity
{
    public string SubscriptionCode { get; set; } = string.Empty;

    public int MemberId { get; set; }
    public Member? Member { get; set; }
    public int MembershipPlanId { get; set; }
    public MembershipPlan? MembershipPlan { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    // Money is always decimal, never floating point.
    public decimal PlanAmount { get; set; }
    public decimal RegistrationFee { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal FinalAmount { get; set; }
    public decimal PaidAmount { get; set; }

    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Pending;

    public int GracePeriodDays { get; set; }
    public bool IsRenewal { get; set; }
    public int? PreviousSubscriptionId { get; set; }
    public Subscription? PreviousSubscription { get; set; }

    // Freeze / pause tracking.
    public DateTime? FreezeStartDate { get; set; }
    public DateTime? FreezeEndDate { get; set; }
    public int FrozenDaysUsed { get; set; }

    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public int? AssignedTrainerId { get; set; }
    public Trainer? AssignedTrainer { get; set; }
    public string? Notes { get; set; }

    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<SubscriptionHistory> History { get; set; } = new List<SubscriptionHistory>();

    public decimal OutstandingAmount => Math.Max(0m, FinalAmount - PaidAmount);

    /// <summary>Last day the member may enter, including the grace period.</summary>
    public DateTime EffectiveEndDate => EndDate.AddDays(GracePeriodDays);

    public int DaysRemaining(DateTime asOf) => (EndDate.Date - asOf.Date).Days;

    /// <summary>True when the member may check in on <paramref name="asOf"/>.</summary>
    public bool IsUsableOn(DateTime asOf) =>
        Status == SubscriptionStatus.Active &&
        asOf.Date >= StartDate.Date &&
        asOf.Date <= EffectiveEndDate.Date;
}

/// <summary>Append-only trail of everything that happened to a subscription.</summary>
public class SubscriptionHistory : BaseEntity
{
    public int SubscriptionId { get; set; }
    public Subscription? Subscription { get; set; }
    public SubscriptionActionType ActionType { get; set; }
    public DateTime ActionDate { get; set; } = DateTime.UtcNow;
    public SubscriptionStatus? OldStatus { get; set; }
    public SubscriptionStatus? NewStatus { get; set; }
    public DateTime? OldEndDate { get; set; }
    public DateTime? NewEndDate { get; set; }
    public decimal? Amount { get; set; }
    public string? Remarks { get; set; }
    public int? PerformedBy { get; set; }
    public User? PerformedByUser { get; set; }
}
