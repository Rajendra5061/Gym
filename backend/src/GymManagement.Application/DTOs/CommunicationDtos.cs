using GymManagement.Application.Common;

namespace GymManagement.Application.DTOs;

/// <summary>
/// One row of the member-messaging audit trail: what was sent to whom, when, and over which
/// channels. <see cref="Kind"/> travels as text so the screen never has to know the enum values.
/// </summary>
public class CommunicationLogDto
{
    public int Id { get; set; }
    public int MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public string MemberCode { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public DateTime SentOnDate { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool EmailSent { get; set; }
    public bool WhatsAppSent { get; set; }
}

public class CommunicationQueryDto : PagedRequest
{
    /// <summary>A <c>MemberNotificationKind</c> name, matched case-insensitively.</summary>
    public string? Kind { get; set; }

    public int? MemberId { get; set; }

    /// <summary>Restricts to one channel: <c>email</c> or <c>whatsapp</c>.</summary>
    public string? Channel { get; set; }

    /// <summary>Inclusive lower bound on the local send date.</summary>
    public DateTime? From { get; set; }

    /// <summary>Inclusive upper bound on the local send date.</summary>
    public DateTime? To { get; set; }
}

/// <summary>One transport and whether it is actually wired up, for the operator screen.</summary>
public class ChannelStatusDto
{
    public string Channel { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

/// <summary>Which channels one occasion goes out on, so the screen can explain a silent send.</summary>
public class OccasionStatusDto
{
    public string Kind { get; set; } = string.Empty;
    public bool Email { get; set; }
    public bool WhatsApp { get; set; }
}

public class CommunicationChannelsDto
{
    public List<ChannelStatusDto> Channels { get; set; } = new();
    public List<OccasionStatusDto> Occasions { get; set; } = new();
}

/// <summary>
/// Send volumes for the operator dashboard: this calendar month and today, per channel, plus how
/// many distinct members were actually reached this month.
/// </summary>
public class CommunicationUsageDto
{
    public int MonthEmails { get; set; }
    public int MonthWhatsApp { get; set; }
    public int TodayEmails { get; set; }
    public int TodayWhatsApp { get; set; }
    public int MonthMembersReached { get; set; }
}

public class TestWhatsAppRequestDto
{
    public string Phone { get; set; } = string.Empty;
}

/// <summary>
/// One outbound message stream and its volume: today, this calendar month, and the month's count
/// per channel. A channel a stream never uses stays 0 rather than null, so the grid needs no
/// special cases.
/// </summary>
public class TrackingStreamDto
{
    public string Stream { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Today { get; set; }
    public int ThisMonth { get; set; }
    public int Email { get; set; }
    public int Sms { get; set; }
    public int WhatsApp { get; set; }
    public int InApp { get; set; }
}

/// <summary>Every outbound message stream side by side, for the "what are we sending?" screen.</summary>
public class MessageTrackingDto
{
    public List<TrackingStreamDto> Streams { get; set; } = new();
    public int TodayTotal { get; set; }
    public int MonthTotal { get; set; }
}

/// <summary>A configured festival wish, mirrored from <c>IWishesDispatcher.Festivals</c>.</summary>
public class CommunicationFestivalDto
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string? Greeting { get; set; }
}
