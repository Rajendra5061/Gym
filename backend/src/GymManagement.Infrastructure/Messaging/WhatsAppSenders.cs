using System.Globalization;
using System.Text;
using GymManagement.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Messaging;

/// <summary>
/// Configuration for outbound WhatsApp messages, bound from the <c>WhatsApp</c> section. The shape
/// follows <see cref="EmailOptions"/> and <see cref="SmsOptions"/>: provider selection plus
/// per-provider settings, credentials from configuration only — never the database.
/// </summary>
public sealed class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";

    /// <summary>
    /// <c>None</c>, <c>File</c> or <c>Http</c>. Left unset it resolves to <c>File</c> in
    /// Development and <c>None</c> everywhere else, so messaging is off until somebody turns it on.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Calling code prefixed to a number stored without one, e.g. <c>+91</c>. Members are usually
    /// entered as a bare ten-digit number; WhatsApp will not accept anything but E.164.
    /// </summary>
    public string? DefaultCountryCode { get; set; }

    /// <summary>Language of the approved templates, e.g. <c>en</c> or <c>en_US</c>.</summary>
    public string? LanguageCode { get; set; }

    /// <summary>
    /// Approved template name per <c>MemberNotificationKind</c>, e.g.
    /// <c>WhatsApp:Templates:Birthday</c>. An occasion with no entry falls back to the built-in
    /// default name; an occasion mapped to an empty string is never sent on WhatsApp, which is how
    /// a gym switches one occasion off while its template is still awaiting approval.
    /// </summary>
    public Dictionary<string, string> Templates { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public WhatsAppFileSinkOptions? FileSink { get; set; }
    public WhatsAppHttpOptions? Http { get; set; }
}

public sealed class WhatsAppFileSinkOptions
{
    /// <summary>Folder the file sink writes to. Defaults to <c>logs/whatsapp-drop</c>.</summary>
    public string? Directory { get; set; }
}

/// <summary>
/// A generic HTTP gateway adapter rather than one vendor's SDK, for the same reason the SMS side
/// has one: Meta's WhatsApp Cloud API, Twilio, MSG91 and Gupshup are all "send an HTTP request
/// carrying the number, the template and its parameters", so one template covers whichever the gym
/// signs up with. Meta's Cloud API is the free option and is what the shipped defaults describe.
///
/// Placeholders replaced in <see cref="Url"/> and <see cref="BodyTemplate"/> before sending:
/// <list type="bullet">
///   <item><c>{to}</c> — recipient in E.164 with the leading plus, e.g. <c>+919876543210</c>.</item>
///   <item><c>{to_digits}</c> — the same number without the plus, which is what Meta documents.</item>
///   <item><c>{template}</c> — the approved template name.</item>
///   <item><c>{lang}</c> — the template language code.</item>
///   <item><c>{params}</c> — the body parameters as Cloud API objects, comma separated and ready
///   to drop straight inside a <c>"parameters": [ ... ]</c> array.</item>
///   <item><c>{params_text}</c> — the same values as a plain JSON string array, for gateways that
///   take a bare list.</item>
///   <item><c>{text}</c> — the whole message rendered as plain text, for a gateway that sends free
///   text rather than templates.</item>
/// </list>
/// </summary>
public sealed class WhatsAppHttpOptions
{
    public string? Url { get; set; }

    /// <summary><c>POST</c> (default) or <c>GET</c>.</summary>
    public string? Method { get; set; }

    /// <summary><c>application/json</c> (default) or <c>application/x-www-form-urlencoded</c>.</summary>
    public string? ContentType { get; set; }

    /// <summary>Request body with the placeholders above. Ignored for GET.</summary>
    public string? BodyTemplate { get; set; }

    /// <summary>Headers sent verbatim, e.g. <c>Authorization</c>. The values are the credentials —
    /// supply them via <c>WhatsApp__Http__Headers__Authorization</c> environment variables or user
    /// secrets, and never commit them.</summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    public int TimeoutSeconds { get; set; } = 20;
}

