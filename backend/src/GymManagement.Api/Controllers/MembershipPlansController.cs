using GymManagement.Api.Authorization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GymManagement.Api.Controllers;

/// <summary>Membership plan catalogue: pricing, duration and the rules subscriptions are priced from.</summary>
[Route("api/membership-plans")]
public sealed class MembershipPlansController : ApiControllerBase
{
    private readonly IMembershipPlanService _plans;

    public MembershipPlansController(IMembershipPlanService plans) => _plans = plans;

    /// <summary>Returns a filtered, paged list of membership plans.</summary>
    [HttpGet]
    [HasPermission(Permissions.PlansView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MembershipPlanDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<MembershipPlanDto>>>> GetPaged(
        [FromQuery] MembershipPlanQueryDto query, CancellationToken ct) =>
        Success(await _plans.GetPagedAsync(query, ct));

    /// <summary>Lists every currently sellable plan, in display order.</summary>
    [HttpGet("active")]
    [HasPermission(Permissions.PlansView)]
    [ProducesResponseType(typeof(ApiResponse<List<MembershipPlanDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<MembershipPlanDto>>>> GetActive(CancellationToken ct) =>
        Success(await _plans.GetActiveAsync(ct));

    /// <summary>Returns a single membership plan.</summary>
    [HttpGet("{id:int}")]
    [HasPermission(Permissions.PlansView)]
    [ProducesResponseType(typeof(ApiResponse<MembershipPlanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MembershipPlanDto>>> GetById(int id, CancellationToken ct) =>
        Success(await _plans.GetByIdAsync(id, ct));

    /// <summary>Creates a membership plan.</summary>
    [HttpPost]
    [HasPermission(Permissions.PlansManage)]
    [ProducesResponseType(typeof(ApiResponse<MembershipPlanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<MembershipPlanDto>>> Create(
        [FromBody] MembershipPlanDto dto, CancellationToken ct) =>
        Success(await _plans.CreateAsync(dto, ct), "Membership plan created.");

    /// <summary>Updates the membership plan identified by the route id.</summary>
    [HttpPut("{id:int}")]
    [HasPermission(Permissions.PlansManage)]
    [ProducesResponseType(typeof(ApiResponse<MembershipPlanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MembershipPlanDto>>> Update(
        int id, [FromBody] MembershipPlanDto dto, CancellationToken ct)
    {
        dto.Id = id;
        return Success(await _plans.UpdateAsync(dto, ct), "Membership plan updated.");
    }

    /// <summary>Soft deletes a membership plan.</summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.PlansManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _plans.SoftDeleteAsync(id, ct);
        return SuccessMessage("Membership plan deleted.");
    }

    /// <summary>Restores a soft-deleted membership plan.</summary>
    [HttpPost("{id:int}/restore")]
    [HasPermission(Permissions.PlansManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Restore(int id, CancellationToken ct)
    {
        await _plans.RestoreAsync(id, ct);
        return SuccessMessage("Membership plan restored.");
    }

    /// <summary>Id/name pairs for plan combo boxes.</summary>
    [HttpGet("lookup")]
    [HasPermission(Permissions.PlansView)]
    [ProducesResponseType(typeof(ApiResponse<List<LookupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<LookupDto>>>> GetLookup(CancellationToken ct) =>
        Success(await _plans.GetLookupAsync(ct));
}
