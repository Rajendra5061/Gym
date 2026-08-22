using GymManagement.Application.Common;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using GymManagement.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Services;

/// <summary>
/// The daily sweep for birthdays and festivals — the two occasions nothing else in the system
/// triggers. It only decides <b>who is due today</b>; whether a member is actually messaged is
/// the notifier's call alone, so the opt-out and once-per-year rules live in exactly one place
/// and an admin re-running the sweep after the scheduler costs nobody a duplicate wish.
///
/// Only active members are swept: an expired or suspended membership is a conversation for the
/// front desk, and a cheery automated wish in the middle of it reads as tone-deaf.
/// </summary>
public sealed class WishesDispatcher : IWishesDispatcher
{
    private readonly GymDbContext _db;
    private readonly IMemberNotifier _notifier;
    private readonly IDateTimeProvider _clock;
    private readonly MemberNotificationsOptions _options;
    private readonly ILogger<WishesDispatcher> _logger;

    public WishesDispatcher(
        GymDbContext db,
        IMemberNotifier notifier,
        IDateTimeProvider clock,
        MemberNotificationsOptions options,
        ILogger<WishesDispatcher> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The configured festivals resolved onto the current year, computed on read so a sweep that
    /// runs across New Year's midnight never wishes anyone on last year's dates. An entry that is
    /// disabled, keyless or set to an impossible date (30 February) is skipped, never thrown on —
    /// this is hand-typed configuration, and one bad row must not silence the rest.
    /// </summary>
    public IReadOnlyList<FestivalGreeting> Festivals
    {
        get
        {
            var year = _clock.Now.Year;
            return _options.Festivals
                .Where(f => f.Enabled
                            && !string.IsNullOrWhiteSpace(f.Key)
                            && f.Month is >= 1 and <= 12
                            && f.Day >= 1 && f.Day <= DateTime.DaysInMonth(year, f.Month))
                .Select(f => new FestivalGreeting(
                    f.Key.Trim(),
                    string.IsNullOrWhiteSpace(f.Name) ? f.Key.Trim() : f.Name.Trim(),
                    new DateTime(year, f.Month, f.Day),
                    f.Greeting))
                .OrderBy(f => f.Date)
                .ToList();
        }
    }

    public async Task<int> SendTodaysWishesAsync(CancellationToken ct = default)
    {
        var today = _clock.Now.Date;

        // Members, not messages: a member whose birthday falls on a festival is one person
        // reached, however many wishes they were owed.
        var reached = new HashSet<int>();

        List<int> birthdays;
        try
        {
            birthdays = await _db.Members.AsNoTracking()
                .Where(m => m.Status == MemberStatus.Active
                            && m.DateOfBirth != null
                            && m.DateOfBirth.Value.Month == today.Month
                            && m.DateOfBirth.Value.Day == today.Day)
                .Select(m => m.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Birthday wishes skipped: the birthday list could not be read.");
            birthdays = new List<int>();
        }

        foreach (var memberId in birthdays)
        {
            try
            {
                var outcome = await _notifier.NotifyBirthdayAsync(memberId, ct).ConfigureAwait(false);
                if (outcome.AnySent) reached.Add(memberId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Birthday wishes for member {MemberId} failed; continuing with the rest.", memberId);
            }
        }

        var festivalsToday = Festivals.Where(f => f.Date == today).ToList();
        if (festivalsToday.Count > 0)
        {
            List<int> everyone;
            try
            {
                everyone = await _db.Members.AsNoTracking()
                    .Where(m => m.Status == MemberStatus.Active)
                    .Select(m => m.Id)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Festival wishes skipped: the member list could not be read.");
                everyone = new List<int>();
            }

            foreach (var festival in festivalsToday)
            {
                foreach (var memberId in everyone)
                {
                    try
                    {
                        var outcome = await _notifier.NotifyFestivalAsync(memberId, festival, ct)
                            .ConfigureAwait(false);
                        if (outcome.AnySent) reached.Add(memberId);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex,
                            "{Festival} wishes for member {MemberId} failed; continuing with the rest.",
                            festival.Name, memberId);
                    }
                }
            }
        }

        if (reached.Count > 0)
            _logger.LogInformation(
                "Today's wishes reached {Count} member(s): {Birthdays} birthday(s), {Festivals} festival(s) on {Date:dd MMM yyyy}.",
                reached.Count, birthdays.Count, festivalsToday.Count, today);

        return reached.Count;
    }
}
