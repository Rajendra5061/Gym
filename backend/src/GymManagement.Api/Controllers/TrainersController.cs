using GymManagement.Api.Authorization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using GymManagement.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace GymManagement.Api.Controllers;

/// <summary>Result of adding a trainer: the trainer plus the optional login account that was created.</summary>
public sealed record CreateTrainerResultDto(TrainerDetailDto Trainer, TemporaryPasswordDto? Account);

/// <summary>Trainer records, their member assignments and workload summaries.</summary>
[Route("api/trainers")]
public sealed class TrainersController : ApiControllerBase
{
    private readonly ITrainerService _trainerService;

    public TrainersController(ITrainerService trainerService) => _trainerService = trainerService;

    /// <summary>Returns a paged, filterable list of trainers.</summary>
    [HttpGet]
    [HasPermission(Permissions.TrainersView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<TrainerListDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<TrainerListDto>>>> GetPaged(
        [FromQuery] TrainerQueryDto query, CancellationToken ct)
    {
        var result = await _trainerService.GetPagedAsync(query, ct);
        return Success(result);
    }

    /// <summary>Returns the full detail of a single trainer.</summary>
    [HttpGet("{id:int}")]
    [HasPermission(Permissions.TrainersView)]
    [ProducesResponseType(typeof(ApiResponse<TrainerDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TrainerDetailDto>>> GetById(int id, CancellationToken ct)
    {
        var result = await _trainerService.GetByIdAsync(id, ct);
        return Success(result);
    }

    /// <summary>Adds a trainer and, when requested, a linked login account with a temporary password.</summary>
    [HttpPost]
    [HasPermission(Permissions.TrainersManage)]
    [ProducesResponseType(typeof(ApiResponse<CreateTrainerResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<CreateTrainerResultDto>>> Create(
        [FromBody] CreateTrainerDto dto, CancellationToken ct)
    {
        var (trainer, account) = await _trainerService.CreateAsync(dto, ct);
        return Success(new CreateTrainerResultDto(trainer, account), "Trainer added.");
    }

    /// <summary>Updates a trainer's profile details.</summary>
    [HttpPut("{id:int}")]
    [HasPermission(Permissions.TrainersManage)]
    [ProducesResponseType(typeof(ApiResponse<TrainerDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TrainerDetailDto>>> Update(
        int id, [FromBody] UpdateTrainerDto dto, CancellationToken ct)
    {
        dto.Id = id;
        var result = await _trainerService.UpdateAsync(dto, ct);
        return Success(result, "Trainer updated.");
    }

    /// <summary>Changes a trainer's status (active, inactive, on leave, resigned).</summary>
    [HttpPost("{id:int}/status")]
    [HasPermission(Permissions.TrainersManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> SetStatus(
        int id, [FromBody] TrainerStatus status, CancellationToken ct)
    {
        await _trainerService.SetStatusAsync(id, status, ct);
        return SuccessMessage($"Trainer status set to {status}.");
    }

    /// <summary>Soft deletes a trainer so the record can be restored from the recycle bin.</summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.TrainersManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _trainerService.SoftDeleteAsync(id, ct);
        return SuccessMessage("Trainer deleted.");
    }

    /// <summary>Restores a previously soft-deleted trainer.</summary>
    [HttpPost("{id:int}/restore")]
    [HasPermission(Permissions.TrainersManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Restore(int id, CancellationToken ct)
    {
        await _trainerService.RestoreAsync(id, ct);
        return SuccessMessage("Trainer restored.");
    }

    /// <summary>Returns trainer id/name pairs for combo boxes.</summary>
    [HttpGet("lookup")]
    [HasPermission(Permissions.TrainersView)]
    [ProducesResponseType(typeof(ApiResponse<List<LookupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<LookupDto>>>> GetLookup(
        [FromQuery] bool onlyActive = true, CancellationToken ct = default)
    {
        var result = await _trainerService.GetLookupAsync(onlyActive, ct);
        return Success(result);
    }

    /// <summary>Assigns a batch of members to a trainer.</summary>
    [HttpPost("assign-members")]
    [HasPermission(Permissions.TrainersManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> AssignMembers(
        [FromBody] AssignTrainerDto dto, CancellationToken ct)
    {
        await _trainerService.AssignMembersAsync(dto, ct);
        return SuccessMessage("Members assigned to the trainer.");
    }

    /// <summary>Returns a page of the members currently assigned to the trainer.</summary>
    [HttpGet("{id:int}/members")]
    [HasPermission(Permissions.TrainersView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MemberListDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PagedResult<MemberListDto>>>> GetAssignedMembers(
        int id, [FromQuery] PagedRequest query, CancellationToken ct)
    {
        var result = await _trainerService.GetAssignedMembersAsync(id, query, ct);
        return Success(result);
    }

    /// <summary>Returns per-trainer workload figures for the given period.</summary>
    [HttpGet("workload")]
    [HasPermission(Permissions.TrainersView)]
    [ProducesResponseType(typeof(ApiResponse<List<TrainerWorkloadDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<List<TrainerWorkloadDto>>>> GetWorkload(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var result = await _trainerService.GetWorkloadAsync(from, to, ct);
        return Success(result);
    }
}
