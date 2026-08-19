using GymManagement.Api.Authorization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymManagement.Api.Controllers;

/// <summary>Member subscriptions: sale, renewal, plan changes, freezes and cancellation.</summary>
public sealed class SubscriptionsController : ApiControllerBase
{
    private readonly ISubscriptionService _subscriptions;
    private readonly ICurrentUserService _currentUser;

    public SubscriptionsController(ISubscriptionService subscriptions, ICurrentUserService currentUser)
    {
        _subscriptions = subscriptions;
        _currentUser = currentUser;
    }

    /// <summary>Server-side price breakdown for a prospective subscription. The client never prices a sale.</summary>
    [HttpPost("quote")]
    [HasPermission(Permissions.SubscriptionsView)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionQuoteDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SubscriptionQuoteDto>>> GetQuote(
        [FromBody] QuoteRequestDto request, CancellationToken ct) =>
        Success(await _subscriptions.GetQuoteAsync(request, ct));

    /// <summary>Returns a filtered, paged list of subscriptions.</summary>
    [HttpGet]
    [HasPermission(Permissions.SubscriptionsView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<SubscriptionDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<SubscriptionDto>>>> GetPaged(
        [FromQuery] SubscriptionQueryDto query, CancellationToken ct) =>
        Success(await _subscriptions.GetPagedAsync(query, ct));

    /// <summary>Returns a single subscription.</summary>
    [HttpGet("{id:int}")]
    [HasPermission(Permissions.SubscriptionsView)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SubscriptionDto>>> GetById(int id, CancellationToken ct) =>
        Success(await _subscriptions.GetByIdAsync(id, ct));

    /// <summary>
    /// Every subscription ever sold to a member, newest first. A member signed in to their own
    /// portal may only request their own subscriptions.
    /// </summary>
    [HttpGet("member/{memberId:int}")]
    [ProducesResponseType(typeof(ApiResponse<List<SubscriptionDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<List<SubscriptionDto>>>> GetForMember(
        int memberId, CancellationToken ct)
    {
        MemberSelfAccess.EnsureCanRead(_currentUser, Permissions.SubscriptionsView, memberId, "subscriptions");

        return Success(await _subscriptions.GetForMemberAsync(memberId, ct));
    }

    /// <summary>The member's currently active subscription, or null when they have none.</summary>
    [HttpGet("member/{memberId:int}/active")]
    [HasPermission(Permissions.SubscriptionsView)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SubscriptionDto?>>> GetActiveForMember(
        int memberId, CancellationToken ct) =>
        Success<SubscriptionDto?>(await _subscriptions.GetActiveForMemberAsync(memberId, ct));

    /// <summary>Audit trail of everything that happened to one subscription.</summary>
    [HttpGet("{id:int}/history")]
    [HasPermission(Permissions.SubscriptionsView)]
    [ProducesResponseType(typeof(ApiResponse<List<SubscriptionHistoryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<List<SubscriptionHistoryDto>>>> GetHistory(
        int id, CancellationToken ct) =>
        Success(await _subscriptions.GetHistoryAsync(id, ct));

    /// <summary>Sells a new subscription, optionally collecting a payment in the same transaction.</summary>
    [HttpPost]
    [HasPermission(Permissions.SubscriptionsCreate)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SubscriptionDto>>> Create(
        [FromBody] CreateSubscriptionDto dto, CancellationToken ct) =>
        Success(await _subscriptions.CreateAsync(dto, ct), "Subscription created.");

    /// <summary>Renews a member's subscription for a further term.</summary>
    [HttpPost("renew")]
    [HasPermission(Permissions.SubscriptionsCreate)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SubscriptionDto>>> Renew(
        [FromBody] RenewSubscriptionDto dto, CancellationToken ct) =>
        Success(await _subscriptions.RenewAsync(dto, ct), "Subscription renewed.");

    /// <summary>Upgrades or downgrades a subscription to a different plan.</summary>
    [HttpPost("change-plan")]
    [HasPermission(Permissions.SubscriptionsManage)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SubscriptionDto>>> ChangePlan(
        [FromBody] ChangePlanDto dto, CancellationToken ct) =>
        Success(await _subscriptions.ChangePlanAsync(dto, ct), "Plan changed.");

    /// <summary>Freezes a subscription for a period, extending the end date accordingly.</summary>
    [HttpPost("freeze")]
    [HasPermission(Permissions.SubscriptionsManage)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SubscriptionDto>>> Freeze(
        [FromBody] FreezeSubscriptionDto dto, CancellationToken ct) =>
        Success(await _subscriptions.FreezeAsync(dto, ct), "Subscription frozen.");

    /// <summary>Resumes a frozen subscription early.</summary>
    [HttpPost("resume")]
    [HasPermission(Permissions.SubscriptionsManage)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SubscriptionDto>>> Resume(
        [FromBody] ResumeSubscriptionDto dto, CancellationToken ct) =>
        Success(await _subscriptions.ResumeAsync(dto, ct), "Subscription resumed.");

    /// <summary>Cancels a subscription, optionally raising a refund for the unused term.</summary>
    [HttpPost("cancel")]
    [HasPermission(Permissions.SubscriptionsCancel)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SubscriptionDto>>> Cancel(
        [FromBody] CancelSubscriptionDto dto, CancellationToken ct) =>
        Success(await _subscriptions.CancelAsync(dto, ct), "Subscription cancelled.");

    /// <summary>Soft deletes a subscription.</summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.SubscriptionsManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _subscriptions.SoftDeleteAsync(id, ct);
        return SuccessMessage("Subscription deleted.");
    }

    /// <summary>Restores a soft-deleted subscription.</summary>
    [HttpPost("{id:int}/restore")]
    [HasPermission(Permissions.SubscriptionsManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Restore(int id, CancellationToken ct)
    {
        await _subscriptions.RestoreAsync(id, ct);
        return SuccessMessage("Subscription restored.");
    }

    /// <summary>Runs the expiry sweep: expires finished terms, auto-resumes freezes and raises reminders.</summary>
    [HttpPost("process-expiries")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<ExpiryProcessingResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ExpiryProcessingResultDto>>> ProcessExpiries(CancellationToken ct) =>
        Success(await _subscriptions.ProcessExpiriesAsync(ct), "Expiry processing complete.");
}
