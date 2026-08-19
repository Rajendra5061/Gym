using GymManagement.Api.Authorization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GymManagement.Api.Controllers;

/// <summary>
/// Trainer salary payments. Recording one also writes the matching operating expense into the
/// "Salaries" category so the profit and loss report stays complete.
/// </summary>
[Route("api/salaries")]
public sealed class SalariesController : ApiControllerBase
{
    private readonly ISalaryPaymentService _salaries;

    public SalariesController(ISalaryPaymentService salaries) => _salaries = salaries;

    /// <summary>Returns a filtered, paged list of salary payments.</summary>
    [HttpGet]
    [HasPermission(Permissions.SalaryView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<SalaryPaymentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<SalaryPaymentDto>>>> GetPaged(
        [FromQuery] SalaryQueryDto query, CancellationToken ct) =>
        Success(await _salaries.GetPagedAsync(query, ct));

    /// <summary>Per-month and per-trainer salary totals for one calendar year (current year by default).</summary>
    [HttpGet("summary")]
    [HasPermission(Permissions.SalaryView)]
    [ProducesResponseType(typeof(ApiResponse<SalarySummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SalarySummaryDto>>> GetSummary(
        [FromQuery] int? year, CancellationToken ct) =>
        Success(await _salaries.GetSummaryAsync(year, ct));

    /// <summary>Records a salary payment and the matching expense in one step.</summary>
    [HttpPost]
    [HasPermission(Permissions.SalaryManage)]
    [ProducesResponseType(typeof(ApiResponse<SalaryPaymentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<SalaryPaymentDto>>> Create(
        [FromBody] SaveSalaryPaymentDto dto, CancellationToken ct) =>
        Success(await _salaries.CreateAsync(dto, ct),
            "Salary payment recorded and the matching expense was added to the Salaries category.");

    /// <summary>Soft deletes a salary payment. The matching expense record is kept.</summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.SalaryManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _salaries.SoftDeleteAsync(id, ct);
        return SuccessMessage("Salary payment deleted. The matching expense record was kept.");
    }
}
