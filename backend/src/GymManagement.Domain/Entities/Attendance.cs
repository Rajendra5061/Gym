using GymManagement.Domain.Common;
using GymManagement.Domain.Enums;

namespace GymManagement.Domain.Entities;

public class Attendance : AuditableEntity
{
    public int MemberId { get; set; }
    public Member? Member { get; set; }

    /// <summary>Local calendar date of the visit. Used for the duplicate check-in rule.</summary>
    public DateTime AttendanceDate { get; set; } = DateTime.Today;
    public DateTime CheckInTime { get; set; } = DateTime.Now;
    public DateTime? CheckOutTime { get; set; }
    public AttendanceStatus Status { get; set; } = AttendanceStatus.CheckedIn;

    /// <summary>Minutes spent in the gym; filled in on check-out.</summary>
    public int? DurationMinutes { get; set; }

    /// <summary>How the visit was recorded: Manual, QrCode, Rfid, Biometric.</summary>
    public string CheckInMethod { get; set; } = "Manual";

    public int? SubscriptionId { get; set; }
    public Subscription? Subscription { get; set; }
    public string? Notes { get; set; }
}
