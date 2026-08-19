using FluentAssertions;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Domain.Constants;
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
/// Collecting, confirming and refunding money — cases Y-01 to Y-10, Y-12, Y-14, Y-15 and Y-18.
/// </summary>
public class PaymentServiceTests : IAsyncLifetime
{
    /// <summary>Monthly plan (2500 + 500) less a 200 discount, taxed at 18%.</summary>
    private const decimal SubscriptionTotal = 3304.00m;

    private readonly FixedClock _clock = new();
    private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Admin();
    private readonly StubSettingsService _settings = new();
    private readonly RecordingAuditService _audit = new();
    private readonly RecordingNotificationService _notifications = new();
    private readonly RecordingReceiptMailer _receiptMailer = new();

    private GymDbContext _db = null!;
    private ReferenceData _data = null!;
    private PaymentService _service = null!;
    private SubscriptionService _subscriptions = null!;
    private Member _member = null!;
    private SubscriptionDto _subscription = null!;

    public async Task InitializeAsync()
    {
        _db = InMemoryDbContextFactory.Create(_currentUser, _clock);
        _data = await InMemoryDbContextFactory.SeedReferenceDataAsync(_db);
        _member = await InMemoryDbContextFactory.AddMemberAsync(_db);

        var codes = new SequentialCodeGenerator();

        _subscriptions = new SubscriptionService(_db, codes, _clock, _currentUser, _audit, _settings,
            _notifications, _receiptMailer, NullLogger<SubscriptionService>.Instance);

        _service = new PaymentService(_db, codes, _clock, _currentUser, _audit, _settings,
            _notifications, new StubPdfExportService(), _receiptMailer,
            NullLogger<PaymentService>.Instance);

        // An unpaid subscription is the starting point for nearly every payment case.
        _subscription = await _subscriptions.CreateAsync(new CreateSubscriptionDto
        {
            MemberId = _member.Id,
            MembershipPlanId = _data.MonthlyPlanId,
            StartDate = _clock.Today,
            DiscountAmount = 200m,
            ChargeRegistrationFee = true
        });
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------- helpers

    private CreatePaymentDto Payment(
        decimal amount,
        int? methodId = null,
        string? reference = null,
        bool markConfirmed = true,
        int? subscriptionId = null) => new()
    {
        MemberId = _member.Id,
        SubscriptionId = subscriptionId ?? _subscription.Id,
        Amount = amount,
        DiscountAmount = 0m,
        TaxAmount = 0m,
        PaymentMethodId = methodId ?? _data.CashMethodId,
        TransactionReference = reference,
        MarkConfirmed = markConfirmed
    };

    private Task<Subscription> ReloadSubscriptionAsync() =>
        _db.Subscriptions.AsNoTracking().FirstAsync(s => s.Id == _subscription.Id);

    // -------------------------------------------------------------- create

    [Fact(DisplayName = "Y-01 A full cash payment settles the subscription and gets a receipt number")]
    public async Task Create_WithAFullPayment_SetsTheSubscriptionToPaid()
    {
        var payment = await _service.CreateAsync(Payment(SubscriptionTotal));

        payment.Status.Should().Be(PaymentStatus.Paid);
        payment.FinalAmount.Should().Be(SubscriptionTotal);
        payment.ReceiptNumber.Should().NotBeNullOrWhiteSpace();

        var subscription = await ReloadSubscriptionAsync();
        subscription.PaidAmount.Should().Be(SubscriptionTotal);
        subscription.PaymentStatus.Should().Be(PaymentStatus.Paid);
        subscription.OutstandingAmount.Should().Be(0m);
    }

    [Fact(DisplayName = "Y-01 A payment activates a subscription that was still pending")]
    public async Task Create_WithAPayment_ActivatesThePendingSubscription()
    {
        await _service.CreateAsync(Payment(SubscriptionTotal));

        (await ReloadSubscriptionAsync()).Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact(DisplayName = "Y-01 Creating a payment writes an audit entry")]
    public async Task Create_WritesAnAuditEntry()
    {
        await _service.CreateAsync(Payment(SubscriptionTotal));

        _audit.Contains(AuditActions.PaymentCreated, nameof(Payment)).Should().BeTrue();
    }

    [Fact(DisplayName = "Y-01 Every receipt number is unique")]
    public async Task Create_ProducesUniqueReceiptNumbers()
    {
        var first = await _service.CreateAsync(Payment(1000m));
        var second = await _service.CreateAsync(Payment(1000m));

        first.ReceiptNumber.Should().NotBe(second.ReceiptNumber);
    }

    [Fact(DisplayName = "Y-02 A part payment leaves the correct outstanding balance")]
    public async Task Create_WithAPartPayment_SetsPartiallyPaid()
    {
        await _service.CreateAsync(Payment(1300m));

        var subscription = await ReloadSubscriptionAsync();
        subscription.PaidAmount.Should().Be(1300.00m);
        subscription.PaymentStatus.Should().Be(PaymentStatus.PartiallyPaid);
        subscription.OutstandingAmount.Should().Be(2004.00m);
    }

    [Fact(DisplayName = "Y-02 Two part payments add up to a settled subscription")]
    public async Task Create_TwoPartPayments_SettleTheSubscription()
    {
        await _service.CreateAsync(Payment(2000m));
        await _service.CreateAsync(Payment(1304m));

        var subscription = await ReloadSubscriptionAsync();
        subscription.PaidAmount.Should().Be(SubscriptionTotal);
        subscription.PaymentStatus.Should().Be(PaymentStatus.Paid);
    }

    [Fact(DisplayName = "Y-03 A payment above the outstanding balance is refused")]
    public async Task Create_AboveTheOutstandingBalance_ThrowsBusinessRule()
    {
        var act = () => _service.CreateAsync(Payment(SubscriptionTotal + 0.01m));

        var error = await act.Should().ThrowAsync<BusinessRuleAppException>();
        error.Which.Message.Should().Contain("3304.00");
    }

    [Fact(DisplayName = "Y-03 A second payment that overshoots the remaining balance is refused")]
    public async Task Create_OvershootingTheRemainder_ThrowsBusinessRule()
    {
        await _service.CreateAsync(Payment(3000m));

        var act = () => _service.CreateAsync(Payment(500m));

        await act.Should().ThrowAsync<BusinessRuleAppException>();
    }

    [Theory(DisplayName = "Y-04 A zero or negative amount is refused")]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Create_WithANonPositiveAmount_ThrowsValidation(int amount)
    {
        var act = () => _service.CreateAsync(Payment(amount));

        var error = await act.Should().ThrowAsync<ValidationAppException>();
        error.Which.Errors.Should().ContainKey(nameof(CreatePaymentDto.Amount));
    }

    [Fact(DisplayName = "Y-05 A UPI payment without a transaction reference is refused")]
    public async Task Create_UpiWithoutAReference_ThrowsValidation()
    {
        var act = () => _service.CreateAsync(Payment(1000m, methodId: _data.UpiMethodId));

        var error = await act.Should().ThrowAsync<ValidationAppException>();
        error.Which.Errors.Should().ContainKey(nameof(CreatePaymentDto.TransactionReference));
    }

    [Fact(DisplayName = "Y-05 A UPI payment with a reference is accepted")]
    public async Task Create_UpiWithAReference_Succeeds()
    {
        var payment = await _service.CreateAsync(
            Payment(1000m, methodId: _data.UpiMethodId, reference: "UTR123456789"));

        payment.TransactionReference.Should().Be("UTR123456789");
    }

    [Fact(DisplayName = "Y-06 Recording the same transaction reference twice is a conflict")]
    public async Task Create_WithADuplicateReference_ThrowsConflict()
    {
        const string reference = "UTR-DUPLICATE-001";
        await _service.CreateAsync(Payment(1000m, methodId: _data.UpiMethodId, reference: reference));

        var act = () => _service.CreateAsync(Payment(1000m, methodId: _data.UpiMethodId, reference: reference));

        var error = await act.Should().ThrowAsync<ConflictAppException>();
        error.Which.Message.Should().Contain(reference);
    }

    [Fact(DisplayName = "A payment against another member's subscription is refused")]
    public async Task Create_ForAMismatchedSubscription_ThrowsValidation()
    {
        var otherMember = await InMemoryDbContextFactory.AddMemberAsync(_db, "Other Member");
        var dto = Payment(1000m);
        dto.MemberId = otherMember.Id;

        var act = () => _service.CreateAsync(dto);

        await act.Should().ThrowAsync<ValidationAppException>();
    }

    // ------------------------------------------------------------- confirm

    [Fact(DisplayName = "Y-07 A payment left unconfirmed awaits confirmation and does not count as paid")]
    public async Task Create_WithoutMarkConfirmed_AwaitsConfirmation()
    {
        var payment = await _service.CreateAsync(
            Payment(SubscriptionTotal, methodId: _data.UpiMethodId, reference: "UTR-PENDING-1",
                markConfirmed: false));

        payment.Status.Should().Be(PaymentStatus.AwaitingConfirmation);

        var subscription = await ReloadSubscriptionAsync();
        subscription.PaidAmount.Should().Be(0m);
        subscription.PaymentStatus.Should().Be(PaymentStatus.Pending);
        subscription.Status.Should().Be(SubscriptionStatus.Pending);
    }

    [Fact(DisplayName = "Y-08 Confirming the payment makes it count towards the subscription")]
    public async Task Confirm_MakesThePaymentCount()
    {
        var payment = await _service.CreateAsync(
            Payment(SubscriptionTotal, methodId: _data.UpiMethodId, reference: "UTR-PENDING-2",
                markConfirmed: false));

        var confirmed = await _service.ConfirmAsync(new ConfirmPaymentDto { PaymentId = payment.Id });

        confirmed.Status.Should().Be(PaymentStatus.Paid);
        confirmed.ConfirmedAtUtc.Should().Be(_clock.UtcNow);

        var subscription = await ReloadSubscriptionAsync();
        subscription.PaidAmount.Should().Be(SubscriptionTotal);
        subscription.PaymentStatus.Should().Be(PaymentStatus.Paid);
        subscription.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact(DisplayName = "Y-08 Confirming writes an audit entry")]
    public async Task Confirm_WritesAnAuditEntry()
    {
        var payment = await _service.CreateAsync(
            Payment(1000m, methodId: _data.CashMethodId, markConfirmed: false));

        await _service.ConfirmAsync(new ConfirmPaymentDto { PaymentId = payment.Id });

        _audit.Contains(AuditActions.PaymentConfirmed, nameof(Payment)).Should().BeTrue();
    }

    [Fact(DisplayName = "Y-08 Confirming an already settled payment is refused")]
    public async Task Confirm_AnAlreadyPaidPayment_ThrowsBusinessRule()
    {
        var payment = await _service.CreateAsync(Payment(1000m));

        var act = () => _service.ConfirmAsync(new ConfirmPaymentDto { PaymentId = payment.Id });

        await act.Should().ThrowAsync<BusinessRuleAppException>();
    }

    // -------------------------------------------------------------- refund

    private async Task<PaymentDto> SettledPaymentAsync(decimal amount = SubscriptionTotal) =>
        await _service.CreateAsync(Payment(amount));

    [Fact(DisplayName = "Y-14 A refund within the refundable balance is raised as pending")]
    public async Task CreateRefund_WithinTheBalance_IsRaisedPending()
    {
        var payment = await SettledPaymentAsync();

        var refund = await _service.CreateRefundAsync(new CreateRefundDto
        {
            PaymentId = payment.Id,
            Amount = 500m,
            Reason = "Goodwill adjustment after a closure."
        });

        refund.Status.Should().Be(RefundStatus.Pending);
        refund.Amount.Should().Be(500.00m);
        refund.RefundNumber.Should().NotBeNullOrWhiteSpace();
    }

    [Fact(DisplayName = "Y-15 A refund above the refundable balance is refused")]
    public async Task CreateRefund_AboveTheRefundableBalance_ThrowsBusinessRule()
    {
        var payment = await SettledPaymentAsync(1000m);

        var act = () => _service.CreateRefundAsync(new CreateRefundDto
        {
            PaymentId = payment.Id,
            Amount = 1000.01m,
            Reason = "Attempting to refund more than was collected."
        });

        await act.Should().ThrowAsync<BusinessRuleAppException>();
    }

    [Fact(DisplayName = "Y-15 A refund with a zero amount is refused")]
    public async Task CreateRefund_WithAZeroAmount_ThrowsValidation()
    {
        var payment = await SettledPaymentAsync(1000m);

        var act = () => _service.CreateRefundAsync(new CreateRefundDto
        {
            PaymentId = payment.Id,
            Amount = 0m,
            Reason = "Nothing to refund."
        });

        await act.Should().ThrowAsync<ValidationAppException>();
    }

    [Fact(DisplayName = "Y-16 Approving a refund adjusts the payment and the subscription")]
    public async Task ApproveRefund_AdjustsThePaymentAndSubscription()
    {
        var payment = await SettledPaymentAsync();
        var refund = await _service.CreateRefundAsync(new CreateRefundDto
        {
            PaymentId = payment.Id,
            Amount = 800m,
            Reason = "Partial refund on early cancellation."
        });

        var approved = await _service.ApproveRefundAsync(new ApproveRefundDto
        {
            RefundId = refund.Id,
            Approve = true
        });

        approved.Status.Should().Be(RefundStatus.Completed);

        var stored = await _db.Payments.AsNoTracking().FirstAsync(p => p.Id == payment.Id);
        stored.RefundedAmount.Should().Be(800.00m);
        stored.Status.Should().Be(PaymentStatus.PartiallyRefunded);

        var subscription = await ReloadSubscriptionAsync();
        subscription.PaidAmount.Should().Be(SubscriptionTotal - 800.00m);
        subscription.PaymentStatus.Should().Be(PaymentStatus.PartiallyPaid);
    }

    [Fact(DisplayName = "Y-16 Refunding the whole payment marks it fully refunded")]
    public async Task ApproveRefund_ForTheWholeAmount_MarksItRefunded()
    {
        var payment = await SettledPaymentAsync(1000m);
        var refund = await _service.CreateRefundAsync(new CreateRefundDto
        {
            PaymentId = payment.Id,
            Amount = 1000m,
            Reason = "Full refund after a duplicate charge."
        });

        await _service.ApproveRefundAsync(new ApproveRefundDto { RefundId = refund.Id, Approve = true });

        var stored = await _db.Payments.AsNoTracking().FirstAsync(p => p.Id == payment.Id);
        stored.Status.Should().Be(PaymentStatus.Refunded);
        stored.RefundedAmount.Should().Be(1000.00m);
    }

    [Fact(DisplayName = "Y-17 Rejecting a refund leaves every money figure untouched")]
    public async Task ApproveRefund_WhenRejected_ChangesNoMoney()
    {
        var payment = await SettledPaymentAsync(1000m);
        var refund = await _service.CreateRefundAsync(new CreateRefundDto
        {
            PaymentId = payment.Id,
            Amount = 400m,
            Reason = "Refund request that will be turned down."
        });

        var rejected = await _service.ApproveRefundAsync(new ApproveRefundDto
        {
            RefundId = refund.Id,
            Approve = false
        });

        rejected.Status.Should().Be(RefundStatus.Rejected);

        var stored = await _db.Payments.AsNoTracking().FirstAsync(p => p.Id == payment.Id);
        stored.RefundedAmount.Should().Be(0m);
        stored.Status.Should().Be(PaymentStatus.Paid);
    }

    [Fact(DisplayName = "Y-14 A caller without payments.refund cannot approve their own refund immediately")]
    public async Task CreateRefund_WithImmediateApprovalButNoPermission_StaysPending()
    {
        _currentUser.Roles = new[] { RoleNames.Staff };
        _currentUser.Permissions = new[] { Permissions.PaymentsCollect };

        var payment = await SettledPaymentAsync(1000m);
        var refund = await _service.CreateRefundAsync(new CreateRefundDto
        {
            PaymentId = payment.Id,
            Amount = 100m,
            Reason = "Requested immediate approval without the permission.",
            ApproveImmediately = true
        });

        refund.Status.Should().Be(RefundStatus.Pending);
    }

    // -------------------------------------------------------------- delete

    [Fact(DisplayName = "Y-18 Deleting a settled payment is refused; a refund must be raised instead")]
    public async Task SoftDelete_ASettledPayment_ThrowsBusinessRule()
    {
        var payment = await SettledPaymentAsync(1000m);

        var act = () => _service.SoftDeleteAsync(payment.Id);

        var error = await act.Should().ThrowAsync<BusinessRuleAppException>();
        error.Which.Message.Should().Contain("refund");
    }

    [Fact(DisplayName = "Y-18 An unconfirmed payment may still be deleted")]
    public async Task SoftDelete_AnUnconfirmedPayment_Succeeds()
    {
        var payment = await _service.CreateAsync(Payment(1000m, markConfirmed: false));

        await _service.SoftDeleteAsync(payment.Id);

        (await _db.Payments.AsNoTracking().AnyAsync(p => p.Id == payment.Id)).Should().BeFalse();
        (await _db.Payments.AsNoTracking().IgnoreQueryFilters().AnyAsync(p => p.Id == payment.Id))
            .Should().BeTrue();
    }

    // ------------------------------------------------------------ receipts

    [Fact(DisplayName = "Y-12 The receipt carries the amount in words")]
    public async Task GetReceipt_IncludesTheAmountInWords()
    {
        var payment = await SettledPaymentAsync();

        var receipt = await _service.GetReceiptAsync(payment.Id);

        receipt.FinalAmount.Should().Be(SubscriptionTotal);
        receipt.AmountInWords.Should().Be("Three Thousand Three Hundred Four Rupees Only");
        receipt.MemberName.Should().Be(_member.FullName);
        receipt.PaymentMethodName.Should().Be("Cash");
    }

    [Fact(DisplayName = "Y-13 The receipt PDF is a real PDF payload")]
    public async Task GetReceiptPdf_ReturnsAPdf()
    {
        var payment = await SettledPaymentAsync();

        var file = await _service.GetReceiptPdfAsync(payment.Id);

        file.ContentType.Should().Be("application/pdf");
        file.FileName.Should().EndWith(".pdf");
        System.Text.Encoding.ASCII.GetString(file.Content, 0, 4).Should().Be("%PDF");
    }

    // ---------------------------------------------------------- UPI intent

    [Fact(DisplayName = "Y-09 A UPI intent returns a deep link and a reference without creating a payment")]
    public async Task CreateUpiIntent_ReturnsADeepLinkAndCreatesNoPayment()
    {
        var intent = await _service.CreateUpiIntentAsync(new UpiPaymentRequestDto
        {
            MemberId = _member.Id,
            SubscriptionId = _subscription.Id,
            Amount = 1000m
        });

        intent.UpiDeepLink.Should().StartWith("upi://pay?");
        intent.UpiDeepLink.Should().Contain("am=1000.00");
        intent.PaymentReference.Should().NotBeNullOrWhiteSpace();
        intent.RequiresManualVerification.Should().BeTrue();
        intent.Instructions.Should().NotBeNullOrWhiteSpace();

        (await _db.Payments.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact(DisplayName = "Y-10 A UPI intent with no UPI id configured is refused")]
    public async Task CreateUpiIntent_WithoutAConfiguredUpiId_ThrowsBusinessRule()
    {
        _settings.Settings.UpiId = null;

        var act = () => _service.CreateUpiIntentAsync(new UpiPaymentRequestDto
        {
            MemberId = _member.Id,
            Amount = 1000m
        });

        var error = await act.Should().ThrowAsync<BusinessRuleAppException>();
        error.Which.Message.Should().Contain("UPI");
    }

    [Fact(DisplayName = "Y-09 A UPI intent above the outstanding balance is refused")]
    public async Task CreateUpiIntent_AboveTheOutstandingBalance_ThrowsBusinessRule()
    {
        var act = () => _service.CreateUpiIntentAsync(new UpiPaymentRequestDto
        {
            MemberId = _member.Id,
            SubscriptionId = _subscription.Id,
            Amount = SubscriptionTotal + 1m
        });

        await act.Should().ThrowAsync<BusinessRuleAppException>();
    }

    // ------------------------------------------------------- receipt email

    [Fact(DisplayName = "The receipt email is requested once a payment has been recorded")]
    public async Task Create_RequestsTheReceiptEmail_AfterTheMoneyIsRecorded()
    {
        var result = await _service.CreateAsync(Payment(1000m));

        _receiptMailer.CountFor(result.Id).Should().Be(1);
    }

    [Fact(DisplayName = "A payment is recorded even when sending the receipt fails outright")]
    public async Task Create_WhenTheReceiptEmailThrows_StillRecordsThePayment()
    {
        // The harshest case the mail path can produce: the mailer itself blows up rather than
        // reporting a failure. The money must still be on the books.
        _receiptMailer.ThrowOnSend = true;

        var act = () => _service.CreateAsync(Payment(1000m));

        await act.Should().NotThrowAsync();

        var stored = await _db.Payments.AsNoTracking().SingleOrDefaultAsync();
        stored.Should().NotBeNull();
        stored!.FinalAmount.Should().Be(1000m);
        stored.Status.Should().Be(PaymentStatus.Paid);
        stored.ReceiptEmailedAtUtc.Should().BeNull();

        // And the subscription ledger moved with it, so nothing was half-applied.
        (await ReloadSubscriptionAsync()).PaidAmount.Should().Be(1000m);
    }

    [Fact(DisplayName = "An unconfirmed payment gets its receipt email only once it is confirmed")]
    public async Task Confirm_RequestsTheReceiptEmail_ForThePaymentBeingConfirmed()
    {
        var created = await _service.CreateAsync(Payment(1000m, methodId: _data.UpiMethodId,
            reference: "UTR-EMAIL-1", markConfirmed: false));

        _receiptMailer.Requests.Should().ContainSingle();

        await _service.ConfirmAsync(new ConfirmPaymentDto { PaymentId = created.Id });

        // Requested on both passes; the mailer's own stamp is what stops a second message going out.
        _receiptMailer.CountFor(created.Id).Should().Be(2);
    }

    // ------------------------------------------------- plan recorded on a payment

    [Fact(DisplayName = "A payment may name the plan it was for without a subscription")]
    public async Task Create_WithAMembershipPlanAndNoSubscription_ReportsThePlanName()
    {
        var result = await _service.CreateAsync(new CreatePaymentDto
        {
            MemberId = _member.Id,
            SubscriptionId = null,
            MembershipPlanId = _data.AnnualPlanId,
            Amount = 1500m,
            PaymentMethodId = _data.CashMethodId
        });

        result.MembershipPlanId.Should().Be(_data.AnnualPlanId);
        result.PlanName.Should().Be("Annual");
        result.SubscriptionId.Should().BeNull();

        // …and it reaches the receipt, which is where a member looks to see what they paid for.
        var receipt = await _service.GetReceiptAsync(result.Id);
        receipt.PlanName.Should().Be("Annual");
    }

    [Fact(DisplayName = "The subscription's plan stays authoritative when both are present")]
    public async Task Create_WithBothASubscriptionAndAPlan_PrefersTheSubscriptionPlan()
    {
        var result = await _service.CreateAsync(new CreatePaymentDto
        {
            MemberId = _member.Id,
            SubscriptionId = _subscription.Id,
            MembershipPlanId = _data.AnnualPlanId,
            Amount = 1000m,
            PaymentMethodId = _data.CashMethodId
        });

        result.PlanName.Should().Be("Monthly");
        result.MembershipPlanId.Should().Be(_data.AnnualPlanId);
    }

    [Fact(DisplayName = "A payment naming a plan that does not exist is refused")]
    public async Task Create_WithAnUnknownMembershipPlan_ThrowsNotFound()
    {
        var act = () => _service.CreateAsync(new CreatePaymentDto
        {
            MemberId = _member.Id,
            MembershipPlanId = 987_654,
            Amount = 500m,
            PaymentMethodId = _data.CashMethodId
        });

        await act.Should().ThrowAsync<NotFoundAppException>();
    }
}
