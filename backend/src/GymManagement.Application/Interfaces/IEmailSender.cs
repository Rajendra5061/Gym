namespace GymManagement.Application.Interfaces;

/// <summary>A single file carried by an outgoing message, held in memory.</summary>
public sealed class EmailAttachment
{
    public EmailAttachment(string fileName, string contentType, byte[] content)
    {
        FileName = string.IsNullOrWhiteSpace(fileName) ? "attachment" : fileName.Trim();
        ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim();
        Content = content ?? Array.Empty<byte>();
    }

    public string FileName { get; }
    public string ContentType { get; }
    public byte[] Content { get; }
}

/// <summary>
/// One outgoing message. Every message carries both an HTML body and a plain-text alternative so
/// that a client which refuses HTML still shows something readable.
/// </summary>
public sealed class EmailMessage
{
    /// <summary>Recipient addresses. A message with no recipient is skipped, never sent.</summary>
    public List<string> To { get; init; } = new();

    public string Subject { get; init; } = string.Empty;

    /// <summary>Self-contained HTML: inline styles only, no external images, scripts or web fonts.</summary>
    public string HtmlBody { get; init; } = string.Empty;

    /// <summary>The plain-text alternative. Must stand on its own, not be a stripped-tag afterthought.</summary>
    public string TextBody { get; init; } = string.Empty;

    public List<EmailAttachment> Attachments { get; init; } = new();
}

public enum EmailDeliveryStatus
{
    /// <summary>The message was handed to the configured provider.</summary>
    Sent = 0,

    /// <summary>Nothing was attempted: no provider is configured, or the message had no recipient.</summary>
    Skipped = 1
}

/// <summary>Outcome of one send attempt. A transport failure surfaces as an exception, not as a result.</summary>
public sealed record EmailDeliveryResult(EmailDeliveryStatus Status, string Provider, string? Detail = null)
{
    public bool WasSent => Status == EmailDeliveryStatus.Sent;

    public static EmailDeliveryResult Sent(string provider, string? detail = null) =>
        new(EmailDeliveryStatus.Sent, provider, detail);

    public static EmailDeliveryResult Skipped(string provider, string? detail = null) =>
        new(EmailDeliveryStatus.Skipped, provider, detail);
}

/// <summary>
/// Delivers an <see cref="EmailMessage"/>.
///
/// Configuration only: host, credentials and the from-address are read from appsettings,
/// environment variables or user secrets — never from the database. The gym settings table is
/// readable by any administrator over the API, so an SMTP password stored there would be handed
/// out by <c>GET /api/settings</c>.
///
/// Sending is off unless a provider has been configured, so a fresh checkout sends nothing and
/// throws nothing.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// False when no provider is configured. Callers may check this first to skip the cost of
    /// rendering a message that would only be discarded.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>Name of the active provider, for logging: <c>None</c>, <c>File</c> or <c>Smtp</c>.</summary>
    string ProviderName { get; }

    /// <summary>
    /// Hands the message to the provider. Throws only when the provider itself fails (bad host,
    /// refused credentials, unwritable folder); callers are expected to treat that as non-fatal.
    /// </summary>
    Task<EmailDeliveryResult> SendAsync(EmailMessage message, CancellationToken ct = default);

    /// <summary>
    /// The configuration this sender will actually use, for an operator screen to report.
    ///
    /// Defaulted to the two members every sender already has so that an implementation which has
    /// nothing more to say — a test double, for instance — needs no change. The senders built by
    /// the factory each override it with the snapshot they hold, which is the normalised copy and
    /// not the raw section: a file sink that substituted its own envelope sender reports the
    /// address it substituted, not the unusable one that was configured.
    ///
    /// No member of the returned type carries a password, and none ever should — this exists to be
    /// returned over the API.
    /// </summary>
    EmailSenderDescription Describe() => new() { Provider = ProviderName, IsEnabled = IsEnabled };
}

/// <summary>
/// What a sender reports about itself: the provider in force and the settings it resolved.
///
/// Deliberately not <c>EmailOptions</c>, which holds the SMTP password. This type has no password
/// member and never will; <see cref="SmtpCredentialsConfigured"/> says only whether one was found.
/// </summary>
public sealed record EmailSenderDescription
{
    /// <summary><c>None</c>, <c>File</c> or <c>Smtp</c>.</summary>
    public string Provider { get; init; } = "None";

    /// <summary>False for the null sender: every message is accepted and discarded.</summary>
    public bool IsEnabled { get; init; }

    public string? FromAddress { get; init; }
    public string? FromName { get; init; }
    public string? ReplyToAddress { get; init; }

    /// <summary>SMTP members are populated by the SMTP sender only; the others leave them null.</summary>
    public string? SmtpHost { get; init; }
    public int? SmtpPort { get; init; }
    public bool? SmtpUseStartTls { get; init; }
    public string? SmtpUserName { get; init; }

    /// <summary>Whether a password resolved from configuration. Never the value.</summary>
    public bool SmtpCredentialsConfigured { get; init; }

    /// <summary>Absolute path of the mail-drop folder, for the file sink only.</summary>
    public string? FileSinkDirectory { get; init; }

    /// <summary>Why sending is off, when the sender knows. Null when it is on.</summary>
    public string? Reason { get; init; }
}
