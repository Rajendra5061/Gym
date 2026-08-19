using FluentAssertions;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using GymManagement.Infrastructure.Services;
using GymManagement.UnitTests.TestBase;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GymManagement.UnitTests.Services;

/// <summary>
/// Selling, freezing, resuming, cancelling and expiring a subscription — cases S-02, S-05, S-11,
/// S-12, S-14, S-15, S-16, S-18, S-19 and S-21.
/// </summary>
public class SubscriptionLifecycleTests : IAsyncLifetime
{
    private readonly FixedClock _clock = new();
    private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Admin();
    private readonly StubSettingsService _settings = new();
    private readonly RecordingAuditService _audit = new();
    private readonly RecordingNotificationService _notifications = new();
    private readonly RecordingReceiptMailer _receiptMailer = new();

    private GymDbContext _db = null!;
    private ReferenceData _data = null!;
    private SubscriptionService _service = null!;
    private Member _member = null!;

    public async Task InitializeAsync()
    {
        _db = InMemoryDbContextFactory.Create(_currentUser, _clock);
        _data = await InMemoryDbContextFactory.SeedReferenceDataAsync(_db);
        _member = await InMemoryDbContextFactory.AddMemberAsync(_db);

        _service = new SubscriptionService(_db, new SequentialCodeGenerator(), _clock, _currentUser,
            _audit, _settings, _notifications, _receiptMailer, NullLogger<SubscriptionService>.Instance);
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------- helpers

    /// <summary>The Monthly plan with a 200 discount bills 3304.00.</summary>
    private const decimal MonthlyTotal = 3304.00m;

    private CreateSubscriptionDto NewSubscription(
        int? memberId = null, int? planId = null, decimal discount = 200m,
        DateTime? startDate = null, CollectPaymentInlineDto? payment = null) => new()
    {
        MemberId = memberId ?? _member.Id,
        MembershipPlanId = planId ?? _data.MonthlyPlanId,
        StartDate = startDate ?? _clock.Today,
        DiscountAmount = discount,
        ChargeRegistrationFee = true,
        Payment = payment
    };

    private CollectPaymentInlineDto CashPayment(decimal amount, bool markConfirmed = true) => new()
    {
        PaymentMethodId = _data.CashMethodId,
        Amount = amount,
        MarkConfirmed = markConfirmed
    };

    private Task<SubscriptionDto> SellActiveAsync(int? memberId = null, DateTime? startDate = null) =>
        _service.CreateAsync(NewSubscription(
            memberId: memberId, startDate: startDate, payment: CashPayment(MonthlyTotal)));

    private Task<Subscription> ReloadAsync(int id) =>
        _db.Subscriptions.AsNoTracking().IgnoreQueryFilters().FirstAsync(s => s.Id == id);

    // -------------------------------------------------------------- create

    [Fact(DisplayName = "S-21 A subscription created with no payment stays Pending and fully outstanding")]
    public async Task Create_WithoutAPayment_LeavesThePaymentPending()
    {
        var result = await _service.CreateAsync(NewSubscription());

        result.FinalAmount.Should().Be(MonthlyTotal);
        result.PaidAmount.Should().Be(0m);
        result.OutstandingAmount.Should().Be(MonthlyTotal);
        result.PaymentStatus.Should().Be(PaymentStatus.Pending);
        result.Status.Should().Be(SubscriptionStatus.Pending);
    }

    [Fact(DisplayName = "S-21 A subscription with nothing paid raises a payment-pending notification")]
    public async Task Create_WithoutAPayment_RaisesAPaymentPendingNotification()
    {
        await _service.CreateAsync(NewSubscription());

        _notifications.CountOf(NotificationType.PaymentPending).Should().Be(1);
        _notifications.CountOf(NotificationType.PaymentSuccessful).Should().Be(0);
    }

    [Fact(DisplayName = "S-02 A subscription paid in full is Active, Paid and has nothing outstanding")]
    public async Task Create_WithAFullInlinePayment_IsActiveAndPaid()
    {
        var result = await _service.CreateAsync(
            NewSubscription(payment: CashPayment(MonthlyTotal)));

        result.PaidAmount.Should().Be(MonthlyTotal);
        result.OutstandingAmount.Should().Be(0m);
        result.PaymentStatus.Should().Be(PaymentStatus.Paid);
        result.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact(DisplayName = "S-02 Creating a subscription generates a code, history and an audit entry")]
    public async Task Create_WritesTheCodeHistoryAndAudit()
    {
        var result = await _service.CreateAsync(NewSubscription(payment: CashPayment(MonthlyTotal)));

        result.SubscriptionCode.Should().NotBeNullOrWhiteSpace();

        var history = await _db.SubscriptionHistory.AsNoTracking()
            .Where(h => h.SubscriptionId == result.Id)
            .ToListAsync();

        history.Should().Contain(h => h.ActionType == SubscriptionActionType.Created);
        history.Should().Contain(h => h.ActionType == SubscriptionActionType.PaymentReceived);
        _audit.Contains(AuditActions.SubscriptionCreated, nameof(Subscription)).Should().BeTrue();
    }

    [Fact(DisplayName = "S-03 The stored total comes from the plan, not from anything the caller sent")]
    public async Task Create_IgnoresAnyClientSuppliedTotal()
    {
        var result = await _service.CreateAsync(NewSubscription());
        var stored = await ReloadAsync(result.Id);

        var quote = await _service.GetQuoteAsync(new QuoteRequestDto
        {
            MembershipPlanId = _data.MonthlyPlanId,
            StartDate = _clock.Today,
            DiscountAmount = 200m,
            ChargeRegistrationFee = true
        });

        stored.PlanAmount.Should().Be(quote.PlanAmount);
        stored.RegistrationFee.Should().Be(quote.RegistrationFee);
        stored.TaxAmount.Should().Be(quote.TaxAmount);
        stored.FinalAmount.Should().Be(quote.FinalAmount);
    }

    [Fact(DisplayName = "S-02 A part payment leaves the subscription partially paid but active")]
    public async Task Create_WithAPartPayment_IsPartiallyPaid()
    {
        var result = await _service.CreateAsync(NewSubscription(payment: CashPayment(1000m)));

        result.PaidAmount.Should().Be(1000m);
        result.OutstandingAmount.Should().Be(MonthlyTotal - 1000m);
        result.PaymentStatus.Should().Be(PaymentStatus.PartiallyPaid);
        result.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact(DisplayName = "S-02 A payment left unconfirmed does not activate the subscription")]
    public async Task Create_WithAnUnconfirmedPayment_StaysPending()
    {
        var result = await _service.CreateAsync(
            NewSubscription(payment: CashPayment(MonthlyTotal, markConfirmed: false)));

        result.PaidAmount.Should().Be(0m);
        result.PaymentStatus.Should().Be(PaymentStatus.Pending);
        result.Status.Should().Be(SubscriptionStatus.Pending);
    }

    [Fact(DisplayName = "An inline payment above the total is refused")]
    public async Task Create_WithAnOverPayment_IsRefused()
    {
        var act = () => _service.CreateAsync(NewSubscription(payment: CashPayment(MonthlyTotal + 1m)));

        await act.Should().ThrowAsync<ValidationAppException>();
    }

    [Fact(DisplayName = "S-05 A second subscription overlapping an active one is refused")]
    public async Task Create_WhenAnActiveTermOverlaps_ThrowsBusinessRule()
    {
        await SellActiveAsync();

        var act = () => _service.CreateAsync(NewSubscription(startDate: _clock.Today.AddDays(5)));

        var error = await act.Should().ThrowAsync<BusinessRuleAppException>();
        error.Which.Message.Should().Contain("Renew");
    }

    [Fact(DisplayName = "S-05 A subscription starting after the active term ends is allowed")]
    public async Task Create_WhenTheNewTermStartsAfterTheOldOne_IsAllowed()
    {
        var first = await SellActiveAsync();

        var second = await _service.CreateAsync(
            NewSubscription(startDate: first.EndDate.AddDays(1), payment: CashPayment(MonthlyTotal)));

        second.Id.Should().NotBe(first.Id);
        second.StartDate.Should().Be(first.EndDate.AddDays(1));
    }

    [Fact(DisplayName = "Selling an inactive plan is refused")]
    public async Task Create_OnAnInactivePlan_ThrowsBusinessRule()
    {
        var act = () => _service.CreateAsync(NewSubscription(planId: _data.InactivePlanId, discount: 0m));

        await act.Should().ThrowAsync<BusinessRuleAppException>();
    }

    [Fact(DisplayName = "Selling to an unknown member is a not-found error")]
    public async Task Create_ForAnUnknownMember_ThrowsNotFound()
    {
        var act = () => _service.CreateAsync(NewSubscription(memberId: 999_999));

        await act.Should().ThrowAsync<NotFoundAppException>();
    }

    // --------------------------------------------------------------- freeze

    [Fact(DisplayName = "S-12 Freezing a plan that does not allow it is refused")]
    public async Task Freeze_OnAPlanWithNoFreezeAllowance_ThrowsBusinessRule()
    {
        var subscription = await _service.CreateAsync(NewSubscription(
            planId: _data.NoFreezePlanId, discount: 0m, payment: CashPayment(2360.00m)));

        var act = () => _service.FreezeAsync(new FreezeSubscriptionDto
        {
            SubscriptionId = subscription.Id,
            FreezeStartDate = _clock.Today,
            FreezeEndDate = _clock.Today.AddDays(3),
            Reason = "Travelling"
        });

        var error = await act.Should().ThrowAsync<BusinessRuleAppException>();
        error.Which.Message.Should().Contain("does not allow freezing");
    }

    [Fact(DisplayName = "S-11 A valid freeze extends the end date by the frozen days and sets Frozen")]
    public async Task Freeze_ExtendsTheEndDateAndSetsTheStatus()
    {
        var subscription = await SellActiveAsync();
        var originalEnd = subscription.EndDate;

        var frozen = await _service.FreezeAsync(new FreezeSubscriptionDto
        {
            SubscriptionId = subscription.Id,
            FreezeStartDate = _clock.Today,
            FreezeEndDate = _clock.Today.AddDays(7),
            Reason = "Away on work"
        });

        frozen.Status.Should().Be(SubscriptionStatus.Frozen);
        frozen.EndDate.Should().Be(originalEnd.AddDays(7));
        frozen.FreezeStartDate.Should().Be(_clock.Today);
        frozen.FreezeEndDate.Should().Be(_clock.Today.AddDays(7));
    }

    [Fact(DisplayName = "S-11 Freezing writes a history row")]
    public async Task Freeze_WritesHistory()
    {
        var subscription = await SellActiveAsync();

        await _service.FreezeAsync(new FreezeSubscriptionDto
        {
            SubscriptionId = subscription.Id,
            FreezeStartDate = _clock.Today,
            FreezeEndDate = _clock.Today.AddDays(7)
        });

        (await _db.SubscriptionHistory.AsNoTracking()
                .AnyAsync(h => h.SubscriptionId == subscription.Id &&
                               h.ActionType == SubscriptionActionType.Frozen))
            .Should().BeTrue();
    }

    [Fact(DisplayName = "S-13 A freeze longer than the plan's remaining allowance is refused")]
    public async Task Freeze_BeyondTheAllowance_ThrowsBusinessRule()
    {
        var subscription = await SellActiveAsync();

        var act = () => _service.FreezeAsync(new FreezeSubscriptionDto
        {
            SubscriptionId = subscription.Id,
            FreezeStartDate = _clock.Today,
            FreezeEndDate = _clock.Today.AddDays(20)
        });

        await act.Should().ThrowAsync<BusinessRuleAppException>();
    }

    [Fact(DisplayName = "A freeze that starts in the past is refused")]
    public async Task Freeze_StartingInThePast_ThrowsValidation()
    {
        var subscription = await SellActiveAsync();

        var act = () => _service.FreezeAsync(new FreezeSubscriptionDto
        {
            SubscriptionId = subscription.Id,
            FreezeStartDate = _clock.Today.AddDays(-1),
            FreezeEndDate = _clock.Today.AddDays(3)
        });

        await act.Should().ThrowAsync<ValidationAppException>();
    }

    [Fact(DisplayName = "Freezing a subscription that is not active is refused")]
    public async Task Freeze_OnANonActiveSubscription_ThrowsBusinessRule()
    {
        var pending = await _service.CreateAsync(NewSubscription());

        var act = () => _service.FreezeAsync(new FreezeSubscriptionDto
        {
            SubscriptionId = pending.Id,
            FreezeStartDate = _clock.Today,
            FreezeEndDate = _clock.Today.AddDays(3)
        });

        await act.Should().ThrowAsync<BusinessRuleAppException>();
    }

    // --------------------------------------------------------------- resume

    [Fact(DisplayName = "S-14 Resuming early credits only the days actually frozen")]
    public async Task Resume_BeforeTheFreezeEnds_AddsOnlyTheActualFrozenDays()
    {
        var subscription = await SellActiveAsync();
        var originalEnd = subscription.EndDate;

        await _service.FreezeAsync(new FreezeSubscriptionDto
        {
            SubscriptionId = subscription.Id,
            FreezeStartDate = _clock.Today,
            FreezeEndDate = _clock.Today.AddDays(7)
        });

        var resumed = await _service.ResumeAsync(new ResumeSubscriptionDto
        {
            SubscriptionId = subscription.Id,
            ResumeDate = _clock.Today.AddDays(3)
        });

        resumed.Status.Should().Be(SubscriptionStatus.Active);
        resumed.EndDate.Should().Be(originalEnd.AddDays(3));
        resumed.FrozenDaysUsed.Should().Be(3);
        resumed.FreezeStartDate.Should().BeNull();
        resumed.FreezeEndDate.Should().BeNull();
    }

    [Fact(DisplayName = "S-14 Resuming on the planned end date keeps the whole extension")]
    public async Task Resume_OnThePlannedEndDate_KeepsTheFullExtension()
    {
        var subscription = await SellActiveAsync();
        var originalEnd = subscription.EndDate;

        await _service.FreezeAsync(new FreezeSubscriptionDto
        {
            SubscriptionId = subscription.Id,
            FreezeStartDate = _clock.Today,
            FreezeEndDate = _clock.Today.AddDays(7)
        });

        var resumed = await _service.ResumeAsync(new ResumeSubscriptionDto
        {
            SubscriptionId = subscription.Id,
            ResumeDate = _clock.Today.AddDays(7)
        });

        resumed.EndDate.Should().Be(originalEnd.AddDays(7));
        resumed.FrozenDaysUsed.Should().Be(7);
    }

    [Fact(DisplayName = "S-14 The frozen allowance used is carried forward to the next freeze")]
    public async Task Resume_ThenFreezeAgain_RespectsTheRemainingAllowance()
    {
        var subscription = await SellActiveAsync();

        await _service.FreezeAsync(new FreezeSubscriptionDto
        {
            SubscriptionId = subscription.Id,
            FreezeStartDate = _clock.Today,
            FreezeEndDate = _clock.Today.AddDays(5)
        });

        await _service.ResumeAsync(new ResumeSubscriptionDto
        {
            SubscriptionId = subscription.Id,
            ResumeDate = _clock.Today.AddDays(5)
        });

        // 7 day allowance, 5 already used: a 3 day freeze must be refused.
        var act = () => _service.FreezeAsync(new FreezeSubscriptionDto
        {
            SubscriptionId = subscription.Id,
            FreezeStartDate = _clock.Today,
            FreezeEndDate = _clock.Today.AddDays(3)
        });

        await act.Should().ThrowAsync<BusinessRuleAppException>();
    }

    [Fact(DisplayName = "Resuming a subscription that is not frozen is refused")]
    public async Task Resume_WhenNotFrozen_ThrowsBusinessRule()
    {
        var subscription = await SellActiveAsync();

        var act = () => _service.ResumeAsync(new ResumeSubscriptionDto
        {
            SubscriptionId = subscription.Id,
            ResumeDate = _clock.Today
        });

        await act.Should().ThrowAsync<BusinessRuleAppException>();
    }

    // --------------------------------------------------------------- cancel

    [Theory(DisplayName = "S-16 Cancelling without a reason is refused")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Cancel_WithoutAReason_ThrowsValidation(string reason)
    {
        var subscription = await SellActiveAsync();

        var act = () => _service.CancelAsync(new CancelSubscriptionDto
        {
            SubscriptionId = subscription.Id,
            Reason = reason
        });

        var error = await act.Should().ThrowAsync<ValidationAppException>();
        error.Which.Errors.Should().ContainKey("Reason");
    }

    [Fact(DisplayName = "S-15 Cancelling with a reason stores the status, reason and timestamp")]
    public async Task Cancel_WithAReason_MarksItCancelled()
    {
        var subscription = await SellActiveAsync();

        var cancelled = await _service.CancelAsync(new CancelSubscriptionDto
        {
            SubscriptionId = subscription.Id,
            Reason = "Member relocated to another city."
        });

        cancelled.Status.Should().Be(SubscriptionStatus.Cancelled);
        cancelled.CancellationReason.Should().Be("Member relocated to another city.");
        cancelled.CancelledAt.Should().Be(_clock.UtcNow);
    }

    [Fact(DisplayName = "S-15 Cancelling writes a history row and an audit entry")]
    public async Task Cancel_WritesHistoryAndAudit()
    {
        var subscription = await SellActiveAsync();

        await _service.CancelAsync(new CancelSubscriptionDto
        {
            SubscriptionId = subscription.Id,
            Reason = "Member relocated."
        });

        (await _db.SubscriptionHistory.AsNoTracking()
                .AnyAsync(h => h.SubscriptionId == subscription.Id &&
                               h.ActionType == SubscriptionActionType.Cancelled))
            .Should().BeTrue();

        _audit.Contains(AuditActions.SubscriptionCancelled, nameof(Subscription)).Should().BeTrue();
    }

    [Fact(DisplayName = "Cancelling an already cancelled subscription is refused")]
    public async Task Cancel_Twice_ThrowsBusinessRule()
    {
        var subscription = await SellActiveAsync();
        var dto = new CancelSubscriptionDto { SubscriptionId = subscription.Id, Reason = "Member relocated." };

        await _service.CancelAsync(dto);

        var act = () => _service.CancelAsync(dto);

        await act.Should().ThrowAsync<BusinessRuleAppException>();
    }

    [Fact(DisplayName = "S-17 Cancelling with a refund raises a pending refund, never an automatic payout")]
    public async Task Cancel_WithARefund_RaisesAPendingRefundRow()
    {
        var subscription = await SellActiveAsync();

        await _service.CancelAsync(new CancelSubscriptionDto
        {
            SubscriptionId = subscription.Id,
            Reason = "Member relocated.",
            RefundRemainingAmount = true
        });

        var refunds = await _db.PaymentRefunds.AsNoTracking().ToListAsync();

        refunds.Should().NotBeEmpty();
        refunds.Should().OnlyContain(r => r.Status == RefundStatus.Pending);
        refunds.Sum(r => r.Amount).Should().BeLessThanOrEqualTo(MonthlyTotal);
    }

    // -------------------------------------------------------- expiry job

    [Fact(DisplayName = "S-18 A term past its grace period is expired by the job")]
    public async Task ProcessExpiries_ExpiresATermPastItsGracePeriod()
    {
        var subscription = await SellActiveAsync();
        var stored = await _db.Subscriptions.FirstAsync(s => s.Id == subscription.Id);

        // Past the end date and past the 3 day grace period the plan grants.
        _clock.Now = stored.EndDate.AddDays(stored.GracePeriodDays + 1).AddHours(9);

        var result = await _service.ProcessExpiriesAsync();

        result.ExpiredCount.Should().Be(1);
        (await ReloadAsync(subscription.Id)).Status.Should().Be(SubscriptionStatus.Expired);
    }

    [Fact(DisplayName = "S-19 Running the expiry job twice on the same day changes nothing the second time")]
    public async Task ProcessExpiries_RunTwice_IsIdempotent()
    {
        var subscription = await SellActiveAsync();
        var stored = await _db.Subscriptions.FirstAsync(s => s.Id == subscription.Id);
        _clock.Now = stored.EndDate.AddDays(stored.GracePeriodDays + 1).AddHours(9);

        var first = await _service.ProcessExpiriesAsync();
        var notificationsAfterFirstRun = _notifications.Raised.Count;

        var second = await _service.ProcessExpiriesAsync();

        first.ExpiredCount.Should().Be(1);
        second.ExpiredCount.Should().Be(0);
        second.NotificationsCreated.Should().Be(0);
        _notifications.Raised.Should().HaveCount(notificationsAfterFirstRun);
    }

    [Fact(DisplayName = "S-18 A term still inside its grace period is not expired")]
    public async Task ProcessExpiries_InsideTheGracePeriod_LeavesTheSubscriptionActive()
    {
        var subscription = await SellActiveAsync();
        var stored = await _db.Subscriptions.FirstAsync(s => s.Id == subscription.Id);
        _clock.Now = stored.EndDate.AddDays(stored.GracePeriodDays).AddHours(9);

        var result = await _service.ProcessExpiriesAsync();

        result.ExpiredCount.Should().Be(0);
        (await ReloadAsync(subscription.Id)).Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact(DisplayName = "S-19 The expiry job reports that there was nothing to do on an idle database")]
    public async Task ProcessExpiries_WithNothingToProcess_ReportsSo()
    {
        var result = await _service.ProcessExpiriesAsync();

        result.ExpiredCount.Should().Be(0);
        result.ResumedFromFreeze.Should().Be(0);
        result.NotificationsCreated.Should().Be(0);
        result.Messages.Should().ContainSingle().Which.Should().Contain("Nothing to process");
    }
}
