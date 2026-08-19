using System.Globalization;
using System.Net;
using System.Text;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Infrastructure.Common;

namespace GymManagement.Infrastructure.Reporting;

/// <summary>
/// Renders a receipt for a mail client.
///
/// Constraints that shape everything below:
/// <list type="bullet">
///   <item>Tables for layout, not divs — Outlook still lays out with Word's HTML engine.</item>
///   <item>Inline styles only; most clients strip &lt;style&gt; blocks and none load a stylesheet.</item>
///   <item>No external image, no web font, no script — nothing that needs the network or gets blocked.</item>
///   <item>
///     A plain-text alternative that is genuinely readable on its own, with the money in an
///     aligned column, rather than tags stripped out of the HTML.
///   </item>
/// </list>
///
/// The message carries amounts, the payment method name and the transaction reference. It never
/// carries a card number, CVV, UPI PIN or password: the source
/// <see cref="PaymentReceiptDto"/> holds none of them, and none is looked up here.
/// </summary>
public sealed class ReceiptEmailBuilder : IReceiptEmailBuilder
{
    /// <summary>Width of the right-aligned money column in the plain-text body.</summary>
    private const int TextWidth = 62;

    private const int MoneyColumn = 20;

    public ReceiptEmailContent Build(PaymentReceiptDto receipt)
    {
        var model = receipt ?? new PaymentReceiptDto();
        var gymName = string.IsNullOrWhiteSpace(model.GymName) ? "Gym" : model.GymName.Trim();

        return new ReceiptEmailContent(
            BuildSubject(model, gymName), BuildHtml(model, gymName), BuildText(model, gymName));
    }

    /// <summary>
    /// The subject names the occasion, not just the paperwork. A member joining sees a welcome, a
    /// returning one sees that their membership continues, and money taken for anything else stays
    /// a plain receipt. The receipt number is kept in every case so it is searchable in a mailbox.
    /// </summary>
    private static string BuildSubject(PaymentReceiptDto m, string gymName)
    {
        var number = string.IsNullOrWhiteSpace(m.ReceiptNumber) ? null : m.ReceiptNumber.Trim();
        var plan = string.IsNullOrWhiteSpace(m.PlanName) ? null : m.PlanName.Trim();
        var reference = number is null ? string.Empty : $" ({number})";

        return m.Purpose switch
        {
            ReceiptPurpose.NewMembership => plan is null
                ? $"Welcome to {gymName} — your membership is active{reference}"
                : $"Welcome to {gymName} — your {plan} membership is active{reference}",

            ReceiptPurpose.Renewal => plan is null
                ? $"Your {gymName} membership is renewed{reference}"
                : $"Your {plan} membership at {gymName} is renewed{reference}",

            _ => number is null
                ? $"Payment receipt — {gymName}"
                : $"Payment receipt {number} — {gymName}",
        };
    }

    /// <summary>
    /// Greedy word wrap for the plain-text body, so the opening paragraph stays inside the same
    /// column as the money table however the wording changes.
    /// </summary>
    private static IEnumerable<string> Wrap(string text, int width)
    {
        var line = new StringBuilder(width);

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }

