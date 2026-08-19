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
/// Member registration, deletion and the server-side member list — cases M-01, M-02, M-08, M-09,
/// M-10 and M-11.
/// </summary>
public class MemberServiceTests : IAsyncLifetime
{
    private readonly FixedClock _clock = new();
    private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Admin();
    private readonly StubSettingsService _settings = new();
    private readonly RecordingAuditService _audit = new();
    private readonly RecordingNotificationService _notifications = new();
    private readonly StubLicenseService _license = new();

    private GymDbContext _db = null!;
    private ReferenceData _data = null!;
    private MemberService _service = null!;

    public async Task InitializeAsync()
    {
        _db = InMemoryDbContextFactory.Create(_currentUser, _clock);
        _data = await InMemoryDbContextFactory.SeedReferenceDataAsync(_db);

        _service = new MemberService(_db, new SequentialCodeGenerator(), new FakePasswordHasher(), _clock,
            _currentUser, _audit, _settings, _notifications, _license,
            NullLogger<MemberService>.Instance);
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------- helpers

    private static CreateMemberDto NewMember(string fullName = "Asha Menon", string phone = "9876543210") => new()
    {
        FullName = fullName,
        Gender = Gender.Female,
        Phone = phone,
        Email = $"{Guid.NewGuid():N}@example.com",
        JoiningDate = FixedClock.DefaultNow.Date
    };

    /// <summary>Adds an active subscription directly so the delete rules have something to trip on.</summary>
    private async Task<Subscription> AddSubscriptionAsync(
        int memberId, SubscriptionStatus status, decimal finalAmount = 3304m, decimal paidAmount = 3304m)
    {
        var subscription = new Subscription
        {
            SubscriptionCode = $"SUB-{Guid.NewGuid():N}"[..18],
            MemberId = memberId,
            MembershipPlanId = _data.MonthlyPlanId,
            StartDate = _clock.Today,
            EndDate = _clock.Today.AddDays(30),
            Status = status,
            FinalAmount = finalAmount,
            PaidAmount = paidAmount,
            PaymentStatus = paidAmount >= finalAmount ? PaymentStatus.Paid : PaymentStatus.PartiallyPaid
        };

        _db.Subscriptions.Add(subscription);
        await _db.SaveChangesAsync();
        return subscription;
    }

    // -------------------------------------------------------------- create

    [Fact(DisplayName = "M-01 Creating a member generates a member code")]
    public async Task Create_GeneratesAMemberCode()
    {
        var (member, account) = await _service.CreateAsync(NewMember());

        member.MemberCode.Should().NotBeNullOrWhiteSpace();
        member.MemberCode.Should().StartWith("GYM-");
        member.FullName.Should().Be("Asha Menon");
        member.Status.Should().Be(MemberStatus.Active);
        account.Should().BeNull("no login account was requested");
    }

    [Fact(DisplayName = "M-01 Creating a member writes an audit entry")]
    public async Task Create_WritesAnAuditEntry()
    {
        var (member, _) = await _service.CreateAsync(NewMember());

        _audit.Entries.Should().Contain(e =>
            e.Action == AuditActions.Create &&
            e.EntityName == nameof(Member) &&
            e.EntityId == member.Id);
    }

    [Fact(DisplayName = "M-01 Creating a member raises a new-registration notification")]
    public async Task Create_RaisesANewRegistrationNotification()
    {
        await _service.CreateAsync(NewMember());

        _notifications.CountOf(NotificationType.NewMemberRegistration).Should().Be(1);
    }

    [Fact(DisplayName = "M-01 Member codes are unique across registrations")]
    public async Task Create_ProducesUniqueMemberCodes()
    {
        var (first, _) = await _service.CreateAsync(NewMember(phone: "9876543210"));
        var (second, _) = await _service.CreateAsync(NewMember("Ravi Kumar", "9876543211"));

        first.MemberCode.Should().NotBe(second.MemberCode);
    }

    [Fact(DisplayName = "M-03 Requesting a login account returns a temporary password")]
    public async Task Create_WithALoginAccount_ReturnsATemporaryPassword()
    {
        var dto = NewMember();
        dto.CreateUserAccount = true;

        var (member, account) = await _service.CreateAsync(dto);

        account.Should().NotBeNull();
        account!.TemporaryPassword.Should().NotBeNullOrWhiteSpace();
        account.UserName.Should().Be(member.MemberCode);

        var user = await _db.Users.AsNoTracking().FirstAsync(u => u.MemberId == member.Id);
        user.MustChangePassword.Should().BeTrue();
    }

    [Fact(DisplayName = "M-02 A second active member with the same phone number is a conflict")]
    public async Task Create_WithADuplicatePhone_ThrowsConflict()
    {
        await _service.CreateAsync(NewMember(phone: "9876543210"));

        var act = () => _service.CreateAsync(NewMember("Someone Else", "9876543210"));

        var error = await act.Should().ThrowAsync<ConflictAppException>();
        error.Which.Message.Should().Contain("9876543210");
    }

    [Fact(DisplayName = "M-05 A future date of birth is rejected by the service too")]
    public async Task Create_WithAFutureDateOfBirth_ThrowsValidation()
    {
        var dto = NewMember();
        dto.DateOfBirth = _clock.Today.AddDays(1);

        var act = () => _service.CreateAsync(dto);

        var error = await act.Should().ThrowAsync<ValidationAppException>();
        error.Which.Errors.Should().ContainKey(nameof(CreateMemberDto.DateOfBirth));
    }

    [Fact(DisplayName = "M-04 Requesting a login account with no email is rejected by the service too")]
    public async Task Create_WithAnAccountButNoEmail_ThrowsValidation()
    {
        var dto = NewMember();
        dto.Email = null;
        dto.CreateUserAccount = true;

        var act = () => _service.CreateAsync(dto);

        var error = await act.Should().ThrowAsync<ValidationAppException>();
        error.Which.Errors.Should().ContainKey("Email");
    }

    [Fact(DisplayName = "G-09 Registration is refused once the licensed member quota is reached")]
    public async Task Create_WhenTheLicenceQuotaIsFull_Throws()
    {
        _license.CapacityFailure = new LicenseAppException("The licence allows at most 50 members.");

        var act = () => _service.CreateAsync(NewMember());

        await act.Should().ThrowAsync<LicenseAppException>();
    }

    // --------------------------------------------------------- soft delete

    [Fact(DisplayName = "M-08 A member holding an active subscription cannot be deleted")]
    public async Task SoftDelete_WithAnActiveSubscription_ThrowsBusinessRule()
    {
        var (member, _) = await _service.CreateAsync(NewMember());
        await AddSubscriptionAsync(member.Id, SubscriptionStatus.Active);

        var act = () => _service.SoftDeleteAsync(member.Id);

        var error = await act.Should().ThrowAsync<BusinessRuleAppException>();
        error.Which.Message.Should().Contain("active subscription");
    }

    [Fact(DisplayName = "M-08 A member with an outstanding balance cannot be deleted")]
    public async Task SoftDelete_WithAnOutstandingBalance_ThrowsBusinessRule()
    {
        var (member, _) = await _service.CreateAsync(NewMember());
        await AddSubscriptionAsync(member.Id, SubscriptionStatus.Expired, finalAmount: 3304m, paidAmount: 1000m);

        var act = () => _service.SoftDeleteAsync(member.Id);

        var error = await act.Should().ThrowAsync<BusinessRuleAppException>();
        error.Which.Message.Should().Contain("outstanding");
    }

    [Fact(DisplayName = "M-09 A member with nothing active and nothing owing is deleted")]
    public async Task SoftDelete_WithNothingOutstanding_Succeeds()
    {
        var (member, _) = await _service.CreateAsync(NewMember());
        await AddSubscriptionAsync(member.Id, SubscriptionStatus.Expired);

        await _service.SoftDeleteAsync(member.Id);

        (await _db.Members.AsNoTracking().AnyAsync(m => m.Id == member.Id)).Should().BeFalse();

        var stored = await _db.Members.AsNoTracking().IgnoreQueryFilters().FirstAsync(m => m.Id == member.Id);
        stored.IsDeleted.Should().BeTrue();
        stored.DeletedAt.Should().Be(_clock.UtcNow);
        stored.DeletedBy.Should().Be(_currentUser.UserId);
    }

    [Fact(DisplayName = "M-09 Deleting a member deactivates their linked login account")]
    public async Task SoftDelete_DeactivatesTheLinkedUser()
    {
        var dto = NewMember();
        dto.CreateUserAccount = true;
        var (member, _) = await _service.CreateAsync(dto);

        await _service.SoftDeleteAsync(member.Id);

        var user = await _db.Users.AsNoTracking().IgnoreQueryFilters().FirstAsync(u => u.MemberId == member.Id);
        user.Status.Should().Be(UserStatus.Inactive);
    }

    [Fact(DisplayName = "M-09 Deleting a member writes an audit entry")]
    public async Task SoftDelete_WritesAnAuditEntry()
    {
        var (member, _) = await _service.CreateAsync(NewMember());

        await _service.SoftDeleteAsync(member.Id);

        _audit.Entries.Should().Contain(e =>
            e.Action == AuditActions.SoftDelete && e.EntityId == member.Id);
    }

    // ------------------------------------------------------------- restore

    [Fact(DisplayName = "M-10 Restoring a member clears the delete flags and brings the row back")]
    public async Task Restore_ClearsTheDeleteFlags()
    {
        var (member, _) = await _service.CreateAsync(NewMember());
        await _service.SoftDeleteAsync(member.Id);

        await _service.RestoreAsync(member.Id);

        var stored = await _db.Members.AsNoTracking().FirstAsync(m => m.Id == member.Id);
        stored.IsDeleted.Should().BeFalse();
        stored.DeletedAt.Should().BeNull();
        stored.DeletedBy.Should().BeNull();
    }

    [Fact(DisplayName = "M-10 Restoring keeps the member's history intact")]
    public async Task Restore_KeepsTheHistory()
    {
        var (member, _) = await _service.CreateAsync(NewMember());
        var subscription = await AddSubscriptionAsync(member.Id, SubscriptionStatus.Expired);
        await _service.SoftDeleteAsync(member.Id);

        await _service.RestoreAsync(member.Id);

        var history = await _service.GetHistoryAsync(member.Id);
        history.Subscriptions.Should().ContainSingle().Which.Id.Should().Be(subscription.Id);
    }

    [Fact(DisplayName = "M-10 Restoring a member who was never deleted is a no-op")]
    public async Task Restore_AnUndeletedMember_DoesNothing()
    {
        var (member, _) = await _service.CreateAsync(NewMember());

        var act = () => _service.RestoreAsync(member.Id);

        await act.Should().NotThrowAsync();
        (await _db.Members.AsNoTracking().AnyAsync(m => m.Id == member.Id)).Should().BeTrue();
    }

    // ---------------------------------------------------------- paged list

    private async Task SeedListAsync()
    {
        await _service.CreateAsync(NewMember("Asha Menon", "9000000001"));
        await _service.CreateAsync(NewMember("Ravi Kumar", "9000000002"));
        await _service.CreateAsync(NewMember("Priya Nair", "9000000003"));
        await _service.CreateAsync(NewMember("Arjun Rao", "9000000004"));
        await _service.CreateAsync(NewMember("Meera Iyer", "9000000005"));

        var suspended = await _db.Members.FirstAsync(m => m.FullName == "Ravi Kumar");
        suspended.Status = MemberStatus.Suspended;
        await _db.SaveChangesAsync();
    }

    [Fact(DisplayName = "M-11 The paged list reports the full total while returning one page")]
    public async Task GetPaged_RespectsThePageSize()
    {
        await SeedListAsync();

        var page = await _service.GetPagedAsync(new MemberQueryDto { PageNumber = 1, PageSize = 2 });

        page.Items.Should().HaveCount(2);
        page.TotalCount.Should().Be(5);
        page.TotalPages.Should().Be(3);
        page.HasNextPage.Should().BeTrue();
        page.HasPreviousPage.Should().BeFalse();
    }

    [Fact(DisplayName = "M-11 The second page continues where the first one stopped")]
    public async Task GetPaged_SecondPage_ContinuesTheSequence()
    {
        await SeedListAsync();

        var first = await _service.GetPagedAsync(new MemberQueryDto { PageNumber = 1, PageSize = 2 });
        var second = await _service.GetPagedAsync(new MemberQueryDto { PageNumber = 2, PageSize = 2 });

        second.Items.Should().HaveCount(2);
        second.Items.Select(i => i.Id).Should().NotIntersectWith(first.Items.Select(i => i.Id));
        second.HasPreviousPage.Should().BeTrue();
    }

    [Fact(DisplayName = "M-11 Filtering by status is applied before paging")]
    public async Task GetPaged_FiltersByStatus()
    {
        await SeedListAsync();

        var page = await _service.GetPagedAsync(new MemberQueryDto { Status = MemberStatus.Suspended });

        page.TotalCount.Should().Be(1);
        page.Items.Should().ContainSingle().Which.FullName.Should().Be("Ravi Kumar");
    }

    [Fact(DisplayName = "M-11 The search term matches the name, the code and the phone number")]
    public async Task GetPaged_SearchesNameCodeAndPhone()
    {
        await SeedListAsync();
        var target = await _db.Members.AsNoTracking().FirstAsync(m => m.FullName == "Priya Nair");

        (await _service.GetPagedAsync(new MemberQueryDto { Search = "Priya" }))
            .Items.Should().ContainSingle().Which.Id.Should().Be(target.Id);

        (await _service.GetPagedAsync(new MemberQueryDto { Search = target.MemberCode }))
            .Items.Should().ContainSingle().Which.Id.Should().Be(target.Id);

        (await _service.GetPagedAsync(new MemberQueryDto { Search = "9000000003" }))
            .Items.Should().ContainSingle().Which.Id.Should().Be(target.Id);
    }

    [Fact(DisplayName = "Z-07 A SQL injection attempt is treated as a literal search string")]
    public async Task GetPaged_WithASqlInjectionAttempt_MatchesNothingAndLeavesTheTableIntact()
    {
        await SeedListAsync();

        var page = await _service.GetPagedAsync(
            new MemberQueryDto { Search = "'; DROP TABLE Members;--" });

        page.TotalCount.Should().Be(0);
        page.Items.Should().BeEmpty();
        (await _db.Members.AsNoTracking().CountAsync()).Should().Be(5);
    }

    [Fact(DisplayName = "M-11 A search that matches nothing returns an empty page, not an error")]
    public async Task GetPaged_WithNoMatches_ReturnsAnEmptyPage()
    {
        await SeedListAsync();

        var page = await _service.GetPagedAsync(new MemberQueryDto { Search = "nobody-by-that-name" });

        page.TotalCount.Should().Be(0);
        page.Items.Should().BeEmpty();
        page.TotalPages.Should().Be(0);
    }

    [Fact(DisplayName = "M-11 Deleted members are hidden from the list unless explicitly included")]
    public async Task GetPaged_HidesDeletedMembers()
    {
        await SeedListAsync();
        var victim = await _db.Members.AsNoTracking().FirstAsync(m => m.FullName == "Arjun Rao");
        await _service.SoftDeleteAsync(victim.Id);

        (await _service.GetPagedAsync(new MemberQueryDto())).TotalCount.Should().Be(4);
        (await _service.GetPagedAsync(new MemberQueryDto { IncludeDeleted = true })).TotalCount.Should().Be(5);
    }

    [Fact(DisplayName = "Z-08 A page size above the ceiling is clamped before the query runs")]
    public async Task GetPaged_WithAnOversizedPageSize_IsClamped()
    {
        await SeedListAsync();

        var query = new MemberQueryDto { PageSize = 100_000 };
        var page = await _service.GetPagedAsync(query);

        query.PageSize.Should().Be(200);
        page.PageSize.Should().Be(200);
    }

    // -------------------------------------------------------------- lookup

    [Fact(DisplayName = "M-06 Looking a member up by code returns the same record")]
    public async Task GetByCode_ReturnsTheMember()
    {
        var (member, _) = await _service.CreateAsync(NewMember());

        var found = await _service.GetByCodeAsync(member.MemberCode);

        found.Should().NotBeNull();
        found!.Id.Should().Be(member.Id);
    }

    [Fact(DisplayName = "M-06 An unknown member code returns null rather than throwing")]
    public async Task GetByCode_ForAnUnknownCode_ReturnsNull()
    {
        (await _service.GetByCodeAsync("GYM-9999-9999")).Should().BeNull();
    }
}
