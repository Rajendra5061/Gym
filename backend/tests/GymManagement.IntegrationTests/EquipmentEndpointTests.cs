using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Domain.Enums;
using Xunit;

namespace GymManagement.IntegrationTests;

/// <summary>
/// Equipment inventory over HTTP — cases E-01 to E-10.
///
/// Covers the asset tag the service generates from the category, the uniqueness rule on that tag,
/// the validation floor on quantity / cost / service dates, the service-due filter and the
/// soft delete and restore pair.
/// </summary>
[Collection(GymApiCollection.Name)]
public class EquipmentEndpointTests : ApiTestBase
{
    public EquipmentEndpointTests(GymApiFixture fixture) : base(fixture) { }

    // ------------------------------------------------------------- helpers

    /// <summary>
    /// A category no other case uses. The asset tag is built from the first three letters of the
    /// category, and the sequence within that three letter prefix is shared across the whole
    /// inventory, so the prefix itself has to be random rather than a fixed word.
    /// </summary>
    private static string UniqueCategory() =>
        new(Guid.NewGuid().ToString("N")[..8]
            .Select(c => char.IsDigit(c) ? (char)('k' + (c - '0')) : c)
            .ToArray());

    /// <summary>The numeric sequence at the end of an <c>EQP-XXX-000</c> asset tag.</summary>
    private static int SequenceOf(string code) => int.Parse(code[^3..]);

    private static SaveEquipmentDto NewEquipmentDto(string? category = null, string? code = null) => new()
    {
        Name = $"Rowing Machine {Guid.NewGuid():N}"[..28],
        Code = code,
        Category = category ?? UniqueCategory(),
        Manufacturer = "Generic",
        SerialNumber = $"SN-{Guid.NewGuid():N}"[..16],
        Quantity = 2,
        Condition = EquipmentCondition.Good,
        Location = "Cardio Zone",
        PurchaseDate = DateTime.Today.AddMonths(-4),
        PurchaseCost = 74999.456m,
        IsActive = true
    };

    private static async Task<EquipmentDetailDto> CreateEquipmentAsync(
        HttpClient client, SaveEquipmentDto? dto = null) =>
        await ReadDataAsync<EquipmentDetailDto>(
            await client.PostAsJsonAsync("/api/equipment", dto ?? NewEquipmentDto()));

    // -------------------------------------------------------------- create

    [Fact(DisplayName = "E-01 Creating equipment generates an asset tag from the category")]
    public async Task Create_GeneratesAnAssetTagFromTheCategory()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewEquipmentDto();

        var created = await CreateEquipmentAsync(client, dto);

