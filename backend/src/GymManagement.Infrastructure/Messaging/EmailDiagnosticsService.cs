using System.Net;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Messaging;

/// <summary>
/// Answers the two questions the Settings → Email screen exists to ask: what is mail actually set
/// to do on this server, and where does a message go if I send one right now.
///
/// Both answers come from the live <see cref="IEmailSender"/> singleton — the one every other
/// module holds — rather than from the configuration section. A section that says
/// <c>Provider: Smtp</c> but names no host produced the null sender at start-up, and the screen has
/// to report the sender that exists, not the intent that failed.
///
/// Nothing here can change how mail is configured. None of it lives in the database and none of it
/// can: the settings table is readable over the API, so an SMTP password there would be handed
/// straight out. Changing a provider means editing the server's Email section and restarting.
/// </summary>
public sealed class EmailDiagnosticsService : IEmailDiagnosticsService
{
    private readonly IEmailSender _email;
    private readonly ISettingsService _settings;
    private readonly ILogger<EmailDiagnosticsService> _logger;

    public EmailDiagnosticsService(
        IEmailSender email,
        ISettingsService settings,
        ILogger<EmailDiagnosticsService> logger)
    {
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public EmailStatusDto GetStatus()
    {
        var description = _email.Describe();

        return new EmailStatusDto
        {
            Provider = description.Provider,
            IsEnabled = description.IsEnabled,
            FromAddress = description.FromAddress,
            FromName = description.FromName,
            ReplyToAddress = description.ReplyToAddress,
            SmtpHost = description.SmtpHost,
            SmtpPort = description.SmtpPort,
            SmtpUseStartTls = description.SmtpUseStartTls,
            SmtpUserName = description.SmtpUserName,
            SmtpCredentialsConfigured = description.SmtpCredentialsConfigured,
            FileSinkDirectory = description.FileSinkDirectory,
            Reason = description.Reason
        };
    }

    public async Task<EmailTestResultDto> SendTestAsync(SendTestEmailDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var to = dto.ToAddress?.Trim() ?? string.Empty;

        // Checked here as well as in the validator: an address the mail layer cannot parse is a bad
        // request, not a delivery outcome, and saying so is more useful than "nothing was sent".
        if (!ValidationRules.IsValidEmail(to))
            throw new ValidationAppException(nameof(SendTestEmailDto.ToAddress),
                "Enter a valid email address to send the test to.");

        var gymName = await ResolveGymNameAsync(ct).ConfigureAwait(false);
        var message = BuildMessage(to, gymName, _email.ProviderName);

        try
        {
            var result = await _email.SendAsync(message, ct).ConfigureAwait(false);

            _logger.LogInformation("Test email for {Recipient} {Outcome} via {Provider}.",
                MailMessageFactory.Mask(to), result.WasSent ? "accepted" : "skipped", result.Provider);

            return new EmailTestResultDto
            {
                Sent = result.WasSent,
                Provider = result.Provider,
                ToAddress = to,
                Destination = result.WasSent ? result.Detail : null,
                Reason = result.WasSent ? null : result.Detail
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A relay that refuses the connection, credentials the server rejects, a drop folder
            // that cannot be written: every one of these is the answer the operator pressed the
            // button to get, so it comes back described rather than as a 500. The provider's own
            // words are what makes it diagnosable, and they can carry no credential — nothing in
            // this layer ever puts the password into an exception.
            _logger.LogWarning(ex, "The test email to {Recipient} could not be sent via {Provider}.",
                MailMessageFactory.Mask(to), _email.ProviderName);

            return new EmailTestResultDto
            {
                Sent = false,
                Provider = _email.ProviderName,
                ToAddress = to,
                Reason = $"The {_email.ProviderName} provider rejected the message: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// The gym name, or a neutral stand-in. A settings row that cannot be read must not turn a
    /// diagnostic into an error — the point of the button is to reach the mail provider.
    /// </summary>
    private async Task<string> ResolveGymNameAsync(CancellationToken ct)
    {
        try
        {
            var branding = await _settings.GetGymBrandingAsync(ct).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(branding.GymName) ? "Gym" : branding.GymName.Trim();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "The gym name could not be read for the test email; using a default.");
            return "Gym";
        }
    }

    /// <summary>
    /// The message itself: fixed content, with nothing caller-supplied in either body. The only
    /// thing the caller chooses is the recipient, so this endpoint cannot be used to send somebody
    /// else's words from the gym's address.
    /// </summary>
    private static EmailMessage BuildMessage(string to, string gymName, string provider)
    {
        var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'");

        var where = provider.Equals("File", StringComparison.OrdinalIgnoreCase)
            ? "It was written to this server's local mail drop rather than transmitted, so the file itself is the confirmation. Nothing left the machine, and nothing reached a real inbox."
            : "It travelled through the provider this server is configured with, so a receipt the application sends can reach a real inbox the same way.";

        var html =
            "<div style=\"font-family:Segoe UI,Helvetica,Arial,sans-serif;font-size:15px;line-height:1.55;" +
            "color:#1f2933;max-width:560px\">" +
            $"<p style=\"margin:0 0 14px;font-size:17px;font-weight:600\">Mail is working at {E(gymName)}.</p>" +
            "<p style=\"margin:0 0 14px\">This is a test message sent from the Settings screen. It carries no " +
            "data, and it is not a receipt.</p>" +
            $"<p style=\"margin:0 0 14px\">{E(where)}</p>" +
            $"<p style=\"margin:0;color:#6b7280;font-size:13px\">Provider: {E(provider)} &middot; {E(stamp)}</p>" +
            "</div>";

        var text = string.Join(Environment.NewLine, new[]
        {
            $"Mail is working at {gymName}.",
            string.Empty,
            "This is a test message sent from the Settings screen. It carries no data, and it is not",
            "a receipt.",
            string.Empty,
            where,
            string.Empty,
            $"Provider: {provider}",
            stamp
        });

        return new EmailMessage
        {
            To = { to },
            Subject = $"Test message from {gymName}",
            HtmlBody = html,
            TextBody = text
        };
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
