using System.Net;

namespace GymManagement.Infrastructure.Messaging;

/// <summary>
/// The wording of every message a member receives directly, in one place so that the six occasions
/// read as one gym talking rather than six features.
///
/// Each occasion renders four things at once: an email subject, an HTML body, a plain-text body
/// that stands on its own, and the WhatsApp template parameters with a plain-text preview of what
/// they will render to. They are built together deliberately — a WhatsApp parameter list that
/// drifts out of step with the approved template is a message the member never sees, and the
/// preview beside it in the Development file drop is what catches that.
///
/// The HTML follows the same rules as the rest of the outbound mail: inline styles only, no
/// external images, scripts or web fonts, so it renders in a locked-down client and cannot phone
/// home from the member's inbox.
/// </summary>
internal static class MemberMessageTemplates
{
    /// <summary>One occasion, rendered for both channels.</summary>
    internal sealed record Rendered(
        string Subject,
        string HtmlBody,
        string TextBody,
        string WhatsAppPreview,
        IReadOnlyList<string> WhatsAppParameters);

    // =====================================================================
    //  Money
    // =====================================================================

    /// <summary>
    /// The first payment of a member's first membership: a welcome, not an invoice.
    ///
    /// WhatsApp template body to submit for approval — <c>payment_received</c>:
    /// <code>
    /// Welcome to {{1}}, {{2}}! We have received {{3}} for your {{4}} membership.
    /// Receipt {{5}}. Your membership runs until {{6}}. See you at the gym.
    /// </code>
    /// </summary>
    public static Rendered PaymentReceived(
        string gymName, string memberName, string planName, string amount,
        string receiptNumber, string validUntil)
    {
        var subject = $"Welcome to {gymName} — payment of {amount} received";

        var html = Shell(gymName,
            $"Welcome to {gymName}, {FirstWord(memberName)}!",
            $"<p style=\"margin:0 0 14px\">We have received <strong>{E(amount)}</strong> for your " +
            $"<strong>{E(planName)}</strong> membership. You are all set.</p>" +
            Facts(("Receipt", receiptNumber), ("Plan", planName), ("Paid", amount), ("Valid until", validUntil)) +
            "<p style=\"margin:14px 0 0\">Come in whenever you are ready — the front desk will show you " +
            "around and your trainer will take it from there.</p>",
            "This confirmation was sent automatically when your payment was recorded.");

        var text = Text(
            $"Welcome to {gymName}, {FirstWord(memberName)}!",
            "",
            $"We have received {amount} for your {planName} membership. You are all set.",
            "",
            $"Receipt     : {receiptNumber}",
            $"Plan        : {planName}",
            $"Paid        : {amount}",
            $"Valid until : {validUntil}",
            "",
            "Come in whenever you are ready — the front desk will show you around and your",
            "trainer will take it from there.",
            "",
            $"{gymName} · This confirmation was sent automatically when your payment was recorded.");

        return new Rendered(subject, html, text,
            $"Welcome to {gymName}, {FirstWord(memberName)}! We have received {amount} for your " +
            $"{planName} membership. Receipt {receiptNumber}. Your membership runs until {validUntil}. " +
            "See you at the gym.",
            new[] { gymName, FirstWord(memberName), amount, planName, receiptNumber, validUntil });
    }

    /// <summary>
    /// A renewal: shorter, warmer, and the end date is the fact that matters.
    ///
    /// WhatsApp template body to submit for approval — <c>payment_renewal</c>:
    /// <code>
    /// Thanks {{1}}! Your {{2}} membership at {{3}} is renewed. We received {{4}},
    /// receipt {{5}}, and you are covered until {{6}}. Keep going.
    /// </code>
    /// </summary>
    public static Rendered RenewalPayment(
        string gymName, string memberName, string planName, string amount,
        string receiptNumber, string validUntil)
    {
        var subject = $"Membership renewed until {validUntil} — {gymName}";

        var html = Shell(gymName,
            $"Thanks, {FirstWord(memberName)} — you are renewed.",
            $"<p style=\"margin:0 0 14px\">We have received <strong>{E(amount)}</strong> and your " +
            $"<strong>{E(planName)}</strong> membership now runs to <strong>{E(validUntil)}</strong>. " +
            "Nothing changes and nothing pauses — carry straight on.</p>" +
            Facts(("Receipt", receiptNumber), ("Plan", planName), ("Paid", amount), ("Valid until", validUntil)),
            "This confirmation was sent automatically when your payment was recorded.");

        var text = Text(
            $"Thanks, {FirstWord(memberName)} — you are renewed.",
            "",
            $"We have received {amount} and your {planName} membership now runs to {validUntil}.",
            "Nothing changes and nothing pauses — carry straight on.",
            "",
            $"Receipt     : {receiptNumber}",
            $"Plan        : {planName}",
            $"Paid        : {amount}",
            $"Valid until : {validUntil}",
            "",
            $"{gymName} · This confirmation was sent automatically when your payment was recorded.");

        return new Rendered(subject, html, text,
            $"Thanks {FirstWord(memberName)}! Your {planName} membership at {gymName} is renewed. " +
            $"We received {amount}, receipt {receiptNumber}, and you are covered until {validUntil}. " +
            "Keep going.",
            new[] { FirstWord(memberName), planName, gymName, amount, receiptNumber, validUntil });
    }

