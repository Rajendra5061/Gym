using GymManagement.Api.Authorization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GymManagement.Api.Controllers;

/// <summary>Runs the reporting catalogue and renders it to Excel or PDF.</summary>
public sealed class ReportsController : ApiControllerBase
{
    private readonly IReportService _reports;

    public ReportsController(IReportService reports) => _reports = reports;

    /// <summary>Runs a report and returns its columns, rows and totals.</summary>
    [HttpPost("run")]
    [HasPermission(Permissions.ReportsBasic)]
    [ProducesResponseType(typeof(ApiResponse<ReportResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ReportResultDto>>> Run(
        [FromBody] ReportRequestDto request, CancellationToken ct) =>
        Success(await _reports.RunAsync(request, ct));

    /// <summary>Runs a report and downloads it as a spreadsheet.</summary>
    [HttpPost("export/excel")]
    [HasPermission(Permissions.ReportsExport)]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportExcel(
        [FromBody] ReportRequestDto request, CancellationToken ct) =>
        FileDownload(await _reports.ExportExcelAsync(request, ct));

    /// <summary>Runs a report and downloads it as a PDF.</summary>
    [HttpPost("export/pdf")]
    [HasPermission(Permissions.ReportsExport)]
    [Produces("application/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportPdf(
        [FromBody] ReportRequestDto request, CancellationToken ct) =>
        FileDownload(await _reports.ExportPdfAsync(request, ct));

    /// <summary>Profit and loss summary for a date range, with revenue and expense breakdowns.</summary>
    [HttpGet("profit-loss")]
    [HasPermission(Permissions.ReportsFinancial)]
    [ProducesResponseType(typeof(ApiResponse<ProfitLossDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ProfitLossDto>>> GetProfitAndLoss(
        [FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct) =>
        Success(await _reports.GetProfitAndLossAsync(from, to, ct));
}
