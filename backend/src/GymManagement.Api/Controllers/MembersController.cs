using GymManagement.Api.Authorization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using GymManagement.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace GymManagement.Api.Controllers;

/// <summary>Result of registering a member: the member plus the optional login account that was created.</summary>
public sealed record CreateMemberResultDto(MemberDetailDto Member, TemporaryPasswordDto? Account);

/// <summary>
/// Member registration and everything hanging off a member record: history, documents, body
/// measurements, progress charts and trainer assignment.
/// </summary>
[Route("api/members")]
public sealed class MembersController : ApiControllerBase
{
    private readonly IMemberService _memberService;
    private readonly ICurrentUserService _currentUser;

    public MembersController(IMemberService memberService, ICurrentUserService currentUser)
    {
        _memberService = memberService;
        _currentUser = currentUser;
    }

    /// <summary>Returns a paged, filterable list of members with their subscription summary.</summary>
    [HttpGet]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MemberListDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<MemberListDto>>>> GetPaged(
        [FromQuery] MemberQueryDto query, CancellationToken ct)
    {
        var result = await _memberService.GetPagedAsync(query, ct);
        return Success(result);
    }

    /// <summary>
    /// Returns the full detail of a single member. Callers who cannot view members generally — a
    /// member signed in to their own portal — may only request their own member id.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<MemberDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MemberDetailDto>>> GetById(int id, CancellationToken ct)
    {
        MemberSelfAccess.EnsureCanRead(_currentUser, Permissions.MembersView, id, "member record");

        var result = await _memberService.GetByIdAsync(id, ct);
        return Success(result);
    }

