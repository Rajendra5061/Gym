using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Domain.Constants;
using GymManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GymManagement.IntegrationTests;

/// <summary>
/// Soft delete and the recycle bin over HTTP — cases B-01 to B-09.
///
/// The bin itself is read-only in this suite plus the guard rails on its two write endpoints. The
/// set-based restore and purge cannot execute here; see <see cref="SetBasedWriteNeedsRelationalProvider"/>.
/// The single-entity restore path that the module screens actually use (POST {id}/restore) is
/// covered for members, plans, equipment, enquiries and feedback in their own suites.
/// </summary>
[Collection(GymApiCollection.Name)]
public class RecycleBinEndpointTests : ApiTestBase
{
    private const string SetBasedWriteNeedsRelationalProvider =
        "Not runnable against the EF Core in-memory provider: RecycleBinService restores and purges " +
        "with ExecuteUpdateAsync / ExecuteDeleteAsync, which the in-memory provider cannot translate " +
        "(verified: it throws 'The LINQ expression … could not be translated'). Every guard that runs " +
        "BEFORE those calls is covered by the cases below, and the single-entity restore each module " +
        "screen actually uses is covered in the module suites. See the final report.";

    public RecycleBinEndpointTests(GymApiFixture fixture) : base(fixture) { }

    // ------------------------------------------------------------- helpers

    /// <summary>Registers a member and immediately soft deletes them, returning the row.</summary>
    private async Task<MemberDetailDto> DeletedMemberAsync(HttpClient client)
    {
        var member = await CreateMemberAsync(client);

        (await client.DeleteAsync($"/api/members/{member.Id}")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        return member;
    }

    private static async Task<PagedResult<RecycleBinItemDto>> BinAsync(
        HttpClient client, string? entityName = null, string? search = null)
    {
        var url = "/api/recycle-bin?PageSize=200";
        if (entityName is not null) url += $"&EntityName={entityName}";
        if (search is not null) url += $"&Search={search}";

        return await ReadDataAsync<PagedResult<RecycleBinItemDto>>(await client.GetAsync(url));
    }

    /// <summary>Creates a login in the given role and returns a client carrying its token.</summary>
    private async Task<HttpClient> ClientInRoleAsync(string role)
    {
        using var admin = await Factory.CreateAuthenticatedClientAsync();

        var roleId = await Factory.FromDbAsync(db =>
            db.Roles.AsNoTracking().Where(r => r.Name == role).Select(r => r.Id).FirstAsync());

        var userName = $"b{Guid.NewGuid():N}"[..14];
        const string password = "BinPassw0rd!";

        await ReadDataAsync<TemporaryPasswordDto>(
            await admin.PostAsJsonAsync("/api/users", new CreateUserDto
            {
                UserName = userName,
                FullName = $"{role} Bin User",
                Email = UniqueEmail(),
                Password = password,
                MustChangePassword = false,
                RoleIds = new List<int> { roleId },
                Status = UserStatus.Active
            }));

        return await Factory.CreateAuthenticatedClientAsync(userName, password);
    }

    // ------------------------------------------------- soft delete lands in the bin

    [Fact(DisplayName = "B-01 A soft-deleted member is hidden from its module but present in the bin")]
    public async Task DeletedMember_AppearsInTheBin()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await DeletedMemberAsync(client);

        // Gone from the members module…
        var members = await ReadDataAsync<PagedResult<MemberListDto>>(
            await client.GetAsync($"/api/members?Search={member.MemberCode}"));
        members.TotalCount.Should().Be(0);

        // …but recoverable from the bin, with the audit stamp filled in.
        var bin = await BinAsync(client, "Members", member.MemberCode);

        var row = bin.Items.Should().ContainSingle().Subject;
        row.EntityName.Should().Be("Members");
        row.EntityId.Should().Be(member.Id);
        row.DisplayName.Should().Contain(member.MemberCode);
        row.DeletedAt.Should().NotBeNull();
        row.DeletedBy.Should().NotBeNull();
        row.DeletedByName.Should().Be("System Administrator");
    }

