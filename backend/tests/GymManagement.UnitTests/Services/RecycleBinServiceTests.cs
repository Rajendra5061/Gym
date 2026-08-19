using FluentAssertions;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Domain.Constants;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using GymManagement.Infrastructure.Services;
using GymManagement.UnitTests.TestBase;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GymManagement.UnitTests.Services;

/// <summary>The recycle bin — cases B-01, B-02, B-03 and B-04.</summary>
public class RecycleBinServiceTests : IAsyncLifetime
{
    private const string SetBasedUpdateNeedsRelationalProvider =
        "Not runnable on the EF Core in-memory provider: RecycleBinService restores and purges with " +
        "ExecuteUpdateAsync / ExecuteDeleteAsync, which the in-memory provider cannot translate. " +
        "The equivalent single-entity restore is covered by MemberServiceTests (case M-10). " +
        "See the final report.";

    private readonly FixedClock _clock = new();
    private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Admin();
    private readonly RecordingAuditService _audit = new();

    private GymDbContext _db = null!;
    private ReferenceData _data = null!;
    private RecycleBinService _service = null!;

    public async Task InitializeAsync()
    {
        _db = InMemoryDbContextFactory.Create(_currentUser, _clock);
        _data = await InMemoryDbContextFactory.SeedReferenceDataAsync(_db);

        _service = new RecycleBinService(_db, _currentUser, _audit,
            NullLogger<RecycleBinService>.Instance);
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------- helpers

    /// <summary>Soft-deletes a member, an exercise and an expense.</summary>
    private async Task<(Member Member, Exercise Exercise, Expense Expense)> SoftDeleteThreeRowsAsync()
    {
        var member = await InMemoryDbContextFactory.AddMemberAsync(_db, "Deleted Member");

        var exercise = new Exercise { Name = "Barbell Bench Press", Equipment = "Barbell" };
        var expense = new Expense
        {
            ExpenseNumber = "EXP-2026-000001",
            ExpenseCategoryId = _data.ExpenseCategoryId,
            Title = "August rent",
            Amount = 45000m,
            ExpenseDate = _clock.Today,
            VendorName = "Landlord"
        };

        _db.Exercises.Add(exercise);
        _db.Expenses.Add(expense);
        await _db.SaveChangesAsync();

        // Remove() is converted into a soft delete by the context.
        _db.Members.Remove(member);
        _db.Exercises.Remove(exercise);
        _db.Expenses.Remove(expense);
        await _db.SaveChangesAsync();

        return (member, exercise, expense);
    }

    // ---------------------------------------------------------------- list

    [Fact(DisplayName = "B-01 A soft-deleted member, exercise and expense all appear in the bin")]
    public async Task GetPaged_ListsDeletedRowsFromEveryEntity()
    {
        var (member, exercise, expense) = await SoftDeleteThreeRowsAsync();

        var page = await _service.GetPagedAsync(new RecycleBinQueryDto { PageSize = 50 });

        page.TotalCount.Should().Be(3);
        page.Items.Should().Contain(i => i.EntityName == "Members" && i.EntityId == member.Id);
        page.Items.Should().Contain(i => i.EntityName == "Exercises" && i.EntityId == exercise.Id);
        page.Items.Should().Contain(i => i.EntityName == "Expenses" && i.EntityId == expense.Id);
    }

    [Fact(DisplayName = "B-01 Each deleted row records when it was deleted and by whom")]
    public async Task GetPaged_RecordsWhenAndByWhomEachRowWasDeleted()
    {
        var (member, _, _) = await SoftDeleteThreeRowsAsync();

        var page = await _service.GetPagedAsync(new RecycleBinQueryDto { EntityName = "Members" });

        var row = page.Items.Should().ContainSingle().Subject;
        row.EntityId.Should().Be(member.Id);
        row.DeletedAt.Should().Be(_clock.UtcNow);
        row.DeletedBy.Should().Be(_currentUser.UserId);
        row.DeletedByName.Should().Be("System Administrator");
        row.DisplayName.Should().Contain("Deleted Member");
    }

    [Fact(DisplayName = "B-01 The bin can be narrowed to a single entity type")]
    public async Task GetPaged_FiltersByEntityName()
    {
        await SoftDeleteThreeRowsAsync();

        var page = await _service.GetPagedAsync(new RecycleBinQueryDto { EntityName = "Exercises" });

        page.TotalCount.Should().Be(1);
        page.Items.Should().OnlyContain(i => i.EntityName == "Exercises");
    }

    [Fact(DisplayName = "B-01 An unknown entity type yields an empty page rather than an error")]
    public async Task GetPaged_ForAnUnknownEntity_ReturnsAnEmptyPage()
    {
        await SoftDeleteThreeRowsAsync();

        var page = await _service.GetPagedAsync(new RecycleBinQueryDto { EntityName = "Aliens" });

        page.TotalCount.Should().Be(0);
        page.Items.Should().BeEmpty();
    }

    [Fact(DisplayName = "B-01 A bin with nothing in it lists nothing")]
    public async Task GetPaged_WhenNothingIsDeleted_IsEmpty()
    {
        var page = await _service.GetPagedAsync(new RecycleBinQueryDto());

        page.TotalCount.Should().Be(0);
        page.Items.Should().BeEmpty();
    }

    [Fact(DisplayName = "B-01 The entity type list reports a count per supported entity")]
    public async Task GetEntityTypes_ReportsTheDeletedCountPerEntity()
    {
        await SoftDeleteThreeRowsAsync();

        var types = await _service.GetEntityTypesAsync();

        types.Should().NotBeEmpty();
        types.Should().Contain(t => t.Name == "Members" && t.Extra == "1" && t.IsActive);
        types.Should().Contain(t => t.Name == "Trainers" && t.Extra == "0" && !t.IsActive);
    }

    // ------------------------------------------------------------- restore

    [Fact(DisplayName = "B-02 Restoring clears the delete flags and returns the number restored",
        Skip = SetBasedUpdateNeedsRelationalProvider)]
    public async Task Restore_ClearsTheFlagsAndReturnsTheCount()
    {
        var (member, _, _) = await SoftDeleteThreeRowsAsync();

        var restored = await _service.RestoreAsync(new RecycleBinActionDto
        {
            EntityName = "Members",
            EntityIds = new List<int> { member.Id }
        });

        restored.Should().Be(1);

        var stored = await _db.Members.AsNoTracking().FirstAsync(m => m.Id == member.Id);
        stored.IsDeleted.Should().BeFalse();
        stored.DeletedAt.Should().BeNull();
        stored.DeletedBy.Should().BeNull();
        _audit.Contains(AuditActions.Restore, "Members").Should().BeTrue();
    }

    [Fact(DisplayName = "B-02 Restoring nothing is a no-op that touches no rows")]
    public async Task Restore_WithNoIds_ReturnsZero()
    {
        await SoftDeleteThreeRowsAsync();

        var restored = await _service.RestoreAsync(new RecycleBinActionDto
        {
            EntityName = "Members",
            EntityIds = new List<int>()
        });

        restored.Should().Be(0);
        _audit.Entries.Should().BeEmpty();
    }

    [Fact(DisplayName = "B-02 Restoring an unsupported entity is a not-found error")]
    public async Task Restore_ForAnUnknownEntity_ThrowsNotFound()
    {
        var act = () => _service.RestoreAsync(new RecycleBinActionDto
        {
            EntityName = "Aliens",
            EntityIds = new List<int> { 1 }
        });

        await act.Should().ThrowAsync<NotFoundAppException>();
    }

    // --------------------------------------------------------------- purge

    [Theory(DisplayName = "B-03 Purging without the exact confirmation text is refused")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("permanently delete")]
    [InlineData("PERMANENTLY  DELETE")]
    [InlineData("DELETE")]
    [InlineData(" PERMANENTLY DELETE ")]
    public async Task Purge_WithoutTheExactConfirmation_ThrowsValidation(string? confirmation)
    {
        var (member, _, _) = await SoftDeleteThreeRowsAsync();

        var act = () => _service.PurgeAsync(new RecycleBinActionDto
        {
            EntityName = "Members",
            EntityIds = new List<int> { member.Id },
            ConfirmationText = confirmation
        });

        var error = await act.Should().ThrowAsync<ValidationAppException>();
        error.Which.Errors.Should().ContainKey(nameof(RecycleBinActionDto.ConfirmationText));
        error.Which.Errors[nameof(RecycleBinActionDto.ConfirmationText)]
            .Should().Contain(m => m.Contains(RecycleBinService.PurgeConfirmation));
    }

    [Fact(DisplayName = "B-03 The confirmation is checked before anything is deleted")]
    public async Task Purge_WithoutConfirmation_DeletesNothing()
    {
        var (member, _, _) = await SoftDeleteThreeRowsAsync();

        var act = () => _service.PurgeAsync(new RecycleBinActionDto
        {
            EntityName = "Members",
            EntityIds = new List<int> { member.Id }
        });

        await act.Should().ThrowAsync<ValidationAppException>();

        (await _db.Members.IgnoreQueryFilters().AsNoTracking().AnyAsync(m => m.Id == member.Id))
            .Should().BeTrue();
    }

    [Fact(DisplayName = "B-04 Purging without the recyclebin.purge permission is forbidden")]
    public async Task Purge_WithoutThePermission_ThrowsForbidden()
    {
        var (member, _, _) = await SoftDeleteThreeRowsAsync();

        _currentUser.Roles = new[] { RoleNames.Staff };
        _currentUser.Permissions = new[] { Permissions.RecycleBinView, Permissions.RecycleBinRestore };

        var act = () => _service.PurgeAsync(new RecycleBinActionDto
        {
            EntityName = "Members",
            EntityIds = new List<int> { member.Id },
            ConfirmationText = RecycleBinService.PurgeConfirmation
        });

        await act.Should().ThrowAsync<ForbiddenAppException>();
    }

    [Fact(DisplayName = "B-04 A caller lacking the permission deletes nothing")]
    public async Task Purge_WithoutThePermission_DeletesNothing()
    {
        var (member, _, _) = await SoftDeleteThreeRowsAsync();
        _currentUser.Roles = new[] { RoleNames.Staff };
        _currentUser.Permissions = Array.Empty<string>();

        var act = () => _service.PurgeAsync(new RecycleBinActionDto
        {
            EntityName = "Members",
            EntityIds = new List<int> { member.Id },
            ConfirmationText = RecycleBinService.PurgeConfirmation
        });

        await act.Should().ThrowAsync<ForbiddenAppException>();

        (await _db.Members.IgnoreQueryFilters().AsNoTracking().AnyAsync(m => m.Id == member.Id))
            .Should().BeTrue();
    }

    [Fact(DisplayName = "B-06 A confirmed, permitted purge physically removes the row",
        Skip = SetBasedUpdateNeedsRelationalProvider)]
    public async Task Purge_WhenConfirmedAndPermitted_RemovesTheRow()
    {
        var (_, exercise, _) = await SoftDeleteThreeRowsAsync();

        var purged = await _service.PurgeAsync(new RecycleBinActionDto
        {
            EntityName = "Exercises",
            EntityIds = new List<int> { exercise.Id },
            ConfirmationText = RecycleBinService.PurgeConfirmation
        });

        purged.Should().Be(1);
        (await _db.Exercises.IgnoreQueryFilters().AsNoTracking().AnyAsync(e => e.Id == exercise.Id))
            .Should().BeFalse();
        _audit.Contains(AuditActions.Delete, "Exercises").Should().BeTrue();
    }
}
