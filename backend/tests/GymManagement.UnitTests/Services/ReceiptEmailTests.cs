using System.Text.RegularExpressions;
using FluentAssertions;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using GymManagement.Infrastructure.Messaging;
using GymManagement.Infrastructure.Reporting;
using GymManagement.Infrastructure.Services;
using GymManagement.UnitTests.TestBase;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GymManagement.UnitTests.Services;

/// <summary>
/// Emailing a receipt: choosing a provider, what the message may contain, and the guarantees the
/// payment flow depends on — send once, never throw, never send without a provider.
/// </summary>
public class ReceiptEmailTests : IAsyncLifetime
{
    private readonly FixedClock _clock = new();
    private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Admin();
    private readonly StubSettingsService _settings = new();
    private readonly CapturingEmailSender _sender = new();

    private GymDbContext _db = null!;
    private ReferenceData _data = null!;
    private Member _member = null!;
    private PaymentReceiptMailer _mailer = null!;

    public async Task InitializeAsync()
    {
        _db = InMemoryDbContextFactory.Create(_currentUser, _clock);
        _data = await InMemoryDbContextFactory.SeedReferenceDataAsync(_db);
        _member = await InMemoryDbContextFactory.AddMemberAsync(_db, "Ravi Kumar");

        _member.Email = "ravi.kumar@example.test";
        await _db.SaveChangesAsync();

        _mailer = NewMailer();
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    private PaymentReceiptMailer NewMailer() => new(
        _db, _settings, new StubPdfExportService(), new ReceiptEmailBuilder(), _sender, _clock,
        NullLogger<PaymentReceiptMailer>.Instance);

    private async Task<Payment> AddPaymentAsync(
        PaymentStatus status = PaymentStatus.Paid, int? memberId = null)
    {
        var payment = new Payment
        {
            ReceiptNumber = $"RCP-2026-{Random.Shared.Next(100_000, 999_999)}",
            MemberId = memberId ?? _member.Id,
            MembershipPlanId = _data.AnnualPlanId,
            Amount = 3000m,
            DiscountAmount = 200m,
            TaxAmount = 504m,
            FinalAmount = 3304m,
            PaymentMethodId = _data.CashMethodId,
            PaymentDate = _clock.Now,
            Status = status,
            CollectedByUserId = _data.AdminUserId
        };

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return payment;
    }

    private Task<DateTime?> StampAsync(int paymentId) => _db.Payments
        .AsNoTracking()
        .Where(p => p.Id == paymentId)
        .Select(p => p.ReceiptEmailedAtUtc)
        .FirstAsync();

    // ============================================================ provider selection

    [Fact]
    public async Task No_configuration_outside_development_sends_nothing_and_throws_nothing()
    {
        // A fresh checkout: no Email section at all.
        var sender = EmailSenderFactory.Create(null, NullLoggerFactory.Instance, isDevelopment: false);

        sender.IsEnabled.Should().BeFalse();
        sender.ProviderName.Should().Be("None");

        var result = await sender.SendAsync(new EmailMessage
        {
            To = { "someone@example.test" },
            Subject = "Receipt",
            TextBody = "body"
        });

        result.WasSent.Should().BeFalse();
        result.Status.Should().Be(EmailDeliveryStatus.Skipped);
    }

    [Fact]
    public void No_configuration_in_development_falls_back_to_the_local_file_sink()
    {
        var sender = EmailSenderFactory.Create(null, NullLoggerFactory.Instance, isDevelopment: true);

        sender.ProviderName.Should().Be("File");
        sender.Should().BeOfType<FileDropEmailSender>();
    }

    [Theory]
    // Asked for SMTP but gave no host, or no from-address: degrade to sending nothing rather than
    // throwing at startup.
    [InlineData("Smtp", null, "someone@example.test")]
    [InlineData("Smtp", "smtp.example.test", null)]
    [InlineData("Carrier pigeon", "smtp.example.test", "someone@example.test")]
    public void A_provider_that_cannot_work_is_disabled_rather_than_fatal(
        string provider, string? host, string? from)
    {
        var sender = EmailSenderFactory.Create(
            new EmailOptions
            {
                Provider = provider,
                FromAddress = from,
                Smtp = new EmailSmtpOptions { Host = host }
            },
            NullLoggerFactory.Instance,
            isDevelopment: false);

        sender.IsEnabled.Should().BeFalse();
        sender.ProviderName.Should().Be("None");
    }

    // ============================================================ the local file sink

    [Fact]
    public async Task The_file_sink_writes_the_message_to_disk_and_transmits_nothing()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"gym-mail-{Guid.NewGuid():N}");