/// <summary>
/// Turns a stored phone number into E.164, which is the only form WhatsApp accepts.
///
/// Members are entered however the front desk typed them — <c>98765 43210</c>, <c>+91-98765-43210</c>,
/// <c>0091 98765 43210</c> — and a malformed number must be skipped rather than sent, because a
/// gateway that rejects one number can fail the whole request.
/// </summary>
public static class PhoneNumberNormalizer
{
    /// <summary>Below this, it is a extension or a typo, not a mobile number.</summary>
    private const int MinimumDigits = 8;

    /// <summary>E.164 caps the whole number, country code included, at fifteen digits.</summary>
    private const int MaximumDigits = 15;

    /// <summary>
    /// At or below this, a number is read as a bare national one and the default calling code is
    /// prefixed. Ten covers India, and every shorter national format with it.
    /// </summary>
    private const int NationalNumberDigits = 10;

    /// <summary>
    /// Returns the number as <c>+&lt;digits&gt;</c>, or null when it cannot be made sense of.
    /// </summary>
    public static string? ToE164(string? raw, string? defaultCountryCode)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var trimmed = raw.Trim();
        var hadPlus = trimmed.StartsWith('+');

        var digits = new string(trimmed.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0) return null;

        // 00 is the international prefix dialled from a landline; it means the same as a plus.
        if (!hadPlus && digits.StartsWith("00", StringComparison.Ordinal))
        {
            digits = digits[2..];
            hadPlus = true;
        }

        // A leading trunk zero (0 98765 43210) is national dialling and never part of E.164. No
        // calling code begins with zero either, so this is safe on both branches below.
        digits = digits.TrimStart('0');

        if (!hadPlus)
        {
            var code = new string((defaultCountryCode ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

            // A number of national length gets the configured calling code; one already long
            // enough to carry its own is left alone. An international number must therefore be
            // stored with its plus — guessing at one sends the message to a stranger.
            if (code.Length > 0 && digits.Length <= NationalNumberDigits)
                digits = code + digits;
        }

        return digits.Length is >= MinimumDigits and <= MaximumDigits ? "+" + digits : null;
    }
}

/// <summary>Accepts and discards every message. The default everywhere outside Development.</summary>
public sealed class NullWhatsAppSender : IWhatsAppSender
{
    private readonly string? _reason;
    private readonly ILogger<NullWhatsAppSender> _logger;

    public NullWhatsAppSender(ILogger<NullWhatsAppSender> logger, string? reason = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _reason = reason;
    }

    public bool IsEnabled => false;

    public string ProviderName => "None";

    public Task<WhatsAppDeliveryResult> SendAsync(WhatsAppMessage message, CancellationToken ct = default)
    {
        var detail = _reason ?? "No WhatsApp provider is configured.";
        _logger.LogDebug("WhatsApp message discarded (to {To}): {Reason}",
            MailMessageFactory.Mask(message?.To), detail);
        return Task.FromResult(WhatsAppDeliveryResult.Skipped(ProviderName, detail));
    }
}

/// <summary>
/// Writes every message to a folder and sends nothing — the Development default, so building a
/// messaging feature can never buzz a real phone. One readable .txt per message, carrying the
/// rendered preview as well as the template and its parameters, because a template that renders
/// correctly and is filled in the wrong order is a bug this file is meant to expose.
/// </summary>
public sealed class FileDropWhatsAppSender : IWhatsAppSender
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly WhatsAppOptions _options;
    private readonly ILogger<FileDropWhatsAppSender> _logger;
    private int _sequence;

