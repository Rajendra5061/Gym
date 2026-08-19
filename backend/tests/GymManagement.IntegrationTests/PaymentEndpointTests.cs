using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Domain.Enums;
using Xunit;

namespace GymManagement.IntegrationTests;

/// <summary>
/// Payment endpoints — cases Y-01, Y-03, Y-05, Y-06, Y-07, Y-08, Y-09, Y-10, Y-12, Y-13, Y-14,
/// Y-16 and Y-18.
/// </summary>
[Collection(GymApiCollection.Name)]
public class PaymentEndpointTests : ApiTestBase
{
    /// <summary>The Monthly plan (2500 + 500) less a 200 discount, taxed at 18%.</summary>
    private const decimal MonthlyTotal = 3304.00m;

    public PaymentEndpointTests(GymApiFixture fixture) : base(fixture) { }

    // ------------------------------------------------------------- helpers

    /// <summary>A member with an unpaid Monthly subscription, ready to be paid for.</summary>
    private async Task<(MemberDetailDto Member, SubscriptionDto Subscription)> UnpaidSubscriptionAsync(
        HttpClient client) => await SellSubscriptionAsync(client, payInFull: false);

    private static CreatePaymentDto PaymentFor(
        MemberDetailDto member, SubscriptionDto subscription, decimal amount,
        int methodId, string? reference = null, bool markConfirmed = true) => new()
    {
        MemberId = member.Id,
        SubscriptionId = subscription.Id,
        Amount = amount,
        DiscountAmount = 0m,
        TaxAmount = 0m,
        PaymentMethodId = methodId,
        TransactionReference = reference,
        MarkConfirmed = markConfirmed
    };

    /// <summary>Reads a subscription back so the money effects of a payment can be asserted.</summary>
    private async Task<SubscriptionDto> ReloadSubscriptionAsync(HttpClient client, int id) =>
        await ReadDataAsync<SubscriptionDto>(await client.GetAsync($"/api/subscriptions/{id}"));

    // ---------------------------------------------------------------- Y-01