        try
        {
            var sink = new FileDropEmailSender(
                new EmailOptions
                {
                    Provider = "File",
                    FromAddress = "no-reply@gym.invalid",
                    FromName = "Test Gym",
                    FileSink = new EmailFileSinkOptions { Directory = folder }
                },
                NullLogger<FileDropEmailSender>.Instance);

            var result = await sink.SendAsync(new EmailMessage
            {
                To = { "ravi.kumar@example.test" },
                Subject = "Payment receipt RCP-2026-000123 — Test Gym",
                HtmlBody = "<table><tr><td>Total payable</td></tr></table>",
                TextBody = "TOTAL PAYABLE                             ₹ 3,304.00",
                Attachments =
                {
                    new EmailAttachment("Receipt_RCP-2026-000123.pdf", "application/pdf",
                        new byte[] { 0x25, 0x50, 0x44, 0x46 })
                }
            });

            result.WasSent.Should().BeTrue();

            Directory.GetFiles(folder, "*.eml").Should().ContainSingle();

            var summary = await File.ReadAllTextAsync(Directory.GetFiles(folder, "*.txt").Single());
            summary.Should().Contain("ravi.kumar@example.test");
            summary.Should().Contain("Payment receipt RCP-2026-000123");
            summary.Should().Contain("Receipt_RCP-2026-000123.pdf");
            summary.Should().Contain("TOTAL PAYABLE");

            // Nothing is left behind between the drop folder and the message files.
            Directory.GetDirectories(folder).Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    // ============================================================ what the message says

    private static PaymentReceiptDto SampleReceipt() => new()
    {
        ReceiptNumber = "RCP-2026-000123",
        PaymentDate = new DateTime(2026, 8, 18, 14, 22, 0),
        GymName = "Test Gym",
        GymAddress = "12 MG Road, Bengaluru",
        GymPhone = "9000000000",
        GymEmail = "hello@gym.test",
        TaxNumber = "29ABCDE1234F1Z5",
        CurrencySymbol = "₹",
        FooterText = "Thank you for training with us.",
        MemberCode = "GYM-2026-0001",
        MemberName = "Ravi Kumar",
        MemberPhone = "9000000001",
        PlanName = "Annual",
        SubscriptionStartDate = new DateTime(2026, 8, 18),
        SubscriptionEndDate = new DateTime(2027, 8, 17),
        Amount = 3000m,
        DiscountAmount = 200m,
        TaxAmount = 504m,
        FinalAmount = 3304m,
        PaidAmount = 3304m,
        OutstandingAmount = 0m,
        AmountInWords = "Rupees Three Thousand Three Hundred Four Only",
        PaymentMethodName = "Cash",
        TransactionReference = "UTR20260818001",
        StatusText = "Paid",
        CollectedByName = "System Administrator"
    };

    [Fact]
    public void The_message_carries_the_amounts_the_method_and_the_reference()
    {
        var built = new ReceiptEmailBuilder().Build(SampleReceipt());

        built.Subject.Should().Contain("RCP-2026-000123").And.Contain("Test Gym");

        foreach (var body in new[] { built.HtmlBody, built.TextBody })
        {
            body.Should().Contain("RCP-2026-000123");
            body.Should().Contain("Ravi Kumar");
            body.Should().Contain("Annual");
            body.Should().Contain("3,304.00");
            body.Should().Contain("Cash");
            body.Should().Contain("UTR20260818001");
            body.Should().Contain("Rupees Three Thousand Three Hundred Four Only");
            body.Should().Contain("PAID IN FULL");
        }
    }

    [Fact]
    public void The_breakdown_visibly_adds_up_in_both_bodies()
    {
        var built = new ReceiptEmailBuilder().Build(SampleReceipt());

        foreach (var body in new[] { built.HtmlBody, built.TextBody })
        {
            body.Should().Contain("3,000.00");   // amount
            body.Should().Contain("200.00");     // less discount
            body.Should().Contain("504.00");     // add tax
            body.Should().Contain("3,304.00");   // total, and it is 3000 - 200 + 504
            body.Should().Contain("discount");
            body.Should().Contain("tax");
        }
    }

    [Fact]
    public void The_message_never_carries_card_cvv_pin_or_password_data()
    {
        var receipt = SampleReceipt();
        var built = new ReceiptEmailBuilder().Build(receipt);

        foreach (var body in new[] { built.Subject, built.HtmlBody, built.TextBody })
        {
            body.Should().NotContainEquivalentOf("cvv");
            body.Should().NotContainEquivalentOf("pin");
            body.Should().NotContainEquivalentOf("password");

            // Nothing shaped like a card number ever reaches the message: the receipt model has no
            // field that could hold one, and this pins that down against future edits.
            Regex.IsMatch(body, @"\d[\d \-]{11,}\d").Should().BeFalse(
                "a receipt must never contain anything shaped like a card number");
        }
    }

    [Fact]
    public void The_plain_text_alternative_stands_on_its_own()
    {
        var text = new ReceiptEmailBuilder().Build(SampleReceipt()).TextBody;

        text.Should().NotContain("<");
        text.Should().Contain("PAYMENT RECEIPT");
        text.Should().Contain("Hello Ravi Kumar,");
        text.Should().Contain("TOTAL PAYABLE");
        text.Should().Contain("Amount in words:");

        // The money column is right-aligned, so every amount line ends with the figure.
        var total = text.Split('\n').Single(l => l.StartsWith("TOTAL PAYABLE", StringComparison.Ordinal));
        total.TrimEnd().Should().EndWith("3,304.00");
    }

    [Fact]
    public void An_outstanding_balance_is_stated_in_words_not_by_colour()
    {
        var receipt = SampleReceipt();
        receipt.PaidAmount = 1000m;
        receipt.OutstandingAmount = 2304m;

        var built = new ReceiptEmailBuilder().Build(receipt);

        built.TextBody.Should().Contain("PART PAYMENT RECEIVED");
        built.TextBody.Should().Contain("2,304.00");
        built.HtmlBody.Should().Contain("PART PAYMENT RECEIVED");
    }

    // ============================================================ the mailer

    [Fact]
    public async Task A_settled_payment_is_emailed_once_and_stamped()
    {
        var payment = await AddPaymentAsync();

        var outcome = await _mailer.SendReceiptAsync(payment.Id);

        outcome.Should().Be(ReceiptEmailOutcome.Sent);
        _sender.Sent.Should().ContainSingle();
        _sender.Sent[0].To.Should().ContainSingle().Which.Should().Be("ravi.kumar@example.test");
        _sender.Sent[0].Attachments.Should().ContainSingle()
            .Which.ContentType.Should().Be("application/pdf");

        (await StampAsync(payment.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task The_same_payment_is_never_emailed_twice()
    {
        var payment = await AddPaymentAsync();

        (await _mailer.SendReceiptAsync(payment.Id)).Should().Be(ReceiptEmailOutcome.Sent);
        var firstStamp = await StampAsync(payment.Id);

        // Whatever replays the flow — a retried request, a second confirmation, a fresh scope —
        // the persisted stamp is what stops it.
        (await _mailer.SendReceiptAsync(payment.Id)).Should().Be(ReceiptEmailOutcome.AlreadySent);
        (await NewMailer().SendReceiptAsync(payment.Id)).Should().Be(ReceiptEmailOutcome.AlreadySent);

        _sender.Sent.Should().ContainSingle("the receipt goes out exactly once per payment");
        (await StampAsync(payment.Id)).Should().Be(firstStamp);
    }

    [Fact]
    public async Task A_member_with_no_address_on_file_is_not_emailed()
    {
        var other = await InMemoryDbContextFactory.AddMemberAsync(_db, "No Email Member");
        var payment = await AddPaymentAsync(memberId: other.Id);

        (await _mailer.SendReceiptAsync(payment.Id)).Should().Be(ReceiptEmailOutcome.NoRecipient);

        _sender.Sent.Should().BeEmpty();
        (await StampAsync(payment.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Nothing_is_sent_when_no_provider_is_configured()
    {
        _sender.IsEnabled = false;
        var payment = await AddPaymentAsync();

        (await _mailer.SendReceiptAsync(payment.Id)).Should().Be(ReceiptEmailOutcome.Disabled);

        _sender.Sent.Should().BeEmpty();
        (await StampAsync(payment.Id)).Should().BeNull();
    }

    [Fact]
    public async Task An_unconfirmed_payment_waits_for_its_receipt()
    {
        var payment = await AddPaymentAsync(PaymentStatus.AwaitingConfirmation);

        (await _mailer.SendReceiptAsync(payment.Id)).Should().Be(ReceiptEmailOutcome.NotSettled);

        _sender.Sent.Should().BeEmpty();
        (await StampAsync(payment.Id)).Should().BeNull();
    }

    [Fact]
    public async Task A_provider_that_throws_is_swallowed_and_leaves_the_payment_unstamped()
    {
        _sender.ThrowOnSend = true;
        var payment = await AddPaymentAsync();

        var send = async () => await _mailer.SendReceiptAsync(payment.Id);

        (await send.Should().NotThrowAsync()).Which.Should().Be(ReceiptEmailOutcome.Failed);

        // Unstamped on purpose: nothing was delivered, so a later attempt is still allowed.
        (await StampAsync(payment.Id)).Should().BeNull();
    }

    [Fact]
    public async Task A_provider_that_declines_the_message_leaves_the_payment_unstamped()
    {
        _sender.SkipEverything = true;
        var payment = await AddPaymentAsync();

        (await _mailer.SendReceiptAsync(payment.Id)).Should().Be(ReceiptEmailOutcome.Failed);
        (await StampAsync(payment.Id)).Should().BeNull();
    }
}