    // =====================================================================
    //  Training
    // =====================================================================

    /// <summary>
    /// A diet plan written or revised. The meals themselves are not listed: a plan is read in the
    /// app where it can be kept up to date, and a stale copy in an inbox is worse than no copy.
    ///
    /// WhatsApp template body to submit for approval — <c>diet_plan_ready</c>:
    /// <code>
    /// Hi {{1}}, your diet plan "{{2}}" at {{3}} is {{4}}. {{5}} meals, starting {{6}}.
    /// Open the app to see it.
    /// </code>
    /// </summary>
    public static Rendered DietPlan(
        string gymName, string memberName, string title, string? goal,
        int mealCount, string startDate, bool isRevision)
    {
        var state = isRevision ? "been updated" : "ready";
        var stateShort = isRevision ? "updated" : "ready";
        var subject = isRevision
            ? $"Your diet plan has been updated — {gymName}"
            : $"Your diet plan is ready — {gymName}";

        var goalLine = string.IsNullOrWhiteSpace(goal)
            ? string.Empty
            : $"<p style=\"margin:0 0 14px\">Goal: <strong>{E(goal)}</strong>.</p>";

        var html = Shell(gymName,
            $"Hi {FirstWord(memberName)}, your diet plan has {state}.",
            $"<p style=\"margin:0 0 14px\"><strong>{E(title)}</strong> — {mealCount} meal" +
            $"{(mealCount == 1 ? string.Empty : "s")}, starting {E(startDate)}.</p>" +
            goalLine +
            "<p style=\"margin:0\">Open the app to see the full plan, meal by meal, with the macros " +
            "for each one. Your trainer can adjust it whenever you need.</p>",
            "Sent automatically when your trainer saved the plan.");

        var text = Text(
            $"Hi {FirstWord(memberName)}, your diet plan has {state}.",
            "",
            $"{title} — {mealCount} meal{(mealCount == 1 ? string.Empty : "s")}, starting {startDate}.",
            string.IsNullOrWhiteSpace(goal) ? null : $"Goal: {goal}.",
            "",
            "Open the app to see the full plan, meal by meal, with the macros for each one.",
            "Your trainer can adjust it whenever you need.",
            "",
            $"{gymName} · Sent automatically when your trainer saved the plan.");

        return new Rendered(subject, html, text,
            $"Hi {FirstWord(memberName)}, your diet plan \"{title}\" at {gymName} is {stateShort}. " +
            $"{mealCount} meals, starting {startDate}. Open the app to see it.",
            new[] { FirstWord(memberName), title, gymName, stateShort, mealCount.ToString(), startDate });
    }

    /// <summary>
    /// A streak milestone. Congratulation only — no upsell, no offer. A member who has trained
    /// thirty days straight is not a sales lead.
    ///
    /// WhatsApp template body to submit for approval — <c>streak_milestone</c>:
    /// <code>
    /// {{1}} days in a row, {{2}}! That is a real habit now. Everyone at {{3}} is
    /// cheering you on — see you tomorrow.
    /// </code>
    /// </summary>
    public static Rendered StreakMilestone(string gymName, string memberName, int days)
    {
        var subject = $"{days} days in a row — well done, {FirstWord(memberName)}";

        var html = Shell(gymName,
            $"{days} days in a row, {FirstWord(memberName)}!",
            "<p style=\"margin:0 0 14px\">That is not motivation any more, that is a habit. " +
            "Turning up on the days you did not feel like it is the whole thing, and you did.</p>" +
            $"<p style=\"margin:0\">Everyone at {E(gymName)} is cheering you on. See you tomorrow.</p>",
            "Sent when a training streak reaches a milestone.");

        var text = Text(
            $"{days} days in a row, {FirstWord(memberName)}!",
            "",
            "That is not motivation any more, that is a habit. Turning up on the days you did",
            "not feel like it is the whole thing, and you did.",
            "",
            $"Everyone at {gymName} is cheering you on. See you tomorrow.",
            "",
            $"{gymName} · Sent when a training streak reaches a milestone.");

        return new Rendered(subject, html, text,
            $"{days} days in a row, {FirstWord(memberName)}! That is a real habit now. " +
            $"Everyone at {gymName} is cheering you on — see you tomorrow.",
            new[] { days.ToString(), FirstWord(memberName), gymName });
    }

    // =====================================================================
    //  Wishes
    // =====================================================================

