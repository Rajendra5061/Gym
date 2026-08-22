using GymManagement.Domain.Common;
using GymManagement.Domain.Enums;

namespace GymManagement.Domain.Entities;

/// <summary>
/// One direct message to a member that actually left the building — a receipt, a diet plan, a
/// streak congratulation, a birthday or a festival wish.
///
/// This table is what makes <c>IMemberNotifier</c> idempotent. The row is claimed <b>before</b> the
/// first channel is attempted, and the unique (MemberId, Kind, DeduplicationKey) index means a
/// restart, a retried request, a manual re-run or two instances racing can never message the same
/// member twice for the same occasion. A claim whose every channel then failed is released again,
/// so a dead gateway costs a delay rather than the message.
///
/// Rows are kept: they are the audit answer to "was this member actually told?".
/// </summary>
public class MemberNotificationLog : BaseEntity
{
    public int MemberId { get; set; }
    public Member? Member { get; set; }

    public MemberNotificationKind Kind { get; set; }

    /// <summary>
    /// What makes this occasion unique for this member, and the second half of the unique index:
    /// the payment id for a receipt, the plan id and its revision for a diet plan, the milestone
    /// for a streak, the year for a birthday, the festival key and year for a wish.
    /// </summary>
    public string DeduplicationKey { get; set; } = string.Empty;

    /// <summary>Local calendar date of the send, for the operator asking "what went out today?".</summary>
    public DateTime SentOnDate { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Which channels actually went out — the audit answer per channel.</summary>
    public bool EmailSent { get; set; }
    public bool WhatsAppSent { get; set; }

    /// <summary>Short human note for the audit trail, e.g. the streak length or the festival name.</summary>
    public string? Detail { get; set; }
}
