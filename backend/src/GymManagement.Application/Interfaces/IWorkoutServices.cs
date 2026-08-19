using GymManagement.Application.Common;
using GymManagement.Application.DTOs;

namespace GymManagement.Application.Interfaces;

public interface IExerciseService
{
    Task<PagedResult<ExerciseDto>> GetPagedAsync(ExerciseQueryDto query, CancellationToken ct = default);
    Task<ExerciseDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<ExerciseDto> CreateAsync(ExerciseDto dto, CancellationToken ct = default);
    Task<ExerciseDto> UpdateAsync(ExerciseDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(int id, CancellationToken ct = default);
    Task RestoreAsync(int id, CancellationToken ct = default);
    Task<List<LookupDto>> GetLookupAsync(CancellationToken ct = default);
}

public interface IWorkoutService
{
    // Plan templates
    Task<PagedResult<WorkoutPlanDto>> GetPlansAsync(WorkoutPlanQueryDto query, CancellationToken ct = default);
    Task<WorkoutPlanDto> GetPlanByIdAsync(int id, CancellationToken ct = default);
    Task<WorkoutPlanDto> SavePlanAsync(WorkoutPlanDto dto, CancellationToken ct = default);
    Task SoftDeletePlanAsync(int id, CancellationToken ct = default);
    Task RestorePlanAsync(int id, CancellationToken ct = default);
    Task<List<LookupDto>> GetPlanLookupAsync(CancellationToken ct = default);

    // Member assignments
    Task<MemberWorkoutPlanDto> AssignPlanAsync(AssignWorkoutPlanDto dto, CancellationToken ct = default);
    Task<List<MemberWorkoutPlanDto>> GetMemberPlansAsync(int memberId, bool onlyActive = false, CancellationToken ct = default);
    Task DeactivateMemberPlanAsync(int memberWorkoutPlanId, CancellationToken ct = default);

    // Sessions
    Task<PagedResult<WorkoutSessionDto>> GetSessionsAsync(WorkoutSessionQueryDto query, CancellationToken ct = default);
    Task<WorkoutSessionDto> GetSessionByIdAsync(int id, CancellationToken ct = default);
    Task<WorkoutSessionDto> SaveSessionAsync(SaveWorkoutSessionDto dto, CancellationToken ct = default);
    Task SoftDeleteSessionAsync(int id, CancellationToken ct = default);
    Task RestoreSessionAsync(int id, CancellationToken ct = default);
}

public interface IAttendanceService
{
    Task<AttendanceDto> CheckInAsync(CheckInRequestDto request, CancellationToken ct = default);
    Task<AttendanceDto> CheckOutAsync(CheckOutRequestDto request, CancellationToken ct = default);
    Task<PagedResult<AttendanceDto>> GetPagedAsync(AttendanceQueryDto query, CancellationToken ct = default);
    Task<AttendanceSummaryDto> GetSummaryAsync(DateTime? date, CancellationToken ct = default);
    Task<List<AttendanceDto>> GetCurrentlyInGymAsync(CancellationToken ct = default);
    Task DeleteAsync(int attendanceId, CancellationToken ct = default);
    Task<List<ChartSeriesDto>> GetTrendAsync(DateTime from, DateTime to, CancellationToken ct = default);
}