        created.Id.Should().BeGreaterThan(0);
        created.Name.Should().Be(dto.Name);
        created.Category.Should().Be(dto.Category);
        created.Code.Should().StartWith($"EQP-{dto.Category[..3].ToUpperInvariant()}-",
            "the asset tag is built from the first three letters of the category");
        created.Code.Should().MatchRegex(@"^EQP-[A-Z]{3}-\d{3}$");
        created.Quantity.Should().Be(2);
        created.Condition.Should().Be(EquipmentCondition.Good);
        created.IsActive.Should().BeTrue();
        created.IsDeleted.Should().BeFalse();
    }

    [Fact(DisplayName = "E-01 Purchase cost is money, so it is rounded to two places on save")]
    public async Task Create_RoundsThePurchaseCost()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var created = await CreateEquipmentAsync(client);

        created.PurchaseCost.Should().Be(74999.46m);
    }

    [Fact(DisplayName = "E-02 The asset tag sequence advances within a category")]
    public async Task Create_AdvancesTheSequenceWithinACategory()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var category = UniqueCategory();

        var first = await CreateEquipmentAsync(client, NewEquipmentDto(category));
        var second = await CreateEquipmentAsync(client, NewEquipmentDto(category));

        var prefix = $"EQP-{category[..3].ToUpperInvariant()}-";
        first.Code.Should().StartWith(prefix);
        second.Code.Should().StartWith(prefix);
        SequenceOf(second.Code).Should().Be(SequenceOf(first.Code) + 1);
    }

    [Fact(DisplayName = "E-03 Reusing an asset tag is a 409 conflict")]
    public async Task Create_WithADuplicateCode_Is409()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var existing = await CreateEquipmentAsync(client);

        var response = await client.PostAsJsonAsync("/api/equipment",
            NewEquipmentDto(code: existing.Code));

        await AssertFailureAsync(response, HttpStatusCode.Conflict, "CONFLICT");
        (await ReadErrorAsync(response)).Message.Should().Contain(existing.Code);
    }

    [Fact(DisplayName = "E-04 A blank name is a 400 on Name")]
    public async Task Create_WithoutAName_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewEquipmentDto();
        dto.Name = "  ";

        var response = await client.PostAsJsonAsync("/api/equipment", dto);

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(SaveEquipmentDto.Name));
    }

    [Fact(DisplayName = "E-04 A blank category is a 400 on Category")]
    public async Task Create_WithoutACategory_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewEquipmentDto();
        dto.Category = "  ";

        var response = await client.PostAsJsonAsync("/api/equipment", dto);

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(SaveEquipmentDto.Category));
    }

    [Fact(DisplayName = "E-04 A quantity below one is a 400 on Quantity")]
    public async Task Create_WithAZeroQuantity_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewEquipmentDto();
        dto.Quantity = 0;

        var response = await client.PostAsJsonAsync("/api/equipment", dto);

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(SaveEquipmentDto.Quantity));
    }

    [Fact(DisplayName = "E-04 A negative purchase cost is a 400 on PurchaseCost")]
    public async Task Create_WithANegativeCost_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewEquipmentDto();
        dto.PurchaseCost = -5m;

        var response = await client.PostAsJsonAsync("/api/equipment", dto);

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(SaveEquipmentDto.PurchaseCost));
    }

    [Fact(DisplayName = "E-04 A purchase date in the future is a 400 on PurchaseDate")]
    public async Task Create_WithAFuturePurchaseDate_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewEquipmentDto();
        dto.PurchaseDate = DateTime.Today.AddDays(1);

        var response = await client.PostAsJsonAsync("/api/equipment", dto);

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(SaveEquipmentDto.PurchaseDate));
    }

    [Fact(DisplayName = "E-04 A next service date before the last service date is a 400")]
    public async Task Create_WithServiceDatesOutOfOrder_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewEquipmentDto();
        dto.LastServicedOn = DateTime.Today.AddDays(-10);
        dto.NextServiceDue = DateTime.Today.AddDays(-20);

        var response = await client.PostAsJsonAsync("/api/equipment", dto);

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(SaveEquipmentDto.NextServiceDue));
    }

    // -------------------------------------------------------------- update

    [Fact(DisplayName = "E-05 Updating equipment persists the change and keeps the asset tag")]
    public async Task Update_PersistsTheChange()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var created = await CreateEquipmentAsync(client);

        var update = NewEquipmentDto(created.Category, created.Code);
        update.Id = created.Id;
        update.Name = "Reconditioned Rower";
        update.Condition = EquipmentCondition.NeedsService;
        update.Quantity = 5;
        update.Location = "Workshop";

        var updated = await ReadDataAsync<EquipmentDetailDto>(
            await client.PutAsJsonAsync($"/api/equipment/{created.Id}", update));

        updated.Id.Should().Be(created.Id);
        updated.Code.Should().Be(created.Code);
        updated.Name.Should().Be("Reconditioned Rower");
        updated.Condition.Should().Be(EquipmentCondition.NeedsService);
        updated.Quantity.Should().Be(5);
        updated.Location.Should().Be("Workshop");
        updated.UpdatedAt.Should().NotBeNull();
    }

    [Fact(DisplayName = "E-05 Updating onto another asset's tag is a 409")]
    public async Task Update_ToADuplicateCode_Is409()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var first = await CreateEquipmentAsync(client);
        var second = await CreateEquipmentAsync(client);

        var update = NewEquipmentDto(second.Category, first.Code);
        update.Id = second.Id;

        var response = await client.PutAsJsonAsync($"/api/equipment/{second.Id}", update);

        await AssertFailureAsync(response, HttpStatusCode.Conflict, "CONFLICT");
    }

    [Fact(DisplayName = "E-06 Updating an unknown asset is a 404")]
    public async Task Update_AnUnknownAsset_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var update = NewEquipmentDto(code: $"EQP-XXX-{Random.Shared.Next(100, 999)}");
        update.Id = 999999;

        var response = await client.PutAsJsonAsync("/api/equipment/999999", update);

        await AssertFailureAsync(response, HttpStatusCode.NotFound, "NOT_FOUND");
    }

    // --------------------------------------------------------------- reads

    [Fact(DisplayName = "E-07 The seeded inventory is listed")]
    public async Task GetPaged_ListsTheSeededInventory()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var page = await ReadDataAsync<PagedResult<EquipmentListDto>>(
            await client.GetAsync("/api/equipment?PageSize=200"));

        page.TotalCount.Should().BeGreaterThan(0);
        page.Items.Should().Contain(e => e.Code == "EQP-CRD-001" && e.Name == "Treadmill");
        page.Items.Should().OnlyContain(e => !e.IsDeleted);
    }

    [Fact(DisplayName = "E-07 The category filter narrows the inventory")]
    public async Task GetPaged_FilteredByCategory_NarrowsTheList()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var created = await CreateEquipmentAsync(client);

        var page = await ReadDataAsync<PagedResult<EquipmentListDto>>(
            await client.GetAsync($"/api/equipment?Category={created.Category}&PageSize=200"));

        page.Items.Should().ContainSingle().Which.Id.Should().Be(created.Id);
    }

    [Fact(DisplayName = "E-07 The condition filter only returns assets in that condition")]
    public async Task GetPaged_FilteredByCondition_OnlyReturnsThatCondition()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var dto = NewEquipmentDto();
        dto.Condition = EquipmentCondition.UnderRepair;
        var repairing = await CreateEquipmentAsync(client, dto);

        var page = await ReadDataAsync<PagedResult<EquipmentListDto>>(
            await client.GetAsync(
                $"/api/equipment?Condition={(int)EquipmentCondition.UnderRepair}&PageSize=200"));

        page.Items.Should().Contain(e => e.Id == repairing.Id);
        page.Items.Should().OnlyContain(e => e.Condition == EquipmentCondition.UnderRepair);
    }

    [Fact(DisplayName = "E-08 The service-due filter returns only assets whose service date has arrived")]
    public async Task GetPaged_ServiceDue_ReturnsOnlyOverdueAssets()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var dto = NewEquipmentDto();
        dto.LastServicedOn = DateTime.Today.AddMonths(-6);
        dto.NextServiceDue = DateTime.Today.AddDays(-2);
        var overdue = await CreateEquipmentAsync(client, dto);

        var notDue = NewEquipmentDto();
        notDue.NextServiceDue = DateTime.Today.AddMonths(3);
        var future = await CreateEquipmentAsync(client, notDue);

        var page = await ReadDataAsync<PagedResult<EquipmentListDto>>(
            await client.GetAsync("/api/equipment?ServiceDue=true&PageSize=200"));

        page.Items.Should().Contain(e => e.Id == overdue.Id);
        page.Items.Should().NotContain(e => e.Id == future.Id);
        page.Items.Should().OnlyContain(e => e.NextServiceDue != null &&
                                             e.NextServiceDue.Value.Date <= DateTime.Today);
    }

    [Fact(DisplayName = "E-08 Searching by serial number finds the asset")]
    public async Task GetPaged_SearchedBySerialNumber_FindsTheAsset()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var dto = NewEquipmentDto();
        var created = await CreateEquipmentAsync(client, dto);

        var page = await ReadDataAsync<PagedResult<EquipmentListDto>>(
            await client.GetAsync($"/api/equipment?Search={dto.SerialNumber}"));

        page.Items.Should().ContainSingle().Which.Id.Should().Be(created.Id);
    }

    [Fact(DisplayName = "E-09 The categories endpoint lists the distinct categories in use")]
    public async Task GetCategories_ListsTheDistinctCategories()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var created = await CreateEquipmentAsync(client);

        var categories = await ReadDataAsync<List<string>>(
            await client.GetAsync("/api/equipment/categories"));

        categories.Should().Contain("Cardio");
        categories.Should().Contain(created.Category);
        categories.Should().OnlyHaveUniqueItems();

        // The service orders in the database (`OrderBy(c => c)`), so the ordering the caller sees is
        // the one the store applies: SQL Server's default collation is case insensitive, and the
        // in-memory provider used by this suite orders the same way. The seeded categories are
        // capitalised while the ones these tests generate are not, so the comparison has to be made
        // case insensitively — an ordinal assertion would be testing the wrong contract.
        categories.Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "E-09 Reading an unknown asset is a 404")]
    public async Task GetById_ForAnUnknownAsset_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        await AssertFailureAsync(
            await client.GetAsync("/api/equipment/999999"), HttpStatusCode.NotFound, "NOT_FOUND");
    }

    // ------------------------------------------------- delete / restore

    [Fact(DisplayName = "E-10 Deleting equipment hides it from the inventory")]
    public async Task Delete_HidesTheAsset()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var created = await CreateEquipmentAsync(client);

        (await client.DeleteAsync($"/api/equipment/{created.Id}")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var page = await ReadDataAsync<PagedResult<EquipmentListDto>>(
            await client.GetAsync($"/api/equipment?Category={created.Category}&PageSize=200"));
        page.Items.Should().NotContain(e => e.Id == created.Id);
    }

    [Fact(DisplayName = "E-10 A deleted asset is a soft delete: it is still readable and restorable")]
    public async Task Restore_BringsTheAssetBack()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var created = await CreateEquipmentAsync(client);

        await client.DeleteAsync($"/api/equipment/{created.Id}");

        // GetById bypasses the soft-delete filter, so the row is still there and flagged.
        var deleted = await ReadDataAsync<EquipmentDetailDto>(
            await client.GetAsync($"/api/equipment/{created.Id}"));
        deleted.IsDeleted.Should().BeTrue();

        (await client.PostAsync($"/api/equipment/{created.Id}/restore", null)).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var restored = await ReadDataAsync<EquipmentDetailDto>(
            await client.GetAsync($"/api/equipment/{created.Id}"));
        restored.IsDeleted.Should().BeFalse();

        var page = await ReadDataAsync<PagedResult<EquipmentListDto>>(
            await client.GetAsync($"/api/equipment?Category={created.Category}&PageSize=200"));
        page.Items.Should().Contain(e => e.Id == created.Id);
    }

    [Fact(DisplayName = "E-10 IncludeDeleted brings soft-deleted assets back into the list")]
    public async Task GetPaged_WithIncludeDeleted_ShowsTheDeletedAsset()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var created = await CreateEquipmentAsync(client);

        await client.DeleteAsync($"/api/equipment/{created.Id}");

        var page = await ReadDataAsync<PagedResult<EquipmentListDto>>(
            await client.GetAsync($"/api/equipment?Category={created.Category}&IncludeDeleted=true&PageSize=200"));

        page.Items.Should().ContainSingle().Which.IsDeleted.Should().BeTrue();
    }

    [Fact(DisplayName = "E-10 Deleting an unknown asset is a 404")]
    public async Task Delete_AnUnknownAsset_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        await AssertFailureAsync(
            await client.DeleteAsync("/api/equipment/999999"), HttpStatusCode.NotFound, "NOT_FOUND");
    }
}
