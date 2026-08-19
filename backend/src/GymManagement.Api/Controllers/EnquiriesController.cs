using GymManagement.Api.Authorization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GymManagement.Api.Controllers;

/// <summary>Walk-in, phone and website leads, up to the point they become members.</summary>
[Route("api/enquiries")]
public sealed class EnquiriesController : ApiControllerBase
{
    private readonly IEnquiryService _enquiries;

    public EnquiriesController(IEnquiryService enquiries) => _enquiries = enquiries;

    /// <summary>Returns a filtered, paged list of enquiries.</summary>
    [HttpGet]
    [HasPermission(Permissions.EnquiriesView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<EnquiryListDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<EnquiryListDto>>>> GetPaged(
        [FromQuery] EnquiryQueryDto query, CancellationToken ct) =>
        Success(await _enquiries.GetPagedAsync(query, ct));

    /// <summary>Returns a single enquiry.</summary>
    [HttpGet("{id:int}")]
    [HasPermission(Permissions.EnquiriesView)]
    [ProducesResponseType(typeof(ApiResponse<EnquiryDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EnquiryDetailDto>>> GetById(int id, CancellationToken ct) =>
        Success(await _enquiries.GetByIdAsync(id, ct));

    /// <summary>Records a new enquiry.</summary>
    [HttpPost]
    [HasPermission(Permissions.EnquiriesManage)]
    [ProducesResponseType(typeof(ApiResponse<EnquiryDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<EnquiryDetailDto>>> Create(
        [FromBody] SaveEnquiryDto dto, CancellationToken ct) =>
        Success(await _enquiries.SaveAsync(dto, ct), "Enquiry recorded.");

    /// <summary>Updates the enquiry identified by the route id.</summary>
    [HttpPut("{id:int}")]
    [HasPermission(Permissions.EnquiriesManage)]
    [ProducesResponseType(typeof(ApiResponse<EnquiryDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EnquiryDetailDto>>> Update(
        int id, [FromBody] SaveEnquiryDto dto, CancellationToken ct)
    {
        dto.Id = id;
        return Success(await _enquiries.SaveAsync(dto, ct), "Enquiry updated.");
    }

    /// <summary>Links the enquiry to the member record it produced and marks it converted.</summary>
    [HttpPost("{id:int}/convert/{memberId:int}")]
    [HasPermission(Permissions.EnquiriesManage)]
    [ProducesResponseType(typeof(ApiResponse<EnquiryDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<EnquiryDetailDto>>> Convert(
        int id, int memberId, CancellationToken ct) =>
        Success(await _enquiries.ConvertToMemberAsync(id, memberId, ct), "Enquiry converted.");

    /// <summary>Soft deletes an enquiry.</summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.EnquiriesManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _enquiries.SoftDeleteAsync(id, ct);
        return SuccessMessage("Enquiry deleted.");
    }

    /// <summary>Restores a soft-deleted enquiry.</summary>
    [HttpPost("{id:int}/restore")]
    [HasPermission(Permissions.EnquiriesManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Restore(int id, CancellationToken ct)
    {
        await _enquiries.RestoreAsync(id, ct);
        return SuccessMessage("Enquiry restored.");
    }
}