    /// <summary>Looks a member up by the printed member code; returns null data when there is no match.</summary>
    [HttpGet("by-code/{code}")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(ApiResponse<MemberDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<MemberDetailDto?>>> GetByCode(
        string code, CancellationToken ct)
    {
        var result = await _memberService.GetByCodeAsync(code, ct);
        return Success(result);
    }

    /// <summary>
    /// Returns subscriptions, payments, attendance, sessions, measurements and documents in one call.
    /// A member signed in to their own portal may only request their own history.
    /// </summary>
    [HttpGet("{id:int}/history")]
    [ProducesResponseType(typeof(ApiResponse<MemberHistoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MemberHistoryDto>>> GetHistory(int id, CancellationToken ct)
    {
        MemberSelfAccess.EnsureCanRead(_currentUser, Permissions.MembersView, id, "history");

        var result = await _memberService.GetHistoryAsync(id, ct);
        return Success(result);
    }

    /// <summary>Registers a member and, when requested, a linked login account with a temporary password.</summary>
    [HttpPost]
    [HasPermission(Permissions.MembersCreate)]
    [ProducesResponseType(typeof(ApiResponse<CreateMemberResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<CreateMemberResultDto>>> Create(
        [FromBody] CreateMemberDto dto, CancellationToken ct)
    {
        var (member, account) = await _memberService.CreateAsync(dto, ct);
        return Success(new CreateMemberResultDto(member, account), "Member registered.");
    }

    /// <summary>Updates a member's profile details.</summary>
    [HttpPut("{id:int}")]
    [HasPermission(Permissions.MembersEdit)]
    [ProducesResponseType(typeof(ApiResponse<MemberDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MemberDetailDto>>> Update(
        int id, [FromBody] UpdateMemberDto dto, CancellationToken ct)
    {
        dto.Id = id;
        var result = await _memberService.UpdateAsync(dto, ct);
        return Success(result, "Member updated.");
    }

    /// <summary>Changes a member's status, optionally recording the reason.</summary>
    [HttpPost("{id:int}/status")]
    [HasPermission(Permissions.MembersEdit)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> SetStatus(
        int id, [FromBody] MemberStatus status, [FromQuery] string? reason, CancellationToken ct)
    {
        await _memberService.SetStatusAsync(id, status, reason, ct);
        return SuccessMessage($"Member status set to {status}.");
    }

    /// <summary>Soft deletes a member so the record can be restored from the recycle bin.</summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.MembersDelete)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _memberService.SoftDeleteAsync(id, ct);
        return SuccessMessage("Member deleted.");
    }

    /// <summary>Restores a previously soft-deleted member.</summary>
    [HttpPost("{id:int}/restore")]
    [HasPermission(Permissions.MembersRestore)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Restore(int id, CancellationToken ct)
    {
        await _memberService.RestoreAsync(id, ct);
        return SuccessMessage("Member restored.");
    }

    /// <summary>Searches members and returns id/name pairs for combo boxes and check-in screens.</summary>
    [HttpGet("lookup")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(ApiResponse<List<LookupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<LookupDto>>>> GetLookup(
        [FromQuery] string? term, [FromQuery] int take = 20, CancellationToken ct = default)
    {
        var result = await _memberService.SearchLookupAsync(term, take, ct);
        return Success(result);
    }

    /// <summary>Assigns a trainer to the member, or clears the assignment when no trainer id is supplied.</summary>
    [HttpPost("{id:int}/assign-trainer")]
    [HasPermission(Permissions.MembersEdit)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> AssignTrainer(
        int id, [FromQuery] int? trainerId, CancellationToken ct)
    {
        await _memberService.AssignTrainerAsync(id, trainerId, ct);
        return SuccessMessage(trainerId.HasValue ? "Trainer assigned." : "Trainer assignment cleared.");
    }

    /// <summary>Lists the documents stored against a member.</summary>
    [HttpGet("{id:int}/documents")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(ApiResponse<List<MemberDocumentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<List<MemberDocumentDto>>>> GetDocuments(
        int id, CancellationToken ct)
    {
        var result = await _memberService.GetDocumentsAsync(id, ct);
        return Success(result);
    }

    /// <summary>Uploads a document (max 10 MB) for a member.</summary>
    [HttpPost("{id:int}/documents")]
    [HasPermission(Permissions.MembersEdit)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [ProducesResponseType(typeof(ApiResponse<MemberDocumentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MemberDocumentDto>>> AddDocument(
        int id, IFormFile file, [FromForm] string documentType, [FromForm] string? notes,
        CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);

        var result = await _memberService.AddDocumentAsync(
            id, documentType, file.FileName, buffer.ToArray(), file.ContentType, notes, ct);

        return Success(result, "Document uploaded.");
    }

    /// <summary>Downloads a stored member document.</summary>
    [HttpGet("documents/{documentId:int}/download")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadDocument(int documentId, CancellationToken ct)
    {
        var file = await _memberService.GetDocumentFileAsync(documentId, ct);
        return FileDownload(file);
    }

    /// <summary>Deletes a stored member document.</summary>
    [HttpDelete("documents/{documentId:int}")]
    [HasPermission(Permissions.MembersEdit)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeleteDocument(int documentId, CancellationToken ct)
    {
        await _memberService.DeleteDocumentAsync(documentId, ct);
        return SuccessMessage("Document deleted.");
    }

    /// <summary>Lists a member's recorded body measurements, newest first.</summary>
    [HttpGet("{id:int}/measurements")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(ApiResponse<List<MemberMeasurementDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<List<MemberMeasurementDto>>>> GetMeasurements(
        int id, CancellationToken ct)
    {
        var result = await _memberService.GetMeasurementsAsync(id, ct);
        return Success(result);
    }

    /// <summary>Creates or updates a body-measurement entry for the member.</summary>
    [HttpPost("{id:int}/measurements")]
    [HasPermission(Permissions.MembersEdit)]
    [ProducesResponseType(typeof(ApiResponse<MemberMeasurementDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MemberMeasurementDto>>> SaveMeasurement(
        int id, [FromBody] MemberMeasurementDto dto, CancellationToken ct)
    {
        dto.MemberId = id;
        var result = await _memberService.SaveMeasurementAsync(dto, ct);
        return Success(result, "Measurement saved.");
    }

    /// <summary>Deletes a body-measurement entry.</summary>
    [HttpDelete("measurements/{measurementId:int}")]
    [HasPermission(Permissions.MembersEdit)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeleteMeasurement(
        int measurementId, CancellationToken ct)
    {
        await _memberService.DeleteMeasurementAsync(measurementId, ct);
        return SuccessMessage("Measurement deleted.");
    }

    /// <summary>Returns the member's progress series (weight, body fat, BMI, volume, calories).</summary>
    [HttpGet("{id:int}/progress")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(ApiResponse<MemberProgressDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MemberProgressDto>>> GetProgress(
        int id, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var result = await _memberService.GetProgressAsync(id, from, to, ct);
        return Success(result);
    }
}
