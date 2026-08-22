using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Infrastructure.Data;
using GymManagement.Infrastructure.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GymManagement.Api.Controllers;

/// <summary>What the pay page posts: how the pretend transfer ends.</summary>
public sealed class SimulatePaymentDto
{
    /// <summary><c>success</c> (the default when omitted) or <c>failure</c>.</summary>
    public string? Outcome { get; set; }
}

/// <summary>
/// The "pay" button of the Development simulator: turns a click on the pay page into a correctly
/// signed webhook delivery, so the full automatic loop — order, payment, webhook, settlement,
/// renewal — runs on a laptop with no gateway account and no money.
///
/// Nothing here bypasses the settlement engine. The endpoint fabricates the same thing a real
/// gateway would send — a JSON event carrying the payment reference, signed with the configured
/// <c>Payments:Gateway:Secret</c> — and hands it to <see cref="IPaymentWebhookService"/>, which
/// verifies the signature, claims the event id and matches the amount exactly as it would for
/// Razorpay or Cashfree. If the simulator can settle a payment it is because the webhook engine
/// agreed, not because this controller said so.
///
/// <b>This endpoint must be invisible in production.</b> It is <c>[AllowAnonymous]</c> because the
/// public pay page has no session, so the only guard left is the environment: every action checks
/// <see cref="IHostEnvironment.IsDevelopment"/> first and answers a bare 404 otherwise — the same
/// answer a route that does not exist would give, so a probing caller cannot even learn the
/// endpoint is compiled in.
/// </summary>
[AllowAnonymous]
[Route("api/payments/dev")]
public sealed class DevPaymentSimulatorController : ApiControllerBase
{
    private readonly GymDbContext _db;
    private readonly IPaymentWebhookService _webhooks;
    private readonly IOptionsMonitor<PaymentGatewayOptions> _options;
    private readonly IDateTimeProvider _clock;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DevPaymentSimulatorController> _logger;

    public DevPaymentSimulatorController(
        GymDbContext db,
        IPaymentWebhookService webhooks,
        IOptionsMonitor<PaymentGatewayOptions> options,
        IDateTimeProvider clock,
        IHostEnvironment environment,
        ILogger<DevPaymentSimulatorController> logger)
    {
        _db = db;
        _webhooks = webhooks;
        _options = options;
        _clock = clock;
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// Simulates the gateway's answer for one payment request: a signed success or failure event,
    /// delivered through the real webhook pipeline.
    /// </summary>
    /// <param name="token">The payment request's opaque link token, as carried in the SMS link.</param>
    [HttpPost("simulate/{token}")]
    [ProducesResponseType(typeof(ApiResponse<PaymentWebhookResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PaymentWebhookResultDto>>> Simulate(
        string token, [FromBody] SimulatePaymentDto? body, CancellationToken ct)
    {
        // Outside Development this controller does not exist, and 404 keeps it that way.
        if (!_environment.IsDevelopment()) return NotFound();

        var outcome = string.IsNullOrWhiteSpace(body?.Outcome)
            ? "success"
            : body!.Outcome!.Trim().ToLowerInvariant();

        if (outcome is not ("success" or "failure"))
        {
            return BadRequest(ApiResponse.Fail(
                "Outcome must be 'success' or 'failure'.", "SIMULATOR_OUTCOME_INVALID"));
        }

        var paymentRequest = await _db.PaymentRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Token == token, ct);

        if (paymentRequest is null)
        {
            return NotFound(ApiResponse.Fail(
                "No payment request carries that token.", "PAYMENT_REQUEST_NOT_FOUND"));
        }

        // The simulated delivery must verify against whatever is actually configured, or the test
        // proves nothing. With no usable secret there is nothing to sign with — say so plainly
        // instead of throwing, because this is the first thing a developer hits on a fresh clone.
        var (slug, gateway) = ResolveGateway(_options.CurrentValue);
        if (gateway is null)
        {
            return Success(new PaymentWebhookResultDto
            {
                Status = PaymentWebhookStatus.NotConfigured,
                PaymentReference = paymentRequest.Reference,
                Message = "No webhook signing secret is configured, so nothing was posted. Run " +
                          "'dotnet user-secrets set \"Payments:Gateway:Enabled\" true' and " +
                          "'dotnet user-secrets set \"Payments:Gateway:Secret\" <any-random-string>' " +
                          "in the API project, then restart."
            });
        }

        var success = outcome == "success";
        var eventId = "evt_sim_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();

        // The exact shape GatewayEventParser reads: id / event / status / reference / amount /
        // currency / utr / vpa, all at the top level. The amount is quoted in the unit the
        // configured gateway expects — rupees normally, paise when AmountsInMinorUnits is set —
        // so the parsed value always equals the requested amount and the settlement's amount
        // check passes for the right reason.
        var rawBody = BuildPayload(
            eventId, paymentRequest.Reference, paymentRequest.Amount, success, gateway.AmountsInMinorUnits);

        var timestamp = new DateTimeOffset(_clock.UtcNow, TimeSpan.Zero)
            .ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        // Signed exactly as the verifier demands for the current options: HMAC-SHA256 over
        // "{timestamp}.{body}" (or the body alone, per SignaturePayload), lowercase hex.
        var signature = WebhookSignatureVerifier.ComputeHexSignature(gateway, rawBody, timestamp);

        _logger.LogInformation(
            "Dev simulator posting a {Outcome} event {EventId} for reference {Reference} through the webhook pipeline.",
            outcome, eventId, paymentRequest.Reference);

        var result = await _webhooks.HandleAsync(new PaymentWebhookRequestDto(
            Provider: slug,
            RawBody: rawBody,
            Signature: signature,
            Timestamp: timestamp,
            SourceIp: "dev-simulator"), ct);

        return Success(result, result.Message);
    }

    /// <summary>
    /// The provider slug the simulated delivery is posted under: the pinned provider when one is
    /// configured, otherwise <c>simulator</c>, otherwise whichever per-provider entry resolves.
    /// Whatever <see cref="PaymentGatewayOptions.Resolve"/> refuses, this refuses too.
    /// </summary>
    private static (string Slug, ResolvedGateway? Gateway) ResolveGateway(PaymentGatewayOptions options)
    {
        var candidates = new List<string?> { options.Provider, "simulator" };
        candidates.AddRange(options.Providers.Keys);

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;

            var slug = candidate.Trim();
            if (options.Resolve(slug) is { } gateway) return (slug, gateway);
        }

        return ("simulator", null);
    }

    private static byte[] BuildPayload(
        string eventId, string reference, decimal amount, bool success, bool amountsInMinorUnits)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("id", eventId);
            writer.WriteString("event", success ? "payment.captured" : "payment.failed");
            writer.WriteString("status", success ? "success" : "failed");
            writer.WriteString("reference", reference);

            if (amountsInMinorUnits)
                writer.WriteNumber("amount", (long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
            else
                writer.WriteNumber("amount", Math.Round(amount, 2, MidpointRounding.AwayFromZero));

            writer.WriteString("currency", "INR");

            // A failed transfer has no bank UTR, so only a success carries one.
            if (success)
                writer.WriteString("utr", "SIMUTR" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)));

            writer.WriteString("vpa", "simulator@upi");
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }
}
