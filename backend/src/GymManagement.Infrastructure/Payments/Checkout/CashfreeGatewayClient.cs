using System.Globalization;
using System.Text;
using System.Text.Json;
using GymManagement.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Payments.Checkout;

/// <summary>
/// Cashfree adapter: one <c>POST /orders</c> per payment, with the payment reference as the
/// <c>order_id</c> itself — Cashfree lets the merchant name the order, so the join key the webhook
/// settlement matches on needs no <c>notes</c> detour.
///
/// Cashfree does not hand back a QR payload from order creation; it renders its own checkout from
/// the returned <c>payment_session_id</c> (via its JS SDK or hosted page). There is no
/// public-stable URL pattern to compose from the session id, so <c>PaymentUrl</c> is the
/// response's own <c>payment_link</c> when the API supplies one and null otherwise, <c>QrData</c>
/// is always null, and the session id travels in <c>Detail</c> for the frontend to boot the
/// Cashfree checkout with.
/// </summary>
public sealed class CashfreeGatewayClient : IPaymentGatewayClient
{
    /// <summary>One shared client: the adapter is a singleton and the gateway is a single host.</summary>
    private static readonly HttpClient Client = new();

    private const int TimeoutSeconds = 20;

    private readonly CashfreeCheckoutOptions _options;
    private readonly ILogger<CashfreeGatewayClient> _logger;

    public CashfreeGatewayClient(CashfreeCheckoutOptions options, ILogger<CashfreeGatewayClient> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsEnabled => true;

    public string Provider => "cashfree";

    public async Task<GatewayOrderResult> CreateOrderAsync(GatewayOrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var baseUrl = _options.BaseUrl.TrimEnd('/');

        // Cashfree quotes order_amount in rupees, and mandates a customer phone number on every
        // order. When the member has no number on file the documented all-nines placeholder keeps
        // the order creatable; the settlement match runs on reference and amount, never on this.
        var body = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["order_id"] = request.Reference,
            ["order_amount"] = Math.Round(request.Amount, 2, MidpointRounding.AwayFromZero),
            ["order_currency"] = request.Currency,
            ["customer_details"] = new Dictionary<string, string?>
            {
                ["customer_id"] = request.Reference,
                ["customer_name"] = string.IsNullOrWhiteSpace(request.CustomerName) ? "Member" : request.CustomerName,
                ["customer_phone"] = string.IsNullOrWhiteSpace(request.CustomerPhone) ? "9999999999" : request.CustomerPhone
            },
            ["order_expiry_time"] = DateTime.SpecifyKind(request.ExpiresAtUtc, DateTimeKind.Utc)
                .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["order_note"] = request.Description
        });

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/orders")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        // Cashfree authenticates with header credentials and versions its API by date.
        httpRequest.Headers.TryAddWithoutValidation("x-client-id", _options.AppId);
        httpRequest.Headers.TryAddWithoutValidation("x-client-secret", _options.SecretKey);
        httpRequest.Headers.TryAddWithoutValidation("x-api-version", _options.ApiVersion);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        using var response = await Client.SendAsync(httpRequest, cts.Token).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // The gateway's own words matter for diagnosis, and its error bodies never contain
            // credentials — those only ever travel in the request headers above.
            throw new HttpRequestException(
                $"Cashfree answered {(int)response.StatusCode} for /orders: {Truncate(text, 300)}");
        }

        string? orderId;
        string? sessionId;
        string? paymentLink;
        using (var document = JsonDocument.Parse(text))
        {
            var root = document.RootElement;
            orderId = ReadString(root, "order_id") ?? request.Reference;
            sessionId = ReadString(root, "payment_session_id");
            paymentLink = ReadString(root, "payment_link");
        }

        _logger.LogInformation(
            "Cashfree order {OrderId} created for reference {Reference} ({Amount:0.00}).",
            orderId, request.Reference, request.Amount);

        return new GatewayOrderResult(
            Created: true,
            OrderId: orderId,
            QrData: null,
            PaymentUrl: paymentLink,
            Detail: sessionId is null
                ? $"Cashfree order {orderId} created, but the response carried no payment_session_id."
                : $"Cashfree order {orderId} created; Cashfree renders its own checkout from " +
                  $"payment_session_id {sessionId}.");
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
