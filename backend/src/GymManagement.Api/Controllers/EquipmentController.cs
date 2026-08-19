using GymManagement.Api.Authorization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GymManagement.Api.Controllers;

/// <summary>Gym equipment inventory and its service schedule.</summary>
[Route("api/equipment")]
public sealed class EquipmentController : ApiControllerBase
{
    private readonly IEquipmentService _equipment;

    public EquipmentController(IEquipmentService equipment) => _equipment = equipment;

    /// <summary>Returns a filtered, paged list of equipment.</summary>
    [HttpGet]
    [HasPermission(Permissions.EquipmentView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<EquipmentListDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<EquipmentListDto>>>> GetPaged(
        [FromQuery] EquipmentQueryDto query, CancellationToken ct) =>
        Success(await _equipment.GetPagedAsync(query, ct));

    /// <summary>Distinct categories already in use, for filter drop-downs.</summary>
    [HttpGet("categories")]
    [HasPermission(Permissions.EquipmentView)]
    [ProducesResponseType(typeof(ApiResponse<List<string>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<string>>>> GetCategories(CancellationToken ct) =>
        Success(await _equipment.GetCategoriesAsync(ct));

    /// <summary>Returns a single equipment record.</summary>
    [HttpGet("{id:int}")]
    [HasPermission(Permissions.EquipmentView)]
    [ProducesResponseType(typeof(ApiResponse<EquipmentDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EquipmentDetailDto>>> GetById(int id, CancellationToken ct) =>
        Success(await _equipment.GetByIdAsync(id, ct));

    /// <summary>Adds a new equipment record.</summary>
    [HttpPost]
    [HasPermission(Permissions.EquipmentManage)]
    [ProducesResponseType(typeof(ApiResponse<EquipmentDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<EquipmentDetailDto>>> Create(
        [FromBody] SaveEquipmentDto dto, CancellationToken ct) =>
        Success(await _equipment.SaveAsync(dto, ct), "Equipment added.");

    /// <summary>Updates the equipment record identified by the route id.</summary>
    [HttpPut("{id:int}")]
    [HasPermission(Permissions.EquipmentManage)]
    [ProducesResponseType(typeof(ApiResponse<EquipmentDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EquipmentDetailDto>>> Update(
        int id, [FromBody] SaveEquipmentDto dto, CancellationToken ct)
    {
        dto.Id = id;
        return Success(await _equipment.SaveAsync(dto, ct), "Equipment updated.");
    }

    /// <summary>Soft deletes an equipment record.</summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.EquipmentManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _equipment.SoftDeleteAsync(id, ct);
        return SuccessMessage("Equipment deleted.");
    }

    /// <summary>Restores a soft-deleted equipment record.</summary>
    [HttpPost("{id:int}/restore")]
    [HasPermission(Permissions.EquipmentManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Restore(int id, CancellationToken ct)
    {
        await _equipment.RestoreAsync(id, ct);
        return SuccessMessage("Equipment restored.");
    }
}