    /// <summary>
    /// Birthday wishes. No discount code, no "treat yourself" offer: a wish that sells something
    /// is not a wish, and a member who opted out of these opted out of exactly this.
    ///
    /// WhatsApp template body to submit for approval — <c>birthday_wish</c>:
    /// <code>
    /// Happy birthday, {{1}}! Everyone at {{2}} hopes you have a brilliant day.
    /// Whatever you do today, we hope it is exactly what you wanted.
    /// </code>
    /// </summary>
    public static Rendered Birthday(string gymName, string memberName)
    {
        var subject = $"Happy birthday, {FirstWord(memberName)}!";

        var html = Shell(gymName,
            $"Happy birthday, {FirstWord(memberName)}!",
            $"<p style=\"margin:0 0 14px\">Everyone at {E(gymName)} hopes you have a brilliant day.</p>" +
            "<p style=\"margin:0\">Whatever you get up to today, we hope it is exactly what you wanted. " +
            "Rest day fully approved.</p>",
            "Sent once a year, on the date of birth we hold for you.");

        var text = Text(
            $"Happy birthday, {FirstWord(memberName)}!",
            "",
            $"Everyone at {gymName} hopes you have a brilliant day.",
            "",
            "Whatever you get up to today, we hope it is exactly what you wanted.",
            "Rest day fully approved.",
            "",
            $"{gymName} · Sent once a year, on the date of birth we hold for you.");

        return new Rendered(subject, html, text,
            $"Happy birthday, {FirstWord(memberName)}! Everyone at {gymName} hopes you have a " +
            "brilliant day. Whatever you do today, we hope it is exactly what you wanted.",
            new[] { FirstWord(memberName), gymName });
    }

    /// <summary>
    /// Festival wishes. The greeting line comes from the gym's own configuration, so a gym writes
    /// Diwali, Eid, Christmas or Sankranti in its own words rather than in ours.
    ///
    /// WhatsApp template body to submit for approval — <c>festival_wish</c>:
    /// <code>
    /// {{1}}, {{2}}! {{3}} Everyone at {{4}} is thinking of you and your family today.
    /// </code>
    /// </summary>
    public static Rendered Festival(string gymName, string memberName, string festivalName, string? greeting)
    {
        var line = string.IsNullOrWhiteSpace(greeting) ? $"Happy {festivalName}" : greeting.Trim();
        var subject = $"{line}, {FirstWord(memberName)}!";

        var html = Shell(gymName,
            $"{line}, {FirstWord(memberName)}!",
            $"<p style=\"margin:0 0 14px\">Everyone at {E(gymName)} is thinking of you and your family " +
            $"this {E(festivalName)}.</p>" +
            "<p style=\"margin:0\">Enjoy every bit of it. The weights will still be here afterwards.</p>",
            "Sent once a year, on the festival dates the gym has set.");

        var text = Text(
            $"{line}, {FirstWord(memberName)}!",
            "",
            $"Everyone at {gymName} is thinking of you and your family this {festivalName}.",
            "",
            "Enjoy every bit of it. The weights will still be here afterwards.",
            "",
            $"{gymName} · Sent once a year, on the festival dates the gym has set.");

        return new Rendered(subject, html, text,
            $"{line}, {FirstWord(memberName)}! Everyone at {gymName} is thinking of you and your " +
            "family today.",
            new[] { line, FirstWord(memberName), $"Enjoy every bit of this {festivalName}.", gymName });
    }

    // =====================================================================
    //  Shared rendering
    // =====================================================================

    /// <summary>
    /// The common HTML frame: one narrow column, system fonts, a heading, the body and a grey
    /// footnote saying why the message arrived. Every message a member gets says that, because
    /// "why am I being emailed this?" is the first thing anyone asks.
    /// </summary>
    private static string Shell(string gymName, string heading, string bodyHtml, string footNote) =>
        "<div style=\"font-family:Segoe UI,Helvetica,Arial,sans-serif;font-size:15px;line-height:1.6;" +
        "color:#1f2933;max-width:560px\">" +
        $"<p style=\"margin:0 0 14px;font-size:17px;font-weight:600\">{E(heading)}</p>" +
        bodyHtml +
        $"<p style=\"margin:18px 0 0;color:#6b7280;font-size:13px\">{E(gymName)} · {E(footNote)}</p>" +
        "</div>";

    /// <summary>A small label/value table, the one place a member looks for the numbers.</summary>
    private static string Facts(params (string Label, string Value)[] rows)
    {
        var cells = string.Concat(rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Value))
            .Select(r =>
                "<tr>" +
                "<td style=\"padding:4px 16px 4px 0;color:#6b7280;white-space:nowrap\">" +
                $"{E(r.Label)}</td>" +
                $"<td style=\"padding:4px 0;font-weight:600\">{E(r.Value)}</td>" +
                "</tr>"));

        return "<table style=\"border-collapse:collapse;font-size:14px;margin:0\">" + cells + "</table>";
    }

    /// <summary>Joins the plain-text body, dropping the lines an occasion had nothing to put in.</summary>
    private static string Text(params string?[] lines) =>
        string.Join('\n', lines.Where(l => l is not null));

    /// <summary>First name only — a greeting with a full formal name reads like a bill.</summary>
    private static string FirstWord(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0) return "there";

        var space = trimmed.IndexOf(' ');
        return space < 0 ? trimmed : trimmed[..space];
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