    [Fact(DisplayName = "B-01 The bin holds nothing for a member who was never deleted")]
    public async Task ALiveMember_IsNotInTheBin()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);

        var bin = await BinAsync(client, "Members", member.MemberCode);

        bin.TotalCount.Should().Be(0);
    }

    [Fact(DisplayName = "B-03 Restoring through the member module clears the row out of the bin")]
    public async Task RestoringThroughTheModule_EmptiesTheBinRow()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await DeletedMemberAsync(client);

        (await BinAsync(client, "Members", member.MemberCode)).TotalCount.Should().Be(1);

        (await client.PostAsync($"/api/members/{member.Id}/restore", null)).StatusCode
            .Should().Be(HttpStatusCode.OK);

        (await BinAsync(client, "Members", member.MemberCode)).TotalCount.Should().Be(0);

        var members = await ReadDataAsync<PagedResult<MemberListDto>>(
            await client.GetAsync($"/api/members?Search={member.MemberCode}"));
        members.TotalCount.Should().Be(1);
    }

    // -------------------------------------------------------------- reads

    [Fact(DisplayName = "B-04 The entity-name filter returns only that entity's deleted rows")]
    public async Task GetPaged_FilteredByEntity_ReturnsOnlyThatEntity()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        await DeletedMemberAsync(client);

        var bin = await BinAsync(client, "Members");

        bin.TotalCount.Should().BeGreaterThan(0);
        bin.Items.Should().OnlyContain(i => i.EntityName == "Members");
    }

    [Fact(DisplayName = "B-04 An unfiltered bin spans every supported entity")]
    public async Task GetPaged_Unfiltered_ReturnsTheDeletedRows()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await DeletedMemberAsync(client);

        var bin = await BinAsync(client);

        bin.Items.Should().Contain(i => i.EntityName == "Members" && i.EntityId == member.Id);
    }

    [Fact(DisplayName = "B-04 An unknown entity name yields an empty page rather than an error")]
    public async Task GetPaged_WithAnUnknownEntity_IsEmpty()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var bin = await BinAsync(client, "NoSuchEntity");

        bin.TotalCount.Should().Be(0);
        bin.Items.Should().BeEmpty();
    }

    [Fact(DisplayName = "B-05 entity-types lists every supported type and counts the deleted rows")]
    public async Task GetEntityTypes_ListsTheSupportedTypes()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        await DeletedMemberAsync(client);

        var types = await ReadDataAsync<List<LookupDto>>(
            await client.GetAsync("/api/recycle-bin/entity-types"));

        types.Should().NotBeEmpty();
        types.Select(t => t.Name).Should().BeEquivalentTo(new[]
        {
            "Members", "Trainers", "Subscriptions", "Payments", "Exercises",
            "WorkoutPlans", "WorkoutSessions", "Expenses", "Users", "MemberDocuments"
        });

        var members = types.Single(t => t.Name == "Members");
        members.IsActive.Should().BeTrue("at least one member is in the bin");
        int.Parse(members.Extra!).Should().BeGreaterThan(0);
    }

    // ------------------------------------------------------ write guards

    [Fact(DisplayName = "B-06 Purging without the confirmation text is a 400 on ConfirmationText")]
    public async Task Purge_WithoutTheConfirmation_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await DeletedMemberAsync(client);

        var response = await client.PostAsJsonAsync("/api/recycle-bin/purge", new RecycleBinActionDto
        {
            EntityName = "Members",
            EntityIds = new List<int> { member.Id },
            ConfirmationText = "delete it please"
        });

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(RecycleBinActionDto.ConfirmationText));

        // The row is still in the bin, untouched.
        (await BinAsync(client, "Members", member.MemberCode)).TotalCount.Should().Be(1);
    }

    [Fact(DisplayName = "B-06 The confirmation text is case sensitive")]
    public async Task Purge_WithTheWrongCase_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await DeletedMemberAsync(client);

        var response = await client.PostAsJsonAsync("/api/recycle-bin/purge", new RecycleBinActionDto
        {
            EntityName = "Members",
            EntityIds = new List<int> { member.Id },
            ConfirmationText = "permanently delete"
        });

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await BinAsync(client, "Members", member.MemberCode)).TotalCount.Should().Be(1);
    }

    [Fact(DisplayName = "B-07 Purging an unsupported entity is a 404 naming the supported ones")]
    public async Task Purge_WithAnUnknownEntity_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/recycle-bin/purge", new RecycleBinActionDto
        {
            EntityName = "NoSuchEntity",
            EntityIds = new List<int> { 1 },
            ConfirmationText = "PERMANENTLY DELETE"
        });

        await AssertFailureAsync(response, HttpStatusCode.NotFound, "NOT_FOUND");
        (await ReadErrorAsync(response)).Message.Should().Contain("Members");
    }

    [Fact(DisplayName = "B-07 Restoring an unsupported entity is a 404")]
    public async Task Restore_WithAnUnknownEntity_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/recycle-bin/restore", new RecycleBinActionDto
        {
            EntityName = "NoSuchEntity",
            EntityIds = new List<int> { 1 }
        });

        await AssertFailureAsync(response, HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact(DisplayName = "B-07 Restoring an empty selection is a 400 on EntityIds")]
    public async Task Restore_WithNoIds_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/recycle-bin/restore", new RecycleBinActionDto
        {
            EntityName = "Members",
            EntityIds = new List<int>()
        });

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(RecycleBinActionDto.EntityIds));
    }

    [Fact(DisplayName = "B-07 Purging an empty selection is a 400 on EntityIds")]
    public async Task Purge_WithNoIds_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/recycle-bin/purge", new RecycleBinActionDto
        {
            EntityName = "Members",
            EntityIds = new List<int>(),
            ConfirmationText = "PERMANENTLY DELETE"
        });

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(RecycleBinActionDto.EntityIds));
    }

    [Fact(DisplayName = "B-07 A selection containing a non-positive id is a 400")]
    public async Task Restore_WithAnInvalidId_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/recycle-bin/restore", new RecycleBinActionDto
        {
            EntityName = "Members",
            EntityIds = new List<int> { 0 }
        });

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact(DisplayName = "B-07 A blank entity name is a 400 on EntityName")]
    public async Task Restore_WithoutAnEntityName_Is400()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/recycle-bin/restore", new RecycleBinActionDto
        {
            EntityName = "",
            EntityIds = new List<int> { 1 }
        });

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(RecycleBinActionDto.EntityName));
    }

    // ----------------------------------------------------- authorisation

    [Fact(DisplayName = "B-08 A Staff account cannot open the recycle bin at all")]
    public async Task Staff_CannotReachTheBin()
    {
        using var client = await ClientInRoleAsync(RoleNames.Staff);

        (await client.GetAsync("/api/recycle-bin")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        (await client.GetAsync("/api/recycle-bin/entity-types")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        (await client.PostAsJsonAsync("/api/recycle-bin/restore", new RecycleBinActionDto
        {
            EntityName = "Members",
            EntityIds = new List<int> { 1 }
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await client.PostAsJsonAsync("/api/recycle-bin/purge", new RecycleBinActionDto
        {
            EntityName = "Members",
            EntityIds = new List<int> { 1 },
            ConfirmationText = "PERMANENTLY DELETE"
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "B-08 An anonymous caller cannot open the recycle bin")]
    public async Task Anonymous_CannotReachTheBin()
    {
        using var client = Factory.CreateClient();

        (await client.GetAsync("/api/recycle-bin")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    // --------------------------------------- set-based restore / purge

    [Fact(DisplayName = "B-02 Restoring from the bin clears the delete flags and returns the count",
        Skip = SetBasedWriteNeedsRelationalProvider)]
    public async Task Restore_FromTheBin_ClearsTheFlags()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await DeletedMemberAsync(client);

        var restored = await ReadDataAsync<int>(
            await client.PostAsJsonAsync("/api/recycle-bin/restore", new RecycleBinActionDto
            {
                EntityName = "Members",
                EntityIds = new List<int> { member.Id }
            }));

        restored.Should().Be(1);
        (await BinAsync(client, "Members", member.MemberCode)).TotalCount.Should().Be(0);

        var members = await ReadDataAsync<PagedResult<MemberListDto>>(
            await client.GetAsync($"/api/members?Search={member.MemberCode}"));
        members.TotalCount.Should().Be(1);
    }

    [Fact(DisplayName = "B-09 A confirmed purge physically removes the row",
        Skip = SetBasedWriteNeedsRelationalProvider)]
    public async Task Purge_WhenConfirmed_RemovesTheRow()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await DeletedMemberAsync(client);

        var purged = await ReadDataAsync<int>(
            await client.PostAsJsonAsync("/api/recycle-bin/purge", new RecycleBinActionDto
            {
                EntityName = "Members",
                EntityIds = new List<int> { member.Id },
                ConfirmationText = "PERMANENTLY DELETE"
            }));

        purged.Should().Be(1);
        (await BinAsync(client, "Members", member.MemberCode)).TotalCount.Should().Be(0);

        var stillThere = await Factory.FromDbAsync(db => db.Members
            .IgnoreQueryFilters()
            .AnyAsync(m => m.Id == member.Id));
        stillThere.Should().BeFalse();
    }
}
