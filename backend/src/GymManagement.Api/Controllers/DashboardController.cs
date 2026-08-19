using GymManagement.Api.Authorization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GymManagement.Api.Controllers;

/// <summary>Admin dashboard cards and charts, plus the member's own portal view.</summary>
public sealed class DashboardController : ApiControllerBase
{
    private readonly IDashboardService _dashboard;
    private readonly ICurrentUserService _currentUser;

    public DashboardController(IDashboardService dashboard, ICurrentUserService currentUser)
    {
        _dashboard = dashboard;
        _currentUser = currentUser;
    }

    /// <summary>Every dashboard card, chart and feed in a single call.</summary>
    [HttpGet]
    [HasPermission(Permissions.DashboardView)]
    [ProducesResponseType(typeof(ApiResponse<DashboardDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<DashboardDto>>> GetDashboard(CancellationToken ct) =>
        Success(await _dashboard.GetDashboardAsync(ct));

    /// <summary>Just the headline counters, for lightweight polling.</summary>
    [HttpGet("stats")]
    [HasPermission(Permissions.DashboardView)]
    [ProducesResponseType(typeof(ApiResponse<DashboardStatsDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<DashboardStatsDto>>> GetStats(CancellationToken ct) =>
        Success(await _dashboard.GetStatsAsync(ct));

    /// <summary>
    /// The compact dashboard for one member. Callers who cannot view members generally — a member
    /// signed in to their own portal — may only request their own member id.
    /// </summary>
    [HttpGet("member/{memberId:int}")]
    [ProducesResponseType(typeof(ApiResponse<MemberDashboardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MemberDashboardDto>>> GetMemberDashboard(
        int memberId, CancellationToken ct)
    {
        MemberSelfAccess.EnsureCanRead(_currentUser, Permissions.MembersView, memberId, "dashboard");

        return Success(await _dashboard.GetMemberDashboardAsync(memberId, ct));
    }
}
