using GymManagement.Application.DTOs;

namespace GymManagement.Application.Interfaces;

/// <summary>
/// Whether an automatic reconciliation gateway is configured, and for which provider slug.
///
/// The answer comes from configuration only — appsettings, environment variables or user secrets.
/// It is deliberately never read from the gym settings table: any administrator can read that table
/// back over <c>GET /api/settings</c>, and a shared signing secret sitting there would let anybody
/// with admin access mint webhook deliveries that mark payments as received.
/// </summary>
public interface IPaymentGatewayConfiguration
{
    /// <summary>True when at least one provider has a usable secret and the feature is switched on.</summary>
    bool IsEnabled { get; }

    /// <summary>True when this provider slug is configured. False for every slug when off.</summary>
    bool IsConfiguredFor(string provider);
}

/// <summary>
/// Verifies and applies gateway webhook deliveries.
///
/// The contract: nothing is written until the signature over the raw body has been verified and
/// the event has been shown to be fresh, and the same event delivered twice settles a payment once.
/// </summary>
public interface IPaymentWebhookService
{
    Task<PaymentWebhookResultDto> HandleAsync(PaymentWebhookRequestDto request, CancellationToken ct = default);

    /// <summary>
    /// Whether reconciliation is configured, and when a delivery was last accepted. Carries no
    /// signing secret. <c>WebhookUrl</c> is left for the API to fill in from the request it
    /// answered — this layer cannot know the origin the gateway will reach the gym on.
    /// </summary>
    Task<PaymentGatewayStatusDto> GetStatusAsync(CancellationToken ct = default);
}
