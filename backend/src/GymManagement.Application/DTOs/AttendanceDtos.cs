using GymManagement.Application.Common;
using GymManagement.Domain.Enums;

namespace GymManagement.Application.DTOs;

public class AttendanceDto
{
    public int Id { get; set; }
    public int MemberId { get; set; }
    public string MemberCode { get; set; } = string.Empty;
    public string MemberName { get; set; } = string.Empty;
    public string? ProfilePhotoPath { get; set; }
    public DateTime AttendanceDate { get; set; }
    public DateTime CheckInTime { get; set; }
    public DateTime? CheckOutTime { get; set; }
    public AttendanceStatus Status { get; set; }
    public string StatusText => Status == AttendanceStatus.CheckedIn ? "Checked In" : "Checked Out";
    public int? DurationMinutes { get; set; }
    public string CheckInMethod { get; set; } = "Manual";
    public int? SubscriptionId { get; set; }
    public string? PlanName { get; set; }
    public string? Notes { get; set; }
}

public class CheckInRequestDto
{
    /// <summary>Either the member id or the member code must be supplied.</summary>
    public int? MemberId { get; set; }
    public string? MemberCode { get; set; }
    public string CheckInMethod { get; set; } = "Manual";
    public string? Notes { get; set; }
    /// <summary>Admin override that allows a member inside despite an expired subscription.</summary>
    public bool OverrideExpiredMembership { get; set; }
}

public class CheckOutRequestDto
{
    public int? AttendanceId { get; set; }
    public int? MemberId { get; set; }
    public string? Notes { get; set; }
}

public class AttendanceQueryDto : PagedRequest
{
    public int? MemberId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public AttendanceStatus? Status { get; set; }
    public string? CheckInMethod { get; set; }
    /// <summary>Shortcut for FromDate = ToDate = today.</summary>
    public bool OnlyToday { get; set; }
}

/// <summary>Counters shown on the attendance screen header.</summary>
public class AttendanceSummaryDto
{
    public DateTime Date { get; set; }
    public int TotalCheckIns { get; set; }
    public int CurrentlyInGym { get; set; }
    public int CheckedOut { get; set; }
    public double AverageDurationMinutes { get; set; }
    public int PeakHour { get; set; }
    public List<HourlyAttendanceDto> HourlyBreakdown { get; set; } = new();
}

public class HourlyAttendanceDto
{
    public int Hour { get; set; }
    public string HourLabel => $"{Hour:00}:00";
    public int Count { get; set; }
}
