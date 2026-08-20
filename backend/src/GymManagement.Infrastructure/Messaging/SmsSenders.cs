using System.Globalization;
using System.Text;
using GymManagement.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Messaging;

/// <summary>
/// Configuration for outbound text messages, bound from the <c>Sms</c> section. The shape follows
/// <see cref="EmailOptions"/>: provider selection plus per-provider settings, credentials from
/// configuration only — never the database.
/// </summary>
public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    /// <summary>
    /// <c>None</c>, <c>File</c> or <c>Http</c>. Left unset it resolves to <c>File</c> in
    /// Development and <c>None</c> everywhere else, so texting is off until somebody turns it on.
    /// </summary>
    public string? Provider { get; set; }

    public SmsFileSinkOptions? FileSink { get; set; }
    public SmsHttpOptions? Http { get; set; }
}

public sealed class SmsFileSinkOptions
{
    /// <summary>Folder the file sink writes to. Defaults to <c>logs/sms-drop</c>.</summary>
    public string? Directory { get; set; }
}

/// <summary>
/// A generic HTTP gateway adapter rather than one vendor's SDK: MSG91, Fast2SMS, Textlocal,
/// Twilio's REST form and WhatsApp's Cloud API are all "send an HTTP request with the number and
/// the text in it", so one template covers whichever gateway the gym signs up with.
/// <c>{to}</c> and <c>{text}</c> in <see cref="BodyTemplate"/> and <see cref="Url"/> are replaced
/// (URL-encoded in the URL, JSON-escaped in a JSON body) before sending.
/// </summary>
public sealed class SmsHttpOptions
{
    public string? Url { get; set; }

    /// <summary><c>POST</c> (default) or <c>GET</c>.</summary>
    public string? Method { get; set; }

    /// <summary><c>application/json</c> (default) or <c>application/x-www-form-urlencoded</c>.</summary>
    public string? ContentType { get; set; }

    /// <summary>Request body with <c>{to}</c>/<c>{text}</c> placeholders. Ignored for GET.</summary>
    public string? BodyTemplate { get; set; }

    /// <summary>Headers sent verbatim, e.g. <c>authkey</c> or <c>Authorization</c>. The values are
    /// the credentials — supply them via <c>Sms__Http__Headers__NAME</c> environment variables or
    /// user secrets, and never commit them.</summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    public int TimeoutSeconds { get; set; } = 20;
}

/// <summary>Accepts and discards every message. The default everywhere outside Development.</summary>
public sealed class NullSmsSender : ISmsSender
{
    private readonly string? _reason;
    private readonly ILogger<NullSmsSender> _logger;

    public NullSmsSender(ILogger<NullSmsSender> logger, string? reason = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _reason = reason;
    }

    public bool IsEnabled => false;

    public string ProviderName => "None";

    public Task<SmsDeliveryResult> SendAsync(SmsMessage message, CancellationToken ct = default)
    {
        var detail = _reason ?? "No SMS provider is configured.";
        _logger.LogDebug("SMS discarded (to {To}): {Reason}", MailMessageFactory.Mask(message?.To), detail);
        return Task.FromResult(SmsDeliveryResult.Skipped(ProviderName, detail));
    }
}

/// <summary>
/// Writes every message to a folder and sends nothing — the Development default, so building a
/// texting feature can never buzz a real phone. One readable .txt per message.
/// </summary>
public sealed class FileDropSmsSender : ISmsSender
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly SmsOptions _options;
    private readonly ILogger<FileDropSmsSender> _logger;
    private int _sequence;

    public FileDropSmsSender(SmsOptions options, ILogger<FileDropSmsSender> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsEnabled => true;

    public string ProviderName => "File";

    public string Directory => Path.GetFullPath(
        string.IsNullOrWhiteSpace(_options.FileSink?.Directory)
            ? "logs/sms-drop"
            : _options.FileSink!.Directory);

    public async Task<SmsDeliveryResult> SendAsync(SmsMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(message.To))
            return SmsDeliveryResult.Skipped(ProviderName, "The message has no recipient number.");

        var directory = Directory;
        System.IO.Directory.CreateDirectory(directory);

        var stamp = DateTime.Now;
        var sequence = Interlocked.Increment(ref _sequence);
        var path = Path.Combine(directory, string.Create(CultureInfo.InvariantCulture,
            $"{stamp:yyyyMMdd-HHmmss-fff}_{sequence:000}.txt"));

        var body = $"Provider: {ProviderName}\nWritten : {stamp:yyyy-MM-dd HH:mm:ss}\n" +
                   $"To      : {message.To}\n\n{message.Text}\n";
        await File.WriteAllTextAsync(path, body, Utf8NoBom, ct).ConfigureAwait(false);

        _logger.LogInformation("SMS written to the local drop for {To}: {File}",
            MailMessageFactory.Mask(message.To), path);
        return SmsDeliveryResult.Sent(ProviderName, path);
    }
}

