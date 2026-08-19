using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Domain.Enums;

namespace GymManagement.IntegrationTests;

/// <summary>
/// Shared plumbing for the endpoint tests: envelope helpers, unique test data and the small
/// arrangement helpers (create a member, sell a subscription) most cases need.
/// </summary>
public abstract class ApiTestBase
{
    protected GymApiFactory Factory { get; }

    protected ApiTestBase(GymApiFixture fixture) => Factory = fixture.Factory;

    // ------------------------------------------------------- unique inputs

    /// <summary>A phone number no other test will use, so the duplicate rule stays deterministic.</summary>
    protected static string UniquePhone() =>
        "9" + Math.Abs(Guid.NewGuid().GetHashCode()).ToString("0000000000")[..9];

    protected static string UniqueEmail() => $"member-{Guid.NewGuid():N}@example.test";

    protected static string UniqueReference() => $"UTR-{Guid.NewGuid():N}"[..24].ToUpperInvariant();

    /// <summary>
    /// A unique person name made of letters only: the PersonName rule rejects digits, so the
    /// usual hex suffix cannot be used here.
    /// </summary>
    protected static string UniqueName(string prefix = "Member")
    {
        var suffix = new string(Guid.NewGuid().ToString("N")[..10]
            .Select(c => char.IsDigit(c) ? (char)('a' + (c - '0')) : c)
            .ToArray());

        return $"{prefix} {char.ToUpperInvariant(suffix[0])}{suffix[1..]}";
    }

    // ------------------------------------------------------ envelope reads

