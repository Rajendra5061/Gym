using System.Security.Cryptography;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using GymManagement.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GymManagement.Infrastructure.Services;

/// <summary>
/// Verifies gateway webhook deliveries and turns the genuine ones into settled payments.
///
/// Order of operations is the security design, and it is strict:
/// <list type="number">
///   <item>
///     <b>Is a gateway configured at all?</b> If not, nothing else runs. The endpoint is inert and
///     the manual flow is unaffected.
///   </item>
///   <item>
///     <b>Signature and freshness, over the raw bytes.</b> HMAC-SHA256 compared in constant time,
///     with the timestamp inside the signed material. A delivery that fails here is logged and
///     answered 401 having written nothing whatsoever — no event row, no payment change.
///   </item>
///   <item>
///     <b>Claim the event id.</b> A row is inserted into <c>PaymentGatewayEvents</c>, whose unique
///     <c>(Provider, EventId)</c> index is what makes two simultaneous deliveries of one event
///     settle a payment once. Only after the claim commits is a payment touched.
///   </item>
///   <item>
///     <b>Match on amount before status.</b> A payment is never moved to Paid for an amount other
///     than the one that was asked for.
///   </item>
/// </list>
/// </summary>
public sealed class PaymentWebhookService : IPaymentWebhookService
{
    private readonly GymDbContext _db;
    private readonly IOptionsMonitor<PaymentGatewayOptions> _options;
    private readonly IPaymentService _payments;
    private readonly IAuditService _audit;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<PaymentWebhookService> _logger;