    public FileDropWhatsAppSender(WhatsAppOptions options, ILogger<FileDropWhatsAppSender> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsEnabled => true;

    public string ProviderName => "File";

    public string Directory => Path.GetFullPath(
        string.IsNullOrWhiteSpace(_options.FileSink?.Directory)
            ? "logs/whatsapp-drop"
            : _options.FileSink!.Directory);

    public async Task<WhatsAppDeliveryResult> SendAsync(WhatsAppMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var to = PhoneNumberNormalizer.ToE164(message.To, _options.DefaultCountryCode);
        if (to is null)
            return WhatsAppDeliveryResult.Skipped(ProviderName, "The message has no usable recipient number.");

        if (string.IsNullOrWhiteSpace(message.TemplateName))
            return WhatsAppDeliveryResult.Skipped(ProviderName, "No approved template is mapped for this occasion.");

        var directory = Directory;
        System.IO.Directory.CreateDirectory(directory);

        var stamp = DateTime.Now;
        var sequence = Interlocked.Increment(ref _sequence);
        var path = Path.Combine(directory, string.Create(CultureInfo.InvariantCulture,
            $"{stamp:yyyyMMdd-HHmmss-fff}_{sequence:000}.txt"));

        var parameters = message.Parameters.Count == 0
            ? "(none)"
            : string.Join("\n", message.Parameters.Select((p, i) => $"          {{{{{i + 1}}}}} = {p}"));

        var body = $"Provider: {ProviderName}\nWritten : {stamp:yyyy-MM-dd HH:mm:ss}\n" +
                   $"To      : {to}\nTemplate: {message.TemplateName} ({message.LanguageCode})\n" +
                   $"Params  :\n{parameters}\n\n{message.PreviewText}\n";

        await File.WriteAllTextAsync(path, body, Utf8NoBom, ct).ConfigureAwait(false);

        _logger.LogInformation("WhatsApp message written to the local drop for {To}: {File}",
            MailMessageFactory.Mask(to), path);
        return WhatsAppDeliveryResult.Sent(ProviderName, path);
    }
}

/// <summary>Real delivery through whatever HTTP gateway the configuration describes.</summary>
public sealed class HttpWhatsAppSender : IWhatsAppSender
{
    /// <summary>One shared client: the sender is a singleton and gateways are a single host.</summary>
    private static readonly HttpClient Client = new();

    private readonly WhatsAppOptions _options;
    private readonly ILogger<HttpWhatsAppSender> _logger;

    public HttpWhatsAppSender(WhatsAppOptions options, ILogger<HttpWhatsAppSender> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsEnabled => true;

    public string ProviderName => "Http";

    public async Task<WhatsAppDeliveryResult> SendAsync(WhatsAppMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var to = PhoneNumberNormalizer.ToE164(message.To, _options.DefaultCountryCode);
        if (to is null)
            return WhatsAppDeliveryResult.Skipped(ProviderName, "The message has no usable recipient number.");

        if (string.IsNullOrWhiteSpace(message.TemplateName))
            return WhatsAppDeliveryResult.Skipped(ProviderName, "No approved template is mapped for this occasion.");

        var http = _options.Http ?? new WhatsAppHttpOptions();
        if (string.IsNullOrWhiteSpace(http.Url))
            return WhatsAppDeliveryResult.Skipped(ProviderName, "WhatsApp:Http:Url is not configured.");

        var isJson = !string.Equals(http.ContentType, "application/x-www-form-urlencoded",
            StringComparison.OrdinalIgnoreCase);

        var digits = to[1..];

        // Cloud API body parameters. Values are single-lined first: WhatsApp rejects a parameter
        // containing a newline or four consecutive spaces, and a rejected template is a message the
        // member never sees.
        var cloudParameters = string.Join(",", message.Parameters
            .Select(p => $"{{\"type\":\"text\",\"text\":\"{JsonEscape(SingleLine(p))}\"}}"));
        var plainParameters = string.Join(",", message.Parameters
            .Select(p => $"\"{JsonEscape(SingleLine(p))}\""));

        string Fill(string template, bool json)
        {
            string V(string value) => json ? JsonEscape(value) : Uri.EscapeDataString(value);

            return template
                .Replace("{to_digits}", V(digits))
                .Replace("{to}", V(to))
                .Replace("{template}", V(message.TemplateName))
                .Replace("{lang}", V(message.LanguageCode))
                // The parameter lists are already JSON; escaping them again would send the braces
                // as literal text, so they are substituted verbatim into a JSON body and
                // form-encoded whole into a form body.
                .Replace("{params_text}", json ? plainParameters : Uri.EscapeDataString(plainParameters))
                .Replace("{params}", json ? cloudParameters : Uri.EscapeDataString(cloudParameters))
                .Replace("{text}", V(message.PreviewText));
        }

        var url = http.Url
            .Replace("{to_digits}", Uri.EscapeDataString(digits))
            .Replace("{to}", Uri.EscapeDataString(to))
            .Replace("{template}", Uri.EscapeDataString(message.TemplateName))
            .Replace("{lang}", Uri.EscapeDataString(message.LanguageCode))
            .Replace("{text}", Uri.EscapeDataString(message.PreviewText));

        using var request = new HttpRequestMessage(
            string.Equals(http.Method, "GET", StringComparison.OrdinalIgnoreCase)
                ? HttpMethod.Get : HttpMethod.Post,
            url);

        foreach (var (name, value) in http.Headers)
            request.Headers.TryAddWithoutValidation(name, value);

        if (request.Method == HttpMethod.Post && !string.IsNullOrWhiteSpace(http.BodyTemplate))
        {
            request.Content = new StringContent(
                Fill(http.BodyTemplate, isJson), Encoding.UTF8,
                isJson ? "application/json" : "application/x-www-form-urlencoded");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(http.TimeoutSeconds, 1, 120)));

        var response = await Client.SendAsync(request, cts.Token).ConfigureAwait(false);
        var status = (int)response.StatusCode;

        if (!response.IsSuccessStatusCode)
        {
            // The gateway's own words matter for diagnosis — "template name does not exist" is the
            // single most common first-run failure. Credentials never appear in a response body,
            // so this is safe to raise to the caller's log.
            var text = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            throw new HttpRequestException(
                $"The WhatsApp gateway answered {status}: {Truncate(text, 300)}");
        }

        _logger.LogInformation("WhatsApp message handed to the gateway for {To} using template {Template} ({Status}).",
            MailMessageFactory.Mask(to), message.TemplateName, status);
        return WhatsAppDeliveryResult.Sent(ProviderName, $"HTTP {status}");
    }

