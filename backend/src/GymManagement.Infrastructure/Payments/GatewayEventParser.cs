using System.Globalization;
using System.Text.Json;

namespace GymManagement.Infrastructure.Payments;

/// <summary>The provider-neutral shape every gateway payload is reduced to.</summary>
public sealed class GatewayEvent
{
    public string EventId { get; init; } = string.Empty;
    public string? EventType { get; init; }
    public string? Status { get; init; }
    public string? PaymentReference { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public string? GatewayTransactionId { get; init; }
    public string? PayerVpa { get; init; }

    public GatewayEventKind Kind { get; init; } = GatewayEventKind.Unknown;
}

public enum GatewayEventKind
{
    Unknown = 0,
    Success = 1,
    Failure = 2
}

/// <summary>
/// Reduces a gateway payload to <see cref="GatewayEvent"/> without binding to any one provider's
/// schema.
///
/// Gateways disagree about names (<c>id</c> / <c>event_id</c>), casing (<c>paymentReference</c> /
/// <c>payment_reference</c>) and nesting (Razorpay buries the payment under
/// <c>payload.payment.entity</c>). So the document is flattened once, shallowest-wins, and the
/// canonical fields are looked up by a list of accepted aliases. Adding a provider is then a matter
/// of adding an alias, not of writing another parser.
///
/// Parsing happens strictly <b>after</b> signature verification: these bytes are already known to
/// have come from the gateway.
/// </summary>
public static class GatewayEventParser
{
    private const int MaxDepth = 6;

    private static readonly string[] EventIdNames =
    {
        "eventid", "id", "event_id", "webhookid", "webhook_id", "deliveryid", "delivery_id",
        "notificationid",
        // Cashfree names its payment id cf_payment_id and sends no other stable identifier;
        // one payment settles once, so it serves as the idempotency key exactly like
        // Razorpay's pay_… id does.
        "cf_payment_id",
    };

    private static readonly string[] EventTypeNames =
        { "event", "type", "eventtype", "event_type", "eventname", "action" };

    private static readonly string[] StatusNames =
        { "status", "state", "paymentstatus", "payment_status", "txnstatus", "result" };

    private static readonly string[] ReferenceNames =
    {
        "paymentreference", "payment_reference", "reference", "referenceid", "reference_id",
        "merchantreference", "merchant_reference", "merchantorderid", "merchant_order_id",
        "orderid", "order_id", "receipt", "tr", "merchanttransactionid"
    };

    private static readonly string[] AmountNames =
    {
        "amount", "amountpaid", "amount_paid", "paidamount", "paid_amount", "value", "txnamount",
        // Cashfree quotes the collected amount as payment_amount (order_amount is what was
        // asked; the payment is what arrived, so it is listed first).
        "payment_amount", "order_amount",
    };

    private static readonly string[] CurrencyNames = { "currency", "currencycode", "currency_code" };

    private static readonly string[] TransactionIdNames =
    {
        "utr", "transactionid", "transaction_id", "gatewaytransactionid", "bankreference",
        "bank_reference", "bankrrn", "rrn", "acquirerreference", "paymentid", "payment_id"
    };

    private static readonly string[] PayerVpaNames =
        { "vpa", "payervpa", "payer_vpa", "customervpa", "customer_vpa", "payeraddress", "upiid" };

    private static readonly string[] SuccessWords =
        { "success", "successful", "succeeded", "captured", "capture", "paid", "completed", "complete", "settled" };

    private static readonly string[] FailureWords =
    {
        "failed", "failure", "fail", "declined", "decline", "rejected", "cancelled", "canceled",
        "expired", "timedout", "timeout", "reversed", "error"
    };

    /// <summary>
    /// Parses the payload, or reports why it cannot be used. A payload that verifies but does not
    /// name an event is refused rather than guessed at: without an event id there is no idempotency.
    /// </summary>
    public static bool TryParse(byte[] rawBody, bool amountsInMinorUnits, out GatewayEvent result, out string error)
    {
        result = new GatewayEvent();
        error = string.Empty;

        if (rawBody.Length == 0)
        {
            error = "the request body is empty";
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawBody, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = MaxDepth * 2
            });
        }
        catch (JsonException ex)
        {
            error = $"the body is not valid JSON ({ex.Message})";
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "the body is not a JSON object";
                return false;
            }

