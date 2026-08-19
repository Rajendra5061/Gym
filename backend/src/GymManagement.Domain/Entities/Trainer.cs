using GymManagement.Domain.Common;
using GymManagement.Domain.Enums;

namespace GymManagement.Domain.Entities;

public class Trainer : SoftDeletableEntity
{
    public string TrainerCode { get; set; } = string.Empty;
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
    public TrainerStatus Status { get; set; } = TrainerStatus.Active;
    public string? PhotoPath { get; set; }
    public string? Notes { get; set; }

    public ICollection<Member> AssignedMembers { get; set; } = new List<Member>();
    public ICollection<WorkoutSession> WorkoutSessions { get; set; } = new List<WorkoutSession>();
    public ICollection<MemberWorkoutPlan> AssignedWorkoutPlans { get; set; } = new List<MemberWorkoutPlan>();
}
