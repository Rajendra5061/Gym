using GymManagement.Domain.Common;

namespace GymManagement.Domain.Entities;

/// <summary>
/// One renewal-reminder email that actually left for a member, one row per member per day.
///
/// This table is what makes the daily mailer idempotent: the unique (MemberId, SentOnDate)
/// index means a restart, a manual "run now" after the scheduled run, or two instances racing
/// can never email the same member twice in a day. Rows are kept — they double as the audit
/// answer to "was this member warned before their membership lapsed?".
/// </summary>
public class ExpiryReminderEmail : BaseEntity
{
    public int MemberId { get; set; }
    public Member? Member { get; set; }

    /// <summary>The subscription the reminder was about, kept even if it is later renewed.</summary>
    public int SubscriptionId { get; set; }
    public Subscription? Subscription { get; set; }

    /// <summary>Local calendar date of the send; the second column of the unique index.</summary>
    public DateTime SentOnDate { get; set; }

    /// <summary>The end date quoted in the email, for the audit trail.</summary>
    public DateTime EndDateAtSend { get; set; }

    /// <summary>Days remaining that the reminder announced (0 = ends today).</summary>
    public int DaysLeftAtSend { get; set; }

    /// <summary>Which channels actually went out this day — the audit answer per channel.</summary>
    public bool EmailSent { get; set; }
    public bool SmsSent { get; set; }
}
