using GymManagement.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Messaging;

/// <summary>
/// The default sender: accepts every message and discards it.
///
/// This is what a fresh checkout gets outside Development, and what a misconfigured provider falls
/// back to. It never throws, so no feature can be broken by mail simply not being set up.
/// </summary>
public sealed class NullEmailSender : IEmailSender
{
    private readonly ILogger<NullEmailSender> _logger;
    private readonly string? _reason;

    public NullEmailSender(ILogger<NullEmailSender> logger, string? reason = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _reason = reason;
    }

    public bool IsEnabled => false;

    public string ProviderName => "None";

    public EmailSenderDescription Describe() => new()
    {
        Provider = ProviderName,
        IsEnabled = IsEnabled,
        Reason = _reason ?? "No mail provider is configured."
    };

    public Task<EmailDeliveryResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var detail = _reason ?? "No mail provider is configured.";

        _logger.LogDebug("Email discarded ({Subject}): {Reason}", message?.Subject, detail);

        return Task.FromResult(EmailDeliveryResult.Skipped(ProviderName, detail));
    }
}
