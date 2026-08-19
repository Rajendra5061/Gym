using GymManagement.Api.Authorization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GymManagement.Api.Controllers;

/// <summary>Gym floor check-in / check-out and the attendance registers built on top of it.</summary>
public sealed class AttendanceController : ApiControllerBase
{
    private readonly IAttendanceService _attendance;

    public AttendanceController(IAttendanceService attendance) => _attendance = attendance;

    /// <summary>Records a check-in for a member identified by id or member code.</summary>
    [HttpPost("check-in")]
    [HasPermission(Permissions.AttendanceManage)]
    [ProducesResponseType(typeof(ApiResponse<AttendanceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AttendanceDto>>> CheckIn(
        [FromBody] CheckInRequestDto request, CancellationToken ct) =>
        Success(await _attendance.CheckInAsync(request, ct), "Check-in recorded.");

    /// <summary>Closes an open attendance record and computes the visit duration.</summary>
    [HttpPost("check-out")]
    [HasPermission(Permissions.AttendanceManage)]
    [ProducesResponseType(typeof(ApiResponse<AttendanceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AttendanceDto>>> CheckOut(
        [FromBody] CheckOutRequestDto request, CancellationToken ct) =>
        Success(await _attendance.CheckOutAsync(request, ct), "Check-out recorded.");

    /// <summary>Returns a filtered, paged attendance register.</summary>
    [HttpGet]
    [HasPermission(Permissions.AttendanceView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AttendanceDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<AttendanceDto>>>> GetPaged(
        [FromQuery] AttendanceQueryDto query, CancellationToken ct) =>
        Success(await _attendance.GetPagedAsync(query, ct));

    /// <summary>Header counters and the hourly breakdown for a single day (defaults to today).</summary>
    [HttpGet("summary")]
    [HasPermission(Permissions.AttendanceView)]
    [ProducesResponseType(typeof(ApiResponse<AttendanceSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AttendanceSummaryDto>>> GetSummary(
        [FromQuery] DateTime? date, CancellationToken ct) =>
        Success(await _attendance.GetSummaryAsync(date, ct));

    /// <summary>Lists the members currently inside the gym (checked in, not yet checked out).</summary>
    [HttpGet("in-gym")]
    [HasPermission(Permissions.AttendanceView)]
    [ProducesResponseType(typeof(ApiResponse<List<AttendanceDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<AttendanceDto>>>> GetCurrentlyInGym(CancellationToken ct) =>
        Success(await _attendance.GetCurrentlyInGymAsync(ct));

    /// <summary>Deletes an attendance record that was captured in error.</summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.AttendanceManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _attendance.DeleteAsync(id, ct);
        return SuccessMessage("Attendance record deleted.");
    }

    /// <summary>Daily attendance counts across a date range, for the trend chart.</summary>
    [HttpGet("trend")]
    [HasPermission(Permissions.AttendanceView)]
    [ProducesResponseType(typeof(ApiResponse<List<ChartSeriesDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<List<ChartSeriesDto>>>> GetTrend(
        [FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct) =>
        Success(await _attendance.GetTrendAsync(from, to, ct));
}
