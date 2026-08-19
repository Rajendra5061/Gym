namespace GymManagement.Infrastructure.Messaging;

/// <summary>Which <see cref="Application.Interfaces.IEmailSender"/> implementation is wired up.</summary>
public enum EmailProviderKind
{
    /// <summary>Nothing is sent and nothing is written. The safe default outside Development.</summary>
    None = 0,

    /// <summary>Each message is written to a folder as an .eml plus a readable .txt. Nothing leaves the machine.</summary>
    File = 1,

    /// <summary>Real delivery over SMTP.</summary>
    Smtp = 2
}

/// <summary>
/// The <c>Email</c> configuration section.
///
/// Every value here comes from configuration — appsettings, environment variables
/// (<c>Email__Smtp__Password</c>) or user secrets. None of it is ever read from, or written to,
/// the database: gym settings are readable by any administrator over the API, so a password there
/// would be handed straight out by <c>GET /api/settings/…</c>.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// <c>None</c>, <c>File</c> or <c>Smtp</c>. Left unset it resolves to <c>File</c> in
    /// Development and <c>None</c> everywhere else, so sending is off until somebody turns it on.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>Envelope sender. Without it nothing can be sent, whichever provider is selected.</summary>
    public string? FromAddress { get; set; }

    public string? FromName { get; set; }

    public string? ReplyToAddress { get; set; }

    public EmailFileSinkOptions FileSink { get; set; } = new();

    public EmailSmtpOptions Smtp { get; set; } = new();
}

public sealed class EmailFileSinkOptions
{
    /// <summary>
    /// Folder the messages are dropped into. Relative paths resolve against the process working
    /// directory, matching how the Serilog file sink is configured.
    /// </summary>
    public string Directory { get; set; } = "logs/mail-drop";
}

public sealed class EmailSmtpOptions
{
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    /// <summary>STARTTLS / implicit TLS. Leave on unless talking to a local relay on loopback.</summary>
    public bool UseStartTls { get; set; } = true;

    public string? UserName { get; set; }

    /// <summary>
    /// Supply through <c>Email__Smtp__Password</c>, user secrets or a secret store. Never commit a
    /// value to appsettings, and never log this — it is deliberately absent from every log message
    /// and from every API response.
    /// </summary>
    public string? Password { get; set; }

    public int TimeoutSeconds { get; set; } = 30;
}
