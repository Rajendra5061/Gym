using GymManagement.Application.Common;

namespace GymManagement.UnitTests.TestBase;

/// <summary>
/// An <see cref="IDateTimeProvider"/> frozen at a settable instant so every expiry, grace period
/// and freeze calculation under test is deterministic.
/// </summary>
public sealed class FixedClock : IDateTimeProvider
{
    /// <summary>The reference "now" used by the whole suite unless a test overrides it.</summary>
    public static readonly DateTime DefaultNow = new(2026, 8, 17, 10, 30, 0, DateTimeKind.Unspecified);

    public FixedClock() : this(DefaultNow) { }

    public FixedClock(DateTime now) => Now = now;

    /// <summary>Local "now". Setting it moves <see cref="UtcNow"/> and <see cref="Today"/> with it.</summary>
    public DateTime Now { get; set; }

    /// <summary>
    /// Stamped as <see cref="DateTimeKind.Utc"/> like the production provider, so anything that
    /// converts to epoch time (the JWT handler, for one) does not shift by the local offset.
    /// </summary>
    public DateTime UtcNow => DateTime.SpecifyKind(Now, DateTimeKind.Utc);

    public DateTime Today => Now.Date;

    /// <summary>Moves the clock forward (or back with a negative value) by whole days.</summary>
    public FixedClock AdvanceDays(int days)
    {
        Now = Now.AddDays(days);
        return this;
    }

    /// <summary>Moves the clock forward by a time span, e.g. to age an open gym visit.</summary>
    public FixedClock Advance(TimeSpan by)
    {
        Now = Now.Add(by);
        return this;
    }
}