    private static string SingleLine(string value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string JsonEscape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}

/// <summary>Picks the <see cref="IWhatsAppSender"/> from configuration — same rules as email and
/// SMS: off unless turned on, and misconfiguration degrades to the null sender, never a crash.</summary>
public static class WhatsAppSenderFactory
{
    public static IWhatsAppSender CreateFromOptionsAccessor(
        Func<WhatsAppOptions?> optionsAccessor, ILoggerFactory loggerFactory, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(optionsAccessor);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        WhatsAppOptions? options;
        try
        {
            options = optionsAccessor();
        }
        catch (Exception ex)
        {
            var log = loggerFactory.CreateLogger(nameof(WhatsAppSenderFactory));
            log.LogError(ex, "The WhatsApp configuration section could not be read; messaging is off.");
            return new NullWhatsAppSender(loggerFactory.CreateLogger<NullWhatsAppSender>(),
                "The WhatsApp configuration section could not be read.");
        }

        options ??= new WhatsAppOptions();
        var provider = options.Provider?.Trim();
        if (string.IsNullOrEmpty(provider)) provider = isDevelopment ? "File" : "None";

        var logger = loggerFactory.CreateLogger(nameof(WhatsAppSenderFactory));
        switch (provider.ToLowerInvariant())
        {
            case "file":
                var sink = new FileDropWhatsAppSender(options, loggerFactory.CreateLogger<FileDropWhatsAppSender>());
                logger.LogInformation(
                    "WhatsApp provider: local file sink. Messages are written to {Directory} and are never sent.",
                    sink.Directory);
                return sink;

            case "http":
                if (string.IsNullOrWhiteSpace(options.Http?.Url))
                {
                    logger.LogError("WhatsApp:Provider is Http but WhatsApp:Http:Url is empty; messaging is off.");
                    return new NullWhatsAppSender(loggerFactory.CreateLogger<NullWhatsAppSender>(),
                        "WhatsApp:Http:Url is not configured.");
                }
                logger.LogInformation("WhatsApp provider: HTTP gateway at {Url}.",
                    new Uri(options.Http.Url).GetLeftPart(UriPartial.Authority));
                return new HttpWhatsAppSender(options, loggerFactory.CreateLogger<HttpWhatsAppSender>());

            case "none":
                logger.LogInformation("WhatsApp provider: none. Messages are accepted and discarded.");
                return new NullWhatsAppSender(loggerFactory.CreateLogger<NullWhatsAppSender>());

            default:
                logger.LogError("WhatsApp:Provider '{Provider}' is not recognised; messaging is off.", provider);
                return new NullWhatsAppSender(loggerFactory.CreateLogger<NullWhatsAppSender>(),
                    $"Unknown WhatsApp provider '{provider}'.");
        }
    }
}
