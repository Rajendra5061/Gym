using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GymManagement.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Payments.Checkout;

/// <summary>
/// Razorpay adapter: one order plus one single-use, fixed-amount UPI QR per payment.
///
/// Two calls, both carrying the payment reference in <c>notes</c> so Razorpay's webhook quotes it
/// back and the existing settlement engine can match the payment:
/// <list type="number">
///   <item><c>POST /v1/orders</c> — the ledger entry, with the reference as the <c>receipt</c>.</item>
///   <item>
///     <c>POST /v1/payments/qr_codes</c> — a dynamic QR closed at the order's expiry, so a member
///     cannot pay a stale amount against a request the gym has since changed.
///   </item>
/// </list>
///
/// Razorpay serves the QR as a hosted PNG (<c>image_url</c>) rather than a raw payload, so when no
/// <c>qr_string</c>/<c>payload</c> field is present the result's <c>QrData</c> uses the documented
/// <c>image:</c> prefix convention and the client displays the image instead of encoding one.
/// </summary>
public sealed class RazorpayGatewayClient : IPaymentGatewayClient
{
    /// <summary>One shared client: the adapter is a singleton and the gateway is a single host.</summary>
    private static readonly HttpClient Client = new();

    private const int TimeoutSeconds = 20;

    private readonly RazorpayCheckoutOptions _options;
    private readonly string _basicCredentials;
    private readonly ILogger<RazorpayGatewayClient> _logger;

    public RazorpayGatewayClient(RazorpayCheckoutOptions options, ILogger<RazorpayGatewayClient> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Razorpay authenticates with HTTP Basic over the key pair. Computed once here; it never
        // appears in logs or error messages.
        _basicCredentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{options.KeyId}:{options.KeySecret}"));
    }

    public bool IsEnabled => true;

    public string Provider => "razorpay";

    public async Task<GatewayOrderResult> CreateOrderAsync(GatewayOrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var baseUrl = _options.BaseUrl.TrimEnd('/');

        // Razorpay quotes amounts in paise. Rounded away from zero so 10.005 is 1001, never 1000.
        var paise = (long)Math.Round(request.Amount * 100m, 0, MidpointRounding.AwayFromZero);

        // ---------------------------------------------------------------- 1. the order
        var orderBody = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["amount"] = paise,
            ["currency"] = request.Currency,
            ["receipt"] = request.Reference,
            ["notes"] = new Dictionary<string, string> { ["reference"] = request.Reference }
        });

        string orderId;
        using (var orderDocument = await PostAsync($"{baseUrl}/v1/orders", "/v1/orders", orderBody, ct)
                   .ConfigureAwait(false))
        {
            orderId = ReadString(orderDocument.RootElement, "id")
                ?? throw new HttpRequestException("Razorpay's order response carries no order id.");
        }

        // ---------------------------------------------------------------- 2. the QR
        // close_by pins the QR's life to the order's expiry (Razorpay requires it to be at least a
        // couple of minutes ahead, which OrderValidMinutes comfortably clears). fixed_amount plus
        // single_use means the QR can collect exactly this amount, exactly once.
        var closeBy = new DateTimeOffset(
            DateTime.SpecifyKind(request.ExpiresAtUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();

        var qrBody = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = "upi_qr",
            ["usage"] = "single_use",
            ["fixed_amount"] = true,
            ["payment_amount"] = paise,
            ["description"] = request.Description,
            ["close_by"] = closeBy,
            ["notes"] = new Dictionary<string, string>
            {
                ["reference"] = request.Reference,
                ["order_id"] = orderId
            }
        });

        string? qrId;
        string? qrData;
        using (var qrDocument = await PostAsync($"{baseUrl}/v1/payments/qr_codes", "/v1/payments/qr_codes", qrBody, ct)
                   .ConfigureAwait(false))
        {
            var root = qrDocument.RootElement;
            qrId = ReadString(root, "id");

            // A raw payload when the API offers one; otherwise the hosted PNG behind the
            // documented "image:" prefix, so the caller always knows how to show it.
            qrData = ReadString(root, "qr_string") ?? ReadString(root, "payload");
            if (qrData is null && ReadString(root, "image_url") is { } imageUrl)
                qrData = $"image:{imageUrl}";
        }

        _logger.LogInformation(
            "Razorpay order {OrderId} and QR {QrId} created for reference {Reference} ({Paise} paise).",
            orderId, qrId ?? "(none)", request.Reference, paise);

        return new GatewayOrderResult(
            Created: true,
            OrderId: orderId,
            QrData: qrData,
            PaymentUrl: null,
            Detail: qrData is null
                ? $"Razorpay order {orderId} created, but the QR response carried no payload or image."
                : $"Razorpay order {orderId} with single-use QR {qrId}, closing at {request.ExpiresAtUtc:u}.");
    }

    /// <summary>
    /// One authenticated POST with a hard 20-second budget. Non-2xx raises with the status and the
    /// gateway's own words — Razorpay's error bodies never contain credentials, so the first 300
    /// characters are safe to surface to a log.
    /// </summary>
    private async Task<JsonDocument> PostAsync(string url, string pathForLog, string jsonBody, CancellationToken ct)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", _basicCredentials);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        using var response = await Client.SendAsync(httpRequest, cts.Token).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Razorpay answered {(int)response.StatusCode} for {pathForLog}: {Truncate(text, 300)}");
        }

        return JsonDocument.Parse(text);
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()!.Trim()
            : null;

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
