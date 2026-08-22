using GymManagement.Domain.Enums;

namespace GymManagement.Infrastructure.Messaging;

/// <summary>
/// Configuration for the occasions members are messaged on directly, bound from the
/// <c>MemberNotifications</c> section.
///
/// Every occasion can switch email and WhatsApp independently, but the defaults are the contract:
/// a gym that configures nothing gets WhatsApp on everywhere and email on everywhere except the
/// two payment occasions, because <c>PaymentReceiptMailer</c> already emails the receipt itself
/// and a member who pays once must not hear about it twice in the same inbox.
/// </summary>
public sealed class MemberNotificationsOptions
{
    public const string SectionName = "MemberNotifications";

    /// <summary>
    /// Per-occasion channel switches, keyed by <see cref="MemberNotificationKind"/> name. Keys are
    /// case-insensitive because this is hand-typed configuration, and a flag left null falls back
    /// to the occasion's default rather than to off.
    /// </summary>
    public Dictionary<string, OccasionChannelOptions> Occasions { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The day counts that earn a congratulation. Every check-in is offered to the notifier, so
    /// this list is what turns a year of silent days into a handful of messages worth getting.
    /// Defaults live in <see cref="EffectiveStreakMilestones"/> rather than here: the
    /// configuration binder APPENDS to a non-empty default array, and a duplicated milestone
    /// list is exactly the kind of quiet wrongness that never gets noticed.
    /// </summary>
    public int[] StreakMilestones { get; set; } = Array.Empty<int>();

    private static readonly int[] DefaultStreakMilestones = { 3, 7, 14, 30, 60, 100 };

    /// <summary>The configured milestones, or the built-in ladder when none are configured.</summary>
    public IReadOnlyList<int> EffectiveStreakMilestones =>
        StreakMilestones is { Length: > 0 } ? StreakMilestones : DefaultStreakMilestones;

    /// <summary>The festivals the gym wishes its members on, recurring yearly on Month/Day.</summary>
    public List<FestivalOptions> Festivals { get; set; } = new();

    /// <summary>
    /// The effective switch for one occasion on one channel, defaults applied: WhatsApp defaults
    /// on for every occasion; email defaults on for everything except
    /// <see cref="MemberNotificationKind.PaymentReceived"/> and
    /// <see cref="MemberNotificationKind.RenewalPayment"/> — the receipt mailer already covers
    /// those, and double-emailing a receipt is a bug, not a feature.
    /// </summary>
    public bool IsChannelEnabled(MemberNotificationKind kind, bool email)
    {
        var configured = Occasions.TryGetValue(kind.ToString(), out var occasion)
            ? (email ? occasion.Email : occasion.WhatsApp)
            : null;
        if (configured.HasValue) return configured.Value;

        return !email
               || kind is not (MemberNotificationKind.PaymentReceived or MemberNotificationKind.RenewalPayment);
    }

    /// <summary>One occasion's switches. Null means "use the default", not "off".</summary>
    public sealed class OccasionChannelOptions
    {
        public bool? Email { get; set; }
        public bool? WhatsApp { get; set; }
    }
}

/// <summary>
/// One festival, as configured under <c>MemberNotifications:Festivals</c>. The greeting is the
/// gym's own wording — Diwali, Eid, Christmas or Sankranti in their words rather than ours — and
/// may be left empty for a plain "Happy &lt;Name&gt;".
/// </summary>
public sealed class FestivalOptions
{
    /// <summary>Stable identifier, half of the once-per-festival-per-year deduplication key.</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Recurrence date. An impossible combination is skipped, never thrown on.</summary>
    public int Month { get; set; }
    public int Day { get; set; }

    /// <summary>The gym's own greeting line; empty means "Happy &lt;Name&gt;".</summary>
    public string? Greeting { get; set; }

    /// <summary>Kept in the list but switched off, so a festival can be paused without retyping it.</summary>
    public bool Enabled { get; set; } = true;
}
