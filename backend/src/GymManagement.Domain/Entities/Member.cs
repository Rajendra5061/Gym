using GymManagement.Domain.Common;
using GymManagement.Domain.Enums;

namespace GymManagement.Domain.Entities;

public class Member : SoftDeletableEntity
{
    /// <summary>Human readable code shown on receipts and ID cards, e.g. <c>GYM-2026-0001</c>.</summary>
    public string MemberCode { get; set; } = string.Empty;

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
    public MemberStatus Status { get; set; } = MemberStatus.Active;
    public string? Notes { get; set; }

    public int? AssignedTrainerId { get; set; }
    public Trainer? AssignedTrainer { get; set; }

    public ICollection<MemberDocument> Documents { get; set; } = new List<MemberDocument>();
    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<Attendance> AttendanceRecords { get; set; } = new List<Attendance>();
    public ICollection<WorkoutSession> WorkoutSessions { get; set; } = new List<WorkoutSession>();
    public ICollection<MemberWorkoutPlan> WorkoutPlans { get; set; } = new List<MemberWorkoutPlan>();
    public ICollection<MemberMeasurement> Measurements { get; set; } = new List<MemberMeasurement>();
}

public class MemberDocument : SoftDeletableEntity
{
    public int MemberId { get; set; }
    public Member? Member { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Body measurement snapshot used for progress charts.</summary>
public class MemberMeasurement : AuditableEntity
{
    public int MemberId { get; set; }
    public Member? Member { get; set; }
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
    public int? RecordedByTrainerId { get; set; }
    public Trainer? RecordedByTrainer { get; set; }
    public string? Notes { get; set; }

    /// <summary>Body mass index derived from the recorded height and weight.</summary>
    public decimal? Bmi => WeightKg is > 0 && HeightCm is > 0
        ? Math.Round(WeightKg.Value / (HeightCm.Value / 100m * (HeightCm.Value / 100m)), 2)
        : null;
}
