using GymManagement.Api.Authorization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymManagement.Api.Controllers;

/// <summary>Payment collection, receipts, UPI intents, refunds and outstanding balances.</summary>
public sealed class PaymentsController : ApiControllerBase
{
    private readonly IPaymentService _payments;
    private readonly ICurrentUserService _currentUser;
    private readonly IPaymentWebhookService _webhooks;

    public PaymentsController(IPaymentService payments, ICurrentUserService currentUser,
        IPaymentWebhookService webhooks)
    {
        _payments = payments;
        _currentUser = currentUser;
        _webhooks = webhooks;
    }

    /// <summary>Whether automatic reconciliation is wired up, and where the gateway should post.</summary>
    /// <remarks>
    /// Never carries the signing secret. <c>signingSecretConfigured</c> reports only that one
    /// resolved; the value is supplied through <c>Payments__Gateway__Secret</c> and is deliberately
    /// not stored in the database, which any administrator can read back over the API.
    ///
    /// <c>webhookUrl</c> is composed from the request this call arrived on. Behind a proxy that is
    /// only correct when forwarded headers are honoured, so <c>webhookPath</c> is always returned
    /// too — it is the half the server can state without guessing.
    /// </remarks>
    [HttpGet("gateway")]
    [HasPermission(Permissions.SettingsView)]
    [ProducesResponseType(typeof(ApiResponse<PaymentGatewayStatusDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PaymentGatewayStatusDto>>> GetGatewayStatus(CancellationToken ct)
    {
        var status = await _webhooks.GetStatusAsync(ct);

        if (!string.IsNullOrWhiteSpace(status.WebhookPath))
            status.WebhookUrl = $"{Request.Scheme}://{Request.Host}{status.WebhookPath}";

        return Success(status);
    }

    /// <summary>Returns a filtered, paged list of payments.</summary>
    [HttpGet]
    [HasPermission(Permissions.PaymentsView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PaymentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<PaymentDto>>>> GetPaged(
        [FromQuery] PaymentQueryDto query, CancellationToken ct) =>
        Success(await _payments.GetPagedAsync(query, ct));

    /// <summary>Returns a single payment.</summary>
    [HttpGet("{id:int}")]
    [HasPermission(Permissions.PaymentsView)]
    [ProducesResponseType(typeof(ApiResponse<PaymentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PaymentDto>>> GetById(int id, CancellationToken ct) =>
        Success(await _payments.GetByIdAsync(id, ct));

    /// <summary>
    /// Full payment history for one member. A member signed in to their own portal may only request
    /// their own payments.
    /// </summary>
    [HttpGet("member/{memberId:int}")]
    [ProducesResponseType(typeof(ApiResponse<List<PaymentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<List<PaymentDto>>>> GetForMember(
        int memberId, CancellationToken ct)
    {
        MemberSelfAccess.EnsureCanRead(_currentUser, Permissions.PaymentsView, memberId, "payments");

        return Success(await _payments.GetForMemberAsync(memberId, ct));
    }

    /// <summary>Records a payment against a member and, optionally, a subscription.</summary>
    [HttpPost]
    [HasPermission(Permissions.PaymentsCollect)]
    [ProducesResponseType(typeof(ApiResponse<PaymentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PaymentDto>>> Create(
        [FromBody] CreatePaymentDto dto, CancellationToken ct) =>
        Success(await _payments.CreateAsync(dto, ct), "Payment recorded.");

    /// <summary>Confirms a payment that was awaiting manual verification.</summary>
    [HttpPost("confirm")]
    [HasPermission(Permissions.PaymentsCollect)]
    [ProducesResponseType(typeof(ApiResponse<PaymentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PaymentDto>>> Confirm(
        [FromBody] ConfirmPaymentDto dto, CancellationToken ct) =>
        Success(await _payments.ConfirmAsync(dto, ct), "Payment confirmed.");

    /// <summary>Soft deletes a payment recorded in error.</summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.PaymentsRefund)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _payments.SoftDeleteAsync(id, ct);
        return SuccessMessage("Payment deleted.");
    }

    /// <summary>Restores a soft-deleted payment.</summary>
    [HttpPost("{id:int}/restore")]
    [HasPermission(Permissions.PaymentsRefund)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Restore(int id, CancellationToken ct)
    {
        await _payments.RestoreAsync(id, ct);
        return SuccessMessage("Payment restored.");
    }

    /// <summary>Returns the printable receipt model for a payment.</summary>
    [HttpGet("{id:int}/receipt")]
    [HasPermission(Permissions.PaymentsView)]
    [ProducesResponseType(typeof(ApiResponse<PaymentReceiptDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PaymentReceiptDto>>> GetReceipt(int id, CancellationToken ct) =>
        Success(await _payments.GetReceiptAsync(id, ct));

    /// <summary>
    /// Downloads the payment receipt as a PDF. A member signed in to their own portal may only
    /// download a receipt for a payment recorded against them.
    /// </summary>
    [HttpGet("{id:int}/receipt/pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReceiptPdf(int id, CancellationToken ct)
    {
        // The route carries a payment id, so the owning member has to be resolved from the payment
        // itself before the self-access rule can be applied. Skipped for callers who already hold
        // payments.view, so staff downloads cost no extra query.
        if (!_currentUser.HasPermission(Permissions.PaymentsView))
        {
            var ownerMemberId = await _payments.GetOwnerMemberIdAsync(id, ct);
            MemberSelfAccess.EnsureCanRead(_currentUser, Permissions.PaymentsView, ownerMemberId, "receipts");
        }

        return FileDownload(await _payments.GetReceiptPdfAsync(id, ct));
    }

    /// <summary>Builds the UPI deep link / QR payload for collecting a payment.</summary>
    [HttpPost("upi-intent")]
    [HasPermission(Permissions.PaymentsCollect)]
    [ProducesResponseType(typeof(ApiResponse<UpiPaymentIntentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UpiPaymentIntentDto>>> CreateUpiIntent(
        [FromBody] UpiPaymentRequestDto request, CancellationToken ct) =>
        Success(await _payments.CreateUpiIntentAsync(request, ct));

    /// <summary>Creates a pay-by-UPI request and texts the public link to the member's phone.</summary>
    [HttpPost("requests")]
    [HasPermission(Permissions.PaymentsCollect)]
    [ProducesResponseType(typeof(ApiResponse<PaymentRequestCreatedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PaymentRequestCreatedDto>>> CreatePaymentRequest(
        [FromBody] CreatePaymentRequestDto dto, CancellationToken ct)
    {
        var result = await _payments.CreatePaymentRequestAsync(dto, ct);
        return Success(result, result.Detail);
    }

    /// <summary>
    /// Resolves a texted pay link for the public pay page. Anonymous by design: the link lands on
    /// a member's phone with no session, and the token is unguessable while the payload carries
    /// only what a paper invoice would — payee, amount and note.
    /// </summary>
    [HttpGet("requests/{token}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<PublicPaymentRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PublicPaymentRequestDto>>> GetPaymentRequest(
        string token, CancellationToken ct) =>
        Success(await _payments.GetPaymentRequestAsync(token, ct));

    /// <summary>
    /// Live outcome of a texted pay link, polled by the pay page every few seconds while it
    /// waits for the gateway to settle. Anonymous for the same reason the link itself is: it is
    /// keyed by the unguessable token and answers with an outcome word and receipt facts —
    /// never money credentials.
    /// </summary>
    [HttpGet("requests/{token}/status")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<PublicPaymentRequestStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PublicPaymentRequestStatusDto>>> GetPaymentRequestStatus(
        string token, CancellationToken ct) =>
        Success(await _payments.GetPaymentRequestStatusAsync(token, ct));

    /// <summary>Raises a refund request. Immediate approval is honoured only for callers holding payments.refund.</summary>
    [HttpPost("refunds")]
    [HasPermission(Permissions.PaymentsCollect)]
    [ProducesResponseType(typeof(ApiResponse<PaymentRefundDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PaymentRefundDto>>> CreateRefund(
        [FromBody] CreateRefundDto dto, CancellationToken ct) =>
        Success(await _payments.CreateRefundAsync(dto, ct), "Refund request created.");

    /// <summary>Approves or rejects a pending refund request.</summary>
    [HttpPost("refunds/approve")]
    [HasPermission(Permissions.PaymentsRefund)]
    [ProducesResponseType(typeof(ApiResponse<PaymentRefundDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PaymentRefundDto>>> ApproveRefund(
        [FromBody] ApproveRefundDto dto, CancellationToken ct) =>
        Success(await _payments.ApproveRefundAsync(dto, ct), "Refund decision recorded.");

    /// <summary>Returns a paged list of refunds.</summary>
    [HttpGet("refunds")]
    [HasPermission(Permissions.PaymentsView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PaymentRefundDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<PaymentRefundDto>>>> GetRefunds(
        [FromQuery] PagedRequest query, CancellationToken ct) =>
        Success(await _payments.GetRefundsAsync(query, ct));

    /// <summary>Members with an unpaid balance on an active subscription.</summary>
    [HttpGet("outstanding")]
    [HasPermission(Permissions.PaymentsView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<OutstandingBalanceDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<OutstandingBalanceDto>>>> GetOutstanding(
        [FromQuery] PagedRequest query, CancellationToken ct) =>
        Success(await _payments.GetOutstandingAsync(query, ct));

    /// <summary>Available payment methods for the collection screen.</summary>
    [HttpGet("methods")]
    [HasPermission(Permissions.PaymentsView)]
    [ProducesResponseType(typeof(ApiResponse<List<PaymentMethodDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<PaymentMethodDto>>>> GetPaymentMethods(
        [FromQuery] bool onlyActive = true, CancellationToken ct = default) =>
        Success(await _payments.GetPaymentMethodsAsync(onlyActive, ct));

    /// <summary>Creates or updates a payment method.</summary>
    [HttpPost("methods")]
    [HasPermission(Permissions.SettingsManage)]
    [ProducesResponseType(typeof(ApiResponse<PaymentMethodDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PaymentMethodDto>>> SavePaymentMethod(
        [FromBody] PaymentMethodDto dto, CancellationToken ct) =>
        Success(await _payments.SavePaymentMethodAsync(dto, ct), "Payment method saved.");
}
