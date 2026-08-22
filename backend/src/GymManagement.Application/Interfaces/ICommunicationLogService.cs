using GymManagement.Application.Common;
using GymManagement.Application.DTOs;

namespace GymManagement.Application.Interfaces;

/// <summary>
/// Read side of the member-messaging audit trail. The send path writes
/// <c>MemberNotificationLog</c> rows to stay idempotent; this service is how an operator reads
/// them back — "what went out, to whom, over which channel?" — without ever touching a sender.
/// </summary>
public interface ICommunicationLogService
{
    /// <summary>Returns a filtered, paged slice of the messaging audit trail.</summary>
    Task<PagedResult<CommunicationLogDto>> GetPagedAsync(CommunicationQueryDto query, CancellationToken ct = default);

    /// <summary>The most recent messages sent to one member, newest first.</summary>
    Task<List<CommunicationLogDto>> GetForMemberAsync(int memberId, int take = 50, CancellationToken ct = default);

    /// <summary>Send volumes for this calendar month and today, per channel.</summary>
    Task<CommunicationUsageDto> GetUsageAsync(CancellationToken ct = default);

    /// <summary>Every outbound message stream side by side: occasions, renewal reminders, pay links and in-app notifications.</summary>
    Task<MessageTrackingDto> GetTrackingAsync(CancellationToken ct = default);
}
