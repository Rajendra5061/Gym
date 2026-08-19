using GymManagement.Application.Common;
using GymManagement.Domain.Enums;

namespace GymManagement.Application.DTOs;

public class DietPlanDto
{
    public int Id { get; set; }
    public int MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public int? TrainerId { get; set; }
    public string? TrainerName { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Goal { get; set; }
    public string? Notes { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DietPlanStatus Status { get; set; }
    public string StatusText => Status.ToString();
    public List<DietPlanMealDto> Meals { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class DietPlanMealDto
{
    public int Id { get; set; }
    public DietMealType MealType { get; set; }
    public string MealTypeText => MealType.ToString();
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? Calories { get; set; }
    public int? ProteinGrams { get; set; }
    public int? CarbsGrams { get; set; }
    public int? FatGrams { get; set; }
    public int DisplayOrder { get; set; }
}

public class SaveDietPlanDto
{
    public int Id { get; set; }
    public int MemberId { get; set; }
    public int? TrainerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Goal { get; set; }
    public string? Notes { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime? EndDate { get; set; }
    public DietPlanStatus Status { get; set; } = DietPlanStatus.Active;
    public List<SaveDietPlanMealDto> Meals { get; set; } = new();
}

public class SaveDietPlanMealDto
{
    public DietMealType MealType { get; set; } = DietMealType.Breakfast;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? Calories { get; set; }
    public int? ProteinGrams { get; set; }
    public int? CarbsGrams { get; set; }
    public int? FatGrams { get; set; }
    public int DisplayOrder { get; set; }
}

public class DietPlanQueryDto : PagedRequest
{
    public int? MemberId { get; set; }
    public int? TrainerId { get; set; }
    public DietPlanStatus? Status { get; set; }
}
