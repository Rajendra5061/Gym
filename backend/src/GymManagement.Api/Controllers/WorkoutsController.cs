using GymManagement.Api.Authorization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GymManagement.Api.Controllers;

/// <summary>
/// Workout plan templates, their assignment to members, and the individual training sessions that
/// get logged against a member.
/// </summary>
[Route("api/workouts")]
public sealed class WorkoutsController : ApiControllerBase
{
    private readonly IWorkoutService _workoutService;
    private readonly ICurrentUserService _currentUser;

    public WorkoutsController(IWorkoutService workoutService, ICurrentUserService currentUser)
    {
        _workoutService = workoutService;
        _currentUser = currentUser;
    }

    // -----------------------------------------------------------------------------------------
    // Plan templates
    // -----------------------------------------------------------------------------------------

    /// <summary>Returns a paged, filterable list of workout plan templates.</summary>
    [HttpGet("plans")]
    [HasPermission(Permissions.WorkoutsView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<WorkoutPlanDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<WorkoutPlanDto>>>> GetPlans(
        [FromQuery] WorkoutPlanQueryDto query, CancellationToken ct)
    {
        var result = await _workoutService.GetPlansAsync(query, ct);
        return Success(result);
    }

    /// <summary>Returns a workout plan template with its exercises.</summary>
    [HttpGet("plans/{id:int}")]
    [HasPermission(Permissions.WorkoutsView)]
    [ProducesResponseType(typeof(ApiResponse<WorkoutPlanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<WorkoutPlanDto>>> GetPlanById(int id, CancellationToken ct)
    {
        var result = await _workoutService.GetPlanByIdAsync(id, ct);
        return Success(result);
    }

    /// <summary>Creates a workout plan template together with its exercises.</summary>
    [HttpPost("plans")]
    [HasPermission(Permissions.WorkoutsManage)]
    [ProducesResponseType(typeof(ApiResponse<WorkoutPlanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<WorkoutPlanDto>>> CreatePlan(
        [FromBody] WorkoutPlanDto dto, CancellationToken ct)
    {
        var result = await _workoutService.SavePlanAsync(dto, ct);
        return Success(result, "Workout plan created.");
    }

    /// <summary>Updates a workout plan template and replaces its exercises.</summary>
    [HttpPut("plans/{id:int}")]
    [HasPermission(Permissions.WorkoutsManage)]
    [ProducesResponseType(typeof(ApiResponse<WorkoutPlanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<WorkoutPlanDto>>> UpdatePlan(
        int id, [FromBody] WorkoutPlanDto dto, CancellationToken ct)
    {
        dto.Id = id;
        var result = await _workoutService.SavePlanAsync(dto, ct);
        return Success(result, "Workout plan updated.");
    }

    /// <summary>Soft deletes a workout plan template.</summary>
    [HttpDelete("plans/{id:int}")]
    [HasPermission(Permissions.WorkoutsManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeletePlan(int id, CancellationToken ct)
    {
        await _workoutService.SoftDeletePlanAsync(id, ct);
        return SuccessMessage("Workout plan deleted.");
    }

    /// <summary>Restores a previously soft-deleted workout plan template.</summary>
    [HttpPost("plans/{id:int}/restore")]
    [HasPermission(Permissions.WorkoutsManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> RestorePlan(int id, CancellationToken ct)
    {
        await _workoutService.RestorePlanAsync(id, ct);
        return SuccessMessage("Workout plan restored.");
    }

    /// <summary>Returns workout plan id/name pairs for combo boxes.</summary>
    [HttpGet("plans/lookup")]
    [HasPermission(Permissions.WorkoutsView)]
    [ProducesResponseType(typeof(ApiResponse<List<LookupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<LookupDto>>>> GetPlanLookup(CancellationToken ct)
    {
        var result = await _workoutService.GetPlanLookupAsync(ct);
        return Success(result);
    }

    // -----------------------------------------------------------------------------------------
    // Member assignments
    // -----------------------------------------------------------------------------------------

    /// <summary>Assigns a workout plan to a member for a date range.</summary>
    [HttpPost("assign")]
    [HasPermission(Permissions.WorkoutsManage)]
    [ProducesResponseType(typeof(ApiResponse<MemberWorkoutPlanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MemberWorkoutPlanDto>>> AssignPlan(
        [FromBody] AssignWorkoutPlanDto dto, CancellationToken ct)
    {
        var result = await _workoutService.AssignPlanAsync(dto, ct);
        return Success(result, "Workout plan assigned.");
    }

    /// <summary>
    /// Lists the workout plans assigned to a member. A member signed in to their own portal may only
    /// request their own assigned plans.
    /// </summary>
    [HttpGet("members/{memberId:int}/plans")]
    [ProducesResponseType(typeof(ApiResponse<List<MemberWorkoutPlanDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<List<MemberWorkoutPlanDto>>>> GetMemberPlans(
        int memberId, [FromQuery] bool onlyActive = false, CancellationToken ct = default)
    {
        MemberSelfAccess.EnsureCanRead(_currentUser, Permissions.WorkoutsView, memberId, "workout plans");

        var result = await _workoutService.GetMemberPlansAsync(memberId, onlyActive, ct);
        return Success(result);
    }

    /// <summary>Deactivates a member's assigned workout plan.</summary>
    [HttpPost("member-plans/{id:int}/deactivate")]
    [HasPermission(Permissions.WorkoutsManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeactivateMemberPlan(int id, CancellationToken ct)
    {
        await _workoutService.DeactivateMemberPlanAsync(id, ct);
        return SuccessMessage("Assigned workout plan deactivated.");
    }

    // -----------------------------------------------------------------------------------------
    // Sessions
    // -----------------------------------------------------------------------------------------

    /// <summary>Returns a paged, filterable list of logged workout sessions.</summary>
    [HttpGet("sessions")]
    [HasPermission(Permissions.WorkoutsView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<WorkoutSessionDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<WorkoutSessionDto>>>> GetSessions(
        [FromQuery] WorkoutSessionQueryDto query, CancellationToken ct)
    {
        var result = await _workoutService.GetSessionsAsync(query, ct);
        return Success(result);
    }

    /// <summary>Returns a single workout session with the exercises performed.</summary>
    [HttpGet("sessions/{id:int}")]
    [HasPermission(Permissions.WorkoutsView)]
    [ProducesResponseType(typeof(ApiResponse<WorkoutSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<WorkoutSessionDto>>> GetSessionById(
        int id, CancellationToken ct)
    {
        var result = await _workoutService.GetSessionByIdAsync(id, ct);
        return Success(result);
    }

    /// <summary>Logs a workout session for a member.</summary>
    [HttpPost("sessions")]
    [HasPermission(Permissions.WorkoutsManage)]
    [ProducesResponseType(typeof(ApiResponse<WorkoutSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<WorkoutSessionDto>>> CreateSession(
        [FromBody] SaveWorkoutSessionDto dto, CancellationToken ct)
    {
        var result = await _workoutService.SaveSessionAsync(dto, ct);
        return Success(result, "Workout session saved.");
    }

    /// <summary>Updates a logged workout session and the exercises performed.</summary>
    [HttpPut("sessions/{id:int}")]
    [HasPermission(Permissions.WorkoutsManage)]
    [ProducesResponseType(typeof(ApiResponse<WorkoutSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<WorkoutSessionDto>>> UpdateSession(
        int id, [FromBody] SaveWorkoutSessionDto dto, CancellationToken ct)
    {
        dto.Id = id;
        var result = await _workoutService.SaveSessionAsync(dto, ct);
        return Success(result, "Workout session updated.");
    }

    /// <summary>Soft deletes a logged workout session.</summary>
    [HttpDelete("sessions/{id:int}")]
    [HasPermission(Permissions.WorkoutsManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeleteSession(int id, CancellationToken ct)
    {
        await _workoutService.SoftDeleteSessionAsync(id, ct);
        return SuccessMessage("Workout session deleted.");
    }

    /// <summary>Restores a previously soft-deleted workout session.</summary>
    [HttpPost("sessions/{id:int}/restore")]
    [HasPermission(Permissions.WorkoutsManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> RestoreSession(int id, CancellationToken ct)
    {
        await _workoutService.RestoreSessionAsync(id, ct);
        return SuccessMessage("Workout session restored.");
    }
}
