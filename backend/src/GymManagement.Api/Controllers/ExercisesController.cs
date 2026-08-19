using GymManagement.Api.Authorization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GymManagement.Api.Controllers;

/// <summary>The exercise library that workout plans and sessions are built from.</summary>
[Route("api/exercises")]
public sealed class ExercisesController : ApiControllerBase
{
    private readonly IExerciseService _exerciseService;

    public ExercisesController(IExerciseService exerciseService) => _exerciseService = exerciseService;

    /// <summary>Returns a paged, filterable list of exercises.</summary>
    [HttpGet]
    [HasPermission(Permissions.ExercisesView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ExerciseDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<ExerciseDto>>>> GetPaged(
        [FromQuery] ExerciseQueryDto query, CancellationToken ct)
    {
        var result = await _exerciseService.GetPagedAsync(query, ct);
        return Success(result);
    }

    /// <summary>Returns a single exercise.</summary>
    [HttpGet("{id:int}")]
    [HasPermission(Permissions.ExercisesView)]
    [ProducesResponseType(typeof(ApiResponse<ExerciseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ExerciseDto>>> GetById(int id, CancellationToken ct)
    {
        var result = await _exerciseService.GetByIdAsync(id, ct);
        return Success(result);
    }

    /// <summary>Adds an exercise to the library.</summary>
    [HttpPost]
    [HasPermission(Permissions.ExercisesManage)]
    [ProducesResponseType(typeof(ApiResponse<ExerciseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ExerciseDto>>> Create(
        [FromBody] ExerciseDto dto, CancellationToken ct)
    {
        var result = await _exerciseService.CreateAsync(dto, ct);
        return Success(result, "Exercise created.");
    }

    /// <summary>Updates an exercise in the library.</summary>
    [HttpPut("{id:int}")]
    [HasPermission(Permissions.ExercisesManage)]
    [ProducesResponseType(typeof(ApiResponse<ExerciseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ExerciseDto>>> Update(
        int id, [FromBody] ExerciseDto dto, CancellationToken ct)
    {
        dto.Id = id;
        var result = await _exerciseService.UpdateAsync(dto, ct);
        return Success(result, "Exercise updated.");
    }

    /// <summary>Soft deletes an exercise so it can be restored from the recycle bin.</summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.ExercisesManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _exerciseService.SoftDeleteAsync(id, ct);
        return SuccessMessage("Exercise deleted.");
    }

    /// <summary>Restores a previously soft-deleted exercise.</summary>
    [HttpPost("{id:int}/restore")]
    [HasPermission(Permissions.ExercisesManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Restore(int id, CancellationToken ct)
    {
        await _exerciseService.RestoreAsync(id, ct);
        return SuccessMessage("Exercise restored.");
    }

    /// <summary>Returns exercise id/name pairs for combo boxes.</summary>
    [HttpGet("lookup")]
    [HasPermission(Permissions.ExercisesView)]
    [ProducesResponseType(typeof(ApiResponse<List<LookupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<LookupDto>>>> GetLookup(CancellationToken ct)
    {
        var result = await _exerciseService.GetLookupAsync(ct);
        return Success(result);
    }
}