    [Fact(DisplayName = "Y-01 A cash payment settles the subscription and returns a receipt number")]
    public async Task Create_CashPayment_SettlesTheSubscription()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, subscription) = await UnpaidSubscriptionAsync(client);
        var cashId = await PaymentMethodIdAsync(client, "CASH");

        var payment = await ReadDataAsync<PaymentDto>(
            await client.PostAsJsonAsync("/api/Payments",
                PaymentFor(member, subscription, MonthlyTotal, cashId)));

        payment.ReceiptNumber.Should().NotBeNullOrWhiteSpace();
        payment.Status.Should().Be(PaymentStatus.Paid);
        payment.FinalAmount.Should().Be(MonthlyTotal);

        var reloaded = await ReloadSubscriptionAsync(client, subscription.Id);
        reloaded.PaidAmount.Should().Be(MonthlyTotal);
        reloaded.OutstandingAmount.Should().Be(0m);
        reloaded.PaymentStatus.Should().Be(PaymentStatus.Paid);
        reloaded.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact(DisplayName = "Y-01 Receipt numbers are unique across payments")]
    public async Task Create_ProducesUniqueReceiptNumbers()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var cashId = await PaymentMethodIdAsync(client, "CASH");

        var (firstMember, firstSubscription) = await UnpaidSubscriptionAsync(client);
        var (secondMember, secondSubscription) = await UnpaidSubscriptionAsync(client);

        var first = await ReadDataAsync<PaymentDto>(
            await client.PostAsJsonAsync("/api/Payments",
                PaymentFor(firstMember, firstSubscription, 1000m, cashId)));

        var second = await ReadDataAsync<PaymentDto>(
            await client.PostAsJsonAsync("/api/Payments",
                PaymentFor(secondMember, secondSubscription, 1000m, cashId)));

        second.ReceiptNumber.Should().NotBe(first.ReceiptNumber);
    }

    [Fact(DisplayName = "Y-02 A part payment leaves the correct outstanding balance")]
    public async Task Create_PartPayment_LeavesTheBalanceOutstanding()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, subscription) = await UnpaidSubscriptionAsync(client);
        var cashId = await PaymentMethodIdAsync(client, "CASH");

        await ReadDataAsync<PaymentDto>(
            await client.PostAsJsonAsync("/api/Payments",
                PaymentFor(member, subscription, 1300m, cashId)));

        var reloaded = await ReloadSubscriptionAsync(client, subscription.Id);
        reloaded.PaidAmount.Should().Be(1300.00m);
        reloaded.OutstandingAmount.Should().Be(2004.00m);
        reloaded.PaymentStatus.Should().Be(PaymentStatus.PartiallyPaid);
    }

    // ---------------------------------------------------------------- Y-03

    [Fact(DisplayName = "Y-03 A payment above the outstanding balance is a 422 quoting the balance")]
    public async Task Create_OverPayment_Is422()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, subscription) = await UnpaidSubscriptionAsync(client);
        var cashId = await PaymentMethodIdAsync(client, "CASH");

        var response = await client.PostAsJsonAsync("/api/Payments",
            PaymentFor(member, subscription, MonthlyTotal + 100m, cashId));

        await AssertFailureAsync(response, HttpStatusCode.UnprocessableEntity, "BUSINESS_RULE");
        (await ReadErrorAsync(response)).Message.Should().Contain("3304.00");
    }

    [Fact(DisplayName = "Y-04 A zero amount is a 400 on Amount")]
    public async Task Create_WithAZeroAmount_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, subscription) = await UnpaidSubscriptionAsync(client);
        var cashId = await PaymentMethodIdAsync(client, "CASH");

        var response = await client.PostAsJsonAsync("/api/Payments",
            PaymentFor(member, subscription, 0m, cashId));

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(CreatePaymentDto.Amount));
    }

    // ---------------------------------------------------------------- Y-05

    [Fact(DisplayName = "Y-05 A UPI payment with no transaction reference is a 400")]
    public async Task Create_UpiWithoutAReference_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, subscription) = await UnpaidSubscriptionAsync(client);
        var upiId = await PaymentMethodIdAsync(client, "UPI");

        var response = await client.PostAsJsonAsync("/api/Payments",
            PaymentFor(member, subscription, 1000m, upiId));

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(CreatePaymentDto.TransactionReference));
    }

    [Fact(DisplayName = "Y-05 A UPI payment with a reference is accepted")]
    public async Task Create_UpiWithAReference_Succeeds()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, subscription) = await UnpaidSubscriptionAsync(client);
        var upiId = await PaymentMethodIdAsync(client, "UPI");
        var reference = UniqueReference();

        var payment = await ReadDataAsync<PaymentDto>(
            await client.PostAsJsonAsync("/api/Payments",
                PaymentFor(member, subscription, 1000m, upiId, reference)));

        payment.TransactionReference.Should().Be(reference);
    }

    // ---------------------------------------------------------------- Y-06

    [Fact(DisplayName = "Y-06 Recording the same transaction reference twice is a 409")]
    public async Task Create_WithADuplicateReference_Is409()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var upiId = await PaymentMethodIdAsync(client, "UPI");
        var reference = UniqueReference();

        var (firstMember, firstSubscription) = await UnpaidSubscriptionAsync(client);
        await ReadDataAsync<PaymentDto>(
            await client.PostAsJsonAsync("/api/Payments",
                PaymentFor(firstMember, firstSubscription, 1000m, upiId, reference)));

        var (secondMember, secondSubscription) = await UnpaidSubscriptionAsync(client);
        var response = await client.PostAsJsonAsync("/api/Payments",
            PaymentFor(secondMember, secondSubscription, 1000m, upiId, reference));

        await AssertFailureAsync(response, HttpStatusCode.Conflict, "CONFLICT");
        (await ReadErrorAsync(response)).Message.Should().Contain(reference);
    }

    // ----------------------------------------------------------- Y-07/Y-08

    [Fact(DisplayName = "Y-07 A payment saved without confirmation awaits it and does not count as paid")]
    public async Task Create_WithoutMarkConfirmed_AwaitsConfirmation()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, subscription) = await UnpaidSubscriptionAsync(client);
        var upiId = await PaymentMethodIdAsync(client, "UPI");

        var payment = await ReadDataAsync<PaymentDto>(
            await client.PostAsJsonAsync("/api/Payments",
                PaymentFor(member, subscription, MonthlyTotal, upiId, UniqueReference(), markConfirmed: false)));

        payment.Status.Should().Be(PaymentStatus.AwaitingConfirmation);

        var reloaded = await ReloadSubscriptionAsync(client, subscription.Id);
        reloaded.PaidAmount.Should().Be(0m);
        reloaded.OutstandingAmount.Should().Be(MonthlyTotal);
        reloaded.PaymentStatus.Should().Be(PaymentStatus.Pending);
    }

    [Fact(DisplayName = "Y-08 Confirming the payment makes it count towards the subscription")]
    public async Task Confirm_MakesThePaymentCount()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, subscription) = await UnpaidSubscriptionAsync(client);
        var upiId = await PaymentMethodIdAsync(client, "UPI");

        var payment = await ReadDataAsync<PaymentDto>(
            await client.PostAsJsonAsync("/api/Payments",
                PaymentFor(member, subscription, MonthlyTotal, upiId, UniqueReference(), markConfirmed: false)));

        var confirmed = await ReadDataAsync<PaymentDto>(
            await client.PostAsJsonAsync("/api/Payments/confirm",
                new ConfirmPaymentDto { PaymentId = payment.Id, Remarks = "Credit seen in the bank statement." }));

        confirmed.Status.Should().Be(PaymentStatus.Paid);
        confirmed.ConfirmedAtUtc.Should().NotBeNull();

        var reloaded = await ReloadSubscriptionAsync(client, subscription.Id);
        reloaded.PaidAmount.Should().Be(MonthlyTotal);
        reloaded.PaymentStatus.Should().Be(PaymentStatus.Paid);
        reloaded.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact(DisplayName = "Y-08 Confirming an already settled payment is a 422")]
    public async Task Confirm_AnAlreadyPaidPayment_Is422()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, subscription) = await UnpaidSubscriptionAsync(client);
        var cashId = await PaymentMethodIdAsync(client, "CASH");

        var payment = await ReadDataAsync<PaymentDto>(
            await client.PostAsJsonAsync("/api/Payments",
                PaymentFor(member, subscription, 1000m, cashId)));

        var response = await client.PostAsJsonAsync("/api/Payments/confirm",
            new ConfirmPaymentDto { PaymentId = payment.Id });

        await AssertFailureAsync(response, HttpStatusCode.UnprocessableEntity, "BUSINESS_RULE");
    }

    // ----------------------------------------------------------- Y-09/Y-10

    [Fact(DisplayName = "Y-09 A UPI intent returns a deep link and a reference without creating a payment")]
    public async Task UpiIntent_ReturnsADeepLinkAndCreatesNoPayment()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, subscription) = await UnpaidSubscriptionAsync(client);

        await WithGymSettingsAsync(client, s =>
        {
            s.UpiId = "myfitnessgym@examplebank";
            s.UpiPayeeName = "My Fitness Gym";
        }, async () =>
        {
            var intent = await ReadDataAsync<UpiPaymentIntentDto>(
                await client.PostAsJsonAsync("/api/Payments/upi-intent", new UpiPaymentRequestDto
                {
                    MemberId = member.Id,
                    SubscriptionId = subscription.Id,
                    Amount = 1500m
                }));

            intent.UpiDeepLink.Should().NotBeNull();
            intent.UpiDeepLink!.Should().StartWith("upi://pay?");
            intent.UpiDeepLink.Should().Contain("am=1500.00");
            intent.UpiDeepLink.Should().Contain("cu=INR");
            intent.UpiId.Should().Be("myfitnessgym@examplebank");
            intent.PaymentReference.Should().NotBeNullOrWhiteSpace();
            intent.RequiresManualVerification.Should().BeTrue();
            intent.Instructions.Should().NotBeNullOrWhiteSpace();
            intent.Amount.Should().Be(1500.00m);

            // No payment row is created: a static QR gives the gym no confirmation.
            var payments = await ReadDataAsync<List<PaymentDto>>(
                await client.GetAsync($"/api/Payments/member/{member.Id}"));
            payments.Should().BeEmpty();
        });
    }

    [Fact(DisplayName = "Y-10 A UPI intent with no UPI id configured is a 422")]
    public async Task UpiIntent_WithoutAConfiguredUpiId_Is422()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, subscription) = await UnpaidSubscriptionAsync(client);

        await WithGymSettingsAsync(client, s =>
        {
            s.UpiId = null;
            s.UpiPayeeName = null;
        }, async () =>
        {
            var response = await client.PostAsJsonAsync("/api/Payments/upi-intent", new UpiPaymentRequestDto
            {
                MemberId = member.Id,
                SubscriptionId = subscription.Id,
                Amount = 1000m
            });

            await AssertFailureAsync(response, HttpStatusCode.UnprocessableEntity, "BUSINESS_RULE");
            (await ReadErrorAsync(response)).Message.Should().Contain("UPI");
        });
    }

    // ----------------------------------------------------------- Y-12/Y-13

    [Fact(DisplayName = "Y-12 The receipt payload carries the amount in words and the gym header")]
    public async Task Receipt_IncludesTheAmountInWords()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, subscription) = await UnpaidSubscriptionAsync(client);
        var cashId = await PaymentMethodIdAsync(client, "CASH");

        var payment = await ReadDataAsync<PaymentDto>(
            await client.PostAsJsonAsync("/api/Payments",
                PaymentFor(member, subscription, MonthlyTotal, cashId)));

        var receipt = await ReadDataAsync<PaymentReceiptDto>(
            await client.GetAsync($"/api/Payments/{payment.Id}/receipt"));

        receipt.ReceiptNumber.Should().Be(payment.ReceiptNumber);
        receipt.FinalAmount.Should().Be(MonthlyTotal);
        receipt.AmountInWords.Should().Be("Three Thousand Three Hundred Four Rupees Only");
        receipt.MemberCode.Should().Be(member.MemberCode);
        receipt.MemberName.Should().Be(member.FullName);
        receipt.GymName.Should().NotBeNullOrWhiteSpace();
        receipt.PaymentMethodName.Should().Be("Cash");
        receipt.PlanName.Should().Be("Monthly");
    }

    [Fact(DisplayName = "Y-13 The receipt PDF endpoint returns application/pdf starting with %PDF")]
    public async Task ReceiptPdf_ReturnsAPdf()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, subscription) = await UnpaidSubscriptionAsync(client);
        var cashId = await PaymentMethodIdAsync(client, "CASH");

        var payment = await ReadDataAsync<PaymentDto>(
            await client.PostAsJsonAsync("/api/Payments",
                PaymentFor(member, subscription, 1000m, cashId)));

        var response = await client.GetAsync($"/api/Payments/{payment.Id}/receipt/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().NotBeEmpty();
        System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }

    // ----------------------------------------------------------- Y-14/Y-16

    [Fact(DisplayName = "Y-14 A refund request within the balance is raised as pending")]
    public async Task CreateRefund_IsRaisedPending()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, subscription) = await UnpaidSubscriptionAsync(client);
        var cashId = await PaymentMethodIdAsync(client, "CASH");

        var payment = await ReadDataAsync<PaymentDto>(
            await client.PostAsJsonAsync("/api/Payments",
                PaymentFor(member, subscription, MonthlyTotal, cashId)));

        var refund = await ReadDataAsync<PaymentRefundDto>(
            await client.PostAsJsonAsync("/api/Payments/refunds", new CreateRefundDto
            {
                PaymentId = payment.Id,
                Amount = 800m,
                Reason = "Goodwill adjustment after a temporary closure."
            }));

        refund.Status.Should().Be(RefundStatus.Pending);
        refund.Amount.Should().Be(800.00m);
        refund.RefundNumber.Should().NotBeNullOrWhiteSpace();
        refund.PaymentId.Should().Be(payment.Id);
    }

    [Fact(DisplayName = "Y-16 Approving a refund adjusts the payment and the subscription")]
    public async Task ApproveRefund_AdjustsThePaymentAndSubscription()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, subscription) = await UnpaidSubscriptionAsync(client);
        var cashId = await PaymentMethodIdAsync(client, "CASH");

        var payment = await ReadDataAsync<PaymentDto>(
            await client.PostAsJsonAsync("/api/Payments",
                PaymentFor(member, subscription, MonthlyTotal, cashId)));

        var refund = await ReadDataAsync<PaymentRefundDto>(
            await client.PostAsJsonAsync("/api/Payments/refunds", new CreateRefundDto
            {
                PaymentId = payment.Id,
                Amount = 800m,
                Reason = "Partial refund on early cancellation."
            }));

        var approved = await ReadDataAsync<PaymentRefundDto>(
            await client.PostAsJsonAsync("/api/Payments/refunds/approve", new ApproveRefundDto
            {
                RefundId = refund.Id,
                Approve = true,
                Remarks = "Approved by the manager."
            }));

        approved.Status.Should().Be(RefundStatus.Completed);
        approved.ApprovedAt.Should().NotBeNull();

        var reloadedPayment = await ReadDataAsync<PaymentDto>(
            await client.GetAsync($"/api/Payments/{payment.Id}"));
        reloadedPayment.RefundedAmount.Should().Be(800.00m);
        reloadedPayment.Status.Should().Be(PaymentStatus.PartiallyRefunded);

        var reloadedSubscription = await ReloadSubscriptionAsync(client, subscription.Id);
        reloadedSubscription.PaidAmount.Should().Be(MonthlyTotal - 800.00m);
        reloadedSubscription.OutstandingAmount.Should().Be(800.00m);
        reloadedSubscription.PaymentStatus.Should().Be(PaymentStatus.PartiallyPaid);
    }

    [Fact(DisplayName = "Y-15 A refund above the refundable balance is a 422")]
    public async Task CreateRefund_AboveTheBalance_Is422()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, subscription) = await UnpaidSubscriptionAsync(client);
        var cashId = await PaymentMethodIdAsync(client, "CASH");

        var payment = await ReadDataAsync<PaymentDto>(
            await client.PostAsJsonAsync("/api/Payments",
                PaymentFor(member, subscription, 1000m, cashId)));

        var response = await client.PostAsJsonAsync("/api/Payments/refunds", new CreateRefundDto
        {
            PaymentId = payment.Id,
            Amount = 1500m,
            Reason = "Attempting to refund more than was ever collected."
        });

        await AssertFailureAsync(response, HttpStatusCode.UnprocessableEntity, "BUSINESS_RULE");
    }

    [Fact(DisplayName = "Y-17 Rejecting a refund leaves every money figure untouched")]
    public async Task ApproveRefund_WhenRejected_ChangesNoMoney()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, subscription) = await UnpaidSubscriptionAsync(client);
        var cashId = await PaymentMethodIdAsync(client, "CASH");

        var payment = await ReadDataAsync<PaymentDto>(
            await client.PostAsJsonAsync("/api/Payments",
                PaymentFor(member, subscription, 1000m, cashId)));

        var refund = await ReadDataAsync<PaymentRefundDto>(
            await client.PostAsJsonAsync("/api/Payments/refunds", new CreateRefundDto
            {
                PaymentId = payment.Id,
                Amount = 400m,
                Reason = "Refund request that will be turned down."
            }));

        var rejected = await ReadDataAsync<PaymentRefundDto>(
            await client.PostAsJsonAsync("/api/Payments/refunds/approve", new ApproveRefundDto
            {
                RefundId = refund.Id,
                Approve = false,
                Remarks = "Outside the refund window."
            }));

        rejected.Status.Should().Be(RefundStatus.Rejected);

        var reloaded = await ReadDataAsync<PaymentDto>(await client.GetAsync($"/api/Payments/{payment.Id}"));
        reloaded.RefundedAmount.Should().Be(0m);
        reloaded.Status.Should().Be(PaymentStatus.Paid);
    }

    // ---------------------------------------------------------------- Y-18

    [Fact(DisplayName = "Y-18 Deleting a settled payment is a 422 telling the operator to refund instead")]
    public async Task Delete_ASettledPayment_Is422()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, subscription) = await UnpaidSubscriptionAsync(client);
        var cashId = await PaymentMethodIdAsync(client, "CASH");

        var payment = await ReadDataAsync<PaymentDto>(
            await client.PostAsJsonAsync("/api/Payments",
                PaymentFor(member, subscription, 1000m, cashId)));

        var response = await client.DeleteAsync($"/api/Payments/{payment.Id}");

        await AssertFailureAsync(response, HttpStatusCode.UnprocessableEntity, "BUSINESS_RULE");
        (await ReadErrorAsync(response)).Message.Should().Contain("refund");
    }

    [Fact(DisplayName = "Y-18 An unconfirmed payment may still be deleted")]
    public async Task Delete_AnUnconfirmedPayment_Succeeds()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, subscription) = await UnpaidSubscriptionAsync(client);
        var cashId = await PaymentMethodIdAsync(client, "CASH");

        var payment = await ReadDataAsync<PaymentDto>(
            await client.PostAsJsonAsync("/api/Payments",
                PaymentFor(member, subscription, 1000m, cashId, markConfirmed: false)));

        var response = await client.DeleteAsync($"/api/Payments/{payment.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var remaining = await ReadDataAsync<List<PaymentDto>>(
            await client.GetAsync($"/api/Payments/member/{member.Id}"));
        remaining.Should().NotContain(p => p.Id == payment.Id);
    }

    // ---------------------------------------------------------- extra reads

    [Fact(DisplayName = "Y-20 The outstanding list contains a subscription that still owes money")]
    public async Task Outstanding_ListsTheUnpaidSubscription()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, subscription) = await UnpaidSubscriptionAsync(client);

        var page = await ReadDataAsync<PagedResult<OutstandingBalanceDto>>(
            await client.GetAsync($"/api/Payments/outstanding?Search={member.MemberCode}"));

        var row = page.Items.Should().ContainSingle().Subject;
        row.SubscriptionId.Should().Be(subscription.Id);
        row.OutstandingAmount.Should().Be(MonthlyTotal);
        row.MemberCode.Should().Be(member.MemberCode);
    }

    [Fact(DisplayName = "The seeded payment methods are listed with their reference requirements")]
    public async Task PaymentMethods_AreListed()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var methods = await ReadDataAsync<List<PaymentMethodDto>>(
            await client.GetAsync("/api/Payments/methods"));

        methods.Should().Contain(m => m.Code == "CASH" && !m.RequiresReference);
        methods.Should().Contain(m => m.Code == "UPI" && m.RequiresReference && m.SupportsQrCode);
        methods.Should().OnlyContain(m => m.IsActive);
    }

    [Fact(DisplayName = "Reading an unknown payment is a 404")]
    public async Task GetById_ForAnUnknownPayment_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/Payments/999999");

        await AssertFailureAsync(response, HttpStatusCode.NotFound, "NOT_FOUND");
    }
}
