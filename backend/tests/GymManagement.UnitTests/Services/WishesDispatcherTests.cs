using FluentAssertions;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using GymManagement.Infrastructure.Messaging;
using GymManagement.Infrastructure.Services;
using GymManagement.UnitTests.TestBase;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GymManagement.UnitTests.Services;

/// <summary>
/// The daily sweep: who is due a wish today, who is left alone, and the guarantees the scheduler
/// and the admin button both depend on — idempotent, count-by-member, and never derailed by one
/// failing member. The clock is the suite default, 17 August 2026.
/// </summary>
public class WishesDispatcherTests : IAsyncLifetime
{
    private readonly FixedClock _clock = new();
    private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Admin();
    private readonly StubSettingsService _settings = new();
    private readonly CapturingEmailSender _email = new();
    private readonly CapturingWhatsAppSender _whatsApp = new();
    private readonly MemberNotificationsOptions _options = new();
    private readonly WhatsAppOptions _whatsAppOptions = new();

    private GymDbContext _db = null!;
    private WishesDispatcher _dispatcher = null!;

    public Task InitializeAsync()
    {
        _db = InMemoryDbContextFactory.Create(_currentUser, _clock);

        var notifier = new MemberNotifier(_db, _email, _whatsApp, _settings, _clock, _options,
            _whatsAppOptions, NullLogger<MemberNotifier>.Instance);
        _dispatcher = new WishesDispatcher(_db, notifier, _clock, _options,
            NullLogger<WishesDispatcher>.Instance);

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    private async Task<Member> AddMemberAsync(
        string name, DateTime? dateOfBirth, MemberStatus status = MemberStatus.Active)
    {
        var member = await InMemoryDbContextFactory.AddMemberAsync(_db, name, status: status);
        member.DateOfBirth = dateOfBirth;
        member.Email = $"{Guid.NewGuid():N}@example.test";
        await _db.SaveChangesAsync();
        return member;
    }

    // ============================================================ birthdays

    [Fact]
    public async Task Todays_birthdays_are_wished_once_and_nobody_else_is()
    {
        await AddMemberAsync("Ravi Kumar", new DateTime(1990, 8, 17));
        await AddMemberAsync("Anil Rao", new DateTime(1992, 8, 18));
        await AddMemberAsync("No Birthday", null);

        var reached = await _dispatcher.SendTodaysWishesAsync();

        reached.Should().Be(1);
        _email.Sent.Should().ContainSingle().Which.Subject.Should().Contain("Ravi");
        _whatsApp.Sent.Should().ContainSingle().Which.TemplateName.Should().Be("birthday_wish");

        // The second run of the day — the admin button after the scheduler — reaches nobody:
        // the notifier's claim, not this sweep, is what remembers.
        (await _dispatcher.SendTodaysWishesAsync()).Should().Be(0);
        _email.Sent.Should().ContainSingle();
        _whatsApp.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task Opted_out_members_are_still_offered_and_the_notifier_skips_them()
    {
        var member = await AddMemberAsync("Ravi Kumar", new DateTime(1990, 8, 17));
        member.WishesOptOut = true;
        await _db.SaveChangesAsync();

        (await _dispatcher.SendTodaysWishesAsync()).Should().Be(0);

        _email.Sent.Should().BeEmpty();
        _whatsApp.Sent.Should().BeEmpty();
    }

    // ============================================================ festivals

    [Fact]
    public async Task A_festival_on_todays_date_reaches_every_active_member_once()
    {
        _options.Festivals.Add(new FestivalOptions
        {
            Key = "sankranti", Name = "Sankranti", Month = 8, Day = 17, Greeting = "Happy Sankranti"
        });

        await AddMemberAsync("Ravi Kumar", null);
        await AddMemberAsync("Anil Rao", null);
        await AddMemberAsync("Lapsed Member", null, MemberStatus.Expired);

        var reached = await _dispatcher.SendTodaysWishesAsync();

        reached.Should().Be(2, "an expired membership is a front-desk conversation, not a wish");
        _whatsApp.Sent.Should().HaveCount(2);
        _whatsApp.Sent[0].TemplateName.Should().Be("festival_wish");
        _whatsApp.Sent[0].Parameters[0].Should().Be("Happy Sankranti");

        // Re-running the sweep the same day wishes nobody twice.
        (await _dispatcher.SendTodaysWishesAsync()).Should().Be(0);
        _whatsApp.Sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_member_whose_birthday_falls_on_a_festival_counts_once_but_hears_twice()
    {
        _options.Festivals.Add(new FestivalOptions { Key = "fest", Name = "Fest Day", Month = 8, Day = 17 });
        await AddMemberAsync("Ravi Kumar", new DateTime(1990, 8, 17));

        var reached = await _dispatcher.SendTodaysWishesAsync();

        reached.Should().Be(1, "the count is members reached, not messages sent");
        _whatsApp.Sent.Should().HaveCount(2, "the member is still owed both wishes");
    }

    // ============================================================ the festival list

    [Fact]
    public void The_festival_list_keeps_valid_enabled_entries_in_date_order()
    {
        _options.Festivals.Add(new FestivalOptions { Key = "diwali", Name = "Diwali", Month = 11, Day = 8 });
        _options.Festivals.Add(new FestivalOptions { Key = "newyear", Name = "New Year", Month = 1, Day = 1 });
        _options.Festivals.Add(new FestivalOptions { Key = "paused", Name = "Paused", Month = 5, Day = 5, Enabled = false });
        _options.Festivals.Add(new FestivalOptions { Key = "impossible", Name = "Bad Date", Month = 2, Day = 30 });
        _options.Festivals.Add(new FestivalOptions { Key = "", Name = "No Key", Month = 6, Day = 6 });

        var festivals = _dispatcher.Festivals;

        festivals.Select(f => f.Key).Should().Equal("newyear", "diwali");
        festivals.Should().OnlyContain(f => f.Date.Year == _clock.Now.Year);
    }

    // ============================================================ resilience

    [Fact]
    public async Task A_notifier_failure_on_one_member_never_stops_the_sweep()
    {
        await AddMemberAsync("Ravi Kumar", new DateTime(1990, 8, 17));
        await AddMemberAsync("Anil Rao", new DateTime(1992, 8, 17));

        var dispatcher = new WishesDispatcher(_db, new ThrowingNotifier(), _clock, _options,
            NullLogger<WishesDispatcher>.Instance);

        var act = async () => await dispatcher.SendTodaysWishesAsync();

        (await act.Should().NotThrowAsync()).Which.Should().Be(0);
    }

    /// <summary>
    /// The real notifier never throws; this one always does, proving the sweep's per-member
    /// isolation rather than relying on the collaborator's good manners.
    /// </summary>
    private sealed class ThrowingNotifier : IMemberNotifier
    {
        public Task<MemberNotificationOutcome> NotifyPaymentAsync(int paymentId, CancellationToken ct = default) =>
            throw new InvalidOperationException("The notifier is broken.");

        public Task<MemberNotificationOutcome> NotifyDietPlanAsync(int dietPlanId, CancellationToken ct = default) =>
            throw new InvalidOperationException("The notifier is broken.");

        public Task<MemberNotificationOutcome> NotifyStreakAsync(int memberId, int streakDays, CancellationToken ct = default) =>
            throw new InvalidOperationException("The notifier is broken.");

        public Task<MemberNotificationOutcome> NotifyBirthdayAsync(int memberId, CancellationToken ct = default) =>
            throw new InvalidOperationException("The notifier is broken.");

        public Task<MemberNotificationOutcome> NotifyFestivalAsync(int memberId, FestivalGreeting festival, CancellationToken ct = default) =>
            throw new InvalidOperationException("The notifier is broken.");

        public bool IsChannelEnabled(MemberNotificationKind kind, bool email) => true;
    }
}
