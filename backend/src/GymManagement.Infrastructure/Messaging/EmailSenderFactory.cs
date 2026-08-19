using GymManagement.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Messaging;

/// <summary>
/// Picks the <see cref="IEmailSender"/> implementation from configuration.
///
/// Two rules drive every decision here:
/// <list type="number">
///   <item>
///     Sending is off unless it has been turned on. With no <c>Email</c> section at all, a
///     Development machine gets the local file sink and everything else gets the null sender — so
///     a fresh checkout sends nothing to anybody.
///   </item>
///   <item>
///     Misconfiguration degrades, it does not crash. A provider that cannot work is logged as an
///     error and replaced by the null sender; the host still starts and payments still complete.
///   </item>
/// </list>
/// </summary>
public static class EmailSenderFactory
{
    /// <summary>Stand-in sender for the local file sink. <c>.invalid</c> can never resolve (RFC 2606).</summary>
    private const string FileSinkFromAddress = "no-reply@gym.invalid";

    /// <summary>
    /// Reads the options through <paramref name="optionsAccessor"/>, then builds the sender.
    ///
    /// Binding the <c>Email</c> section is itself something that can fail — a non-numeric
    /// <c>Email:Smtp:Port</c> makes the configuration binder throw — and the accessor is where that
    /// happens. Left uncaught the throw surfaces in whatever first resolves
    /// <see cref="IEmailSender"/>, which is the payments module, so one mistyped mail setting
    /// answered every <c>POST /api/payments</c> <i>and</i> every <c>GET /api/payments</c> with a
    /// 500: money could neither be collected nor read back. A section that cannot be read is
    /// therefore treated as exactly what it is — a misconfiguration — and handled the same way as
    /// every other one here: logged as an error, replaced by the null sender, mail off, money
    /// moving.
    ///
    /// The exception is logged in full. Only members that need type conversion can fail to bind,
    /// and every secret in this section (<c>Smtp:Password</c> above all) is a string, which never
    /// does — so no credential can reach the log this way. Keep it that way.
    ///
    /// Deliberately not an overload of <see cref="Create(EmailOptions?, ILoggerFactory, bool)"/>:
    /// a caller passing a bare <c>null</c> would not compile against the two together.
    /// </summary>
    public static IEmailSender CreateFromOptionsAccessor(
        Func<EmailOptions?> optionsAccessor, ILoggerFactory loggerFactory, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(optionsAccessor);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        EmailOptions? options;
        try
        {
            options = optionsAccessor();
        }
        catch (Exception ex)
        {
            var startup = loggerFactory.CreateLogger(typeof(EmailSenderFactory).FullName!);

            startup.LogError(ex,
                "The Email configuration section could not be read, so email sending is disabled. " +
                "Correct the values under 'Email'; nothing else in the application is affected.");

            return new NullEmailSender(loggerFactory.CreateLogger<NullEmailSender>(),
                "The Email configuration section is invalid and could not be read.");
        }

        return Create(options, loggerFactory, isDevelopment);
    }

    public static IEmailSender Create(EmailOptions? options, ILoggerFactory loggerFactory, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var settings = Normalise(options);
        var startup = loggerFactory.CreateLogger(typeof(EmailSenderFactory).FullName!);

        var kind = ResolveKind(settings.Provider, isDevelopment, startup);

        switch (kind)
        {
            case EmailProviderKind.File:
            {
                // Nothing leaves the machine, so an envelope sender is supplied rather than demanded.
                if (!MailMessageFactory.TryParseAddress(settings.FromAddress, null, out _))
                    settings.FromAddress = FileSinkFromAddress;

                var sender = new FileDropEmailSender(settings, loggerFactory.CreateLogger<FileDropEmailSender>());

                startup.LogInformation(
                    "Email provider: local file sink. Messages are written to {Directory} and are never sent.",
                    sender.Directory);

                return sender;
            }

            case EmailProviderKind.Smtp:
            {
                if (string.IsNullOrWhiteSpace(settings.Smtp?.Host))
                    return Disabled(loggerFactory, startup, "Email:Provider is Smtp but Email:Smtp:Host is not set.");

                if (!MailMessageFactory.TryParseAddress(settings.FromAddress, null, out _))
                    return Disabled(loggerFactory, startup,
                        "Email:Provider is Smtp but Email:FromAddress is missing or not a valid address.");

                startup.LogInformation("Email provider: SMTP via {Host}:{Port} (TLS {Tls}).",
                    settings.Smtp!.Host, settings.Smtp.Port, settings.Smtp.UseStartTls ? "on" : "off");

                return new SmtpEmailSender(settings, loggerFactory.CreateLogger<SmtpEmailSender>());
            }

            default:
                startup.LogInformation(
                    "Email provider: none. No message will be sent; set Email:Provider to File or Smtp to enable it.");

                return new NullEmailSender(loggerFactory.CreateLogger<NullEmailSender>(),
                    "No mail provider is configured (Email:Provider is None).");
        }
    }

    private static IEmailSender Disabled(ILoggerFactory loggerFactory, ILogger startup, string reason)
    {
        // An error, because somebody asked for delivery and will not get it — but not an exception,
        // because a mail misconfiguration must not stop the API from starting.
        startup.LogError("Email sending is disabled: {Reason}", reason);

        return new NullEmailSender(loggerFactory.CreateLogger<NullEmailSender>(), reason);
    }

    private static EmailProviderKind ResolveKind(string? provider, bool isDevelopment, ILogger startup)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return isDevelopment ? EmailProviderKind.File : EmailProviderKind.None;

        if (Enum.TryParse<EmailProviderKind>(provider.Trim(), ignoreCase: true, out var parsed))
            return parsed;

        startup.LogError(
            "Email:Provider '{Provider}' is not recognised. Expected None, File or Smtp; sending stays off.",
            provider);

        return EmailProviderKind.None;
    }

    /// <summary>Defensive copy so the senders hold a snapshot nothing else can mutate.</summary>
    private static EmailOptions Normalise(EmailOptions? options)
    {
        var source = options ?? new EmailOptions();

        return new EmailOptions
        {
            Provider = source.Provider,
            FromAddress = source.FromAddress,
            FromName = source.FromName,
            ReplyToAddress = source.ReplyToAddress,
            FileSink = new EmailFileSinkOptions
            {
                Directory = string.IsNullOrWhiteSpace(source.FileSink?.Directory)
                    ? "logs/mail-drop"
                    : source.FileSink!.Directory
            },
            Smtp = new EmailSmtpOptions
            {
                Host = source.Smtp?.Host,
                Port = source.Smtp?.Port is > 0 and <= 65535 ? source.Smtp.Port : 587,
                UseStartTls = source.Smtp?.UseStartTls ?? true,
                UserName = source.Smtp?.UserName,
                Password = source.Smtp?.Password,
                TimeoutSeconds = source.Smtp?.TimeoutSeconds ?? 30
            }
        };
    }
}
