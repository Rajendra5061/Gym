using GymManagement.Domain.Common;
using GymManagement.Domain.Enums;

namespace GymManagement.Domain.Entities;

/// <summary>
/// A physical asset owned by the gym: cardio machines, racks, free weights and accessories.
/// Kept in the recycle bin when deleted so that historical service records stay readable.
/// </summary>
public class Equipment : SoftDeletableEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Asset tag printed on the machine, e.g. <c>EQP-TRD-001</c>. Unique.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Free-text grouping such as Cardio, Strength, Free Weights or Accessories.</summary>
    public string Category { get; set; } = string.Empty;

    public string? SerialNumber { get; set; }
    public string? Manufacturer { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public decimal? PurchaseCost { get; set; }
    public int Quantity { get; set; } = 1;
    public EquipmentCondition Condition { get; set; } = EquipmentCondition.Good;

    /// <summary>Floor / zone the unit stands in.</summary>
    public string? Location { get; set; }

    public DateTime? WarrantyExpiry { get; set; }
    public DateTime? LastServicedOn { get; set; }
    public DateTime? NextServiceDue { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>A prospective member captured at the front desk, over the phone or from the website.</summary>
public class Enquiry : SoftDeletableEntity
{
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public EnquirySource Source { get; set; } = EnquirySource.WalkIn;

    /// <summary>Plan the lead asked about. Cleared if the plan is removed.</summary>
    public int? InterestedPlanId { get; set; }
    public MembershipPlan? InterestedPlan { get; set; }

    public string? Message { get; set; }
    public EnquiryStatus Status { get; set; } = EnquiryStatus.New;
    public DateTime? FollowUpDate { get; set; }

    /// <summary>Staff member responsible for chasing the lead.</summary>
    public int? AssignedToUserId { get; set; }
    public User? AssignedToUser { get; set; }

    /// <summary>Set once the lead signs up; points at the member record that was created.</summary>
    public int? ConvertedMemberId { get; set; }
    public Member? ConvertedMember { get; set; }

    public string? Notes { get; set; }
}

/// <summary>A suggestion, complaint or rating submitted by a member.</summary>
public class Feedback : SoftDeletableEntity
{
    public int MemberId { get; set; }
    public Member? Member { get; set; }

    public string? Subject { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>Optional 1–5 star rating.</summary>
    public int? Rating { get; set; }

    public FeedbackStatus Status { get; set; } = FeedbackStatus.New;
    public string? AdminResponse { get; set; }

    public int? RespondedByUserId { get; set; }
    public User? RespondedByUser { get; set; }
    public DateTime? RespondedAt { get; set; }

    /// <summary>Private feedback is visible only to the author and to staff.</summary>
    public bool IsPrivate { get; set; } = true;
}
