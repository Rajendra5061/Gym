using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Domain.Enums;
using Xunit;

namespace GymManagement.IntegrationTests;

/// <summary>
/// Subscription endpoints — cases S-01, S-02, S-03, S-04, S-05, S-07, S-11, S-14, S-15, S-16,
/// S-18 and S-19.
/// </summary>
[Collection(GymApiCollection.Name)]
public class SubscriptionEndpointTests : ApiTestBase
{
    /// <summary>
    /// The provider limit that used to block this case is gone: the expiry filter no longer uses
    /// EF.Functions.DateDiffDay, and the job now runs under the in-memory provider — the unit suite
    /// exercises it directly (SubscriptionLifecycleTests, cases S-18/S-19/S-20) against a database
    /// of its own. What still blocks it *here* is shared state, not translation: process-expiries
    /// also runs SyncMemberStatusesAsync, which flips every Active member with no usable
    /// subscription to Expired, and auto-resumes frozen subscriptions. This suite shares one host
    /// and one database, and several cases deliberately create members with no subscription, so
    /// running the job mid-suite would rewrite rows other classes assert on and make the run order
    /// dependent. See the final report.
    /// </summary>
    private const string ExpiryJobMutatesTheSharedFixture =
        "Not runnable inside the shared integration fixture: process-expiries also runs " +
        "SyncMemberStatusesAsync, which demotes every Active member holding no usable subscription " +
        "to Expired and auto-resumes frozen subscriptions. This suite shares one database and " +
        "several cases create members with no subscription, so the job would rewrite rows other " +
        "classes assert on. The job itself is covered against an isolated database by " +
        "SubscriptionLifecycleTests cases S-18/S-19/S-20. See the final report.";

    /// <summary>The Monthly plan (2500 + 500) less a 200 discount, taxed at 18%.</summary>
    private const decimal MonthlyTotal = 3304.00m;

    public SubscriptionEndpointTests(GymApiFixture fixture) : base(fixture) { }

    // --------------------------------------------------------------- quote

    [Fact(DisplayName = "S-01 The quote returns 2500 / 500 / 200 / 504 / 3304 for the Monthly plan")]
    public async Task Quote_ForTheMonthlyPlan_ReturnsTheVerifiedFigures()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var planId = await MonthlyPlanIdAsync(client);

        var quote = await ReadDataAsync<SubscriptionQuoteDto>(
            await client.PostAsJsonAsync("/api/subscriptions/quote", new QuoteRequestDto
            {
                MembershipPlanId = planId,
                StartDate = DateTime.Today,
                DiscountAmount = 200m,
                ChargeRegistrationFee = true
            }));