    public PaymentWebhookService(
        GymDbContext db,
        IOptionsMonitor<PaymentGatewayOptions> options,
        IPaymentService payments,
        IAuditService audit,
        IDateTimeProvider clock,
        ILogger<PaymentWebhookService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _payments = payments ?? throw new ArgumentNullException(nameof(payments));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PaymentWebhookResultDto> HandleAsync(
        PaymentWebhookRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ---------------------------------------------------------------- 1. configured?
        var gateway = _options.CurrentValue.Resolve(request.Provider);
        if (gateway is null)
        {
            _logger.LogInformation(
                "Webhook delivery for provider '{Provider}' from {SourceIp} refused: no gateway is configured.",
                Safe(request.Provider), request.SourceIp ?? "unknown");

            return new PaymentWebhookResultDto
            {
                Status = PaymentWebhookStatus.NotConfigured,
                Message = "Automatic payment reconciliation is not configured."
            };
        }

        // ---------------------------------------------------------------- 2. verify
        var check = WebhookSignatureVerifier.Verify(
            gateway, request.RawBody, request.Signature, request.Timestamp, _clock.UtcNow);

        if (!check.IsValid)
        {
            // Deliberately one log line and one status for every failure mode: the caller learns
            // only that it was rejected, while the operator gets the reason. Nothing was written.
            _logger.LogWarning(
                "Rejected webhook delivery for {Provider} from {SourceIp}: {Reason}. Nothing was changed.",
                gateway.Provider, request.SourceIp ?? "unknown", check.Reason);

            return new PaymentWebhookResultDto
            {
                Status = check.IsStale ? PaymentWebhookStatus.StaleEvent : PaymentWebhookStatus.InvalidSignature,
                Message = "Signature verification failed."
            };
        }

        // ---------------------------------------------------------------- 3. parse
        if (!GatewayEventParser.TryParse(request.RawBody, gateway.AmountsInMinorUnits, out var evt, out var parseError))
        {
            _logger.LogWarning(
                "Verified webhook delivery for {Provider} could not be read: {Reason}. Nothing was changed.",
                gateway.Provider, parseError);

            return new PaymentWebhookResultDto
            {
                Status = PaymentWebhookStatus.Malformed,
                Message = $"The payload could not be read: {parseError}."
            };
        }

        var digest = Convert.ToHexString(SHA256.HashData(request.RawBody)).ToLowerInvariant();

        // ---------------------------------------------------------------- 4. claim the event id
        var (claim, duplicate) = await ClaimEventAsync(gateway.Provider, evt, digest, ct).ConfigureAwait(false);

        if (duplicate)
        {
            _logger.LogInformation(
                "Webhook event {EventId} from {Provider} was already processed ({Outcome}); ignoring the repeat.",
                evt.EventId, gateway.Provider, claim?.Outcome);

            return new PaymentWebhookResultDto
            {
                Status = PaymentWebhookStatus.Duplicate,
                Duplicate = true,
                EventId = evt.EventId,
                PaymentReference = evt.PaymentReference,
                PaymentId = claim?.PaymentId,
                Message = "This event was already processed. Nothing changed."
            };
        }

        // ---------------------------------------------------------------- 5. act on it
        try
        {
            return await ProcessAsync(gateway, evt, claim!, request.SourceIp, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The claim must not outlive a failed attempt. Leaving it behind would make the
            // gateway's retry look like a duplicate and the payment would never be reconciled, so
            // the claim is withdrawn and the delivery is allowed to fail loudly.
            _logger.LogError(ex,
                "Processing webhook event {EventId} from {Provider} failed; withdrawing the claim so a retry can be honoured.",
                evt.EventId, gateway.Provider);

            await WithdrawClaimAsync(claim!, ct).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Inserts the claim row, or reports that this event id is already recorded.
    ///
    /// Two guards, and they do different jobs. The read is the cheap sequential case. The unique
    /// index on <c>(Provider, EventId)</c> is the real one: two deliveries arriving at the same
    /// instant both pass the read, both insert, and the database fails the loser — which is then
    /// answered as a duplicate. Without the index the read alone would be a classic check-then-act
    /// race that settles the payment twice.
    /// </summary>
    private async Task<(PaymentGatewayEvent? Claim, bool Duplicate)> ClaimEventAsync(
        string provider, GatewayEvent evt, string digest, CancellationToken ct)
    {
        var existing = await _db.PaymentGatewayEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Provider == provider && e.EventId == evt.EventId, ct)
            .ConfigureAwait(false);

        if (existing is not null) return (existing, true);

        var claim = new PaymentGatewayEvent
        {
            Provider = provider,
            EventId = evt.EventId,
            EventType = evt.EventType,
            PaymentReference = evt.PaymentReference,
            Amount = evt.Amount,
            Currency = evt.Currency,
            GatewayStatus = evt.Status,
            GatewayTransactionId = evt.GatewayTransactionId,
            PayloadDigest = digest,
            Outcome = PaymentGatewayEventOutcome.Received,
            ReceivedAtUtc = _clock.UtcNow
        };

        _db.PaymentGatewayEvents.Add(claim);

        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return (claim, false);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Lost the race with a simultaneous delivery of the same event.
            _db.Entry(claim).State = EntityState.Detached;

            var winner = await _db.PaymentGatewayEvents
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Provider == provider && e.EventId == evt.EventId, ct)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Concurrent delivery of webhook event {EventId} from {Provider} lost the insert race; treating it as a duplicate.",
                evt.EventId, provider);

            return (winner, true);
        }
    }

