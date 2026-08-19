using GymManagement.Api.Authorization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GymManagement.Api.Controllers;

/// <summary>
/// Member suggestions, complaints and ratings. Staff manage every row; a member reaches only
/// their own through <c>GET mine</c> and <c>POST</c>, which the service enforces.
/// </summary>
[Route("api/feedback")]
public sealed class FeedbackController : ApiControllerBase
{
    private readonly IFeedbackService _feedback;

    public FeedbackController(IFeedbackService feedback) => _feedback = feedback;

    /// <summary>Returns a filtered, paged list of feedback.</summary>
    [HttpGet]
    [HasPermission(Permissions.FeedbackView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<FeedbackListDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<FeedbackListDto>>>> GetPaged(
        [FromQuery] FeedbackQueryDto query, CancellationToken ct) =>
        Success(await _feedback.GetPagedAsync(query, ct));

    /// <summary>Feedback submitted by the signed-in member.</summary>
    [HttpGet("mine")]
    [HasPermission(Permissions.FeedbackSubmit)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<FeedbackListDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<FeedbackListDto>>>> GetMine(
        [FromQuery] PagedRequest query, CancellationToken ct) =>
        Success(await _feedback.GetMineAsync(query, ct));

    /// <summary>Returns a single feedback record.</summary>
    [HttpGet("{id:int}")]
    [HasPermission(Permissions.FeedbackView)]
    [ProducesResponseType(typeof(ApiResponse<FeedbackDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<FeedbackDetailDto>>> GetById(int id, CancellationToken ct) =>
        Success(await _feedback.GetByIdAsync(id, ct));

    /// <summary>Submits feedback as the signed-in member.</summary>
    [HttpPost]
    [HasPermission(Permissions.FeedbackSubmit)]
    [ProducesResponseType(typeof(ApiResponse<FeedbackDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<FeedbackDetailDto>>> Submit(
        [FromBody] CreateFeedbackDto dto, CancellationToken ct) =>
        Success(await _feedback.SubmitAsync(dto, ct), "Thank you for your feedback.");

    /// <summary>Creates or updates a feedback record from the back office.</summary>
    [HttpPut("{id:int}")]
    [HasPermission(Permissions.FeedbackManage)]
    [ProducesResponseType(typeof(ApiResponse<FeedbackDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<FeedbackDetailDto>>> Update(
        int id, [FromBody] SaveFeedbackDto dto, CancellationToken ct)
    {
        dto.Id = id;
        return Success(await _feedback.SaveAsync(dto, ct), "Feedback updated.");
    }

    /// <summary>Records the administrator reply and moves the item to Reviewed.</summary>
    [HttpPost("{id:int}/respond")]
    [HasPermission(Permissions.FeedbackManage)]
    [ProducesResponseType(typeof(ApiResponse<FeedbackDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<FeedbackDetailDto>>> Respond(
        int id, [FromBody] RespondToFeedbackRequest request, CancellationToken ct) =>
        Success(await _feedback.RespondAsync(id, request.Response, ct), "Response sent.");

    /// <summary>Soft deletes a feedback record.</summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.FeedbackManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _feedback.SoftDeleteAsync(id, ct);
        return SuccessMessage("Feedback deleted.");
    }

    /// <summary>Restores a soft-deleted feedback record.</summary>
    [HttpPost("{id:int}/restore")]
    [HasPermission(Permissions.FeedbackManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Restore(int id, CancellationToken ct)
    {
        await _feedback.RestoreAsync(id, ct);
        return SuccessMessage("Feedback restored.");
    }

    /// <summary>Body of <c>POST api/feedback/{id}/respond</c>.</summary>
    public sealed class RespondToFeedbackRequest
    {
        public string Response { get; set; } = string.Empty;
    }
}