        quote.PlanAmount.Should().Be(2500.00m);
        quote.RegistrationFee.Should().Be(500.00m);
        quote.DiscountAmount.Should().Be(200.00m);
        quote.TaxAmount.Should().Be(504.00m);
        quote.FinalAmount.Should().Be(3304.00m);
        quote.MaxAllowedDiscount.Should().Be(450.00m);
    }

    [Fact(DisplayName = "S-01 The quoted end date is 30 days after the start, inclusive")]
    public async Task Quote_ReturnsTheTermDates()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var planId = await MonthlyPlanIdAsync(client);
        var start = DateTime.Today;

        var quote = await ReadDataAsync<SubscriptionQuoteDto>(
            await client.PostAsJsonAsync("/api/subscriptions/quote", new QuoteRequestDto
            {
                MembershipPlanId = planId,
                StartDate = start,
                DiscountAmount = 0m,
                ChargeRegistrationFee = true
            }));

        quote.StartDate.Date.Should().Be(start);
        quote.EndDate.Date.Should().Be(start.AddMonths(1).AddDays(-1));
        quote.TotalDays.Should().Be(30);
    }

    // -------------------------------------------------------------- create

    [Fact(DisplayName = "S-02 Creating with an inline payment returns Active, Paid and nothing outstanding")]
    public async Task Create_WithAFullPayment_IsActiveAndPaid()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var (_, subscription) = await SellSubscriptionAsync(client);

        subscription.SubscriptionCode.Should().NotBeNullOrWhiteSpace();
        subscription.FinalAmount.Should().Be(MonthlyTotal);
        subscription.PaidAmount.Should().Be(MonthlyTotal);
        subscription.OutstandingAmount.Should().Be(0m);
        subscription.PaymentStatus.Should().Be(PaymentStatus.Paid);
        subscription.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact(DisplayName = "S-21 Creating with no payment leaves it Pending and fully outstanding")]
    public async Task Create_WithoutAPayment_IsPendingAndOutstanding()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var (_, subscription) = await SellSubscriptionAsync(client, payInFull: false);

        subscription.PaidAmount.Should().Be(0m);
        subscription.OutstandingAmount.Should().Be(MonthlyTotal);
        subscription.PaymentStatus.Should().Be(PaymentStatus.Pending);
        subscription.Status.Should().Be(SubscriptionStatus.Pending);
    }

    [Fact(DisplayName = "S-03 The client cannot dictate the total: the server recalculates from the plan")]
    public async Task Create_WithATamperedTotal_StoresTheServerCalculatedAmount()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);
        var planId = await MonthlyPlanIdAsync(client);

        // A hand-built body carrying money fields the create DTO does not even expose. Anything the
        // client invents here must be ignored in favour of the plan's own pricing.
        var tampered = new Dictionary<string, object?>
        {
            ["memberId"] = member.Id,
            ["membershipPlanId"] = planId,
            ["startDate"] = DateTime.Today.ToString("yyyy-MM-dd"),
            ["discountAmount"] = 200m,
            ["chargeRegistrationFee"] = true,

            // Tampering attempts:
            ["finalAmount"] = 1m,
            ["planAmount"] = 1m,
            ["registrationFee"] = 0m,
            ["taxAmount"] = 0m,
            ["taxPercent"] = 0m,
            ["paidAmount"] = 99999m
        };

        var response = await client.PostAsync("/api/subscriptions",
            new StringContent(JsonSerializer.Serialize(tampered), System.Text.Encoding.UTF8, "application/json"));

        var created = await ReadDataAsync<SubscriptionDto>(response);

        var quote = await ReadDataAsync<SubscriptionQuoteDto>(
            await client.PostAsJsonAsync("/api/subscriptions/quote", new QuoteRequestDto
            {
                MembershipPlanId = planId,
                StartDate = DateTime.Today,
                DiscountAmount = 200m,
                ChargeRegistrationFee = true
            }));

        created.FinalAmount.Should().Be(quote.FinalAmount);
        created.FinalAmount.Should().Be(MonthlyTotal);
        created.PlanAmount.Should().Be(2500.00m);
        created.RegistrationFee.Should().Be(500.00m);
        created.TaxAmount.Should().Be(504.00m);
        created.PaidAmount.Should().Be(0m, "no payment was supplied, whatever the body claimed");

        // And the same thing is what actually landed in the database.
        var stored = await ReadDataAsync<SubscriptionDto>(
            await client.GetAsync($"/api/subscriptions/{created.Id}"));
        stored.FinalAmount.Should().Be(MonthlyTotal);
    }

    [Fact(DisplayName = "S-04 A discount above the plan's maximum is a 400")]
    public async Task Create_WithAnExcessDiscount_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);
        var planId = await MonthlyPlanIdAsync(client);

        var response = await client.PostAsJsonAsync("/api/subscriptions", new CreateSubscriptionDto
        {
            MemberId = member.Id,
            MembershipPlanId = planId,
            StartDate = DateTime.Today,
            DiscountAmount = 1000m,
            ChargeRegistrationFee = true
        });

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var error = await ReadErrorAsync(response);
        error.ValidationErrors.Should().ContainKey("DiscountAmount");
        error.ValidationErrors!["DiscountAmount"].Should().Contain(m => m.Contains("450.00"));
    }

    [Fact(DisplayName = "S-04 An excess discount is rejected on the quote as well")]
    public async Task Quote_WithAnExcessDiscount_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var planId = await MonthlyPlanIdAsync(client);

        var response = await client.PostAsJsonAsync("/api/subscriptions/quote", new QuoteRequestDto
        {
            MembershipPlanId = planId,
            StartDate = DateTime.Today,
            DiscountAmount = 1000m,
            ChargeRegistrationFee = true
        });

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact(DisplayName = "S-06 A start date more than 90 days ahead is a 400")]
    public async Task Create_WithAFarFutureStartDate_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);
        var planId = await MonthlyPlanIdAsync(client);

        var response = await client.PostAsJsonAsync("/api/subscriptions", new CreateSubscriptionDto
        {
            MemberId = member.Id,
            MembershipPlanId = planId,
            StartDate = DateTime.Today.AddDays(200),
            DiscountAmount = 0m
        });

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors.Should().ContainKey("StartDate");
    }

    [Fact(DisplayName = "S-05 An overlapping subscription is a 422 telling the operator to renew")]
    public async Task Create_WhenAnActiveTermOverlaps_Is422()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, _) = await SellSubscriptionAsync(client);
        var planId = await MonthlyPlanIdAsync(client);

        var response = await client.PostAsJsonAsync("/api/subscriptions", new CreateSubscriptionDto
        {
            MemberId = member.Id,
            MembershipPlanId = planId,
            StartDate = DateTime.Today.AddDays(5),
            DiscountAmount = 0m
        });

        await AssertFailureAsync(response, HttpStatusCode.UnprocessableEntity, "BUSINESS_RULE");
        (await ReadErrorAsync(response)).Message.Should().Contain("Renew");
    }

    // --------------------------------------------------------------- renew

    [Fact(DisplayName = "S-07 A renewal is flagged and starts the day after the previous term ended")]
    public async Task Renew_StartsAfterThePreviousTerm()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, original) = await SellSubscriptionAsync(client);

        var renewed = await ReadDataAsync<SubscriptionDto>(
            await client.PostAsJsonAsync("/api/subscriptions/renew", new RenewSubscriptionDto
            {
                MemberId = member.Id,
                PreviousSubscriptionId = original.Id,
                DiscountAmount = 0m
            }));

        renewed.Id.Should().NotBe(original.Id);
        renewed.IsRenewal.Should().BeTrue();
        renewed.PreviousSubscriptionId.Should().Be(original.Id);
        renewed.StartDate.Date.Should().Be(original.EndDate.Date.AddDays(1));
        renewed.StartDate.Should().BeAfter(original.EndDate);
    }

    [Fact(DisplayName = "S-07 A renewal does not re-charge the registration fee")]
    public async Task Renew_DoesNotChargeTheRegistrationFeeAgain()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, original) = await SellSubscriptionAsync(client);

        var renewed = await ReadDataAsync<SubscriptionDto>(
            await client.PostAsJsonAsync("/api/subscriptions/renew", new RenewSubscriptionDto
            {
                MemberId = member.Id,
                PreviousSubscriptionId = original.Id,
                DiscountAmount = 0m
            }));

        renewed.RegistrationFee.Should().Be(0m);
        // 2500 + 18% tax
        renewed.FinalAmount.Should().Be(2950.00m);
    }

    // ------------------------------------------------------ freeze / resume

    [Fact(DisplayName = "S-11/S-14 Freezing extends the term and resuming early gives back the unused days")]
    public async Task FreezeThenResume_AdjustsTheEndDate()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (_, subscription) = await SellSubscriptionAsync(client);
        var originalEnd = subscription.EndDate.Date;

        var frozen = await ReadDataAsync<SubscriptionDto>(
            await client.PostAsJsonAsync("/api/subscriptions/freeze", new FreezeSubscriptionDto
            {
                SubscriptionId = subscription.Id,
                FreezeStartDate = DateTime.Today,
                FreezeEndDate = DateTime.Today.AddDays(7),
                Reason = "Travelling for work"
            }));

        frozen.Status.Should().Be(SubscriptionStatus.Frozen);
        frozen.EndDate.Date.Should().Be(originalEnd.AddDays(7));

        var resumed = await ReadDataAsync<SubscriptionDto>(
            await client.PostAsJsonAsync("/api/subscriptions/resume", new ResumeSubscriptionDto
            {
                SubscriptionId = subscription.Id,
                ResumeDate = DateTime.Today.AddDays(3),
                Remarks = "Back early"
            }));

        resumed.Status.Should().Be(SubscriptionStatus.Active);
        resumed.EndDate.Date.Should().Be(originalEnd.AddDays(3));
        resumed.FrozenDaysUsed.Should().Be(3);
        resumed.FreezeStartDate.Should().BeNull();
        resumed.FreezeEndDate.Should().BeNull();
    }

    [Fact(DisplayName = "S-12 Freezing a plan that forbids it is a 422")]
    public async Task Freeze_OnAPlanThatForbidsIt_Is422()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);

        // The seeded Day Pass allows no freeze days at all.
        var planId = await PlanIdAsync(client, "Day Pass");
        var cashId = await PaymentMethodIdAsync(client, "CASH");

        var quote = await ReadDataAsync<SubscriptionQuoteDto>(
            await client.PostAsJsonAsync("/api/subscriptions/quote", new QuoteRequestDto
            {
                MembershipPlanId = planId,
                StartDate = DateTime.Today,
                DiscountAmount = 0m,
                ChargeRegistrationFee = true
            }));

        var subscription = await ReadDataAsync<SubscriptionDto>(
            await client.PostAsJsonAsync("/api/subscriptions", new CreateSubscriptionDto
            {
                MemberId = member.Id,
                MembershipPlanId = planId,
                StartDate = DateTime.Today,
                DiscountAmount = 0m,
                Payment = new CollectPaymentInlineDto
                {
                    PaymentMethodId = cashId,
                    Amount = quote.FinalAmount,
                    MarkConfirmed = true
                }
            }));

        var response = await client.PostAsJsonAsync("/api/subscriptions/freeze", new FreezeSubscriptionDto
        {
            SubscriptionId = subscription.Id,
            FreezeStartDate = DateTime.Today,
            FreezeEndDate = DateTime.Today.AddDays(2)
        });

        await AssertFailureAsync(response, HttpStatusCode.UnprocessableEntity, "BUSINESS_RULE");
        (await ReadErrorAsync(response)).Message.Should().Contain("does not allow freezing");
    }

    // -------------------------------------------------------------- cancel

    [Fact(DisplayName = "S-15 Cancelling with a reason stores the status and the reason")]
    public async Task Cancel_WithAReason_Succeeds()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (_, subscription) = await SellSubscriptionAsync(client);

        var cancelled = await ReadDataAsync<SubscriptionDto>(
            await client.PostAsJsonAsync("/api/subscriptions/cancel", new CancelSubscriptionDto
            {
                SubscriptionId = subscription.Id,
                Reason = "Member relocated to another city."
            }));

        cancelled.Status.Should().Be(SubscriptionStatus.Cancelled);
        cancelled.CancellationReason.Should().Be("Member relocated to another city.");
        cancelled.CancelledAt.Should().NotBeNull();
    }

    [Theory(DisplayName = "S-16 Cancelling without a reason is a 400 on Reason")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Cancel_WithoutAReason_Is400(string reason)
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (_, subscription) = await SellSubscriptionAsync(client);

        var response = await client.PostAsJsonAsync("/api/subscriptions/cancel", new CancelSubscriptionDto
        {
            SubscriptionId = subscription.Id,
            Reason = reason
        });

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(CancelSubscriptionDto.Reason));
    }

    // ------------------------------------------------------- process expiries

    [Fact(DisplayName = "S-18/S-19 Running process-expiries twice is idempotent",
        Skip = ExpiryJobMutatesTheSharedFixture)]
    public async Task ProcessExpiries_IsIdempotent()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var first = await ReadDataAsync<ExpiryProcessingResultDto>(
            await client.PostAsync("/api/subscriptions/process-expiries", null));

        var second = await ReadDataAsync<ExpiryProcessingResultDto>(
            await client.PostAsync("/api/subscriptions/process-expiries", null));

        second.ExpiredCount.Should().Be(0, "the first run already expired everything that was due");
        second.NotificationsCreated.Should().Be(0, "the deduplication key blocks a second alert the same day");
        first.ProcessedAtUtc.Should().BeBefore(second.ProcessedAtUtc.AddSeconds(1));
    }

    [Fact(DisplayName = "S-18 process-expiries is restricted to administrators")]
    public async Task ProcessExpiries_RequiresTheAdminRole()
    {
        using var client = Factory.CreateClient();

        var response = await client.PostAsync("/api/subscriptions/process-expiries", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------- reads

    [Fact(DisplayName = "A subscription can be read back by id and appears in the member's list")]
    public async Task GetById_AndForMember_ReturnTheSubscription()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (member, subscription) = await SellSubscriptionAsync(client);

        var byId = await ReadDataAsync<SubscriptionDto>(
            await client.GetAsync($"/api/subscriptions/{subscription.Id}"));
        byId.Id.Should().Be(subscription.Id);

        var forMember = await ReadDataAsync<List<SubscriptionDto>>(
            await client.GetAsync($"/api/subscriptions/member/{member.Id}"));
        forMember.Should().ContainSingle().Which.Id.Should().Be(subscription.Id);
    }

    [Fact(DisplayName = "S-02 A subscription records its own history")]
    public async Task GetHistory_ReturnsTheCreationEntry()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var (_, subscription) = await SellSubscriptionAsync(client);

        var history = await ReadDataAsync<List<SubscriptionHistoryDto>>(
            await client.GetAsync($"/api/subscriptions/{subscription.Id}/history"));

        history.Should().NotBeEmpty();
        history.Should().Contain(h => h.ActionType == SubscriptionActionType.Created);
        history.Should().Contain(h => h.ActionType == SubscriptionActionType.PaymentReceived);
    }

    [Fact(DisplayName = "Reading an unknown subscription is a 404")]
    public async Task GetById_ForAnUnknownSubscription_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/subscriptions/999999");

        await AssertFailureAsync(response, HttpStatusCode.NotFound, "NOT_FOUND");
    }
}
