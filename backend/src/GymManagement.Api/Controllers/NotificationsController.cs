using GymManagement.Api.Authorization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GymManagement.Api.Controllers;

/// <summary>In-app notifications and the reminder generator that raises them.</summary>
public sealed class NotificationsController : ApiControllerBase
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications) => _notifications = notifications;

    /// <summary>Returns a filtered, paged list of notifications.</summary>
    [HttpGet]
    [HasPermission(Permissions.NotificationsView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<NotificationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<NotificationDto>>>> GetPaged(
        [FromQuery] NotificationQueryDto query, CancellationToken ct) =>
        Success(await _notifications.GetPagedAsync(query, ct));

    /// <summary>Unread and severity counters for the notification bell.</summary>
    [HttpGet("counts")]
    [HasPermission(Permissions.NotificationsView)]
    [ProducesResponseType(typeof(ApiResponse<NotificationCountsDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<NotificationCountsDto>>> GetCounts(CancellationToken ct) =>
        Success(await _notifications.GetCountsAsync(ct));

    /// <summary>Sends a notification to a user, a member or all staff.</summary>
    [HttpPost]
    [HasPermission(Permissions.NotificationsManage)]
    [ProducesResponseType(typeof(ApiResponse<NotificationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<NotificationDto>>> Create(
        [FromBody] CreateNotificationDto dto, CancellationToken ct) =>
        Success(await _notifications.CreateAsync(dto, ct), "Notification sent.");

    /// <summary>Marks the supplied notifications as read.</summary>
    [HttpPost("mark-read")]
    [HasPermission(Permissions.NotificationsView)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse>> MarkAsRead(
        [FromBody] List<int> ids, CancellationToken ct)
    {
        await _notifications.MarkAsReadAsync(ids, ct);
        return SuccessMessage("Notifications marked as read.");
    }

    /// <summary>Marks every notification visible to the caller as read.</summary>
    [HttpPost("mark-all-read")]
    [HasPermission(Permissions.NotificationsView)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> MarkAllAsRead(CancellationToken ct)
    {
        await _notifications.MarkAllAsReadAsync(ct);
        return SuccessMessage("All notifications marked as read.");
    }

    /// <summary>Deletes a notification.</summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.NotificationsManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _notifications.DeleteAsync(id, ct);
        return SuccessMessage("Notification deleted.");
    }

    /// <summary>Raises expiry and pending-payment reminders. Idempotent for a given day.</summary>
    [HttpPost("generate-alerts")]
    [HasPermission(Permissions.NotificationsManage)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<int>>> GenerateSystemAlerts(CancellationToken ct) =>
        Success(await _notifications.GenerateSystemAlertsAsync(ct), "System alerts generated.");
}
