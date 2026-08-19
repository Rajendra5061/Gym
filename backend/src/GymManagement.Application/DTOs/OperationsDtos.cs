using GymManagement.Application.Common;
using GymManagement.Domain.Enums;

namespace GymManagement.Application.DTOs;

// ---------------------------------------------------------------------------------------------
// Equipment
// ---------------------------------------------------------------------------------------------

/// <summary>Row shown in the equipment inventory grid.</summary>
public class EquipmentListDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public int Quantity { get; set; }
    public EquipmentCondition Condition { get; set; }
    public string ConditionText => Condition.ToString();
    public string? Location { get; set; }
    public DateTime? NextServiceDue { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
}

/// <summary>Full equipment record.</summary>
public class EquipmentDetailDto : EquipmentListDto
{
    public string? SerialNumber { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public decimal? PurchaseCost { get; set; }
    public DateTime? WarrantyExpiry { get; set; }
    public DateTime? LastServicedOn { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Create (Id = 0) or update an equipment record.</summary>
public class SaveEquipmentDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Asset tag. Leave blank on create to have one generated from the category.</summary>
    public string? Code { get; set; }

    public string Category { get; set; } = string.Empty;
    public string? SerialNumber { get; set; }
    public string? Manufacturer { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public decimal? PurchaseCost { get; set; }
    public int Quantity { get; set; } = 1;
    public EquipmentCondition Condition { get; set; } = EquipmentCondition.Good;
    public string? Location { get; set; }
    public DateTime? WarrantyExpiry { get; set; }
    public DateTime? LastServicedOn { get; set; }
    public DateTime? NextServiceDue { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

public class EquipmentQueryDto : PagedRequest
{
    public string? Category { get; set; }
    public EquipmentCondition? Condition { get; set; }
    public string? Location { get; set; }
    public bool? IsActive { get; set; }

    /// <summary>True to return only rows whose next service date falls on or before today.</summary>
    public bool? ServiceDue { get; set; }

    public bool IncludeDeleted { get; set; }
}

// ---------------------------------------------------------------------------------------------
// Enquiries
// ---------------------------------------------------------------------------------------------

/// <summary>Row shown in the enquiries / leads grid.</summary>
public class EnquiryListDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public EnquirySource Source { get; set; }
    public string SourceText => Source.ToString();
    public EnquiryStatus Status { get; set; }
    public string StatusText => Status.ToString();
    public int? InterestedPlanId { get; set; }
    public string? InterestedPlanName { get; set; }
    public DateTime? FollowUpDate { get; set; }
    public int? AssignedToUserId { get; set; }
    public string? AssignedToUserName { get; set; }
    public int? ConvertedMemberId { get; set; }
    public string? ConvertedMemberName { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>Full enquiry record.</summary>
public class EnquiryDetailDto : EnquiryListDto
{
    public string? Message { get; set; }
    public string? Notes { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Create (Id = 0) or update an enquiry.</summary>
public class SaveEnquiryDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public EnquirySource Source { get; set; } = EnquirySource.WalkIn;
    public int? InterestedPlanId { get; set; }
    public string? Message { get; set; }
    public EnquiryStatus Status { get; set; } = EnquiryStatus.New;
    public DateTime? FollowUpDate { get; set; }
    public int? AssignedToUserId { get; set; }
    public string? Notes { get; set; }
}

public class EnquiryQueryDto : PagedRequest
{
    public EnquiryStatus? Status { get; set; }
    public EnquirySource? Source { get; set; }
    public int? AssignedToUserId { get; set; }
    public int? InterestedPlanId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }

    /// <summary>True to return only leads whose follow-up date has arrived or passed.</summary>
    public bool? FollowUpDue { get; set; }

    public bool IncludeDeleted { get; set; }
}

// ---------------------------------------------------------------------------------------------
// Feedback
// ---------------------------------------------------------------------------------------------

/// <summary>Row shown in the feedback grid.</summary>
public class FeedbackListDto
{
    public int Id { get; set; }
    public int MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public string? MemberCode { get; set; }
    public string? Subject { get; set; }
    public int? Rating { get; set; }
    public FeedbackStatus Status { get; set; }
    public string StatusText => Status.ToString();
    public bool IsPrivate { get; set; } = true;
    public bool HasResponse { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>Full feedback record including the administrator reply.</summary>
public class FeedbackDetailDto : FeedbackListDto
{
    public string Message { get; set; } = string.Empty;
    public string? AdminResponse { get; set; }
    public int? RespondedByUserId { get; set; }
    public string? RespondedByUserName { get; set; }
    public DateTime? RespondedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Payload posted to <c>POST api/feedback</c> by a member.</summary>
public class CreateFeedbackDto
{
    public string? Subject { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? Rating { get; set; }
    public bool IsPrivate { get; set; } = true;

    /// <summary>
    /// Staff with the manage permission may file feedback on behalf of a member. Ignored for a
    /// member, who always posts against their own record.
    /// </summary>
    public int? MemberId { get; set; }
}

/// <summary>Create (Id = 0) or update a feedback record from the back office.</summary>
public class SaveFeedbackDto
{
    public int Id { get; set; }
    public int MemberId { get; set; }
    public string? Subject { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? Rating { get; set; }
    public FeedbackStatus Status { get; set; } = FeedbackStatus.New;
    public bool IsPrivate { get; set; } = true;
}

public class FeedbackQueryDto : PagedRequest
{
    public int? MemberId { get; set; }
    public FeedbackStatus? Status { get; set; }
    public int? MinRating { get; set; }
    public int? MaxRating { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public bool IncludeDeleted { get; set; }
}