        if (line.Length > 0) yield return line.ToString();
    }

    /// <summary>The opening line, matched to the same occasion as the subject.</summary>
    private static string Opening(PaymentReceiptDto m) => m.Purpose switch
    {
        ReceiptPurpose.NewMembership =>
            "Welcome aboard — your membership is now active. Your payment has been received, "
            + "and the details are below.",
        ReceiptPurpose.Renewal =>
            "Thank you — your membership has been renewed and your payment has been received. "
            + "The new term and the details are below.",
        _ =>
            "Thank you — we have received your payment. The details are below.",
    };

    // ------------------------------------------------------------------------------------ HTML

    private static string BuildHtml(PaymentReceiptDto m, string gymName)
    {
        var currency = m.CurrencySymbol;
        var (headline, detail) = ReceiptPresentation.Status(m);

        var html = new StringBuilder(8_000);

        html.Append(
            "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" " +
            "style=\"margin:0;padding:24px 8px;background-color:#f2f2f4;" +
            "font-family:Segoe UI,Helvetica,Arial,sans-serif;\"><tr><td align=\"center\">");

        html.Append(
            "<table role=\"presentation\" width=\"620\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" " +
            "style=\"width:100%;max-width:620px;background-color:#ffffff;border:1px solid #c9c9cf;\">");

        // ---- gym header
        html.Append("<tr><td style=\"padding:26px 30px 18px 30px;border-bottom:3px solid #1a1a1a;\">")
            .Append("<div style=\"font-size:22px;font-weight:700;color:#111111;letter-spacing:0.4px;\">")
            .Append(E(gymName)).Append("</div>");

        AppendIf(html, m.GymAddress,
            v => $"<div style=\"font-size:12px;color:#55555f;padding-top:5px;\">{E(v)}</div>");

        var contact = ReceiptPresentation.Join("&nbsp;&nbsp;|&nbsp;&nbsp;",
            Label("Phone", m.GymPhone), Label("Email", m.GymEmail), Label("Tax No", m.TaxNumber));

        AppendIf(html, contact,
            v => $"<div style=\"font-size:12px;color:#55555f;padding-top:3px;\">{v}</div>");

        html.Append("</td></tr>");

        // ---- title + receipt number / date
        html.Append("<tr><td style=\"padding:20px 30px 4px 30px;\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr>")
            .Append("<td style=\"font-size:15px;font-weight:700;color:#111111;letter-spacing:1.6px;\">PAYMENT RECEIPT</td>")
            .Append("<td align=\"right\" style=\"font-size:12px;color:#55555f;\">")
            .Append(E(ReceiptPresentation.DateTimeStamp(m.PaymentDate)))
            .Append("</td></tr></table>")
            .Append("<div style=\"font-size:13px;color:#111111;padding-top:6px;\">Receipt no <strong>")
            .Append(E(ReceiptPresentation.OrDash(m.ReceiptNumber)))
            .Append("</strong></div></td></tr>");

        // ---- greeting
        html.Append("<tr><td style=\"padding:16px 30px 0 30px;font-size:14px;color:#26262c;line-height:1.55;\">")
            .Append("Hello ").Append(E(ReceiptPresentation.OrDash(m.MemberName))).Append(",<br>")
            .Append(E(Opening(m)))
            .Append(" The same receipt is attached to this email as a PDF.")
            .Append("</td></tr>");

        // ---- member / membership
        html.Append("<tr><td style=\"padding:18px 30px 0 30px;\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\">")
            .Append("<tr valign=\"top\">");

        html.Append("<td width=\"50%\" style=\"padding:12px 14px;border:1px solid #d8d8de;\">")
            .Append(Caption("Member"))
            .Append(Pair("Name", m.MemberName))
            .Append(Pair("Member ID", m.MemberCode))
            .Append(Pair("Phone", m.MemberPhone))
            .Append("</td><td width=\"8\"></td>");

        html.Append("<td width=\"50%\" style=\"padding:12px 14px;border:1px solid #d8d8de;\">")
            .Append(Caption("Membership"))
            .Append(Pair("Plan", m.PlanName));

        if (m.SubscriptionStartDate.HasValue || m.SubscriptionEndDate.HasValue)
            html.Append(Pair("Valid", ReceiptPresentation.Term(m)));

        html.Append(Pair("Received by", m.CollectedByName))
            .Append("</td></tr></table></td></tr>");

        // ---- itemised breakdown
        html.Append("<tr><td style=\"padding:20px 30px 0 30px;\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" " +
                    "style=\"border-collapse:collapse;font-size:13px;color:#1a1a1a;\">");

        html.Append("<tr>")
            .Append("<th align=\"left\" style=\"padding:9px 10px;background-color:#ececef;border:1px solid #d8d8de;" +
                    "font-size:11px;letter-spacing:1px;text-transform:uppercase;color:#3a3a42;\">Description</th>")
            .Append("<th align=\"right\" style=\"padding:9px 10px;background-color:#ececef;border:1px solid #d8d8de;" +
                    "font-size:11px;letter-spacing:1px;text-transform:uppercase;color:#3a3a42;\">Amount</th>")
            .Append("</tr>");

        html.Append(Row(ReceiptPresentation.LineItemDescription(m), ReceiptPresentation.Money(m.Amount, currency)));

        if (m.DiscountAmount > 0m)
            html.Append(Row("Less: discount", ReceiptPresentation.MoneyDeducted(m.DiscountAmount, currency)));

        if (m.TaxAmount > 0m)
            html.Append(Row("Add: tax", ReceiptPresentation.MoneyAdded(m.TaxAmount, currency)));

        html.Append(Row("Total payable", ReceiptPresentation.Money(m.FinalAmount, currency), strong: true));
        html.Append(Row("Amount paid — this receipt", ReceiptPresentation.Money(m.FinalAmount, currency)));

        if (m.PaidAmount != m.FinalAmount)
            html.Append(Row("Paid to date", ReceiptPresentation.Money(m.PaidAmount, currency)));

        html.Append(Row("Balance outstanding", ReceiptPresentation.Money(m.OutstandingAmount, currency),
            strong: m.OutstandingAmount > 0m));

        html.Append("</table></td></tr>");

        // ---- amount in words
        AppendIf(html, m.AmountInWords, v =>
            "<tr><td style=\"padding:10px 30px 0 30px;\">" +
            "<div style=\"padding:10px 12px;border:1px solid #d8d8de;background-color:#fafafb;" +
            "font-size:12px;color:#26262c;\"><span style=\"color:#55555f;\">Amount in words:</span> " +
            $"<em>{E(v)}</em></div></td></tr>");

        // ---- payment details
        html.Append("<tr><td style=\"padding:16px 30px 0 30px;\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" " +
                    "style=\"border:1px solid #d8d8de;\"><tr valign=\"top\">")
            .Append("<td width=\"50%\" style=\"padding:12px 14px;\">")
            .Append(Caption("Payment"))
            .Append(Pair("Method", m.PaymentMethodName))
            .Append(Pair("Reference", m.TransactionReference))
            .Append("</td>")
            .Append("<td width=\"50%\" style=\"padding:12px 14px;\">")
            .Append(Caption("Status"))
            .Append(Pair("Recorded as", ReceiptPresentation.Humanise(m.StatusText)))
            .Append(Pair("Date", ReceiptPresentation.DateTimeStamp(m.PaymentDate)))
            .Append("</td></tr></table></td></tr>");

        // ---- verdict. Bordered and in words, never colour alone.
        html.Append("<tr><td style=\"padding:16px 30px 0 30px;\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" " +
                    "style=\"border:2px solid #1a1a1a;background-color:#fafafb;\"><tr>")
            .Append("<td style=\"padding:12px 14px;\">")
            .Append("<div style=\"font-size:14px;font-weight:700;letter-spacing:1.2px;color:#111111;\">")
            .Append(E(headline)).Append("</div>")
            .Append("<div style=\"font-size:12px;color:#3a3a42;padding-top:3px;\">").Append(E(detail))
            .Append("</div></td></tr></table></td></tr>");

        AppendIf(html, m.Notes, v =>
            "<tr><td style=\"padding:14px 30px 0 30px;font-size:12px;color:#55555f;\">" +
            $"<strong>Notes:</strong> {E(v)}</td></tr>");

        // ---- footer
        html.Append("<tr><td style=\"padding:22px 30px 26px 30px;\">")
            .Append("<div style=\"border-top:1px solid #d8d8de;padding-top:12px;font-size:11px;" +
                    "color:#6a6a74;line-height:1.6;\">");

        AppendIf(html, m.FooterText, v => $"<div style=\"padding-bottom:4px;\"><em>{E(v)}</em></div>");

        html.Append("This is a computer-generated receipt and is valid without a signature.");

        var reachUs = ReceiptPresentation.Join("&nbsp;&nbsp;|&nbsp;&nbsp;",
            Label("Phone", m.GymPhone), Label("Email", m.GymEmail));

        AppendIf(html, reachUs, v => $"<div style=\"padding-top:4px;\">Questions about this receipt? {v}</div>");

        html.Append("</div></td></tr>");

        html.Append("</table></td></tr></table>");

        return html.ToString();
    }

    private static string Caption(string text) =>
        "<div style=\"font-size:10px;letter-spacing:1.3px;text-transform:uppercase;color:#6a6a74;" +
        $"padding-bottom:7px;\">{E(text)}</div>";

    private static string Pair(string label, string? value) =>
        "<div style=\"font-size:13px;color:#1a1a1a;padding-top:2px;\">" +
        $"<span style=\"color:#55555f;\">{E(label)}:</span> {E(ReceiptPresentation.OrDash(value))}</div>";

    private static string Row(string label, string amount, bool strong = false)
    {
        var weight = strong ? "700" : "400";
        var background = strong ? "#f4f4f6" : "#ffffff";

        return "<tr>" +
               $"<td style=\"padding:9px 10px;border:1px solid #d8d8de;background-color:{background};font-weight:{weight};\">{E(label)}</td>" +
               $"<td align=\"right\" style=\"padding:9px 10px;border:1px solid #d8d8de;background-color:{background};" +
               $"font-weight:{weight};white-space:nowrap;\">{E(amount)}</td></tr>";
    }

    private static string? Label(string caption, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"{E(caption)}: {E(value)}";

    private static void AppendIf(StringBuilder builder, string? value, Func<string, string> render)
    {
        if (!string.IsNullOrWhiteSpace(value)) builder.Append(render(value.Trim()));
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    // ------------------------------------------------------------------------------ plain text

    private static string BuildText(PaymentReceiptDto m, string gymName)
    {
        var currency = m.CurrencySymbol;
        var (headline, detail) = ReceiptPresentation.Status(m);
        var text = new StringBuilder(2_000);

        text.AppendLine(gymName.ToUpperInvariant());

        AppendLineIf(text, m.GymAddress);
        AppendLineIf(text, ReceiptPresentation.Join("  |  ",
            Plain("Phone", m.GymPhone), Plain("Email", m.GymEmail), Plain("Tax No", m.TaxNumber)));

        text.AppendLine();
        text.AppendLine("PAYMENT RECEIPT");
        text.AppendLine(new string('=', TextWidth));
        text.AppendLine();

        text.AppendLine($"Hello {ReceiptPresentation.OrDash(m.MemberName)},");
        text.AppendLine();
        foreach (var line in Wrap(Opening(m) + " The same receipt is attached to this email as a PDF.", TextWidth))
            text.AppendLine(line);
        text.AppendLine();

        AppendField(text, "Receipt no", ReceiptPresentation.OrDash(m.ReceiptNumber));
        AppendField(text, "Date", ReceiptPresentation.DateTimeStamp(m.PaymentDate));
        AppendField(text, "Member", $"{ReceiptPresentation.OrDash(m.MemberName)} ({ReceiptPresentation.OrDash(m.MemberCode)})");

        if (!string.IsNullOrWhiteSpace(m.PlanName))
            AppendField(text, "Plan", m.PlanName!);

        if (m.SubscriptionStartDate.HasValue || m.SubscriptionEndDate.HasValue)
            AppendField(text, "Valid", ReceiptPresentation.Term(m));

        text.AppendLine();
        text.AppendLine(new string('-', TextWidth));
        AppendAmount(text, ReceiptPresentation.LineItemDescription(m), ReceiptPresentation.Money(m.Amount, currency));

        if (m.DiscountAmount > 0m)
            AppendAmount(text, "Less: discount", ReceiptPresentation.MoneyDeducted(m.DiscountAmount, currency));

        if (m.TaxAmount > 0m)
            AppendAmount(text, "Add: tax", ReceiptPresentation.MoneyAdded(m.TaxAmount, currency));

        text.AppendLine(new string('-', TextWidth));
        AppendAmount(text, "TOTAL PAYABLE", ReceiptPresentation.Money(m.FinalAmount, currency));
        AppendAmount(text, "Amount paid - this receipt", ReceiptPresentation.Money(m.FinalAmount, currency));

        if (m.PaidAmount != m.FinalAmount)
            AppendAmount(text, "Paid to date", ReceiptPresentation.Money(m.PaidAmount, currency));

        AppendAmount(text, "Balance outstanding", ReceiptPresentation.Money(m.OutstandingAmount, currency));
        text.AppendLine(new string('=', TextWidth));

        if (!string.IsNullOrWhiteSpace(m.AmountInWords))
        {
            text.AppendLine();
            text.AppendLine($"Amount in words: {m.AmountInWords.Trim()}");
        }

        text.AppendLine();
        AppendField(text, "Payment method", ReceiptPresentation.OrDash(m.PaymentMethodName));
        AppendField(text, "Reference", ReceiptPresentation.OrDash(m.TransactionReference));
        AppendField(text, "Recorded as", ReceiptPresentation.Humanise(m.StatusText));
        AppendField(text, "Received by", ReceiptPresentation.OrDash(m.CollectedByName));

        text.AppendLine();
        text.AppendLine($"** {headline} **");
        text.AppendLine(detail);

        if (!string.IsNullOrWhiteSpace(m.Notes))
        {
            text.AppendLine();
            text.AppendLine($"Notes: {m.Notes.Trim()}");
        }

        text.AppendLine();
        text.AppendLine(new string('-', TextWidth));

        AppendLineIf(text, m.FooterText);
        text.AppendLine("This is a computer-generated receipt and is valid without a signature.");

        var reachUs = ReceiptPresentation.Join("  |  ", Plain("Phone", m.GymPhone), Plain("Email", m.GymEmail));
        if (!string.IsNullOrWhiteSpace(reachUs))
            text.AppendLine($"Questions about this receipt? {reachUs}");

        return text.ToString();
    }

    /// <summary>Label on the left, amount right-aligned to a fixed column so the figures line up.</summary>
    private static void AppendAmount(StringBuilder text, string label, string amount)
    {
        var room = Math.Max(1, TextWidth - MoneyColumn);
        var left = label.Length > room ? label[..room] : label.PadRight(room);

        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"{left}{amount.PadLeft(MoneyColumn)}"));
    }

    private static void AppendField(StringBuilder text, string label, string value) =>
        text.AppendLine($"{label.PadRight(16)}: {value}");

    private static void AppendLineIf(StringBuilder text, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) text.AppendLine(value.Trim());
    }

    private static string? Plain(string caption, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"{caption}: {value.Trim()}";
}
