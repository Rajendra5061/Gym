using GymManagement.Application.Common;
using GymManagement.Domain.Enums;

namespace GymManagement.Application.DTOs;

public class MemberListDto
{
    public int Id { get; set; }
    public string MemberCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public Gender Gender { get; set; }
    public string GenderText => Gender.ToString();
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public DateTime JoiningDate { get; set; }
    public MemberStatus Status { get; set; }
    public string StatusText => Status.ToString();
    public string? ProfilePhotoPath { get; set; }
    public string? AssignedTrainerName { get; set; }

    // Flattened active-subscription summary for the member grid.
    public string? CurrentPlanName { get; set; }
    public DateTime? SubscriptionEndDate { get; set; }
    public int? DaysRemaining { get; set; }
    public SubscriptionStatus? SubscriptionStatus { get; set; }
    public decimal OutstandingAmount { get; set; }
    public bool IsExpiringSoon { get; set; }
}

public class MemberDetailDto
{
    public int Id { get; set; }
    public string MemberCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public Gender Gender { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public int? Age { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelation { get; set; }
    public DateTime JoiningDate { get; set; }
    public string? ProfilePhotoPath { get; set; }
    public string? BloodGroup { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public MemberStatus Status { get; set; }
    public string? Notes { get; set; }
    public int? AssignedTrainerId { get; set; }
    public string? AssignedTrainerName { get; set; }
    public bool HasUserAccount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public SubscriptionDto? ActiveSubscription { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalOutstanding { get; set; }
    public int TotalVisits { get; set; }
    public DateTime? LastVisitDate { get; set; }
}

public class CreateMemberDto
{
    public string FullName { get; set; } = string.Empty;
    public Gender Gender { get; set; } = Gender.Unspecified;
    public DateTime? DateOfBirth { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelation { get; set; }
    public DateTime JoiningDate { get; set; } = DateTime.Today;
    public string? ProfilePhotoPath { get; set; }
    public string? BloodGroup { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public string? Notes { get; set; }
    public int? AssignedTrainerId { get; set; }
    /// <summary>When true a member login account is created and a temporary password returned.</summary>
    public bool CreateUserAccount { get; set; }
}

public class UpdateMemberDto : CreateMemberDto
{
    public int Id { get; set; }
    public MemberStatus Status { get; set; } = MemberStatus.Active;
}

public class MemberQueryDto : PagedRequest
{
    public MemberStatus? Status { get; set; }
    public Gender? Gender { get; set; }
    public int? TrainerId { get; set; }
    public int? MembershipPlanId { get; set; }
    public DateTime? JoinedFrom { get; set; }
    public DateTime? JoinedTo { get; set; }
    /// <summary>Only members whose subscription has already ended.</summary>
    public bool? OnlyExpired { get; set; }
    /// <summary>Only members expiring within <see cref="ExpiringWithinDays"/>.</summary>
    public bool? OnlyExpiringSoon { get; set; }
    public int ExpiringWithinDays { get; set; } = 7;
    public bool? OnlyWithOutstanding { get; set; }
    public bool IncludeDeleted { get; set; }
}

public class MemberDocumentDto
{
    public int Id { get; set; }
    public int MemberId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class MemberMeasurementDto
{
    public int Id { get; set; }
    public int MemberId { get; set; }
    public DateTime MeasuredOn { get; set; } = DateTime.Today;
    public decimal? WeightKg { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? BodyFatPercent { get; set; }
    public decimal? MuscleMassKg { get; set; }
    public decimal? ChestCm { get; set; }
    public decimal? WaistCm { get; set; }
    public decimal? HipCm { get; set; }
    public decimal? ArmCm { get; set; }
    public decimal? ThighCm { get; set; }
    public decimal? Bmi { get; set; }
    public int? RecordedByTrainerId { get; set; }
    public string? RecordedByTrainerName { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Everything the member details screen needs in one round trip.</summary>
public class MemberHistoryDto
{
    public MemberDetailDto Member { get; set; } = new();
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
    public List<PaymentDto> Payments { get; set; } = new();
    public List<AttendanceDto> Attendance { get; set; } = new();
    public List<WorkoutSessionDto> WorkoutSessions { get; set; } = new();
    public List<MemberMeasurementDto> Measurements { get; set; } = new();
    public List<MemberDocumentDto> Documents { get; set; } = new();
}
