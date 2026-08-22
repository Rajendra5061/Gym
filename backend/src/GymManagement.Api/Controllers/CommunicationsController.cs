using GymManagement.Api.Authorization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using GymManagement.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace GymManagement.Api.Controllers;

/// <summary>
/// The member-messaging audit trail and its operator controls: what actually went out, which
/// channels are wired up, and the manual triggers for wishes and a WhatsApp test send.
/// </summary>
public sealed class CommunicationsController : ApiControllerBase
{
    private readonly ICommunicationLogService _logs;
    private readonly IMemberNotifier _notifier;
    private readonly IWishesDispatcher _wishes;
    private readonly IEmailSender _email;
    private readonly ISmsSender _sms;
    private readonly IWhatsAppSender _whatsApp;

    public CommunicationsController(
        ICommunicationLogService logs,
        IMemberNotifier notifier,
        IWishesDispatcher wishes,
        IEmailSender email,
        ISmsSender sms,
        IWhatsAppSender whatsApp)
    {
        _logs = logs;
        _notifier = notifier;
        _wishes = wishes;
        _email = email;
        _sms = sms;
        _whatsApp = whatsApp;
    }

    /// <summary>Returns a filtered, paged slice of the messaging audit trail.</summary>
    [HttpGet]
    [HasPermission(Permissions.NotificationsView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<CommunicationLogDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<CommunicationLogDto>>>> GetPaged(
        [FromQuery] CommunicationQueryDto query, CancellationToken ct) =>
        Success(await _logs.GetPagedAsync(query, ct));

    /// <summary>The most recent messages sent to one member, for the member detail screen.</summary>
    [HttpGet("member/{memberId:int}")]
    [HasPermission(Permissions.NotificationsView)]
    [ProducesResponseType(typeof(ApiResponse<List<CommunicationLogDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<CommunicationLogDto>>>> GetForMember(
        int memberId, [FromQuery] int take = 50, CancellationToken ct = default) =>
        Success(await _logs.GetForMemberAsync(memberId, take, ct));

    /// <summary>Send volumes for this calendar month and today, per channel.</summary>
    [HttpGet("usage")]
    [HasPermission(Permissions.NotificationsView)]
    [ProducesResponseType(typeof(ApiResponse<CommunicationUsageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CommunicationUsageDto>>> GetUsage(CancellationToken ct) =>
        Success(await _logs.GetUsageAsync(ct));

    /// <summary>
    /// Every outbound message stream side by side — occasions, renewal reminders, pay links and
    /// in-app notifications — so the operator can see the whole outbox in one call.
    /// </summary>
    [HttpGet("tracking")]
    [HasPermission(Permissions.NotificationsView)]
    [ProducesResponseType(typeof(ApiResponse<MessageTrackingDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<MessageTrackingDto>>> GetTracking(CancellationToken ct) =>
        Success(await _logs.GetTrackingAsync(ct));

    /// <summary>
    /// Which transports are wired up and which occasions each one carries, so the operator screen
    /// can explain a silent send instead of leaving the operator to guess at configuration.
    /// </summary>
    [HttpGet("channels")]
    [HasPermission(Permissions.NotificationsView)]
    [ProducesResponseType(typeof(ApiResponse<CommunicationChannelsDto>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<CommunicationChannelsDto>> GetChannels() =>
        Success(new CommunicationChannelsDto
        {
            Channels = new List<ChannelStatusDto>
            {
                new() { Channel = "email", Provider = _email.ProviderName, Enabled = _email.IsEnabled },
                new() { Channel = "sms", Provider = _sms.ProviderName, Enabled = _sms.IsEnabled },
                new() { Channel = "whatsapp", Provider = _whatsApp.ProviderName, Enabled = _whatsApp.IsEnabled }
            },
            Occasions = Enum.GetValues<MemberNotificationKind>()
                .Select(kind => new OccasionStatusDto
                {
                    Kind = kind.ToString(),
                    Email = _notifier.IsChannelEnabled(kind, email: true),
                    WhatsApp = _notifier.IsChannelEnabled(kind, email: false)
                })
                .ToList()
        });

    /// <summary>The festival wishes configured for the gym, ordered by date.</summary>
    [HttpGet("festivals")]
    [HasPermission(Permissions.NotificationsView)]
    [ProducesResponseType(typeof(ApiResponse<List<CommunicationFestivalDto>>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<List<CommunicationFestivalDto>>> GetFestivals() =>
        Success(_wishes.Festivals
            .Select(f => new CommunicationFestivalDto
            {
                Key = f.Key,
                Name = f.Name,
                Date = f.Date,
                Greeting = f.Greeting
            })
            .ToList());

    /// <summary>
    /// Sends today's birthday and festival wishes, exactly as the daily scheduler would.
    /// Idempotent per member per occasion — running it after the scheduled pass sends nothing.
    /// </summary>
    [HttpPost("send-wishes")]
    [HasPermission(Permissions.NotificationsManage)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<int>>> SendWishes(CancellationToken ct)
    {
        var count = await _wishes.SendTodaysWishesAsync(ct);
        return Success(count, count > 0
            ? $"Wishes sent to {count} member(s)."
            : "Nothing to send today — no birthdays or festivals, or everyone was already wished.");
    }

    /// <summary>
    /// Sends Meta's stock <c>hello_world</c> template to one phone number so the operator can prove
    /// the WhatsApp gateway end to end. A gateway failure comes back as a successful envelope with
    /// <c>sent: false</c> and the error text — a failed test is information, not a server fault.
    /// </summary>
    [HttpPost("test-whatsapp")]
    [HasPermission(Permissions.NotificationsManage)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<object>>> TestWhatsApp(
        [FromBody] TestWhatsAppRequestDto dto, CancellationToken ct)
    {
        var phone = dto?.Phone?.Trim();
        if (string.IsNullOrWhiteSpace(phone))
            throw new ValidationAppException(nameof(TestWhatsAppRequestDto.Phone), "A phone number is required.");

        // hello_world ships pre-approved on every fresh WhatsApp Cloud API account, so the test
        // works before the gym's own templates have been reviewed.
        var message = new WhatsAppMessage
        {
            To = phone,
            TemplateName = "hello_world",
            LanguageCode = "en_US",
            Parameters = Array.Empty<string>(),
            PreviewText = "Test message from the gym management system."
        };

        try
        {
            var result = await _whatsApp.SendAsync(message, ct);
            return Success<object>(
                new { sent = result.WasSent, provider = result.Provider, detail = result.Detail },
                result.WasSent
                    ? $"Test message handed to the {result.Provider} provider."
                    : result.Detail ?? "Nothing was sent — no WhatsApp provider is configured.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Success<object>(
                new { sent = false, provider = _whatsApp.ProviderName, detail = ex.Message },
                ex.Message);
        }
    }
}
