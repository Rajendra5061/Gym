using GymManagement.Application.Common;
using GymManagement.Application.DTOs;

namespace GymManagement.Application.Interfaces;

public interface IDietPlanService
{
    Task<PagedResult<DietPlanDto>> GetPagedAsync(DietPlanQueryDto query, CancellationToken ct = default);
    Task<DietPlanDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<DietPlanDto> SaveAsync(SaveDietPlanDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(int id, CancellationToken ct = default);
    Task<List<DietPlanDto>> GetMemberPlansAsync(int memberId, CancellationToken ct = default);
}