    /// <summary>Reads the envelope and fails loudly if the call was not a success.</summary>
    protected static async Task<T> ReadDataAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"the call should succeed. Body: {body}");

        var envelope = await ReadEnvelopeAsync<T>(response, body);
        envelope.Success.Should().BeTrue($"the envelope should report success. Body: {body}");
        envelope.Data.Should().NotBeNull($"the envelope should carry data. Body: {body}");

        return envelope.Data!;
    }

    protected static async Task<ApiResponse<T>> ReadEnvelopeAsync<T>(
        HttpResponseMessage response, string? body = null)
    {
        body ??= await response.Content.ReadAsStringAsync();

        var envelope = System.Text.Json.JsonSerializer.Deserialize<ApiResponse<T>>(body, GymApiFactory.Json);
        envelope.Should().NotBeNull($"the response should be an ApiResponse envelope. Body: {body}");
        return envelope!;
    }

    protected static async Task<ApiResponse> ReadErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        var envelope = System.Text.Json.JsonSerializer.Deserialize<ApiResponse>(body, GymApiFactory.Json);
        envelope.Should().NotBeNull($"the failure should use the ApiResponse envelope. Body: {body}");
        envelope!.Success.Should().BeFalse($"a failure envelope must not report success. Body: {body}");
        return envelope;
    }

    /// <summary>Asserts the status code and the error code the middleware maps the exception to.</summary>
    protected static async Task AssertFailureAsync(
        HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedErrorCode)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expectedStatus, $"Body: {body}");

        var envelope = await ReadErrorAsync(response);
        envelope.ErrorCode.Should().Be(expectedErrorCode);
    }

    // ------------------------------------------------------- arrangements

    protected static CreateMemberDto NewMemberDto(string? fullName = null, string? phone = null) => new()
    {
        FullName = fullName ?? UniqueName(),
        Gender = Gender.Male,
        Phone = phone ?? UniquePhone(),
        Email = UniqueEmail(),
        DateOfBirth = new DateTime(1994, 6, 15),
        JoiningDate = DateTime.Today
    };

    /// <summary>Registers a member and returns the created detail row.</summary>
    protected static async Task<MemberDetailDto> CreateMemberAsync(
        HttpClient client, CreateMemberDto? dto = null)
    {
        var response = await client.PostAsJsonAsync("/api/members", dto ?? NewMemberDto());
        var result = await ReadDataAsync<CreateMemberResponse>(response);
        return result.Member;
    }

    /// <summary>The Monthly plan seeded by <c>DbSeeder</c>: 2500 + 500 registration at 18% tax.</summary>
    protected async Task<int> MonthlyPlanIdAsync(HttpClient client) => await PlanIdAsync(client, "Monthly");

    protected async Task<int> PlanIdAsync(HttpClient client, string planName)
    {
        var page = await ReadDataAsync<PagedResult<MembershipPlanDto>>(
            await client.GetAsync("/api/membership-plans?PageSize=200"));

        var plan = page.Items.FirstOrDefault(p => p.Name == planName);
        plan.Should().NotBeNull($"the seeded '{planName}' plan should exist");
        return plan!.Id;
    }

    protected async Task<int> PaymentMethodIdAsync(HttpClient client, string code)
    {
        var methods = await ReadDataAsync<List<PaymentMethodDto>>(
            await client.GetAsync("/api/Payments/methods"));

        var method = methods.FirstOrDefault(m => m.Code == code);
        method.Should().NotBeNull($"the seeded '{code}' payment method should exist");
        return method!.Id;
    }

    /// <summary>Sells a fully paid Monthly subscription to a brand new member.</summary>
    protected async Task<(MemberDetailDto Member, SubscriptionDto Subscription)> SellSubscriptionAsync(
        HttpClient client, decimal discount = 200m, bool payInFull = true)
    {
        var member = await CreateMemberAsync(client);
        var planId = await MonthlyPlanIdAsync(client);
        var cashId = await PaymentMethodIdAsync(client, "CASH");

        var quote = await ReadDataAsync<SubscriptionQuoteDto>(
            await client.PostAsJsonAsync("/api/subscriptions/quote", new QuoteRequestDto
            {
                MembershipPlanId = planId,
                StartDate = DateTime.Today,
                DiscountAmount = discount,
                ChargeRegistrationFee = true
            }));

        var create = new CreateSubscriptionDto
        {
            MemberId = member.Id,
            MembershipPlanId = planId,
            StartDate = DateTime.Today,
            DiscountAmount = discount,
            ChargeRegistrationFee = true,
            Payment = payInFull
                ? new CollectPaymentInlineDto
                {
                    PaymentMethodId = cashId,
                    Amount = quote.FinalAmount,
                    MarkConfirmed = true
                }
                : null
        };

        var subscription = await ReadDataAsync<SubscriptionDto>(
            await client.PostAsJsonAsync("/api/subscriptions", create));

        return (member, subscription);
    }

    // -------------------------------------------------------- gym settings

    /// <summary>
    /// Temporarily replaces the gym settings, restoring them however the test ends. The suite runs
    /// in a single xunit collection, so a case may safely change a global setting for its duration
    /// as long as it puts the original back.
    /// </summary>
    protected static async Task WithGymSettingsAsync(
        HttpClient client, Action<GymSettingsDto> mutate, Func<Task> body)
    {
        var original = await ReadDataAsync<GymSettingsDto>(await client.GetAsync("/api/Settings/gym"));

        var changed = CloneSettings(original);
        mutate(changed);

        (await client.PutAsJsonAsync("/api/Settings/gym", changed))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        try
        {
            await body();
        }
        finally
        {
            await client.PutAsJsonAsync("/api/Settings/gym", CloneSettings(original));
        }
    }

    protected static GymSettingsDto CloneSettings(GymSettingsDto source) => new()
    {
        Id = source.Id,
        GymName = source.GymName,
        LegalName = source.LegalName,
        Address = source.Address,
        City = source.City,
        State = source.State,
        PostalCode = source.PostalCode,
        Country = source.Country,
        Phone = source.Phone,
        Email = source.Email,
        Website = source.Website,
        LogoPath = source.LogoPath,
        TaxNumber = source.TaxNumber,
        CurrencyCode = source.CurrencyCode,
        CurrencySymbol = source.CurrencySymbol,
        UpiId = source.UpiId,
        UpiPayeeName = source.UpiPayeeName,
        UpiQrImagePath = source.UpiQrImagePath,
        OpeningTime = source.OpeningTime,
        ClosingTime = source.ClosingTime,
        ReceiptPrefix = source.ReceiptPrefix,
        MemberCodePrefix = source.MemberCodePrefix,
        ReceiptFooterText = source.ReceiptFooterText,
        ExpiryReminderDays = source.ExpiryReminderDays,
        DefaultGracePeriodDays = source.DefaultGracePeriodDays,
        MaxFailedLoginAttempts = source.MaxFailedLoginAttempts,
        LockoutMinutes = source.LockoutMinutes,
        AllowExpiredMemberCheckIn = source.AllowExpiredMemberCheckIn
    };

    /// <summary>Mirrors the anonymous result record the members controller returns.</summary>
    protected sealed class CreateMemberResponse
    {
        public MemberDetailDto Member { get; set; } = new();
        public TemporaryPasswordDto? Account { get; set; }
    }
}
