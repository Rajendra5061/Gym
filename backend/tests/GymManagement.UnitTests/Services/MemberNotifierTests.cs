using FluentAssertions;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using GymManagement.Infrastructure.Messaging;
using GymManagement.Infrastructure.Services;
using GymManagement.UnitTests.TestBase;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GymManagement.UnitTests.Services;

/// <summary>
/// Keeps every WhatsApp message in memory instead of delivering it, mirroring
/// <see cref="CapturingEmailSender"/> so a test can read the template, its parameters and the
/// preview. Shared with <see cref="WishesDispatcherTests"/>.
/// </summary>
public sealed class CapturingWhatsAppSender : IWhatsAppSender
{
    public List<WhatsAppMessage> Sent { get; } = new();

    public bool IsEnabled { get; set; } = true;

    public string ProviderName => "Capture";

    /// <summary>Set to make every send throw the way an unreachable gateway would.</summary>
    public bool ThrowOnSend { get; set; }

    /// <summary>Set to make the provider decline the message rather than throw.</summary>
    public bool SkipEverything { get; set; }

    public Task<WhatsAppDeliveryResult> SendAsync(WhatsAppMessage message, CancellationToken ct = default)
    {
        if (ThrowOnSend) throw new InvalidOperationException("The WhatsApp gateway is unavailable.");

        if (SkipEverything) return Task.FromResult(WhatsAppDeliveryResult.Skipped(ProviderName, "declined"));

        Sent.Add(message);
        return Task.FromResult(WhatsAppDeliveryResult.Sent(ProviderName));
    }
}

/// <summary>
/// Messaging a member on their own occasions: the claim that makes every occasion send-once, the
/// release that keeps a dead gateway from eating a message, the two opt-outs, and the channel
/// defaults the payment occasions depend on.
/// </summary>
public class MemberNotifierTests : IAsyncLifetime
{
    private readonly FixedClock _clock = new();
    private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Admin();
    private readonly StubSettingsService _settings = new();
    private readonly CapturingEmailSender _email = new();
    private readonly CapturingWhatsAppSender _whatsApp = new();
    private readonly MemberNotificationsOptions _options = new();
    private readonly WhatsAppOptions _whatsAppOptions = new();

    private GymDbContext _db = null!;
    private ReferenceData _data = null!;
    private Member _member = null!;
    private MemberNotifier _notifier = null!;

