using GymManagement.Api.Authorization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GymManagement.Api.Controllers;

/// <summary>Diet plans written for members, each with its meal lines and macros.</summary>
[Route("api/diet-plans")]
public sealed class DietPlansController : ApiControllerBase
{
    private readonly IDietPlanService _dietPlans;
    private readonly ICurrentUserService _currentUser;

    public DietPlansController(IDietPlanService dietPlans, ICurrentUserService currentUser)
    {
        _dietPlans = dietPlans;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Returns a paged, filterable list of diet plans. Trainer-linked callers who cannot view the
    /// trainer roster are always scoped to their own plans.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.DietView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<DietPlanDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<DietPlanDto>>>> GetPaged(
        [FromQuery] DietPlanQueryDto query, CancellationToken ct) =>
        Success(await _dietPlans.GetPagedAsync(query, ct));

    /// <summary>Returns a single diet plan with its meals.</summary>
    [HttpGet("{id:int}")]
    [HasPermission(Permissions.DietView)]
    [ProducesResponseType(typeof(ApiResponse<DietPlanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<DietPlanDto>>> GetById(int id, CancellationToken ct) =>
        Success(await _dietPlans.GetByIdAsync(id, ct));

    /// <summary>Creates a diet plan together with its meals.</summary>
    [HttpPost]
    [HasPermission(Permissions.DietManage)]
    [ProducesResponseType(typeof(ApiResponse<DietPlanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<DietPlanDto>>> Create(
        [FromBody] SaveDietPlanDto dto, CancellationToken ct) =>
        Success(await _dietPlans.SaveAsync(dto, ct), "Diet plan created.");

    /// <summary>Updates a diet plan and replaces its meals.</summary>
    [HttpPut("{id:int}")]
    [HasPermission(Permissions.DietManage)]
    [ProducesResponseType(typeof(ApiResponse<DietPlanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<DietPlanDto>>> Update(
        int id, [FromBody] SaveDietPlanDto dto, CancellationToken ct)
    {
        dto.Id = id;
        return Success(await _dietPlans.SaveAsync(dto, ct), "Diet plan updated.");
    }

    /// <summary>Soft deletes a diet plan.</summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.DietManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _dietPlans.SoftDeleteAsync(id, ct);
        return SuccessMessage("Diet plan deleted.");
    }

    /// <summary>
    /// Lists the diet plans written for a member. A member signed in to their own portal may only
    /// request their own diet plans.
    /// </summary>
    [HttpGet("members/{memberId:int}")]
    [ProducesResponseType(typeof(ApiResponse<List<DietPlanDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<List<DietPlanDto>>>> GetMemberPlans(
        int memberId, CancellationToken ct)
    {
        MemberSelfAccess.EnsureCanRead(_currentUser, Permissions.DietView, memberId, "diet plans");

        return Success(await _dietPlans.GetMemberPlansAsync(memberId, ct));
    }
}
