using GymManagement.Application.Common;
using GymManagement.Domain.Enums;

namespace GymManagement.Application.DTOs;

public class TrainerListDto
{
    public int Id { get; set; }
    public string TrainerCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public Gender Gender { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Specialization { get; set; }
    public int? ExperienceYears { get; set; }
    public DateTime JoiningDate { get; set; }
    public TrainerStatus Status { get; set; }
    public string StatusText => Status.ToString();
    public string? PhotoPath { get; set; }
    public int AssignedMemberCount { get; set; }
}

public class TrainerDetailDto : TrainerListDto
{
    public string? Address { get; set; }
    public string? Certifications { get; set; }
    public decimal? MonthlySalary { get; set; }
    public decimal? CommissionPercent { get; set; }
    public string? Notes { get; set; }
    public bool HasUserAccount { get; set; }
    public int SessionsThisMonth { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateTrainerDto
{
    public string FullName { get; set; } = string.Empty;
    public Gender Gender { get; set; } = Gender.Unspecified;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Specialization { get; set; }
    public string? Certifications { get; set; }
    public int? ExperienceYears { get; set; }
    public DateTime JoiningDate { get; set; } = DateTime.Today;
    public decimal? MonthlySalary { get; set; }
    public decimal? CommissionPercent { get; set; }
    public string? PhotoPath { get; set; }
    public string? Notes { get; set; }
    public bool CreateUserAccount { get; set; }
}

public class UpdateTrainerDto : CreateTrainerDto
{
    public int Id { get; set; }
    public TrainerStatus Status { get; set; } = TrainerStatus.Active;
}

public class TrainerQueryDto : PagedRequest
{
    public TrainerStatus? Status { get; set; }
    public string? Specialization { get; set; }
    public bool IncludeDeleted { get; set; }
}

public class AssignTrainerDto
{
    public int TrainerId { get; set; }
    public List<int> MemberIds { get; set; } = new();
}

/// <summary>Workload summary used by the trainer report and dashboard.</summary>
public class TrainerWorkloadDto
{
    public int TrainerId { get; set; }
    public string TrainerName { get; set; } = string.Empty;
    public string? Specialization { get; set; }
    public int AssignedMembers { get; set; }
    public int ActiveWorkoutPlans { get; set; }
    public int SessionsInPeriod { get; set; }
    public int DistinctMembersTrained { get; set; }
    public decimal TotalSessionMinutes { get; set; }
}
