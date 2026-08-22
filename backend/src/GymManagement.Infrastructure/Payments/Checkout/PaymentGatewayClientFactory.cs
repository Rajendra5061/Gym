using GymManagement.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Payments.Checkout;

/// <summary>
/// Accepts every order request and creates nothing. The default everywhere outside Development,
/// and the shape misconfiguration degrades to — a gym with no checkout provider keeps its manual
/// UPI flow, it does not get a crash.
/// </summary>
public sealed class NullPaymentGatewayClient : IPaymentGatewayClient
{
    private readonly string? _reason;
    private readonly ILogger<NullPaymentGatewayClient> _logger;

    public NullPaymentGatewayClient(ILogger<NullPaymentGatewayClient> logger, string? reason = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _reason = reason;
    }

    public bool IsEnabled => false;

    public string Provider => "None";

    public Task<GatewayOrderResult> CreateOrderAsync(GatewayOrderRequest request, CancellationToken ct = default)
    {
        var detail = _reason ?? "No checkout provider is configured.";
        _logger.LogDebug("Gateway order skipped for reference {Reference}: {Reason}",
            request?.Reference ?? "(none)", detail);

        return Task.FromResult(GatewayOrderResult.Skipped(detail));
    }
}

/// <summary>
/// Picks the <see cref="IPaymentGatewayClient"/> from configuration — same rules as the SMS and
/// email senders: off unless turned on, simulator by default in Development so the full loop is
/// demonstrable, and misconfiguration degrades to the null client, never a crash.
/// </summary>
public static class PaymentGatewayClientFactory
{
    public static IPaymentGatewayClient CreateFromOptionsAccessor(
        Func<PaymentCheckoutOptions?> optionsAccessor, ILoggerFactory loggerFactory, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(optionsAccessor);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        PaymentCheckoutOptions? options;
        try
        {
            options = optionsAccessor();
        }
        catch (Exception ex)
        {
            var log = loggerFactory.CreateLogger(nameof(PaymentGatewayClientFactory));
            log.LogError(ex, "The Payments:Checkout configuration section could not be read; order creation is off.");
            return new NullPaymentGatewayClient(loggerFactory.CreateLogger<NullPaymentGatewayClient>(),
                "The Payments:Checkout configuration section could not be read.");
        }

        options ??= new PaymentCheckoutOptions();
        var provider = options.Provider?.Trim();
        if (string.IsNullOrEmpty(provider)) provider = isDevelopment ? "simulator" : "none";

        var logger = loggerFactory.CreateLogger(nameof(PaymentGatewayClientFactory));
        switch (provider.ToLowerInvariant())
        {
            case "simulator":
                logger.LogInformation(
                    "Checkout provider: simulator. Orders are fabricated locally and no money can move.");
                return new SimulatorGatewayClient(loggerFactory.CreateLogger<SimulatorGatewayClient>());

            case "razorpay":
                if (string.IsNullOrWhiteSpace(options.Razorpay?.KeyId) ||
                    string.IsNullOrWhiteSpace(options.Razorpay?.KeySecret))
                {
                    logger.LogError(
                        "Payments:Checkout:Provider is razorpay but Razorpay:KeyId/KeySecret are not both set; " +
                        "order creation is off.");
                    return new NullPaymentGatewayClient(loggerFactory.CreateLogger<NullPaymentGatewayClient>(),
                        "Payments:Checkout:Razorpay credentials are not configured.");
                }
                logger.LogInformation("Checkout provider: Razorpay at {BaseUrl}.",
                    options.Razorpay.BaseUrl);
                return new RazorpayGatewayClient(options.Razorpay,
                    loggerFactory.CreateLogger<RazorpayGatewayClient>());

            case "cashfree":
                if (string.IsNullOrWhiteSpace(options.Cashfree?.AppId) ||
                    string.IsNullOrWhiteSpace(options.Cashfree?.SecretKey))
                {
                    logger.LogError(
                        "Payments:Checkout:Provider is cashfree but Cashfree:AppId/SecretKey are not both set; " +
                        "order creation is off.");
                    return new NullPaymentGatewayClient(loggerFactory.CreateLogger<NullPaymentGatewayClient>(),
                        "Payments:Checkout:Cashfree credentials are not configured.");
                }
                logger.LogInformation("Checkout provider: Cashfree at {BaseUrl}.",
                    options.Cashfree.BaseUrl);
                return new CashfreeGatewayClient(options.Cashfree,
                    loggerFactory.CreateLogger<CashfreeGatewayClient>());

            case "none":
                logger.LogInformation("Checkout provider: none. Gateway orders are skipped and the manual UPI flow stands alone.");
                return new NullPaymentGatewayClient(loggerFactory.CreateLogger<NullPaymentGatewayClient>());

            default:
                logger.LogError("Payments:Checkout:Provider '{Provider}' is not recognised; order creation is off.",
                    provider);
                return new NullPaymentGatewayClient(loggerFactory.CreateLogger<NullPaymentGatewayClient>(),
                    $"Unknown checkout provider '{provider}'.");
        }
    }
}
