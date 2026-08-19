using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Domain.Enums;
using Xunit;

namespace GymManagement.IntegrationTests;

/// <summary>
/// Membership plan catalogue over HTTP — cases P-01 to P-12.
///
/// Plans are the pricing source subscriptions are calculated from, so the cases here cover the
/// catalogue itself: the generated plan code, the uniqueness of the name, the validation floor on
/// every money and duration field, and the rule that a plan still carrying live subscriptions may
/// not be deleted. Cases create their own plans rather than touching the six seeded ones, which
/// the subscription and payment suites depend on.
/// </summary>
[Collection(GymApiCollection.Name)]
public class MembershipPlanEndpointTests : ApiTestBase
{
    public MembershipPlanEndpointTests(GymApiFixture fixture) : base(fixture) { }

    // ------------------------------------------------------------- helpers

    /// <summary>A plan name no other case will use, so the uniqueness rule stays deterministic.</summary>
    private static string UniquePlanName() => $"Test Plan {Guid.NewGuid():N}"[..24];

    private static MembershipPlanDto NewPlanDto(string? name = null) => new()
    {
        Name = name ?? UniquePlanName(),
        Description = "Created by the integration suite.",
        DurationType = PlanDurationType.Month,
        DurationValue = 1,
        Price = 1800m,
        RegistrationFee = 250m,
        TaxPercent = 18m,
        MaxDiscountPercent = 10m,
        GracePeriodDays = 2,
        MaxFreezeDays = 5,
        DisplayOrder = 99,
        Status = PlanStatus.Active
    };

    private static async Task<MembershipPlanDto> CreatePlanAsync(
        HttpClient client, MembershipPlanDto? dto = null) =>
        await ReadDataAsync<MembershipPlanDto>(
            await client.PostAsJsonAsync("/api/membership-plans", dto ?? NewPlanDto()));

    // -------------------------------------------------------------- create

    [Fact(DisplayName = "P-01 Creating a plan returns a generated plan code and echoes the pricing")]
    public async Task Create_ReturnsAGeneratedPlanCode()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewPlanDto();

        var created = await CreatePlanAsync(client, dto);

