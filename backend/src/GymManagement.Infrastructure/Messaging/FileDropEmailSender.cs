using System.Globalization;
using System.Net.Mail;
using System.Text;
using GymManagement.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Messaging;

/// <summary>
/// Writes every message to a folder and sends nothing. The default in Development, so building or
/// demonstrating a mail feature cannot put a message in a real person's inbox.
///
/// Two files are written per message:
/// <list type="bullet">
///   <item><c>*.eml</c> — the complete MIME message, attachments included, openable in any mail client.</item>
///   <item><c>*.txt</c> — headers and the plain-text alternative verbatim, for reading in a terminal.</item>
/// </list>
/// The .eml is produced by the same <see cref="SmtpClient"/> the real sender uses, in pickup
/// directory mode, so what is inspected here is exactly what SMTP would have transmitted.
/// </summary>
public sealed class FileDropEmailSender : IEmailSender
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly EmailOptions _options;
    private readonly ILogger<FileDropEmailSender> _logger;
    private int _sequence;

    public FileDropEmailSender(EmailOptions options, ILogger<FileDropEmailSender> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsEnabled => true;

    public string ProviderName => "File";

    /// <summary>Absolute path of the folder messages are dropped into.</summary>
    public string Directory => Path.GetFullPath(
        string.IsNullOrWhiteSpace(_options.FileSink?.Directory)
            ? "logs/mail-drop"
            : _options.FileSink!.Directory);

    public EmailSenderDescription Describe() => new()
    {
        Provider = ProviderName,
        IsEnabled = IsEnabled,
        FromAddress = _options.FromAddress,
        FromName = _options.FromName,
        ReplyToAddress = _options.ReplyToAddress,
        FileSinkDirectory = Directory
    };

    public async Task<EmailDeliveryResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var mail = MailMessageFactory.TryBuild(message, _options, out var reason);
        if (mail is null)
        {
            _logger.LogDebug("Receipt email not written to the mail drop: {Reason}", reason);
            return EmailDeliveryResult.Skipped(ProviderName, reason);
        }

        var directory = Directory;

        // Deliberately not swallowed: an unwritable drop folder is a real failure, and the caller
        // is the one that decides it must not affect the business operation.
        System.IO.Directory.CreateDirectory(directory);

        var stamp = DateTime.Now;
        var sequence = Interlocked.Increment(ref _sequence);
        var baseName = string.Create(CultureInfo.InvariantCulture,
            $"{stamp:yyyyMMdd-HHmmss-fff}_{sequence:000}_{Slug(message.Subject)}");

        var emlPath = Path.Combine(directory, baseName + ".eml");
        var txtPath = Path.Combine(directory, baseName + ".txt");

        // SmtpClient names pickup files with a GUID of its own. Staging each message in its own
        // private folder means the file just written can be identified without racing any other
        // send, and then moved out under a name that says what it is.
        var staging = Path.Combine(directory, $".staging-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(staging);
        try
        {
            using (var client = new SmtpClient
                   {
                       DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
                       PickupDirectoryLocation = staging
                   })
            {
                await client.SendMailAsync(mail, ct).ConfigureAwait(false);
            }

            var produced = System.IO.Directory.GetFiles(staging).FirstOrDefault()
                ?? throw new IOException($"No message file was produced in '{staging}'.");

            File.Move(produced, emlPath, overwrite: true);
        }
        finally
        {
            try
            {
                System.IO.Directory.Delete(staging, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not remove the mail staging folder {Folder}.", staging);
            }
        }

        await File.WriteAllTextAsync(txtPath, BuildSummary(message, mail, stamp), Utf8NoBom, ct)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Receipt email written to the local mail drop for {Recipient}: {File}",
            MailMessageFactory.Mask(message.To.FirstOrDefault()), emlPath);

        return EmailDeliveryResult.Sent(ProviderName, emlPath);
    }

    /// <summary>The readable sidecar: what was sent, to whom, and the plain-text body unaltered.</summary>
    private string BuildSummary(EmailMessage message, MailMessage mail, DateTime stamp)
    {
        var builder = new StringBuilder();

        builder.Append("Provider: ").AppendLine(ProviderName);
        builder.Append("Written : ")
            .AppendLine(stamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        builder.Append("From    : ").AppendLine(mail.From?.ToString() ?? "-");
        builder.Append("To      : ").AppendLine(string.Join(", ", mail.To.Select(a => a.Address)));

        if (mail.ReplyToList.Count > 0)
            builder.Append("Reply-To: ").AppendLine(string.Join(", ", mail.ReplyToList.Select(a => a.Address)));

        builder.Append("Subject : ").AppendLine(mail.Subject);

        foreach (var attachment in message.Attachments ?? new List<EmailAttachment>())
        {
            builder.Append("Attached: ")
                .Append(attachment.FileName)
                .Append(" (").Append(attachment.ContentType)
                .Append(", ").Append(attachment.Content.Length.ToString("N0", CultureInfo.InvariantCulture))
                .AppendLine(" bytes)");
        }

        builder.AppendLine();
        builder.AppendLine("----- plain text body -----");
        builder.AppendLine(message.TextBody);

        return builder.ToString();
    }

    /// <summary>A short, filesystem-safe fragment of the subject so the folder can be skimmed.</summary>
    private static string Slug(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return "message";

        var builder = new StringBuilder(48);
        foreach (var ch in subject.Trim())
        {
            if (builder.Length >= 48) break;

            if (char.IsLetterOrDigit(ch) && ch < 128) builder.Append(ch);
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "message" : slug;
    }
}
