using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GymManagement.IntegrationTests;

/// <summary>
/// The gateway webhook: <c>POST /api/payments/webhook/{provider}</c>.
///
/// Everything here is exercised over real HTTP against the real pipeline, because most of what is
/// being asserted lives in that pipeline — the endpoint being reachable without a token, the raw
/// body surviving to the signature check, the status code a gateway will act on.
///
/// The suite runs two hosts on purpose: one with a gateway configured and one without. The "no
/// gateway" host is not a special case, it is the default the application ships with, and it has to
/// keep proving that the manual flow is untouched.
/// </summary>
public sealed class PaymentWebhookTests : IAsyncLifetime
{
    private WebhookApiFactory _configured = null!;
    private WebhookApiFactory _unconfigured = null!;

    public async Task InitializeAsync()
    {
        _configured = new WebhookApiFactory(gatewayEnabled: true);
        _unconfigured = new WebhookApiFactory(gatewayEnabled: false);

        await _configured.InitialiseAsync();
        await _unconfigured.InitialiseAsync();
    }

    public Task DisposeAsync()
    {
        _configured.Dispose();
        _unconfigured.Dispose();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ the happy path

    [Fact]
    public async Task Signed_success_event_settles_the_payment_and_attributes_it_to_the_gateway()
    {
        var arranged = await ArrangeAwaitingPaymentAsync(_configured, 1500m);

        using var anonymous = _configured.CreateClient();
        var response = await anonymous.PostAsync(
            "/api/payments/webhook/simulator",
            _configured.SignedEvent(SuccessPayload("evt_settle_ok", arranged.Reference, 1500m)));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var result = await ReadResultAsync(response);
        result.Status.Should().Be(PaymentWebhookStatus.Settled);
        result.Duplicate.Should().BeFalse();
        result.PaymentId.Should().Be(arranged.PaymentId);

        await _configured.WithDbAsync(async db =>
        {
            var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == arranged.PaymentId);

            payment.Status.Should().Be(PaymentStatus.Paid);
            payment.ConfirmedAtUtc.Should().NotBeNull("the gateway settlement stamps confirmed-at");

            // The money trail must not name a member of staff for something no member of staff did.
            payment.ConfirmedByUserId.Should().BeNull();
            payment.Notes.Should().Contain("simulator").And.Contain("evt_settle_ok");

            var audit = await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityName == nameof(GymManagement.Domain.Entities.Payment) &&
                            a.EntityId == arranged.PaymentId &&
                            a.Action == AuditActions.PaymentGatewaySettled)
                .ToListAsync();

            audit.Should().ContainSingle("the settlement writes exactly one audit entry");
            audit[0].UserId.Should().BeNull("no user performed this");
            audit[0].UserName.Should().Be("gateway:simulator");

            var recorded = await db.PaymentGatewayEvents.AsNoTracking()
                .SingleAsync(e => e.Provider == "simulator" && e.EventId == "evt_settle_ok");

            recorded.Outcome.Should().Be(PaymentGatewayEventOutcome.Settled);
            recorded.PaymentId.Should().Be(arranged.PaymentId);
            recorded.PayloadDigest.Should().NotBeNullOrWhiteSpace("the verified bytes are fingerprinted");
        });
    }

    [Fact]
    public async Task Settling_by_gateway_emails_the_receipt_exactly_once()
    {
        var arranged = await ArrangeAwaitingPaymentAsync(_configured, 990m);

        using var anonymous = _configured.CreateClient();
        var response = await anonymous.PostAsync(
            "/api/payments/webhook/simulator",
            _configured.SignedEvent(SuccessPayload("evt_receipt", arranged.Reference, 990m)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await _configured.WithDbAsync(async db =>
        {
            var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == arranged.PaymentId);
            payment.ReceiptEmailedAtUtc.Should().NotBeNull("the existing receipt mailer fires on settlement");
        });

        // The local file sink writes the message as .eml plus a readable .txt sidecar and transmits
        // nothing. One sidecar naming this receipt means one message left the building.
        var dropped = Directory.GetFiles(_configured.MailDropDirectory, "*.txt")
            .Where(f => File.ReadAllText(f).Contains(arranged.ReceiptNumber, StringComparison.Ordinal))
            .ToList();

        dropped.Should().ContainSingle("the member is emailed one receipt for this payment");
    }

    // ------------------------------------------------------------------ idempotency

    [Fact]
    public async Task The_same_event_delivered_twice_settles_the_payment_once()
    {
        var arranged = await ArrangeAwaitingPaymentAsync(_configured, 2100m);
        var payload = SuccessPayload("evt_replay", arranged.Reference, 2100m);

        using var anonymous = _configured.CreateClient();

        var first = await anonymous.PostAsync("/api/payments/webhook/simulator", _configured.SignedEvent(payload));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadResultAsync(first)).Status.Should().Be(PaymentWebhookStatus.Settled);

        var confirmedAt = await _configured.FromDbAsync(db => db.Payments
            .AsNoTracking().Where(p => p.Id == arranged.PaymentId)
            .Select(p => p.ConfirmedAtUtc).SingleAsync());

        // The retry a gateway sends when it never saw the first answer.
        var second = await anonymous.PostAsync("/api/payments/webhook/simulator", _configured.SignedEvent(payload));

        second.StatusCode.Should().Be(HttpStatusCode.OK, "a repeat must be accepted so the gateway stops retrying");

        var repeat = await ReadResultAsync(second);
        repeat.Status.Should().Be(PaymentWebhookStatus.Duplicate);
        repeat.Duplicate.Should().BeTrue();

        await _configured.WithDbAsync(async db =>
        {
            (await db.PaymentGatewayEvents.AsNoTracking()
                    .CountAsync(e => e.Provider == "simulator" && e.EventId == "evt_replay"))
                .Should().Be(1, "the event id is claimed once");

            var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == arranged.PaymentId);
            payment.ConfirmedAtUtc.Should().Be(confirmedAt, "nothing about the payment changed on the repeat");

            (await db.AuditLogs.AsNoTracking()
                    .CountAsync(a => a.EntityId == arranged.PaymentId &&
                                     a.Action == AuditActions.PaymentGatewaySettled))
                .Should().Be(1, "a repeat writes no second settlement entry");
        });
    }

    [Fact]
    public async Task Concurrent_deliveries_are_serialised_by_a_unique_index_not_by_a_lookup()
    {
        // Two deliveries of one event arriving together are decided by the database, not by the
        // application: the settlement path inserts its claim row before touching the payment, and
        // this index is what makes exactly one of those inserts survive. A read-then-insert guard
        // alone would be a check-then-act race that settles the payment twice.
        //
        // Asserted against the model rather than by firing parallel requests because these cases
        // run on the EF in-memory provider, which does not enforce indexes at all — a green
        // parallel test here would prove nothing about SQL Server. The generated migration creates
        // this index, and the live SQL Server run is where two simultaneous deliveries are actually
        // fired at it.
        await _configured.WithDbAsync(db =>
        {
            var entity = db.Model.FindEntityType(typeof(GymManagement.Domain.Entities.PaymentGatewayEvent));
            entity.Should().NotBeNull();

            var index = entity!.GetIndexes().SingleOrDefault(i =>
                i.Properties.Select(p => p.Name).SequenceEqual(new[] { "Provider", "EventId" }));

            index.Should().NotBeNull("the event ledger is keyed on provider + event id");
            index!.IsUnique.Should().BeTrue("the uniqueness is what serialises simultaneous deliveries");

            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task A_second_event_for_a_payment_that_is_already_paid_settles_nothing_again()
    {
        var arranged = await ArrangeAwaitingPaymentAsync(_configured, 3300m);

        using var anonymous = _configured.CreateClient();

        var first = await anonymous.PostAsync("/api/payments/webhook/simulator",
            _configured.SignedEvent(SuccessPayload("evt_first", arranged.Reference, 3300m)));

        (await ReadResultAsync(first)).Status.Should().Be(PaymentWebhookStatus.Settled);

        var confirmedAt = await _configured.FromDbAsync(db => db.Payments
            .AsNoTracking().Where(p => p.Id == arranged.PaymentId)
            .Select(p => p.ConfirmedAtUtc).SingleAsync());

        // A different event id, so the idempotency ledger does not catch it. The payment's own
        // status is the second line of defence.
        var second = await anonymous.PostAsync("/api/payments/webhook/simulator",
            _configured.SignedEvent(SuccessPayload("evt_second", arranged.Reference, 3300m)));

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadResultAsync(second)).Status.Should().Be(PaymentWebhookStatus.AlreadySettled);

        await _configured.WithDbAsync(async db =>
        {
            var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == arranged.PaymentId);
            payment.ConfirmedAtUtc.Should().Be(confirmedAt, "the second event changed nothing");

            (await db.AuditLogs.AsNoTracking()
                    .CountAsync(a => a.EntityId == arranged.PaymentId &&
                                     a.Action == AuditActions.PaymentGatewaySettled))
                .Should().Be(1);
        });
    }

    // ------------------------------------------------------------------ verification failures

    [Fact]
    public async Task A_wrong_signature_is_refused_and_changes_nothing()
    {
        var arranged = await ArrangeAwaitingPaymentAsync(_configured, 1200m);
        var payload = SuccessPayload("evt_bad_sig", arranged.Reference, 1200m);

        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        content.Headers.Add("X-Payment-Signature",
            "sha256=" + new string('a', 64));
        content.Headers.Add("X-Payment-Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());

        using var anonymous = _configured.CreateClient();
        var response = await anonymous.PostAsync("/api/payments/webhook/simulator", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertNothingHappenedAsync(arranged.PaymentId, "evt_bad_sig");
    }

    [Fact]
    public async Task A_body_tampered_with_after_signing_is_refused()
    {
        var arranged = await ArrangeAwaitingPaymentAsync(_configured, 1200m);

        // Signed for 1200, delivered claiming 12000. The MAC covers the bytes, so the swap shows.
        var signed = SuccessPayload("evt_tampered", arranged.Reference, 1200m);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = Sign(timestamp, signed);

        var tampered = SuccessPayload("evt_tampered", arranged.Reference, 12000m);
        var content = new StringContent(tampered, Encoding.UTF8, "application/json");
        content.Headers.Add("X-Payment-Signature", "sha256=" + signature);
        content.Headers.Add("X-Payment-Timestamp", timestamp);

        using var anonymous = _configured.CreateClient();
        var response = await anonymous.PostAsync("/api/payments/webhook/simulator", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertNothingHappenedAsync(arranged.PaymentId, "evt_tampered");
    }

    [Fact]
    public async Task A_delivery_with_no_signature_is_refused_and_changes_nothing()
    {
        var arranged = await ArrangeAwaitingPaymentAsync(_configured, 1300m);

        var content = new StringContent(
            SuccessPayload("evt_no_sig", arranged.Reference, 1300m), Encoding.UTF8, "application/json");
        content.Headers.Add("X-Payment-Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());

        using var anonymous = _configured.CreateClient();
        var response = await anonymous.PostAsync("/api/payments/webhook/simulator", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertNothingHappenedAsync(arranged.PaymentId, "evt_no_sig");
    }

    [Fact]
    public async Task A_correctly_signed_but_stale_event_is_refused_and_changes_nothing()
    {
        var arranged = await ArrangeAwaitingPaymentAsync(_configured, 1400m);
        var payload = SuccessPayload("evt_stale", arranged.Reference, 1400m);

        // A genuine capture from an hour ago, replayed. The signature is perfect for the timestamp
        // it carries — freshness is the only thing standing between a replay and a settlement.
        var stale = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds().ToString();

        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        content.Headers.Add("X-Payment-Signature", "sha256=" + Sign(stale, payload));
        content.Headers.Add("X-Payment-Timestamp", stale);

        using var anonymous = _configured.CreateClient();
        var response = await anonymous.PostAsync("/api/payments/webhook/simulator", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertNothingHappenedAsync(arranged.PaymentId, "evt_stale");
    }

    [Fact]
    public async Task A_signature_that_does_not_cover_the_timestamp_cannot_be_re_dated()
    {
        var arranged = await ArrangeAwaitingPaymentAsync(_configured, 1450m);
        var payload = SuccessPayload("evt_redated", arranged.Reference, 1450m);

        // Captured an hour ago, then presented with a fresh timestamp to slip past the window.
        var old = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds().ToString();
        var fresh = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        content.Headers.Add("X-Payment-Signature", "sha256=" + Sign(old, payload));
        content.Headers.Add("X-Payment-Timestamp", fresh);

        using var anonymous = _configured.CreateClient();
        var response = await anonymous.PostAsync("/api/payments/webhook/simulator", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertNothingHappenedAsync(arranged.PaymentId, "evt_redated");
    }

    // ------------------------------------------------------------------ money rules

    [Fact]
    public async Task An_amount_that_does_not_match_is_refused_logged_and_never_reconciled()
    {
        var arranged = await ArrangeAwaitingPaymentAsync(_configured, 2500m);

        using var anonymous = _configured.CreateClient();
        var response = await anonymous.PostAsync(
            "/api/payments/webhook/simulator",
            _configured.SignedEvent(SuccessPayload("evt_mismatch", arranged.Reference, 2400m)));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await response.Content.ReadAsStringAsync();
        var envelope = JsonSerializer.Deserialize<ApiResponse>(body, GymApiFactory.Json);
        envelope!.Success.Should().BeFalse();
        envelope.ErrorCode.Should().Be("WEBHOOK_AMOUNT_MISMATCH");

        await _configured.WithDbAsync(async db =>
        {
            var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == arranged.PaymentId);
            payment.Status.Should().Be(PaymentStatus.AwaitingConfirmation, "an unmatched amount settles nothing");
            payment.ConfirmedAtUtc.Should().BeNull();

            var recorded = await db.PaymentGatewayEvents.AsNoTracking()
                .SingleAsync(e => e.EventId == "evt_mismatch");

            recorded.Outcome.Should().Be(PaymentGatewayEventOutcome.AmountMismatch);
            recorded.Amount.Should().Be(2400m);

            (await db.AuditLogs.AsNoTracking()
                    .CountAsync(a => a.EntityId == arranged.PaymentId &&
                                     a.Action == AuditActions.PaymentGatewayRejected))
                .Should().Be(1, "the refusal is on the record, not only in the log file");
        });
    }

    [Fact]
    public async Task A_failure_event_is_recorded_and_the_payment_is_left_for_staff()
    {
        var arranged = await ArrangeAwaitingPaymentAsync(_configured, 1750m);

        var payload = $$"""
            {"id":"evt_failed","event":"payment.failed","status":"failed",
             "reference":"{{arranged.Reference}}","amount":1750.00,"currency":"INR"}
            """;

        using var anonymous = _configured.CreateClient();
        var response = await anonymous.PostAsync("/api/payments/webhook/simulator", _configured.SignedEvent(payload));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadResultAsync(response)).Status.Should().Be(PaymentWebhookStatus.FailureRecorded);

        await _configured.WithDbAsync(async db =>
        {
            var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == arranged.PaymentId);
            payment.Status.Should().Be(PaymentStatus.AwaitingConfirmation, "a failed collection settles nothing");

            var recorded = await db.PaymentGatewayEvents.AsNoTracking().SingleAsync(e => e.EventId == "evt_failed");
            recorded.Outcome.Should().Be(PaymentGatewayEventOutcome.FailureRecorded);
            recorded.PaymentId.Should().Be(arranged.PaymentId);

            (await db.AuditLogs.AsNoTracking()
                    .CountAsync(a => a.Action == AuditActions.PaymentGatewayFailed &&
                                     a.EntityId == arranged.PaymentId))
                .Should().Be(1, "a failure is never silently dropped");
        });
    }

    [Fact]
    public async Task An_unknown_reference_is_recorded_rather_than_settled_or_retried_forever()
    {
        using var anonymous = _configured.CreateClient();

        var response = await anonymous.PostAsync(
            "/api/payments/webhook/simulator",
            _configured.SignedEvent(SuccessPayload("evt_orphan", "UPI-NOBODY-HAS-THIS", 100m)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadResultAsync(response)).Status.Should().Be(PaymentWebhookStatus.PaymentNotFound);

        await _configured.WithDbAsync(async db =>
        {
            var recorded = await db.PaymentGatewayEvents.AsNoTracking().SingleAsync(e => e.EventId == "evt_orphan");
            recorded.Outcome.Should().Be(PaymentGatewayEventOutcome.PaymentNotFound);
            recorded.PaymentId.Should().BeNull();
        });
    }

    // ------------------------------------------------------------------ endpoint shape

    [Fact]
    public async Task The_endpoint_needs_no_token_and_a_signed_event_is_not_rejected_as_unauthenticated()
    {
        var arranged = await ArrangeAwaitingPaymentAsync(_configured, 800m);

        using var anonymous = _configured.CreateClient();
        anonymous.DefaultRequestHeaders.Authorization.Should().BeNull("a gateway holds no JWT");

        var response = await anonymous.PostAsync(
            "/api/payments/webhook/simulator",
            _configured.SignedEvent(SuccessPayload("evt_anonymous", arranged.Reference, 800m)));

        // The point is that the FallbackPolicy did not eat the request: a 401 here would be
        // indistinguishable from a signature failure, so the settlement is what proves it through.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadResultAsync(response)).Status.Should().Be(PaymentWebhookStatus.Settled);
    }

    [Fact]
    public async Task A_provider_slug_that_is_not_configured_is_inert()
    {
        var arranged = await ArrangeAwaitingPaymentAsync(_configured, 850m);

        using var anonymous = _configured.CreateClient();
        var response = await anonymous.PostAsync(
            "/api/payments/webhook/someone-elses-gateway",
            _configured.SignedEvent(SuccessPayload("evt_wrong_provider", arranged.Reference, 850m)));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        await AssertNothingHappenedAsync(arranged.PaymentId, "evt_wrong_provider");
    }

    // ------------------------------------------------------------------ off by default

    [Fact]
    public async Task With_no_gateway_configured_the_endpoint_is_inert_and_manual_confirmation_still_works()
    {
        var arranged = await ArrangeAwaitingPaymentAsync(_unconfigured, 1900m);

        using var anonymous = _unconfigured.CreateClient();
        var response = await anonymous.PostAsync(
            "/api/payments/webhook/simulator",
            _unconfigured.SignedEvent(SuccessPayload("evt_off", arranged.Reference, 1900m)));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var envelope = JsonSerializer.Deserialize<ApiResponse>(
            await response.Content.ReadAsStringAsync(), GymApiFactory.Json);

        envelope!.ErrorCode.Should().Be("GATEWAY_NOT_CONFIGURED");

        await _unconfigured.WithDbAsync(async db =>
        {
            (await db.PaymentGatewayEvents.AsNoTracking().CountAsync()).Should().Be(0, "nothing is recorded");

            var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == arranged.PaymentId);
            payment.Status.Should().Be(PaymentStatus.AwaitingConfirmation);
        });

        // …and the flow the gym uses today is exactly as it was.
        using var staff = await _unconfigured.CreateAuthenticatedClientAsync();

        var confirm = await staff.PostAsJsonAsync("/api/payments/confirm", new ConfirmPaymentDto
        {
            PaymentId = arranged.PaymentId,
            Remarks = "UTR checked against the bank statement."
        });

        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());

        await _unconfigured.WithDbAsync(async db =>
        {
            var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == arranged.PaymentId);
            payment.Status.Should().Be(PaymentStatus.Paid);
            payment.ConfirmedByUserId.Should().NotBeNull("a person confirmed this one");
        });
    }

    [Fact]
    public async Task The_upi_intent_tells_the_operator_which_world_they_are_in()
    {
        var withGateway = await UpiIntentAsync(_configured);
        withGateway.RequiresManualVerification.Should().BeFalse("a configured gateway verifies the transfer");
        withGateway.Instructions.Should().NotContain("NO automatic verification");

        var withoutGateway = await UpiIntentAsync(_unconfigured);
        withoutGateway.RequiresManualVerification.Should().BeTrue("nothing will reconcile this on its own");
        withoutGateway.Instructions.Should().Contain("NO automatic verification");
    }

    // ------------------------------------------------------------------ helpers

    private async Task AssertNothingHappenedAsync(int paymentId, string eventId) =>
        await _configured.WithDbAsync(async db =>
        {
            var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == paymentId);

            payment.Status.Should().Be(PaymentStatus.AwaitingConfirmation);
            payment.ConfirmedAtUtc.Should().BeNull();

            (await db.PaymentGatewayEvents.AsNoTracking().CountAsync(e => e.EventId == eventId))
                .Should().Be(0, "a refused delivery writes nothing at all");
        });

    private static async Task<PaymentWebhookResultDto> ReadResultAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var envelope = JsonSerializer.Deserialize<ApiResponse<PaymentWebhookResultDto>>(body, GymApiFactory.Json);

        envelope.Should().NotBeNull($"the endpoint answers with the standard envelope. Body: {body}");
        envelope!.Data.Should().NotBeNull($"the envelope carries the outcome. Body: {body}");

        return envelope.Data!;
    }

    private static string SuccessPayload(string eventId, string reference, decimal amount) =>
        $$"""
          {"id":"{{eventId}}","event":"payment.captured","status":"success",
           "reference":"{{reference}}","amount":{{amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}},
           "currency":"INR","utr":"UTR{{Math.Abs(eventId.GetHashCode())}}","vpa":"member@examplebank"}
          """;

    private static string Sign(string timestamp, string payload) =>
        Convert.ToHexString(HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(WebhookApiFactory.GatewaySecret),
                Encoding.UTF8.GetBytes($"{timestamp}.{payload}")))
            .ToLowerInvariant();

    private static async Task<UpiPaymentIntentDto> UpiIntentAsync(WebhookApiFactory factory)
    {
        using var staff = await factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(staff);

        await factory.WithDbAsync(async db =>
        {
            var gym = await db.GymSettings.FirstAsync();
            gym.UpiId = "gym@examplebank";
            gym.UpiPayeeName = "Example Gym";
            await db.SaveChangesAsync();
        });

        var response = await staff.PostAsJsonAsync("/api/payments/upi-intent", new UpiPaymentRequestDto
        {
            MemberId = member,
            Amount = 500m
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var envelope = JsonSerializer.Deserialize<ApiResponse<UpiPaymentIntentDto>>(
            await response.Content.ReadAsStringAsync(), GymApiFactory.Json);

        return envelope!.Data!;
    }

    /// <summary>
    /// The state the gym is actually in when a gateway calls: an operator has recorded the UPI
    /// collection against the reference the intent minted, and it is awaiting confirmation.
    /// </summary>
    private static async Task<Arranged> ArrangeAwaitingPaymentAsync(WebhookApiFactory factory, decimal amount)
    {
        using var staff = await factory.CreateAuthenticatedClientAsync();

        var memberId = await CreateMemberAsync(staff);
        var upiMethodId = await UpiMethodIdAsync(staff);
        var reference = $"UPI{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(1000, 9999)}{Guid.NewGuid():N}"[..24];

        var response = await staff.PostAsJsonAsync("/api/payments", new CreatePaymentDto
        {
            MemberId = memberId,
            Amount = amount,
            PaymentMethodId = upiMethodId,
            TransactionReference = reference,
            MarkConfirmed = false,
            Notes = "Awaiting the gateway."
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var envelope = JsonSerializer.Deserialize<ApiResponse<PaymentDto>>(
            await response.Content.ReadAsStringAsync(), GymApiFactory.Json);

        var payment = envelope!.Data!;
        payment.Status.Should().Be(PaymentStatus.AwaitingConfirmation);

        return new Arranged(payment.Id, payment.ReceiptNumber, reference, amount);
    }

    private static async Task<int> CreateMemberAsync(HttpClient staff)
    {
        var suffix = new string(Guid.NewGuid().ToString("N")[..10]
            .Select(c => char.IsDigit(c) ? (char)('a' + (c - '0')) : c).ToArray());

        var response = await staff.PostAsJsonAsync("/api/members", new CreateMemberDto
        {
            FullName = $"Webhook {char.ToUpperInvariant(suffix[0])}{suffix[1..]}",
            Gender = Gender.Male,
            Phone = "9" + Math.Abs(Guid.NewGuid().GetHashCode()).ToString("0000000000")[..9],
            Email = $"member-{Guid.NewGuid():N}@example.test",
            DateOfBirth = new DateTime(1994, 6, 15),
            JoiningDate = DateTime.Today
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").GetProperty("member").GetProperty("id").GetInt32();
    }

    private static async Task<int> UpiMethodIdAsync(HttpClient staff)
    {
        var response = await staff.GetAsync("/api/Payments/methods");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = JsonSerializer.Deserialize<ApiResponse<List<PaymentMethodDto>>>(
            await response.Content.ReadAsStringAsync(), GymApiFactory.Json);

        var upi = envelope!.Data!.First(m => m.Code == "UPI");
        return upi.Id;
    }

    private sealed record Arranged(int PaymentId, string ReceiptNumber, string Reference, decimal Amount);
}

/// <summary>
/// A host for the webhook cases, with the gateway either configured or not.
///
/// It is separate from <see cref="GymApiFactory"/> because whether a gateway exists is decided by
/// configuration read at startup, and these cases need both answers in one run. The mail file sink
/// is pointed at a throwaway folder so the receipt that settlement triggers can be seen.
/// </summary>
internal sealed class WebhookApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Test-only signing secret. Nothing in this repository ever signs real money.</summary>
    public const string GatewaySecret = "integration-test-gateway-hmac-secret-value-not-a-real-key";

    private const string JwtSecret = "integration-test-jwt-signing-secret-of-at-least-48-characters-long";

    private readonly bool _gatewayEnabled;
    private readonly string _databaseName = $"gym-webhook-{Guid.NewGuid():N}";

    public WebhookApiFactory(bool gatewayEnabled)
    {
        _gatewayEnabled = gatewayEnabled;

        MailDropDirectory = Path.Combine(Path.GetTempPath(), $"gym-webhook-mail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(MailDropDirectory);
    }

    /// <summary>Where the local mail sink drops the receipts this host sends.</summary>
    public string MailDropDirectory { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=(unused);Database=GymTests;",
            ["Database:AutoMigrate"] = "false",

            ["Jwt:Secret"] = JwtSecret,
            ["Jwt:Issuer"] = "GymManagement.IntegrationTests",
            ["Jwt:Audience"] = "GymManagement.IntegrationTests.Client",
            ["Jwt:AccessTokenMinutes"] = "60",
            ["Jwt:RefreshTokenDays"] = "7",

            ["License:Secret"] = GymApiFactory.LicenseSecret,
            ["License:TrialDays"] = "30",
            ["License:TrialMaxMembers"] = "5000",

            ["RateLimiting:GlobalPermitPerMinute"] = "1000000",
            ["RateLimiting:AuthPermitPerMinute"] = "1000000",
            ["RateLimiting:WebhookPermitPerMinute"] = "1000000",

            // The receipt the settlement triggers has to land somewhere observable, and never in
            // a real inbox: the file sink writes .eml/.txt to a throwaway folder and transmits
            // nothing.
            ["Email:Provider"] = "File",
            ["Email:FromAddress"] = "no-reply@gym.invalid",
            ["Email:FromName"] = "Gym Management Tests",
            ["Email:FileSink:Directory"] = MailDropDirectory,

            ["Serilog:MinimumLevel:Default"] = "Warning",
            ["Swagger:Enabled"] = "false",

            // The feature under test. Off is the shipped default, and one of the two hosts keeps
            // proving that. The secret lives here, in configuration — never in the database.
            ["Payments:Gateway:Enabled"] = _gatewayEnabled ? "true" : "false",
            ["Payments:Gateway:Provider"] = "simulator",
            ["Payments:Gateway:Secret"] = _gatewayEnabled ? GatewaySecret : string.Empty,
            ["Payments:Gateway:ToleranceSeconds"] = "300"
        };

        foreach (var (key, value) in settings) builder.UseSetting(key, value);

        builder.ConfigureServices(services =>
        {
            foreach (var descriptor in services
                         .Where(d => d.ServiceType == typeof(DbContextOptions<GymDbContext>) ||
                                     d.ServiceType == typeof(DbContextOptions) ||
                                     d.ServiceType == typeof(GymDbContext))
                         .ToList())
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<GymDbContext>(options => options
                .UseInMemoryDatabase(_databaseName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        });
    }

    public async Task InitialiseAsync()
    {
        using var scope = Services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<GymDbContext>();
        await context.Database.EnsureCreatedAsync();

        await scope.ServiceProvider.GetRequiredService<IDbSeeder>().SeedAsync();
    }

    public async Task WithDbAsync(Func<GymDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<GymDbContext>());
    }

    public async Task<T> FromDbAsync<T>(Func<GymDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<GymDbContext>());
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        using var anonymous = CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            UserNameOrEmail = GymApiFactory.AdminUserName,
            Password = GymApiFactory.AdminPassword,
            DeviceInfo = "payment-webhook-tests"
        });

        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponseDto>>(GymApiFactory.Json);

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", envelope!.Data!.AccessToken);

        return client;
    }

    /// <summary>
    /// Builds the body a gateway would send: the payload, a fresh timestamp, and an HMAC-SHA256 of
    /// <c>{timestamp}.{body}</c> in hex. Computed here from first principles rather than by calling
    /// the production verifier, so a mistake in that verifier cannot cancel itself out.
    /// </summary>
    public HttpContent SignedEvent(string payload, string? timestamp = null)
    {
        timestamp ??= DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        var signature = Convert.ToHexString(HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(GatewaySecret),
                Encoding.UTF8.GetBytes($"{timestamp}.{payload}")))
            .ToLowerInvariant();

        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        content.Headers.Add("X-Payment-Signature", $"sha256={signature}");
        content.Headers.Add("X-Payment-Timestamp", timestamp);

        return content;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing) return;

        try { Directory.Delete(MailDropDirectory, recursive: true); }
        catch (IOException) { /* a throwaway folder failing to vanish is not a test failure */ }
    }
}