        created.Id.Should().BeGreaterThan(0);
        created.PlanCode.Should().NotBeNullOrWhiteSpace();
        created.PlanCode.Should().MatchRegex(@"^PLN-\d{4}-\d{4}$");
        created.Name.Should().Be(dto.Name);
        created.Price.Should().Be(1800.00m);
        created.RegistrationFee.Should().Be(250.00m);
        created.TaxPercent.Should().Be(18.00m);
        created.MaxDiscountPercent.Should().Be(10.00m);
        created.Status.Should().Be(PlanStatus.Active);
        created.ActiveSubscriptionCount.Should().Be(0);
    }

    [Fact(DisplayName = "P-01 Two plans never receive the same code")]
    public async Task Create_ProducesUniqueCodes()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var first = await CreatePlanAsync(client);
        var second = await CreatePlanAsync(client);

        second.PlanCode.Should().NotBe(first.PlanCode);
    }

    [Fact(DisplayName = "P-02 A second plan with the same name is a 409 conflict")]
    public async Task Create_WithADuplicateName_Is409()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var name = UniquePlanName();

        await CreatePlanAsync(client, NewPlanDto(name));

        var response = await client.PostAsJsonAsync("/api/membership-plans", NewPlanDto(name));

        await AssertFailureAsync(response, HttpStatusCode.Conflict, "CONFLICT");
        (await ReadErrorAsync(response)).Message.Should().Contain(name);
    }

    [Fact(DisplayName = "P-03 A blank plan name is a 400 on Name")]
    public async Task Create_WithoutAName_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewPlanDto();
        dto.Name = "   ";

        var response = await client.PostAsJsonAsync("/api/membership-plans", dto);

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(MembershipPlanDto.Name));
    }

    [Fact(DisplayName = "P-04 A negative price is a 400 on Price")]
    public async Task Create_WithANegativePrice_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewPlanDto();
        dto.Price = -1m;

        var response = await client.PostAsJsonAsync("/api/membership-plans", dto);

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(MembershipPlanDto.Price));
    }

    [Fact(DisplayName = "P-04 A duration below one is a 400 on DurationValue")]
    public async Task Create_WithAZeroDuration_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewPlanDto();
        dto.DurationValue = 0;

        var response = await client.PostAsJsonAsync("/api/membership-plans", dto);

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(MembershipPlanDto.DurationValue));
    }

    [Theory(DisplayName = "P-04 A tax percent outside 0-100 is a 400 on TaxPercent")]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Create_WithAnOutOfRangeTaxPercent_Is400(int taxPercent)
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewPlanDto();
        dto.TaxPercent = taxPercent;

        var response = await client.PostAsJsonAsync("/api/membership-plans", dto);

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(MembershipPlanDto.TaxPercent));
    }

    [Fact(DisplayName = "P-04 A maximum discount above 100 percent is a 400")]
    public async Task Create_WithAnOutOfRangeDiscountCap_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewPlanDto();
        dto.MaxDiscountPercent = 150m;

        var response = await client.PostAsJsonAsync("/api/membership-plans", dto);

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(MembershipPlanDto.MaxDiscountPercent));
    }

    [Fact(DisplayName = "P-04 A negative grace period and freeze allowance are both rejected")]
    public async Task Create_WithNegativeDayCounts_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewPlanDto();
        dto.GracePeriodDays = -1;
        dto.MaxFreezeDays = -1;

        var response = await client.PostAsJsonAsync("/api/membership-plans", dto);

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var errors = (await ReadErrorAsync(response)).ValidationErrors;
        errors.Should().ContainKey(nameof(MembershipPlanDto.GracePeriodDays));
        errors.Should().ContainKey(nameof(MembershipPlanDto.MaxFreezeDays));
    }

    // -------------------------------------------------------------- update

    [Fact(DisplayName = "P-05 Updating a plan persists the new pricing")]
    public async Task Update_PersistsTheChange()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var created = await CreatePlanAsync(client);

        var update = NewPlanDto(created.Name);
        update.Id = created.Id;
        update.Price = 2100m;
        update.MaxDiscountPercent = 12m;
        update.Status = PlanStatus.Inactive;

        var updated = await ReadDataAsync<MembershipPlanDto>(
            await client.PutAsJsonAsync($"/api/membership-plans/{created.Id}", update));

        updated.Id.Should().Be(created.Id);
        updated.PlanCode.Should().Be(created.PlanCode, "the code is assigned once and never changes");
        updated.Price.Should().Be(2100.00m);
        updated.MaxDiscountPercent.Should().Be(12.00m);
        updated.Status.Should().Be(PlanStatus.Inactive);

        var reloaded = await ReadDataAsync<MembershipPlanDto>(
            await client.GetAsync($"/api/membership-plans/{created.Id}"));
        reloaded.Price.Should().Be(2100.00m);
    }

    [Fact(DisplayName = "P-05 The route id wins over an id in the body")]
    public async Task Update_UsesTheRouteId()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var created = await CreatePlanAsync(client);

        var update = NewPlanDto(created.Name);
        update.Id = 999999; // the controller overwrites this with the route id
        update.Price = 2400m;

        var updated = await ReadDataAsync<MembershipPlanDto>(
            await client.PutAsJsonAsync($"/api/membership-plans/{created.Id}", update));

        updated.Id.Should().Be(created.Id);
        updated.Price.Should().Be(2400.00m);
    }

    [Fact(DisplayName = "P-06 Renaming a plan onto another plan's name is a 409")]
    public async Task Update_ToADuplicateName_Is409()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var first = await CreatePlanAsync(client);
        var second = await CreatePlanAsync(client);

        var update = NewPlanDto(first.Name);
        update.Id = second.Id;

        var response = await client.PutAsJsonAsync($"/api/membership-plans/{second.Id}", update);

        await AssertFailureAsync(response, HttpStatusCode.Conflict, "CONFLICT");
    }

    [Fact(DisplayName = "P-06 A plan may keep its own name on update")]
    public async Task Update_KeepingItsOwnName_Succeeds()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var created = await CreatePlanAsync(client);

        var update = NewPlanDto(created.Name);
        update.Id = created.Id;
        update.Price = 1950m;

        var updated = await ReadDataAsync<MembershipPlanDto>(
            await client.PutAsJsonAsync($"/api/membership-plans/{created.Id}", update));

        updated.Name.Should().Be(created.Name);
        updated.Price.Should().Be(1950.00m);
    }

    [Fact(DisplayName = "P-07 Updating an unknown plan is a 404")]
    public async Task Update_AnUnknownPlan_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync("/api/membership-plans/999999", NewPlanDto());

        await AssertFailureAsync(response, HttpStatusCode.NotFound, "NOT_FOUND");
    }

    // --------------------------------------------------------------- reads

    [Fact(DisplayName = "P-08 The six seeded plans are all listed with their verified pricing")]
    public async Task GetPaged_ListsTheSeededCatalogue()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var page = await ReadDataAsync<PagedResult<MembershipPlanDto>>(
            await client.GetAsync("/api/membership-plans?PageSize=200"));

        page.Items.Should().Contain(p => p.Name == "Day Pass");
        page.Items.Should().Contain(p => p.Name == "Weekly");
        page.Items.Should().Contain(p => p.Name == "Quarterly");
        page.Items.Should().Contain(p => p.Name == "Half Yearly");
        page.Items.Should().Contain(p => p.Name == "Annual");

        var monthly = page.Items.Single(p => p.Name == "Monthly");
        monthly.Price.Should().Be(2500.00m);
        monthly.RegistrationFee.Should().Be(500.00m);
        monthly.TaxPercent.Should().Be(18.00m);
        monthly.MaxDiscountPercent.Should().Be(15.00m);
        monthly.MaxFreezeDays.Should().Be(7);
        monthly.GracePeriodDays.Should().Be(3);
    }

    [Fact(DisplayName = "P-08 The status filter only returns plans in that status")]
    public async Task GetPaged_FilteredByStatus_OnlyReturnsThatStatus()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var dto = NewPlanDto();
        dto.Status = PlanStatus.Inactive;
        var inactive = await CreatePlanAsync(client, dto);

        var page = await ReadDataAsync<PagedResult<MembershipPlanDto>>(
            await client.GetAsync($"/api/membership-plans?Status={(int)PlanStatus.Inactive}&PageSize=200"));

        page.Items.Should().Contain(p => p.Id == inactive.Id);
        page.Items.Should().OnlyContain(p => p.Status == PlanStatus.Inactive);
    }

    [Fact(DisplayName = "P-09 GET active omits an inactive plan but keeps the sellable ones")]
    public async Task GetActive_OmitsInactivePlans()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var dto = NewPlanDto();
        dto.Status = PlanStatus.Inactive;
        var inactive = await CreatePlanAsync(client, dto);

        var active = await ReadDataAsync<List<MembershipPlanDto>>(
            await client.GetAsync("/api/membership-plans/active"));

        active.Should().NotContain(p => p.Id == inactive.Id);
        active.Should().Contain(p => p.Name == "Monthly");
        active.Should().OnlyContain(p => p.Status == PlanStatus.Active);
    }

    [Fact(DisplayName = "P-09 The lookup returns id/name pairs for the combo boxes")]
    public async Task GetLookup_ReturnsThePlans()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var lookup = await ReadDataAsync<List<LookupDto>>(
            await client.GetAsync("/api/membership-plans/lookup"));

        lookup.Should().NotBeEmpty();
        lookup.Should().Contain(l => l.Name == "Monthly");
        lookup.Should().OnlyContain(l => l.Id > 0);
    }

    [Fact(DisplayName = "P-10 Reading an unknown plan is a 404")]
    public async Task GetById_ForAnUnknownPlan_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        await AssertFailureAsync(
            await client.GetAsync("/api/membership-plans/999999"), HttpStatusCode.NotFound, "NOT_FOUND");
    }

    // ------------------------------------------------- delete / restore

    [Fact(DisplayName = "P-11 An unused plan is soft deleted and disappears from the catalogue")]
    public async Task Delete_AnUnusedPlan_Succeeds()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var created = await CreatePlanAsync(client);

        (await client.DeleteAsync($"/api/membership-plans/{created.Id}")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var page = await ReadDataAsync<PagedResult<MembershipPlanDto>>(
            await client.GetAsync("/api/membership-plans?PageSize=200"));
        page.Items.Should().NotContain(p => p.Id == created.Id);
    }

    [Fact(DisplayName = "P-11 A deleted plan can be restored")]
    public async Task Restore_BringsThePlanBack()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var created = await CreatePlanAsync(client);

        await client.DeleteAsync($"/api/membership-plans/{created.Id}");

        (await client.PostAsync($"/api/membership-plans/{created.Id}/restore", null)).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var page = await ReadDataAsync<PagedResult<MembershipPlanDto>>(
            await client.GetAsync("/api/membership-plans?PageSize=200"));
        page.Items.Should().Contain(p => p.Id == created.Id);
    }

    [Fact(DisplayName = "P-12 A plan carrying an active subscription cannot be deleted")]
    public async Task Delete_APlanInUse_Is422()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var plan = await CreatePlanAsync(client);
        var member = await CreateMemberAsync(client);
        var cashId = await PaymentMethodIdAsync(client, "CASH");

        var quote = await ReadDataAsync<SubscriptionQuoteDto>(
            await client.PostAsJsonAsync("/api/subscriptions/quote", new QuoteRequestDto
            {
                MembershipPlanId = plan.Id,
                StartDate = DateTime.Today,
                DiscountAmount = 0m,
                ChargeRegistrationFee = true
            }));

        await ReadDataAsync<SubscriptionDto>(
            await client.PostAsJsonAsync("/api/subscriptions", new CreateSubscriptionDto
            {
                MemberId = member.Id,
                MembershipPlanId = plan.Id,
                StartDate = DateTime.Today,
                DiscountAmount = 0m,
                ChargeRegistrationFee = true,
                Payment = new CollectPaymentInlineDto
                {
                    PaymentMethodId = cashId,
                    Amount = quote.FinalAmount,
                    MarkConfirmed = true
                }
            }));

        var response = await client.DeleteAsync($"/api/membership-plans/{plan.Id}");

        await AssertFailureAsync(response, HttpStatusCode.UnprocessableEntity, "BUSINESS_RULE");
        (await ReadErrorAsync(response)).Message.Should().Contain("Inactive");
    }

    [Fact(DisplayName = "P-12 Deleting an unknown plan is a 404")]
    public async Task Delete_AnUnknownPlan_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        await AssertFailureAsync(
            await client.DeleteAsync("/api/membership-plans/999999"), HttpStatusCode.NotFound, "NOT_FOUND");
    }
}
