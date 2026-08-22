using System.Text;
using FluentAssertions;
using GymManagement.Infrastructure.Payments;
using Xunit;

namespace GymManagement.UnitTests.Services;

/// <summary>
/// The provider payloads the parser must reduce correctly — written against the REAL webhook
/// shapes Razorpay and Cashfree document, so switching a gym onto either gateway is a matter
/// of configuration, never of discovering a parsing gap in production.
/// </summary>
public class GatewayEventParserTests
{
    private static bool Parse(string json, bool minorUnits, out GatewayEvent evt, out string error) =>
        GatewayEventParser.TryParse(Encoding.UTF8.GetBytes(json), minorUnits, out evt, out error);

    [Fact]
    public void RazorpayCapturedPayloadParsesEndToEnd()
    {
        // The documented payment.captured envelope: everything of interest nested under
        // payload.payment.entity, amount in paise, our reference riding in notes.
        const string json = """
        {
          "entity": "event",
          "account_id": "acc_TestAccount01",
          "event": "payment.captured",
          "contains": ["payment"],
          "payload": {
            "payment": {
              "entity": {
                "id": "pay_NDb1JcLuHrRCzX",
                "amount": 250000,
                "currency": "INR",
                "status": "captured",
                "order_id": "order_NDb0zKr9rJxPqA",
                "method": "upi",
                "vpa": "rajendra@ybl",
                "notes": { "reference": "UPI202608221206004342" },
                "acquirer_data": { "rrn": "422814763501" }
              }
            }
          },
          "created_at": 1787812345
        }
        """;

        Parse(json, minorUnits: true, out var evt, out var error).Should().BeTrue(error);

        evt.EventId.Should().Be("pay_NDb1JcLuHrRCzX");
        evt.Kind.Should().Be(GatewayEventKind.Success);
        evt.PaymentReference.Should().Be("UPI202608221206004342");
        evt.Amount.Should().Be(2500.00m);
        evt.Currency.Should().Be("INR");
        evt.GatewayTransactionId.Should().Be("422814763501");
        evt.PayerVpa.Should().Be("rajendra@ybl");
    }

    [Fact]
    public void RazorpayFailedPayloadIsClassifiedAsFailure()
    {
        const string json = """
        {
          "entity": "event",
          "event": "payment.failed",
          "payload": {
            "payment": {
              "entity": {
                "id": "pay_NDb2FailedTry01",
                "amount": 250000,
                "currency": "INR",
                "status": "failed",
                "error_code": "BAD_REQUEST_ERROR",
                "notes": { "reference": "UPI202608221206004342" }
              }
            }
          }
        }
        """;

        Parse(json, minorUnits: true, out var evt, out var error).Should().BeTrue(error);

        evt.Kind.Should().Be(GatewayEventKind.Failure);
        evt.EventId.Should().Be("pay_NDb2FailedTry01");
        evt.PaymentReference.Should().Be("UPI202608221206004342");
    }

    [Fact]
    public void CashfreeSuccessPayloadParsesEndToEnd()
    {
        // Cashfree's PAYMENT_SUCCESS_WEBHOOK: order_id is the merchant's own (our reference,
        // because the checkout client names the order after it), amounts in rupees, the
        // payment id under cf_payment_id.
        const string json = """
        {
          "data": {
            "order": {
              "order_id": "UPI202608221206004342",
              "order_amount": 2500.00,
              "order_currency": "INR"
            },
            "payment": {
              "cf_payment_id": 885061731,
              "payment_status": "SUCCESS",
              "payment_amount": 2500.00,
              "payment_currency": "INR",
              "bank_reference": "422814763501",
              "payment_method": { "upi": { "upi_id": "rajendra@ybl" } }
            }
          },
          "event_time": "2026-08-22T12:06:35+05:30",
          "type": "PAYMENT_SUCCESS_WEBHOOK"
        }
        """;

        Parse(json, minorUnits: false, out var evt, out var error).Should().BeTrue(error);

        evt.EventId.Should().Be("885061731");
        evt.Kind.Should().Be(GatewayEventKind.Success);
        evt.PaymentReference.Should().Be("UPI202608221206004342");
        evt.Amount.Should().Be(2500.00m);
        evt.GatewayTransactionId.Should().Be("422814763501");
    }

    [Fact]
    public void CashfreeFailurePayloadIsClassifiedAsFailure()
    {
        const string json = """
        {
          "data": {
            "order": { "order_id": "UPI202608221206004342", "order_amount": 2500.00 },
            "payment": {
              "cf_payment_id": 885061732,
              "payment_status": "FAILED",
              "payment_amount": 2500.00
            }
          },
          "type": "PAYMENT_FAILED_WEBHOOK"
        }
        """;

        Parse(json, minorUnits: false, out var evt, out var error).Should().BeTrue(error);

        evt.Kind.Should().Be(GatewayEventKind.Failure);
        evt.EventId.Should().Be("885061732");
    }

    [Fact]
    public void PayloadWithoutAnyEventIdIsRefused()
    {
        // No id anywhere means no idempotency, and the parser must refuse rather than guess.
        Parse("""{ "event": "payment.captured", "amount": 100 }""", false, out _, out var error)
            .Should().BeFalse();
        error.Should().Contain("event id");
    }
}
