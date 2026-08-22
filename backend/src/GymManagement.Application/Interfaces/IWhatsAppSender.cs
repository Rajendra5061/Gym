namespace GymManagement.Application.Interfaces;

/// <summary>
/// One outgoing WhatsApp message.
///
/// Deliberately not an <see cref="SmsMessage"/> with a longer body. WhatsApp Business messaging
/// outside the 24-hour customer-service window may only send a <b>pre-approved template</b>: the
/// gateway is given a template name, a language and the ordered values that fill its placeholders,
/// and it renders the text itself. A free-text body would be accepted by the API and then dropped
/// on the floor, which is the worst possible failure — a send that reports success and never
/// arrives. So the template is the message here, and the rendered wording is only a preview.
/// </summary>
public sealed class WhatsAppMessage
{
    /// <summary>Recipient number as the gym recorded it; normalised to E.164 before sending.</summary>
    public string To { get; init; } = string.Empty;

    /// <summary>
    /// Name of the template approved in the WhatsApp Business account, e.g. <c>payment_received</c>.
    /// A gym that has not yet had its templates approved leaves the mapping empty and nothing is sent.
    /// </summary>
    public string TemplateName { get; init; } = string.Empty;

    /// <summary>Template language, e.g. <c>en</c> or <c>en_US</c>. Must match the approved template.</summary>
    public string LanguageCode { get; init; } = "en";

    /// <summary>
    /// The ordered body placeholders — <c>{{1}}</c>, <c>{{2}}</c>, … — in template order. WhatsApp
    /// rejects a parameter containing a newline or four consecutive spaces, so values are collapsed
    /// to a single line before they are sent.
    /// </summary>
    public IReadOnlyList<string> Parameters { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The same message rendered as plain text. This is what the file sink writes and what a
    /// non-template gateway receives through the <c>{text}</c> placeholder, so it must read as a
    /// complete message on its own — not as a debugging dump of the parameters.
    /// </summary>
    public string PreviewText { get; init; } = string.Empty;
}

public enum WhatsAppDeliveryStatus
{
    /// <summary>The message was handed to the configured provider.</summary>
    Sent = 0,

    /// <summary>Nothing was attempted: no provider configured, no recipient, or no template mapped.</summary>
    Skipped = 1,
}

/// <summary>Outcome of one send attempt. A transport failure surfaces as an exception, not a result.</summary>
public sealed record WhatsAppDeliveryResult(WhatsAppDeliveryStatus Status, string Provider, string? Detail = null)
{
    public bool WasSent => Status == WhatsAppDeliveryStatus.Sent;

    public static WhatsAppDeliveryResult Sent(string provider, string? detail = null) =>
        new(WhatsAppDeliveryStatus.Sent, provider, detail);

    public static WhatsAppDeliveryResult Skipped(string provider, string? detail = null) =>
        new(WhatsAppDeliveryStatus.Skipped, provider, detail);
}

/// <summary>
/// Delivers a <see cref="WhatsAppMessage"/>. The contract mirrors <see cref="IEmailSender"/> and
/// <see cref="ISmsSender"/> exactly, and for the same reasons:
///
/// Configuration only — gateway URL, phone-number id and access token come from appsettings,
/// environment variables or user secrets, never from the database, whose settings table any
/// administrator can read back over the API.
///
/// Sending is off unless a provider has been configured, so a fresh checkout messages nobody and
/// throws nothing.
/// </summary>
public interface IWhatsAppSender
{
    /// <summary>False when no provider is configured; callers may skip building the message.</summary>
    bool IsEnabled { get; }

    /// <summary>Name of the active provider, for logging: <c>None</c>, <c>File</c> or <c>Http</c>.</summary>
    string ProviderName { get; }

    /// <summary>
    /// Hands the message to the provider. Throws only when the provider itself fails (gateway
    /// unreachable, token refused); callers are expected to treat that as non-fatal.
    /// </summary>
    Task<WhatsAppDeliveryResult> SendAsync(WhatsAppMessage message, CancellationToken ct = default);
}
