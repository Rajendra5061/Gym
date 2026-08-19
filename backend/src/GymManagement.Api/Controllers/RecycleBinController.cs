using GymManagement.Api.Authorization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GymManagement.Api.Controllers;

/// <summary>Soft-deleted records across every module, with restore and permanent purge.</summary>
[Route("api/recycle-bin")]
public sealed class RecycleBinController : ApiControllerBase
{
    private readonly IRecycleBinService _recycleBin;

    public RecycleBinController(IRecycleBinService recycleBin) => _recycleBin = recycleBin;

    /// <summary>Returns a filtered, paged list of deleted records.</summary>
    [HttpGet]
    [HasPermission(Permissions.RecycleBinView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<RecycleBinItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<RecycleBinItemDto>>>> GetPaged(
        [FromQuery] RecycleBinQueryDto query, CancellationToken ct) =>
        Success(await _recycleBin.GetPagedAsync(query, ct));

    /// <summary>Entity types that currently hold deleted records, for the filter combo box.</summary>
    [HttpGet("entity-types")]
    [HasPermission(Permissions.RecycleBinView)]
    [ProducesResponseType(typeof(ApiResponse<List<LookupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<LookupDto>>>> GetEntityTypes(CancellationToken ct) =>
        Success(await _recycleBin.GetEntityTypesAsync(ct));

    /// <summary>Restores the selected deleted records and returns how many were restored.</summary>
    [HttpPost("restore")]
    [HasPermission(Permissions.RecycleBinRestore)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<int>>> Restore(
        [FromBody] RecycleBinActionDto dto, CancellationToken ct) =>
        Success(await _recycleBin.RestoreAsync(dto, ct), "Records restored.");

    /// <summary>Permanently deletes the selected records and returns how many were purged.</summary>
    [HttpPost("purge")]
    [HasPermission(Permissions.RecycleBinPurge)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<int>>> Purge(
        [FromBody] RecycleBinActionDto dto, CancellationToken ct) =>
        Success(await _recycleBin.PurgeAsync(dto, ct), "Records permanently deleted.");
}
