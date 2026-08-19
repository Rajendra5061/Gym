using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Infrastructure.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GymManagement.Api.Controllers;

/// <summary>
/// Where a payment gateway tells the gym that money arrived.
///
/// This is the one endpoint in the application that answers an unauthenticated caller with a state
/// change, so every guard it has is load-bearing:
///
/// <list type="bullet">
///   <item>
///     <b>Anonymous, deliberately.</b> A gateway cannot hold a JWT. <c>[AllowAnonymous]</c> also
///     supplies the authorization metadata that keeps the host's FallbackPolicy — which demands an
///     authenticated user for anything without any — from rejecting the delivery before it is read.
///     Authentication is replaced by proof of the shared secret, not dropped.
///   </item>
///   <item>
///     <b>The raw body is read first.</b> No <c>[FromBody]</c> parameter exists on the action, so
///     model binding never touches the stream. The signature is defined over the exact bytes the
///     gateway signed; a round trip through a DTO would reorder keys, renormalise numbers and
///     re-encode escapes, and the MAC would then be checked against something nobody signed.
///   </item>
///   <item>
///     <b>The body is capped before it is read.</b> An anonymous endpoint that buffers whatever it
///     is sent is a memory-exhaustion invitation, so anything over the configured limit is refused
///     unread and the read itself stops at the cap.
///   </item>
///   <item>
///     <b>Rate limited in its own partition.</b> Program.cs routes this path to a per-source-IP
///     partition of its own instead of the per-user one. Webhook traffic therefore cannot spend a
///     signed-in user's budget, an anonymous flood cannot lock staff out of the API, and the
///     endpoint is still bounded — it is excluded from the user limiter, not from limiting.
///   </item>
/// </list>
///
/// With no gateway configured every request here answers 503 and nothing else happens.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/payments/webhook")]
[Produces("application/json")]
public sealed class PaymentWebhookController : ControllerBase
{
    private readonly IPaymentWebhookService _webhooks;
    private readonly IOptionsMonitor<PaymentGatewayOptions> _options;
    private readonly ILogger<PaymentWebhookController> _logger;

    public PaymentWebhookController(
        IPaymentWebhookService webhooks,
        IOptionsMonitor<PaymentGatewayOptions> options,
        ILogger<PaymentWebhookController> logger)
    {
        _webhooks = webhooks;
        _options = options;
        _logger = logger;
    }

    /// <summary>Receives one signed event from a payment gateway.</summary>
    /// <param name="provider">The gateway slug this gym configured, e.g. <c>simulator</c>.</param>
    [HttpPost("{provider}")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ApiResponse<PaymentWebhookResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Receive(string provider, CancellationToken ct)
    {
        var options = _options.CurrentValue;
        var maxBytes = Math.Clamp(options.MaxBodyBytes, 1024, 1024 * 1024);

        if (Request.ContentLength is { } declared && declared > maxBytes)
        {
            _logger.LogWarning("Refused a {Bytes}-byte webhook body on /api/payments/webhook: over the {Max}-byte cap.",
                declared, maxBytes);

            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                ApiResponse.Fail("The webhook payload is too large.", "PAYLOAD_TOO_LARGE"));
        }

        var rawBody = await ReadRawBodyAsync(maxBytes, ct);
        if (rawBody is null)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                ApiResponse.Fail("The webhook payload is too large.", "PAYLOAD_TOO_LARGE"));
        }

        // Resolved per provider so a gym running two gateways can honour each one's header names.
        var resolved = options.Resolve(provider);
        var signatureHeader = resolved?.SignatureHeader ?? options.SignatureHeader;
        var timestampHeader = resolved?.TimestampHeader ?? options.TimestampHeader;

        var request = new PaymentWebhookRequestDto(
            Provider: provider ?? string.Empty,
            RawBody: rawBody,
            Signature: FirstHeader(signatureHeader),
            Timestamp: FirstHeader(timestampHeader),
            SourceIp: HttpContext.Connection.RemoteIpAddress?.ToString());

        var result = await _webhooks.HandleAsync(request, ct);

        return Translate(result);
    }

    /// <summary>
    /// Buffers the request body, stopping at the cap. Returns null when the sender exceeded it —
    /// which also catches a chunked body that declared no Content-Length.
    /// </summary>
    private async Task<byte[]?> ReadRawBodyAsync(int maxBytes, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8 * 1024];

        while (true)
        {
            var read = await Request.Body.ReadAsync(chunk, ct);
            if (read == 0) break;

            if (buffer.Length + read > maxBytes) return null;

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private string? FirstHeader(string name) =>
        Request.Headers.TryGetValue(name, out var values) ? values.FirstOrDefault() : null;

    /// <summary>
    /// Maps an outcome to a status code with the gateway's retry behaviour in mind.
    ///
    /// 2xx means "stop retrying": the event is settled, or was already, or has been recorded for a
    /// human. 401 and 4xx mean "this delivery was refused" — a mismatched amount is a 409 precisely
    /// so it does not quietly disappear into a gateway's success log.
    /// </summary>
    private IActionResult Translate(PaymentWebhookResultDto result) => result.Status switch
    {
        PaymentWebhookStatus.NotConfigured => StatusCode(StatusCodes.Status503ServiceUnavailable,
            ApiResponse.Fail(result.Message, "GATEWAY_NOT_CONFIGURED")),

        PaymentWebhookStatus.InvalidSignature => Unauthorized(
            ApiResponse.Fail(result.Message, "WEBHOOK_SIGNATURE_INVALID")),

        PaymentWebhookStatus.StaleEvent => Unauthorized(
            ApiResponse.Fail(result.Message, "WEBHOOK_EVENT_STALE")),

        PaymentWebhookStatus.Malformed => BadRequest(
            ApiResponse.Fail(result.Message, "WEBHOOK_PAYLOAD_INVALID")),

        PaymentWebhookStatus.AmountMismatch => Conflict(
            ApiResponse.Fail(result.Message, "WEBHOOK_AMOUNT_MISMATCH")),

        PaymentWebhookStatus.Rejected => Conflict(
            ApiResponse.Fail(result.Message, "WEBHOOK_EVENT_REJECTED")),

        _ => Ok(ApiResponse<PaymentWebhookResultDto>.Ok(result, result.Message))
    };
}
