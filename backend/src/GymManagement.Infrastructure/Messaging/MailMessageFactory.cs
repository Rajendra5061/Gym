using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using GymManagement.Application.Interfaces;

namespace GymManagement.Infrastructure.Messaging;

/// <summary>
/// Turns an <see cref="EmailMessage"/> into a <see cref="MailMessage"/>. Shared by the SMTP sender
/// and by the file sink so both produce byte-identical MIME — what is inspected in Development is
/// exactly what production would put on the wire.
/// </summary>
internal static class MailMessageFactory
{
    /// <summary>
    /// Builds the MIME message, or returns null when it has no deliverable recipient. Ownership of
    /// the returned message (and of the attachment streams it holds) passes to the caller.
    /// </summary>
    public static MailMessage? TryBuild(EmailMessage message, EmailOptions options, out string? reason)
    {
        reason = null;

        if (!TryParseAddress(options.FromAddress, options.FromName, out var from))
        {
            reason = "Email:FromAddress is missing or not a valid address.";
            return null;
        }

        var recipients = (message.To ?? new List<string>())
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(a => TryParseAddress(a, null, out var parsed) ? parsed : null)
            .Where(a => a is not null)
            .ToList();

        if (recipients.Count == 0)
        {
            reason = "The message has no valid recipient address.";
            return null;
        }

        var mail = new MailMessage
        {
            From = from!,
            Subject = Collapse(message.Subject),
            SubjectEncoding = Encoding.UTF8,
            HeadersEncoding = Encoding.UTF8,
            // A body is set as well as the alternate views: a client that understands neither view
            // still has something to show, and the pickup-directory writer wants a body part.
            Body = message.TextBody ?? string.Empty,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };

        foreach (var recipient in recipients) mail.To.Add(recipient!);

        if (TryParseAddress(options.ReplyToAddress, null, out var replyTo))
            mail.ReplyToList.Add(replyTo!);

        // Plain text first: mail clients pick the last alternative they can render, so the HTML
        // view has to come second for it to win.
        if (!string.IsNullOrWhiteSpace(message.TextBody))
        {
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                message.TextBody, Encoding.UTF8, MediaTypeNames.Text.Plain));
        }

        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                message.HtmlBody, Encoding.UTF8, MediaTypeNames.Text.Html));
        }

        foreach (var attachment in message.Attachments ?? new List<EmailAttachment>())
        {
            if (attachment is null || attachment.Content.Length == 0) continue;

            var stream = new MemoryStream(attachment.Content, writable: false);
            mail.Attachments.Add(new Attachment(stream, attachment.FileName, attachment.ContentType));
        }

        return mail;
    }

    /// <summary>Parses an address, returning false instead of throwing on anything malformed.</summary>
    public static bool TryParseAddress(string? address, string? displayName, out MailAddress? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(address)) return false;

        try
        {
            parsed = string.IsNullOrWhiteSpace(displayName)
                ? new MailAddress(address.Trim())
                : new MailAddress(address.Trim(), displayName.Trim(), Encoding.UTF8);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Shortens an address for logging: <c>ravi.kumar@example.com</c> becomes <c>r***@example.com</c>.
    /// A log file is read by more people than a mailbox is.
    /// </summary>
    public static string Mask(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return "(none)";

        var trimmed = address.Trim();
        var at = trimmed.IndexOf('@');
        if (at <= 0) return "***";

        return $"{trimmed[0]}***{trimmed[at..]}";
    }

    /// <summary>Headers may not contain CR or LF; folding one in would let a caller inject headers.</summary>
    private static string Collapse(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "(no subject)"
            : value.Replace("\r", " ", StringComparison.Ordinal)
                   .Replace("\n", " ", StringComparison.Ordinal)
                   .Trim();
}