/// <summary>Real delivery through whatever HTTP gateway the configuration describes.</summary>
public sealed class HttpSmsSender : ISmsSender
{
    /// <summary>One shared client: the sender is a singleton and gateways are a single host.</summary>
    private static readonly HttpClient Client = new();

    private readonly SmsOptions _options;
    private readonly ILogger<HttpSmsSender> _logger;

    public HttpSmsSender(SmsOptions options, ILogger<HttpSmsSender> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsEnabled => true;

    public string ProviderName => "Http";

    public async Task<SmsDeliveryResult> SendAsync(SmsMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(message.To))
            return SmsDeliveryResult.Skipped(ProviderName, "The message has no recipient number.");

        var http = _options.Http ?? new SmsHttpOptions();
        if (string.IsNullOrWhiteSpace(http.Url))
            return SmsDeliveryResult.Skipped(ProviderName, "Sms:Http:Url is not configured.");

        var isJson = !string.Equals(http.ContentType, "application/x-www-form-urlencoded",
            StringComparison.OrdinalIgnoreCase);

        string Fill(string template, bool json) => template
            .Replace("{to}", json ? JsonEscape(message.To) : Uri.EscapeDataString(message.To))
            .Replace("{text}", json ? JsonEscape(message.Text) : Uri.EscapeDataString(message.Text));

        var url = http.Url
            .Replace("{to}", Uri.EscapeDataString(message.To))
            .Replace("{text}", Uri.EscapeDataString(message.Text));

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
            // The gateway's own words matter for diagnosis; credentials never appear in a
            // response body, so this is safe to raise to the caller's log.
            var text = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            throw new HttpRequestException(
                $"The SMS gateway answered {status}: {Truncate(text, 300)}");
        }

        _logger.LogInformation("SMS handed to the gateway for {To} ({Status}).",
            MailMessageFactory.Mask(message.To), status);
        return SmsDeliveryResult.Sent(ProviderName, $"HTTP {status}");
    }

    private static string JsonEscape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}

/// <summary>Picks the <see cref="ISmsSender"/> from configuration — same rules as email:
/// off unless turned on, and misconfiguration degrades to the null sender, never a crash.</summary>
public static class SmsSenderFactory
{
    public static ISmsSender CreateFromOptionsAccessor(
        Func<SmsOptions?> optionsAccessor, ILoggerFactory loggerFactory, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(optionsAccessor);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        SmsOptions? options;
        try
        {
            options = optionsAccessor();
        }
        catch (Exception ex)
        {
            var log = loggerFactory.CreateLogger(nameof(SmsSenderFactory));
            log.LogError(ex, "The Sms configuration section could not be read; texting is off.");
            return new NullSmsSender(loggerFactory.CreateLogger<NullSmsSender>(),
                "The Sms configuration section could not be read.");
        }

        options ??= new SmsOptions();
        var provider = options.Provider?.Trim();
        if (string.IsNullOrEmpty(provider)) provider = isDevelopment ? "File" : "None";

        var logger = loggerFactory.CreateLogger(nameof(SmsSenderFactory));
        switch (provider.ToLowerInvariant())
        {
            case "file":
                var sink = new FileDropSmsSender(options, loggerFactory.CreateLogger<FileDropSmsSender>());
                logger.LogInformation(
                    "SMS provider: local file sink. Messages are written to {Directory} and are never sent.",
                    sink.Directory);
                return sink;

            case "http":
                if (string.IsNullOrWhiteSpace(options.Http?.Url))
                {
                    logger.LogError("Sms:Provider is Http but Sms:Http:Url is empty; texting is off.");
                    return new NullSmsSender(loggerFactory.CreateLogger<NullSmsSender>(),
                        "Sms:Http:Url is not configured.");
                }
                logger.LogInformation("SMS provider: HTTP gateway at {Url}.",
                    new Uri(options.Http.Url).GetLeftPart(UriPartial.Authority));
                return new HttpSmsSender(options, loggerFactory.CreateLogger<HttpSmsSender>());

            case "none":
                logger.LogInformation("SMS provider: none. Text messages are accepted and discarded.");
                return new NullSmsSender(loggerFactory.CreateLogger<NullSmsSender>());

            default:
                logger.LogError("Sms:Provider '{Provider}' is not recognised; texting is off.", provider);
                return new NullSmsSender(loggerFactory.CreateLogger<NullSmsSender>(),
                    $"Unknown SMS provider '{provider}'.");
        }
    }
}
