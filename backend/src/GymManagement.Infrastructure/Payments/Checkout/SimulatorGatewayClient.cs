using System.Globalization;
using System.Security.Cryptography;
using GymManagement.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Payments.Checkout;

/// <summary>
/// The Development checkout gateway. It exists so the <b>entire</b> automatic loop — create an
/// order, "pay" it, receive a signed webhook, settle the payment, renew the subscription — can be
/// demonstrated on a laptop with no gateway account, no credentials and no money.
///
/// Orders are fabricated locally and nothing leaves the process. The QR payload it returns is a
/// syntactically valid <c>upi://pay</c> intent, so a phone will scan it and open a UPI app — but
/// the payee is <c>simulator@upi</c>, which is not a real VPA, so it pays nobody. The "payment"
/// happens when the pay page (or an operator) posts the outcome to
/// <c>POST /api/payments/dev/simulate/{token}</c>, which mints a correctly signed webhook delivery
/// and hands it to the same settlement engine a real gateway would reach.
/// </summary>
public sealed class SimulatorGatewayClient : IPaymentGatewayClient
{
    private readonly ILogger<SimulatorGatewayClient> _logger;

    public SimulatorGatewayClient(ILogger<SimulatorGatewayClient> logger) =>
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public bool IsEnabled => true;

    public string Provider => "simulator";

    public Task<GatewayOrderResult> CreateOrderAsync(GatewayOrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderId = $"order_sim_{Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant()}";

        // A display name derived from the description, so the UPI app's confirmation screen reads
        // like the real thing it is standing in for.
        var payee = string.IsNullOrWhiteSpace(request.Description)
            ? "Simulated Payee"
            : Truncate(request.Description.Trim(), 40);

        var note = string.IsNullOrWhiteSpace(request.Description)
            ? "Simulated payment"
            : Truncate(request.Description.Trim(), 80);

        // Scannable, clearly fake: simulator@upi is not a registered VPA, so no transfer can ever
        // complete against this intent. The tr element carries the reference — the same join key a
        // real gateway would echo back in its webhook.
        var qrData =
            "upi://pay?pa=simulator@upi" +
            $"&pn={Uri.EscapeDataString(payee)}" +
            $"&am={request.Amount.ToString("F2", CultureInfo.InvariantCulture)}" +
            "&cu=INR" +
            $"&tr={Uri.EscapeDataString(request.Reference)}" +
            $"&tn={Uri.EscapeDataString(note)}";

        _logger.LogInformation(
            "Simulated gateway order {OrderId} created for reference {Reference} ({Amount:0.00}); " +
            "nothing was sent anywhere.",
            orderId, request.Reference, request.Amount);

        // PaymentUrl stays null on purpose: the frontend routes simulator checkout to its own pay
        // page, which knows how to post the outcome back to the Development simulator endpoint.
        return Task.FromResult(new GatewayOrderResult(
            Created: true,
            OrderId: orderId,
            QrData: qrData,
            PaymentUrl: null,
            Detail: "Simulated order from the Development gateway. The QR pays nobody; settle it by " +
                    "posting an outcome to POST /api/payments/dev/simulate/{token}."));
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
