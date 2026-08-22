using GymManagement.Domain.Enums;

namespace GymManagement.Application.Interfaces;

/// <summary>
/// What one occasion's dispatch actually achieved, per channel. Never an exception: every caller
/// is a business operation that has already committed, and a message is never worth undoing one.
/// </summary>
public sealed record MemberNotificationOutcome(
    bool EmailSent, bool WhatsAppSent, bool AlreadySent = false, string? Reason = null)
{
    public bool AnySent => EmailSent || WhatsAppSent;

    public static readonly MemberNotificationOutcome Nothing = new(false, false);

    public static MemberNotificationOutcome Skipped(string reason) => new(false, false, false, reason);

    public static MemberNotificationOutcome Duplicate(string reason) => new(false, false, true, reason);
}

/// <summary>
/// Messages a member directly — on their own email address and their own WhatsApp number — on the
/// occasions that matter to them: money received, a diet plan written, a training streak reached,
/// a birthday, a festival.
///
/// Four rules govern every method here, and they are the reason this is one service rather than a
/// call to <see cref="IEmailSender"/> scattered through six others:
/// <list type="number">
///   <item><b>Never throws.</b> Every caller has already committed a payment, a plan or a check-in.
///   A dead relay or a refused gateway is logged and dropped; it may never turn a successful
///   business operation into a failed request.</item>
///   <item><b>Each channel is attempted independently.</b> Email failing must not cost the member
///   their WhatsApp message, and the reverse.</item>
///   <item><b>At most one message per member per occasion.</b> A <c>MemberNotificationLog</c> row is
///   claimed before the first channel is attempted, and the database's unique index — not the
///   process — is what enforces it.</item>
///   <item><b>Nothing sensitive travels.</b> Amounts, plan names and dates, never a card number, a
///   UPI PIN, a payer VPA or a password.</item>
/// </list>
/// </summary>
public interface IMemberNotifier
{
    /// <summary>
    /// Confirms a payment to the member who made it. Called only after the payment transaction has
    /// committed. Whether the member reads "welcome" or "renewed" is decided here from their own
    /// history, so no caller has to know which it is.
    /// </summary>
    Task<MemberNotificationOutcome> NotifyPaymentAsync(int paymentId, CancellationToken ct = default);

    /// <summary>Tells the member their diet plan is ready, or that it has been revised.</summary>
    Task<MemberNotificationOutcome> NotifyDietPlanAsync(int dietPlanId, CancellationToken ct = default);

    /// <summary>
    /// Congratulates the member on a streak milestone. A streak length that is not a milestone
    /// sends nothing, so callers may hand over every check-in without filtering first.
    /// </summary>
    Task<MemberNotificationOutcome> NotifyStreakAsync(int memberId, int streakDays, CancellationToken ct = default);

    /// <summary>Wishes the member a happy birthday, once in a given year.</summary>
    Task<MemberNotificationOutcome> NotifyBirthdayAsync(int memberId, CancellationToken ct = default);

    /// <summary>Wishes the member a happy festival, once per festival per year.</summary>
    Task<MemberNotificationOutcome> NotifyFestivalAsync(
        int memberId, FestivalGreeting festival, CancellationToken ct = default);

    /// <summary>Whether the occasion is switched on for a channel, for the operator screen to report.</summary>
    bool IsChannelEnabled(MemberNotificationKind kind, bool email);
}

/// <summary>One festival the gym wishes its members on, as configured under <c>Festivals</c>.</summary>
public sealed record FestivalGreeting(string Key, string Name, DateTime Date, string? Greeting);

/// <summary>
/// The daily sweep for the two occasions that have no business trigger of their own: birthdays and
/// festivals. Runs from the daily scheduler and from an admin-triggered endpoint, so — exactly like
/// <see cref="IExpiryReminderMailer"/> — it must be safe to call any number of times.
/// </summary>
public interface IWishesDispatcher
{
    /// <summary>Sends today's birthday and festival wishes. Returns how many members were reached.</summary>
    Task<int> SendTodaysWishesAsync(CancellationToken ct = default);

    /// <summary>The festivals configured for the gym, ordered by date. Empty when none are set.</summary>
    IReadOnlyList<FestivalGreeting> Festivals { get; }
}