    private async Task WithdrawClaimAsync(PaymentGatewayEvent claim, CancellationToken ct)
    {
        try
        {
            var tracked = await _db.PaymentGatewayEvents
                .FirstOrDefaultAsync(e => e.Id == claim.Id, CancellationToken.None)
                .ConfigureAwait(false);

            if (tracked is null) return;

            _db.PaymentGatewayEvents.Remove(tracked);
            await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Could not withdraw the claim for webhook event {EventId}; a retry of it will be ignored as a duplicate.",
                claim.EventId);
        }
    }

    private async Task<PaymentWebhookResultDto> ProcessAsync(
        ResolvedGateway gateway, GatewayEvent evt, PaymentGatewayEvent claim, string? sourceIp, CancellationToken ct)
    {
        var result = new PaymentWebhookResultDto
        {
            EventId = evt.EventId,
            PaymentReference = evt.PaymentReference
        };

        if (string.IsNullOrWhiteSpace(evt.PaymentReference))
        {
            await CompleteAsync(claim, PaymentGatewayEventOutcome.Ignored, null,
                "the payload quotes no payment reference", ct).ConfigureAwait(false);

            _logger.LogWarning(
                "Webhook event {EventId} from {Provider} quotes no payment reference; recorded and ignored.",
                evt.EventId, gateway.Provider);

            result.Status = PaymentWebhookStatus.Ignored;
            result.Message = "The event quotes no payment reference. Recorded for review.";
            return result;
        }

        // A failure notice never touches the money. It is recorded so the gym can see that the
        // member tried and the transfer did not complete, and the payment stays where staff can
        // still deal with it by hand.
        if (evt.Kind == GatewayEventKind.Failure)
        {
            var failedPaymentId = await FindPaymentIdAsync(evt.PaymentReference, ct).ConfigureAwait(false);

            await CompleteAsync(claim, PaymentGatewayEventOutcome.FailureRecorded, failedPaymentId,
                $"gateway reported '{evt.Status ?? evt.EventType}'", ct).ConfigureAwait(false);

            await _audit.LogForUserAsync(null, GatewayActor(gateway.Provider),
                AuditActions.PaymentGatewayFailed, nameof(Payment), failedPaymentId,
                null,
                new { evt.EventId, evt.PaymentReference, evt.Status, evt.Amount },
                $"Gateway {gateway.Provider} reported a failed collection for reference {evt.PaymentReference}.",
                sourceIp, $"webhook:{gateway.Provider}", ct).ConfigureAwait(false);

            _logger.LogWarning(
                "Gateway {Provider} reported failure '{Status}' for reference {Reference} (event {EventId}). " +
                "The payment was left untouched for manual handling.",
                gateway.Provider, evt.Status ?? evt.EventType, evt.PaymentReference, evt.EventId);

            result.Status = PaymentWebhookStatus.FailureRecorded;
            result.PaymentId = failedPaymentId;
            result.Message = "Failure event recorded. The payment was not settled.";
            return result;
        }

        if (evt.Kind != GatewayEventKind.Success)
        {
            await CompleteAsync(claim, PaymentGatewayEventOutcome.Ignored, null,
                $"unrecognised status '{evt.Status ?? evt.EventType}'", ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Webhook event {EventId} from {Provider} carries status '{Status}', which settles nothing. Recorded and ignored.",
                evt.EventId, gateway.Provider, evt.Status ?? evt.EventType);

            result.Status = PaymentWebhookStatus.Ignored;
            result.Message = "The event does not report a completed collection. Recorded for review.";
            return result;
        }

        // A success event with no amount cannot be reconciled: there is nothing to check the
        // request against, and "probably the right amount" is not a standard for money.
        if (evt.Amount is null)
        {
            var unpricedPaymentId = await FindPaymentIdAsync(evt.PaymentReference, ct).ConfigureAwait(false);

            await CompleteAsync(claim, PaymentGatewayEventOutcome.AmountMismatch, unpricedPaymentId,
                "the success event quotes no amount", ct).ConfigureAwait(false);

            _logger.LogWarning(
                "Webhook event {EventId} from {Provider} reports success for reference {Reference} but quotes no amount. Refused.",
                evt.EventId, gateway.Provider, evt.PaymentReference);

            result.Status = PaymentWebhookStatus.AmountMismatch;
            result.PaymentId = unpricedPaymentId;
            result.Message = "The event reports success but quotes no amount. It was refused and recorded.";
            return result;
        }

        var settlement = await _payments.SettleFromGatewayAsync(new GatewaySettlementDto
        {
            Provider = gateway.Provider,
            EventId = evt.EventId,
            PaymentReference = evt.PaymentReference,
            Amount = evt.Amount.Value,
            GatewayTransactionId = evt.GatewayTransactionId,
            PayerVpa = evt.PayerVpa
        }, ct).ConfigureAwait(false);

        result.PaymentId = settlement.PaymentId;
        result.ReceiptNumber = settlement.ReceiptNumber;

        switch (settlement.Status)
        {
            case GatewaySettlementStatus.Settled:
                await CompleteAsync(claim, PaymentGatewayEventOutcome.Settled, settlement.PaymentId,
                    $"settled payment {settlement.ReceiptNumber}", ct).ConfigureAwait(false);

                _logger.LogInformation(
                    "Gateway {Provider} settled payment {Receipt} (id {PaymentId}) for reference {Reference} from event {EventId}.",
                    gateway.Provider, settlement.ReceiptNumber, settlement.PaymentId, evt.PaymentReference, evt.EventId);

                result.Status = PaymentWebhookStatus.Settled;
                result.Message = "Payment settled.";
                return result;

            case GatewaySettlementStatus.AlreadySettled:
                await CompleteAsync(claim, PaymentGatewayEventOutcome.AlreadySettled, settlement.PaymentId,
                    "the payment was already settled", ct).ConfigureAwait(false);

                result.Status = PaymentWebhookStatus.AlreadySettled;
                result.Message = "The payment was already settled. Nothing changed.";
                return result;

            case GatewaySettlementStatus.PaymentNotFound:
                await CompleteAsync(claim, PaymentGatewayEventOutcome.PaymentNotFound, null,
                    "no live payment carries this reference", ct).ConfigureAwait(false);

                _logger.LogWarning(
                    "Gateway {Provider} reported a collection for reference {Reference} (event {EventId}) " +
                    "but no live payment carries it. Recorded for reconciliation by hand.",
                    gateway.Provider, evt.PaymentReference, evt.EventId);

                result.Status = PaymentWebhookStatus.PaymentNotFound;
                result.Message = "No payment carries that reference. The event was recorded for review.";
                return result;

            case GatewaySettlementStatus.AmountMismatch:
                await CompleteAsync(claim, PaymentGatewayEventOutcome.AmountMismatch, settlement.PaymentId,
                    settlement.Detail, ct).ConfigureAwait(false);

                await _audit.LogForUserAsync(null, GatewayActor(gateway.Provider),
                    AuditActions.PaymentGatewayRejected, nameof(Payment), settlement.PaymentId,
                    null,
                    new { evt.EventId, evt.PaymentReference, Reported = evt.Amount, Expected = settlement.ExpectedAmount },
                    $"Gateway {gateway.Provider} reported {evt.Amount:0.00} against {settlement.ExpectedAmount:0.00} " +
                    $"for reference {evt.PaymentReference}. Refused.",
                    sourceIp, $"webhook:{gateway.Provider}", ct).ConfigureAwait(false);

                _logger.LogWarning(
                    "Refused webhook event {EventId} from {Provider}: reference {Reference} was requested at " +
                    "{Expected:0.00} but the gateway reported {Reported:0.00}. The payment was NOT settled.",
                    evt.EventId, gateway.Provider, evt.PaymentReference, settlement.ExpectedAmount, evt.Amount);

                result.Status = PaymentWebhookStatus.AmountMismatch;
                result.Message = "The amount reported does not match the amount requested. The payment was not settled.";
                return result;

            default:
                await CompleteAsync(claim, PaymentGatewayEventOutcome.Rejected, settlement.PaymentId,
                    settlement.Detail, ct).ConfigureAwait(false);

                await _audit.LogForUserAsync(null, GatewayActor(gateway.Provider),
                    AuditActions.PaymentGatewayRejected, nameof(Payment), settlement.PaymentId,
                    null, new { evt.EventId, evt.PaymentReference, settlement.Detail },
                    $"Gateway {gateway.Provider} event refused: {settlement.Detail}",
                    sourceIp, $"webhook:{gateway.Provider}", ct).ConfigureAwait(false);

                _logger.LogWarning(
                    "Refused webhook event {EventId} from {Provider} for reference {Reference}: {Detail}",
                    evt.EventId, gateway.Provider, evt.PaymentReference, settlement.Detail);

                result.Status = PaymentWebhookStatus.Rejected;
                result.Message = settlement.Detail ?? "The event could not be applied to the payment.";
                return result;
        }
    }

    private async Task<int?> FindPaymentIdAsync(string reference, CancellationToken ct) =>
        await _db.Payments
            .AsNoTracking()
            .Where(p => p.TransactionReference == reference)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    private async Task CompleteAsync(
        PaymentGatewayEvent claim, PaymentGatewayEventOutcome outcome, int? paymentId, string? detail, CancellationToken ct)
    {
        var tracked = await _db.PaymentGatewayEvents
            .FirstOrDefaultAsync(e => e.Id == claim.Id, ct)
            .ConfigureAwait(false);

        if (tracked is null) return;

        tracked.Outcome = outcome;
        tracked.PaymentId = paymentId;
        tracked.ProcessedAtUtc = _clock.UtcNow;
        tracked.Detail = detail is null || detail.Length <= 512 ? detail : detail[..512];

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The audit trail names the gateway where it would otherwise name a user.</summary>
    private static string GatewayActor(string provider) => $"gateway:{provider}";

    /// <summary>Route segments are attacker-controlled; never log one raw.</summary>
    private static string Safe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "(none)";

        var text = value.Trim();
        if (text.Length > 64) text = text[..64];

        return new string(text.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '?').ToArray());
    }

    /// <summary>True for SQL Server unique index / unique constraint violations (2601, 2627).</summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner.GetType().GetProperty("Number")?.GetValue(inner) is int number && number is 2601 or 2627)
                return true;

            if (inner.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
                inner.Message.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Reports the wiring without revealing the key. Every branch here mirrors what
    /// <see cref="PaymentGatewayOptions.Resolve"/> would actually do, so the screen cannot claim a
    /// gateway is live in a case where a real delivery would be turned away.
    /// </summary>
    public async Task<PaymentGatewayStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var options = _options.CurrentValue;

        // A pinned slug is the one the gateway must post to. With none pinned the shared secret
        // serves any slug, and there is no single URL to quote — so a per-provider key, if there is
        // exactly one, is a better answer than nothing.
        var slug = FirstNonEmpty(options.Provider)
            ?? (options.Providers.Count == 1 ? FirstNonEmpty(options.Providers.Keys.First()) : null);

        // "Enabled with no key" is not configured. Resolve() refuses such a delivery, and this must
        // agree with it: a screen that says "live" while every webhook is rejected is worse than one
        // that says nothing.
        var secretPresent = !string.IsNullOrWhiteSpace(options.Secret)
            || options.Providers.Values.Any(p => !string.IsNullOrWhiteSpace(p?.Secret));

        var configured = options.Enabled && secretPresent;

        var lastEvent = await _db.PaymentGatewayEvents
            .AsNoTracking()
            .OrderByDescending(e => e.ReceivedAtUtc)
            .Select(e => (DateTime?)e.ReceivedAtUtc)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return new PaymentGatewayStatusDto
        {
            IsConfigured = configured,
            Provider = slug,
            Mode = FirstNonEmpty(options.Mode),
            WebhookPath = $"/api/payments/webhook/{slug ?? "{provider}"}",
            SigningSecretConfigured = secretPresent,
            RequiresManualVerification = !configured,
            LastEventAtUtc = lastEvent,
            Message = Describe(options.Enabled, secretPresent, lastEvent)
        };
    }

    /// <summary>The one sentence the gateway screen exists to say.</summary>
    private static string Describe(bool enabled, bool secretPresent, DateTime? lastEvent)
    {
        if (!enabled && !secretPresent)
        {
            return "No payment gateway is configured. UPI collection works, but nothing reports the "
                + "transfer back, so an operator confirms each payment by hand.";
        }

        if (!enabled)
        {
            return "A signing secret is present but Payments:Gateway:Enabled is false, so "
                + "reconciliation is switched off and every webhook delivery is refused.";
        }

        if (!secretPresent)
        {
            return "Payments:Gateway:Enabled is true but no signing secret resolved. Deliveries are "
                + "refused rather than trusted — set Payments__Gateway__Secret to finish the wiring.";
        }

        return lastEvent is null
            ? "Reconciliation is armed. No delivery has arrived yet, so the wiring is unproven — "
              + "send a test event from the gateway console to confirm it reaches this server."
            : $"Reconciliation is live. The last delivery was accepted at {lastEvent:u}.";
    }

    private static string? FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();
}
