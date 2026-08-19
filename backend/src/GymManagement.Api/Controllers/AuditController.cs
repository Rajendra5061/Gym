using GymManagement.Api.Authorization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GymManagement.Api.Controllers;

/// <summary>Read-only access to the audit trail and login attempt history.</summary>
[HasPermission(Permissions.AuditView)]
public sealed class AuditController : ApiControllerBase
{
    private readonly IAuditQueryService _audit;

    public AuditController(IAuditQueryService audit) => _audit = audit;

    /// <summary>Returns a filtered, paged audit log.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AuditLogDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<AuditLogDto>>>> GetPaged(
        [FromQuery] AuditQueryDto query, CancellationToken ct) =>
        Success(await _audit.GetPagedAsync(query, ct));

    /// <summary>Returns a paged list of successful and failed sign-in attempts.</summary>
    [HttpGet("login-attempts")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<LoginAttemptDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<LoginAttemptDto>>>> GetLoginAttempts(
        [FromQuery] PagedRequest query, CancellationToken ct) =>
        Success(await _audit.GetLoginAttemptsAsync(query, ct));

    /// <summary>The most recent activity entries, for the dashboard feed.</summary>
    [HttpGet("recent-activity")]
    [ProducesResponseType(typeof(ApiResponse<List<RecentActivityDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<RecentActivityDto>>>> GetRecentActivity(
        [FromQuery] int take = 10, CancellationToken ct = default) =>
        Success(await _audit.GetRecentActivityAsync(take, ct));

    /// <summary>Distinct action names present in the audit log, for the filter combo box.</summary>
    [HttpGet("actions")]
    [ProducesResponseType(typeof(ApiResponse<List<string>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<string>>>> GetDistinctActions(CancellationToken ct) =>
        Success(await _audit.GetDistinctActionsAsync(ct));

    /// <summary>Distinct entity names present in the audit log, for the filter combo box.</summary>
    [HttpGet("entities")]
    [ProducesResponseType(typeof(ApiResponse<List<string>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<string>>>> GetDistinctEntities(CancellationToken ct) =>
        Success(await _audit.GetDistinctEntitiesAsync(ct));
}
