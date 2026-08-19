using GymManagement.Domain.Common;
using GymManagement.Domain.Enums;

namespace GymManagement.Domain.Entities;

/// <summary>
/// A nutrition plan written for one member, usually by their trainer. Kept in the recycle bin
/// when deleted so past plans stay readable from a member's history.
/// </summary>
public class DietPlan : SoftDeletableEntity
{
    public int MemberId { get; set; }
    public Member? Member { get; set; }

    /// <summary>The trainer who wrote the plan. Optional: an admin can author one too.</summary>
    public int? TrainerId { get; set; }
    public Trainer? Trainer { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>What the plan is trying to achieve, e.g. fat loss or muscle gain.</summary>
    public string? Goal { get; set; }
    public string? Notes { get; set; }

    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime? EndDate { get; set; }
    public DietPlanStatus Status { get; set; } = DietPlanStatus.Active;

    public ICollection<DietPlanMeal> Meals { get; set; } = new List<DietPlanMeal>();
}

/// <summary>One meal line of a diet plan: what to eat in a given slot and its macros.</summary>
public class DietPlanMeal : BaseEntity
{
    public int DietPlanId { get; set; }
    public DietPlan? DietPlan { get; set; }

    public DietMealType MealType { get; set; } = DietMealType.Breakfast;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public int? Calories { get; set; }
    public int? ProteinGrams { get; set; }
    public int? CarbsGrams { get; set; }
    public int? FatGrams { get; set; }

    public int DisplayOrder { get; set; }
}