    public async Task InitializeAsync()
    {
        _db = InMemoryDbContextFactory.Create(_currentUser, _clock);
        _data = await InMemoryDbContextFactory.SeedReferenceDataAsync(_db);
        _member = await InMemoryDbContextFactory.AddMemberAsync(_db, "Ravi Kumar", phone: "9876543210");

        _member.Email = "ravi.kumar@example.test";
        await _db.SaveChangesAsync();

        _notifier = NewNotifier();
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    private MemberNotifier NewNotifier() => new(
        _db, _email, _whatsApp, _settings, _clock, _options, _whatsAppOptions,
        NullLogger<MemberNotifier>.Instance);

    private async Task<Payment> AddPaymentAsync(
        PaymentStatus status = PaymentStatus.Paid, int? memberId = null)
    {
        var payment = new Payment
        {
            ReceiptNumber = $"RCP-2026-{Random.Shared.Next(100_000, 999_999)}",
            MemberId = memberId ?? _member.Id,
            MembershipPlanId = _data.AnnualPlanId,
            Amount = 3000m,
            DiscountAmount = 200m,
            TaxAmount = 504m,
            FinalAmount = 3304m,
            PaymentMethodId = _data.CashMethodId,
            PaymentDate = _clock.Now,
            Status = status,
            CollectedByUserId = _data.AdminUserId
        };

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return payment;
    }

    // ============================================================ claim and dedupe

    [Fact]
    public async Task Birthday_wishes_go_out_once_per_year_on_both_channels()
    {
        var first = await _notifier.NotifyBirthdayAsync(_member.Id);

        first.EmailSent.Should().BeTrue();
        first.WhatsAppSent.Should().BeTrue();

        var second = await _notifier.NotifyBirthdayAsync(_member.Id);
        second.AlreadySent.Should().BeTrue();
        second.AnySent.Should().BeFalse();

        // A fresh scope changes nothing: the log row remembers, not the instance.
        (await NewNotifier().NotifyBirthdayAsync(_member.Id)).AlreadySent.Should().BeTrue();

        _email.Sent.Should().ContainSingle();
        _whatsApp.Sent.Should().ContainSingle()
            .Which.Parameters.Should().Equal("Ravi", "Test Gym");
    }

    [Fact]
    public async Task A_claim_whose_every_channel_failed_is_released_so_a_retry_can_succeed()
    {
        _email.ThrowOnSend = true;
        _whatsApp.ThrowOnSend = true;

        var outcome = await _notifier.NotifyBirthdayAsync(_member.Id);

        outcome.AnySent.Should().BeFalse();
        outcome.AlreadySent.Should().BeFalse();
        (await _db.MemberNotificationLogs.AnyAsync())
            .Should().BeFalse("a dead gateway costs a delay, not the message");

        _email.ThrowOnSend = false;
        _whatsApp.ThrowOnSend = false;

        (await _notifier.NotifyBirthdayAsync(_member.Id)).AnySent.Should().BeTrue();
        _email.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task Channels_that_decline_rather_than_throw_also_release_the_claim()
    {
        _email.SkipEverything = true;
        _whatsApp.SkipEverything = true;

        var outcome = await _notifier.NotifyBirthdayAsync(_member.Id);

        outcome.AnySent.Should().BeFalse();
        (await _db.MemberNotificationLogs.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Nothing_is_claimed_when_no_channel_is_available()
    {
        _email.IsEnabled = false;
        _whatsApp.IsEnabled = false;

        var outcome = await _notifier.NotifyBirthdayAsync(_member.Id);

        outcome.AnySent.Should().BeFalse();
        outcome.Reason.Should().NotBeNull();
        (await _db.MemberNotificationLogs.AnyAsync()).Should().BeFalse();
    }

    // ============================================================ the two opt-outs

    [Fact]
    public async Task Wishes_opt_out_suppresses_the_birthday_but_never_the_payment()
    {
        _member.WishesOptOut = true;
        await _db.SaveChangesAsync();

        var birthday = await _notifier.NotifyBirthdayAsync(_member.Id);
        birthday.AnySent.Should().BeFalse();
        birthday.Reason.Should().Contain("opted out");

        var payment = await AddPaymentAsync();
        var outcome = await _notifier.NotifyPaymentAsync(payment.Id);

        outcome.WhatsAppSent.Should().BeTrue("a payment confirmation is transactional, not a wish");
    }

    [Fact]
    public async Task WhatsApp_opt_out_silences_only_the_whatsapp_channel()
    {
        _member.WhatsAppOptOut = true;
        await _db.SaveChangesAsync();

        var outcome = await _notifier.NotifyBirthdayAsync(_member.Id);

        outcome.EmailSent.Should().BeTrue();
        outcome.WhatsAppSent.Should().BeFalse();
        _whatsApp.Sent.Should().BeEmpty();
    }

    // ============================================================ streaks

    [Fact]
    public async Task A_streak_that_is_not_a_milestone_sends_nothing_and_claims_nothing()
    {
        var outcome = await _notifier.NotifyStreakAsync(_member.Id, 5);

        outcome.Should().Be(MemberNotificationOutcome.Nothing);
        _email.Sent.Should().BeEmpty();
        _whatsApp.Sent.Should().BeEmpty();
        (await _db.MemberNotificationLogs.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task A_milestone_streak_is_congratulated_exactly_once()
    {
        (await _notifier.NotifyStreakAsync(_member.Id, 7)).AnySent.Should().BeTrue();
        (await _notifier.NotifyStreakAsync(_member.Id, 7)).AlreadySent.Should().BeTrue();

        _whatsApp.Sent.Should().ContainSingle()
            .Which.Parameters.Should().Equal("7", "Ravi", "Test Gym");
    }

    // ============================================================ payments

    [Fact]
    public async Task The_first_confirmed_payment_reads_welcome_and_a_later_one_reads_renewed()
    {
        var first = await AddPaymentAsync();
        (await _notifier.NotifyPaymentAsync(first.Id)).WhatsAppSent.Should().BeTrue();

        _whatsApp.Sent.Should().ContainSingle();
        _whatsApp.Sent[0].TemplateName.Should().Be("payment_received");
        _whatsApp.Sent[0].Parameters.Should().Equal(
            "Test Gym", "Ravi", "₹3304.00", "Annual", first.ReceiptNumber, "—");

        _clock.AdvanceDays(30);
        var renewal = await AddPaymentAsync();
        (await _notifier.NotifyPaymentAsync(renewal.Id)).WhatsAppSent.Should().BeTrue();

        _whatsApp.Sent.Should().HaveCount(2);
        _whatsApp.Sent[1].TemplateName.Should().Be("payment_renewal");
        // The renewal template leads with the member, not the gym.
        _whatsApp.Sent[1].Parameters.Should().Equal(
            "Ravi", "Annual", "Test Gym", "₹3304.00", renewal.ReceiptNumber, "—");
    }

    [Fact]
    public async Task Payments_never_email_by_default_because_the_receipt_mailer_already_does()
    {
        var payment = await AddPaymentAsync();

        var outcome = await _notifier.NotifyPaymentAsync(payment.Id);

        outcome.WhatsAppSent.Should().BeTrue();
        outcome.EmailSent.Should().BeFalse();
        _email.Sent.Should().BeEmpty("PaymentReceiptMailer already emails the receipt itself");
    }

    [Fact]
    public async Task An_unconfirmed_payment_is_not_announced()
    {
        var payment = await AddPaymentAsync(PaymentStatus.AwaitingConfirmation);

        var outcome = await _notifier.NotifyPaymentAsync(payment.Id);

        outcome.AnySent.Should().BeFalse();
        _whatsApp.Sent.Should().BeEmpty();
        (await _db.MemberNotificationLogs.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task A_missing_payment_is_skipped_without_throwing()
    {
        var act = async () => await _notifier.NotifyPaymentAsync(999_999);

        var outcome = (await act.Should().NotThrowAsync()).Which;
        outcome.AnySent.Should().BeFalse();
        outcome.AlreadySent.Should().BeFalse();
    }

    // ============================================================ diet plans

    [Fact]
    public async Task A_diet_plan_is_announced_once_per_day_and_a_later_edit_reads_as_updated()
    {
        var plan = new DietPlan
        {
            MemberId = _member.Id,
            Title = "Cut Phase 1",
            Goal = "Fat loss",
            StartDate = _clock.Today,
            CreatedAt = _clock.UtcNow,
            Meals =
            {
                new DietPlanMeal { Title = "Oats", MealType = DietMealType.Breakfast },
                new DietPlanMeal { Title = "Dal and rice", MealType = DietMealType.Lunch },
            }
        };
        _db.DietPlans.Add(plan);
        await _db.SaveChangesAsync();

        (await _notifier.NotifyDietPlanAsync(plan.Id)).AnySent.Should().BeTrue();
        (await _notifier.NotifyDietPlanAsync(plan.Id)).AlreadySent
            .Should().BeTrue("a same-day edit must not spam the member");

        _whatsApp.Sent.Should().ContainSingle();
        _whatsApp.Sent[0].TemplateName.Should().Be("diet_plan_ready");
        _whatsApp.Sent[0].Parameters.Should()
            .ContainInOrder("Ravi", "Cut Phase 1", "Test Gym", "ready", "2");

        // Tomorrow's revision is a fresh occasion, and it reads as an update.
        _clock.AdvanceDays(1);
        plan.Notes = "Swapped lunch";
        await _db.SaveChangesAsync();

        (await _notifier.NotifyDietPlanAsync(plan.Id)).AnySent.Should().BeTrue();
        _whatsApp.Sent.Should().HaveCount(2);
        _whatsApp.Sent[1].Parameters.Should().Contain("updated");
    }

    // ============================================================ channel switches

    [Fact]
    public void The_channel_defaults_are_email_off_for_payments_and_everything_else_on()
    {
        _notifier.IsChannelEnabled(MemberNotificationKind.PaymentReceived, email: true).Should().BeFalse();
        _notifier.IsChannelEnabled(MemberNotificationKind.RenewalPayment, email: true).Should().BeFalse();
        _notifier.IsChannelEnabled(MemberNotificationKind.DietPlan, email: true).Should().BeTrue();
        _notifier.IsChannelEnabled(MemberNotificationKind.Birthday, email: true).Should().BeTrue();
        _notifier.IsChannelEnabled(MemberNotificationKind.PaymentReceived, email: false).Should().BeTrue();
        _notifier.IsChannelEnabled(MemberNotificationKind.Festival, email: false).Should().BeTrue();
    }

    [Fact]
    public void A_configured_occasion_switch_overrides_the_default()
    {
        _options.Occasions["PaymentReceived"] =
            new MemberNotificationsOptions.OccasionChannelOptions { Email = true, WhatsApp = false };

        _notifier.IsChannelEnabled(MemberNotificationKind.PaymentReceived, email: true).Should().BeTrue();
        _notifier.IsChannelEnabled(MemberNotificationKind.PaymentReceived, email: false).Should().BeFalse();
    }

    [Fact]
    public async Task An_occasion_mapped_to_an_empty_template_never_goes_out_on_whatsapp()
    {
        _whatsAppOptions.Templates["Birthday"] = "";

        var outcome = await _notifier.NotifyBirthdayAsync(_member.Id);

        outcome.EmailSent.Should().BeTrue();
        outcome.WhatsAppSent.Should().BeFalse();
        _whatsApp.Sent.Should().BeEmpty();
    }
}