            var flat = new Dictionary<string, (int Depth, JsonElement Value)>(StringComparer.OrdinalIgnoreCase);
            Flatten(document.RootElement, 0, flat);

            var eventId = ReadString(flat, EventIdNames);
            if (string.IsNullOrWhiteSpace(eventId))
            {
                error = "the payload carries no event id, so a repeat delivery could not be recognised";
                return false;
            }

            var eventType = ReadString(flat, EventTypeNames);
            var status = ReadString(flat, StatusNames);

            result = new GatewayEvent
            {
                EventId = Trim(eventId, 128)!,
                EventType = Trim(eventType, 96),
                Status = Trim(status, 48),
                PaymentReference = Trim(ReadString(flat, ReferenceNames), 128),
                Amount = ReadAmount(flat, amountsInMinorUnits),
                Currency = Trim(ReadString(flat, CurrencyNames), 8),
                GatewayTransactionId = Trim(ReadString(flat, TransactionIdNames), 128),
                PayerVpa = Trim(ReadString(flat, PayerVpaNames), 128),
                Kind = Classify(status, eventType)
            };

            return true;
        }
    }

    /// <summary>
    /// Success or failure, read from the status word and falling back to the event name. Anything
    /// unrecognised stays <see cref="GatewayEventKind.Unknown"/> and settles nothing — an
    /// unfamiliar status must never be optimistically taken for money received.
    /// </summary>
    private static GatewayEventKind Classify(string? status, string? eventType)
    {
        var kind = FromWord(status);
        return kind != GatewayEventKind.Unknown ? kind : FromWord(eventType);
    }

    private static GatewayEventKind FromWord(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return GatewayEventKind.Unknown;

        var text = value.Trim().ToLowerInvariant();

        // "payment.captured" → the last segment carries the verdict.
        var tail = text.Split('.', '_', '-', ':').LastOrDefault() ?? text;

        if (FailureWords.Any(w => tail == w) || FailureWords.Any(w => text == w)) return GatewayEventKind.Failure;
        if (SuccessWords.Any(w => tail == w) || SuccessWords.Any(w => text == w)) return GatewayEventKind.Success;

        return GatewayEventKind.Unknown;
    }

    /// <summary>
    /// Records every scalar leaf by its property name, keeping the shallowest occurrence. Depth
    /// order is what makes <c>payload.payment.entity.amount</c> readable as <c>amount</c> while a
    /// deeper, less relevant <c>amount</c> cannot displace a top-level one.
    /// </summary>
    private static void Flatten(JsonElement element, int depth, IDictionary<string, (int Depth, JsonElement Value)> sink)
    {
        if (depth > MaxDepth) return;

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var name = property.Name;

                    if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        Flatten(property.Value, depth + 1, sink);
                        continue;
                    }

                    if (!sink.TryGetValue(name, out var existing) || depth < existing.Depth)
                        sink[name] = (depth, property.Value);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Flatten(item, depth + 1, sink);

                break;
        }
    }

    private static string? ReadString(IDictionary<string, (int Depth, JsonElement Value)> flat, string[] names)
    {
        foreach (var name in names)
        {
            if (!flat.TryGetValue(name, out var hit)) continue;

            var text = hit.Value.ValueKind switch
            {
                JsonValueKind.String => hit.Value.GetString(),
                JsonValueKind.Number => hit.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
        }

        return null;
    }

    private static decimal? ReadAmount(IDictionary<string, (int Depth, JsonElement Value)> flat, bool minorUnits)
    {
        foreach (var name in AmountNames)
        {
            if (!flat.TryGetValue(name, out var hit)) continue;

            decimal value;
            switch (hit.Value.ValueKind)
            {
                case JsonValueKind.Number when hit.Value.TryGetDecimal(out var number):
                    value = number;
                    break;

                case JsonValueKind.String when decimal.TryParse(hit.Value.GetString(),
                    NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed):
                    value = parsed;
                    break;

                default:
                    continue;
            }

            if (minorUnits) value /= 100m;

            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        return null;
    }

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var text = value.Trim();
        return text.Length <= maxLength ? text : text[..maxLength];
    }
}
