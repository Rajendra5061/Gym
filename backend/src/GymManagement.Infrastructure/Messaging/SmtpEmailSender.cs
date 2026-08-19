using System.Net;
using System.Net.Mail;
using GymManagement.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Messaging;

/// <summary>
/// Real delivery over SMTP.
///
/// Host, port, user name and password come from configuration only — never from the gym settings
/// table, which any administrator can read back over the API. The password is never logged, never
/// echoed in an exception message raised here, and never returned to a caller.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(EmailOptions options, ILogger<SmtpEmailSender> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsEnabled => true;

    public string ProviderName => "Smtp";

    public EmailSenderDescription Describe()
    {
        var smtp = _options.Smtp ?? new EmailSmtpOptions();

        return new EmailSenderDescription
        {
            Provider = ProviderName,
            IsEnabled = IsEnabled,
            FromAddress = _options.FromAddress,
            FromName = _options.FromName,
            ReplyToAddress = _options.ReplyToAddress,
            SmtpHost = smtp.Host,
            SmtpPort = smtp.Port,
            SmtpUseStartTls = smtp.UseStartTls,
            SmtpUserName = smtp.UserName,
            // Whether one resolved — never the value, which does not leave this class.
            SmtpCredentialsConfigured = !string.IsNullOrWhiteSpace(smtp.Password)
        };
    }

    public async Task<EmailDeliveryResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var mail = MailMessageFactory.TryBuild(message, _options, out var reason);
        if (mail is null)
        {
            _logger.LogDebug("Email not sent over SMTP: {Reason}", reason);
            return EmailDeliveryResult.Skipped(ProviderName, reason);
        }

        var smtp = _options.Smtp ?? new EmailSmtpOptions();

        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            DeliveryMethod = SmtpDeliveryMethod.Network,
            EnableSsl = smtp.UseStartTls,
            Timeout = Math.Clamp(smtp.TimeoutSeconds, 1, 300) * 1000
        };

        if (!string.IsNullOrWhiteSpace(smtp.UserName))
        {
            client.UseDefaultCredentials = false;
            client.Credentials = new NetworkCredential(smtp.UserName, smtp.Password ?? string.Empty);
        }

        await client.SendMailAsync(mail, ct).ConfigureAwait(false);

        // Host and port only. Credentials are never written to a log.
        _logger.LogInformation("Email '{Subject}' sent to {Recipient} via {Host}:{Port}.",
            mail.Subject, MailMessageFactory.Mask(mail.To.FirstOrDefault()?.Address),
            smtp.Host, smtp.Port);

        return EmailDeliveryResult.Sent(ProviderName, $"{smtp.Host}:{smtp.Port}");
    }
}
