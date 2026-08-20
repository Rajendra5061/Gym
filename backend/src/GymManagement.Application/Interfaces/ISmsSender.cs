namespace GymManagement.Application.Interfaces;

/// <summary>One outgoing text message: a phone number and a short body, nothing else.</summary>
public sealed class SmsMessage
{
    /// <summary>Recipient number as the gym recorded it. A message with no recipient is skipped.</summary>
    public string To { get; init; } = string.Empty;

    /// <summary>
    /// The text. Callers keep it near one SMS segment (~160 GSM characters) — gateways split and
    /// bill long bodies per segment, and a renewal nudge has no business being an essay.
    /// </summary>
    public string Text { get; init; } = string.Empty;
}

public enum SmsDeliveryStatus
{
    /// <summary>The message was handed to the configured provider.</summary>
    Sent = 0,

    /// <summary>Nothing was attempted: no provider is configured, or the message had no recipient.</summary>
    Skipped = 1,
}

/// <summary>Outcome of one send attempt. A transport failure surfaces as an exception, not a result.</summary>
public sealed record SmsDeliveryResult(SmsDeliveryStatus Status, string Provider, string? Detail = null)
{
    public bool WasSent => Status == SmsDeliveryStatus.Sent;

    public static SmsDeliveryResult Sent(string provider, string? detail = null) =>
        new(SmsDeliveryStatus.Sent, provider, detail);

    public static SmsDeliveryResult Skipped(string provider, string? detail = null) =>
        new(SmsDeliveryStatus.Skipped, provider, detail);
}

/// <summary>
/// Delivers an <see cref="SmsMessage"/>. The contract mirrors <see cref="IEmailSender"/> exactly,
/// and for the same reasons:
///
/// Configuration only — gateway URL, API key and sender id come from appsettings, environment
/// variables or user secrets, never from the database, whose settings table any administrator can
/// read back over the API.
///
/// Sending is off unless a provider has been configured, so a fresh checkout texts nobody and
/// throws nothing.
/// </summary>
public interface ISmsSender
{
    /// <summary>False when no provider is configured; callers may skip building the message.</summary>
    bool IsEnabled { get; }

    /// <summary>Name of the active provider, for logging: <c>None</c>, <c>File</c> or <c>Http</c>.</summary>
    string ProviderName { get; }

    /// <summary>
    /// Hands the message to the provider. Throws only when the provider itself fails (gateway
    /// unreachable, credentials refused); callers are expected to treat that as non-fatal.
    /// </summary>
    Task<SmsDeliveryResult> SendAsync(SmsMessage message, CancellationToken ct = default);
}
