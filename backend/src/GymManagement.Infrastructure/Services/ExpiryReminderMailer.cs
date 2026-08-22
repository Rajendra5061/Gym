using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using GymManagement.Application.Common;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using GymManagement.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Services;

/// <summary>
/// Emails members whose active membership ends within the reminder window — by default the last
/// three days, so a member hears on day −3, −2, −1 and on the end date itself, once per day.
///
/// The rules follow <see cref="PaymentReceiptMailer"/>, because the stakes are the same:
/// <list type="number">
///   <item><b>Never throws.</b> A dead relay or a bad address must not take down the daily run;
///   every member is attempted independently and failures are logged and skipped.</item>
///   <item><b>Once per member per day.</b> The <see cref="ExpiryReminderEmail"/> row is written
///   in the same save as the send is logged, and its unique (MemberId, SentOnDate) index makes a
///   duplicate attempt — a restart, a manual run after the scheduled one — a silent skip.</item>
///   <item><b>Nothing sensitive travels.</b> Plan name, end date and days left; no amounts owed,
///   no payment details.</item>
/// </list>
/// </summary>
public sealed class ExpiryReminderMailer : IExpiryReminderMailer
{
    private const int DefaultWindowDays = 3;

    private readonly GymDbContext _db;
    private readonly IEmailSender _email;
    private readonly ISmsSender _sms;
    private readonly ISettingsService _settings;
    private readonly IConfiguration _configuration;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ExpiryReminderMailer> _logger;

    public ExpiryReminderMailer(
        GymDbContext db,
        IEmailSender email,
        ISmsSender sms,
        ISettingsService settings,
        IConfiguration configuration,
        IDateTimeProvider clock,
        ILogger<ExpiryReminderMailer> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _sms = sms ?? throw new ArgumentNullException(nameof(sms));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The window in days, shared with the dashboard so the count and the emails agree.</summary>
    public static int WindowDays(IConfiguration configuration)
    {
        var days = configuration.GetValue("Notifications:DailyAlerts:ExpiryEmailDaysBefore", DefaultWindowDays);
        return days is < 0 or > 30 ? DefaultWindowDays : days;
    }

    public async Task<int> SendDueRemindersAsync(CancellationToken ct = default)
    {
        // Two channels, either sufficient: with both providers off there is nothing to do.
        if (!_email.IsEnabled && !_sms.IsEnabled) return 0;

        var today = _clock.Now.Date;
        var windowDays = WindowDays(_configuration);
        var windowEnd = today.AddDays(windowDays);

        List<DueRow> due;
        try
        {
            due = await _db.Subscriptions.AsNoTracking()
                .Where(s => s.Status == SubscriptionStatus.Active
                            && s.EndDate >= today
                            && s.EndDate <= windowEnd
                            && s.Member != null
                            // Reachable somehow: an email address or a phone number will do.
                            && ((s.Member.Email != null && s.Member.Email != "")
                                || (s.Member.Phone != null && s.Member.Phone != ""))
                            // A member who already booked the next term is covered, not lapsing —
                            // "your membership ends in 2 days" would be false, so no reminder.
                            && !_db.Subscriptions.Any(o =>
                                o.MemberId == s.MemberId
                                && o.Id != s.Id
                                && (o.Status == SubscriptionStatus.Active
                                    || o.Status == SubscriptionStatus.Pending)
                                && o.EndDate > s.EndDate))
                .Select(s => new DueRow(
                    s.MemberId, s.Id, s.Member!.FullName, s.Member.Email, s.Member.Phone,
                    s.MembershipPlan != null ? s.MembershipPlan.Name : "membership",
                    s.EndDate,
                    s.MembershipPlan != null ? s.MembershipPlan.Price : 0m,
                    s.FinalAmount - s.PaidAmount))
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Expiry reminder emails skipped: the due list could not be read.");
            return 0;
        }

        if (due.Count == 0) return 0;

        var alreadySent = await _db.ExpiryReminderEmails.AsNoTracking()
            .Where(r => r.SentOnDate == today)
            .Select(r => r.MemberId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var sentToday = alreadySent.ToHashSet();

        var gymName = await ResolveGymNameAsync(ct).ConfigureAwait(false);

        // When the gym has a UPI id, every reminder carries a personal tap-to-pay link, so
        // the member can renew from the message itself instead of walking to the front desk.
        var upiReady = false;
        try
        {
            var gym = await _settings.GetGymSettingsAsync(ct).ConfigureAwait(false);
            upiReady = !string.IsNullOrWhiteSpace(gym.UpiId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Gym settings unavailable; reminders go out without pay links.");
        }
        var payBaseUrl = (_configuration["App:PublicBaseUrl"] ?? "http://localhost:5175").TrimEnd('/');

        var sent = 0;

        foreach (var row in due)
        {
            if (sentToday.Contains(row.MemberId)) continue;

            var daysLeft = (int)(row.EndDate.Date - today).TotalDays;

            try
            {
                var payLink = upiReady
                    ? await TryCreatePayLinkAsync(row, payBaseUrl, ct).ConfigureAwait(false)
                    : null;

                // Each channel is attempted independently; either one succeeding claims the
                // member's day so tomorrow brings the next reminder, not a repeat of today's.
                var emailed = false;
                if (_email.IsEnabled && !string.IsNullOrWhiteSpace(row.Email))
                {
                    try
                    {
                        var result = await _email
                            .SendAsync(BuildMessage(row, gymName, daysLeft, payLink), ct)
                            .ConfigureAwait(false);
                        emailed = result.WasSent;
                        if (!emailed)
                            _logger.LogDebug("Reminder email for member {MemberId} skipped: {Reason}",
                                row.MemberId, result.Detail);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // A relay that rejects this address must not cost the member their SMS —
                        // the channels fail independently, in both directions.
                        _logger.LogWarning(ex,
                            "Reminder email to member {MemberId} failed; trying SMS.", row.MemberId);
                    }
                }

                var texted = false;
                if (_sms.IsEnabled && !string.IsNullOrWhiteSpace(row.Phone))
                {
                    try
                    {
                        var result = await _sms
                            .SendAsync(BuildSms(row, gymName, daysLeft, payLink), ct)
                            .ConfigureAwait(false);
                        texted = result.WasSent;
                        if (!texted)
                            _logger.LogDebug("Reminder SMS for member {MemberId} skipped: {Reason}",
                                row.MemberId, result.Detail);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // A gateway failure must not cost the member their email claim.
                        _logger.LogWarning(ex,
                            "Reminder SMS to member {MemberId} failed; the email still counts.",
                            row.MemberId);
                    }
                }

                if (!emailed && !texted) continue;

                _db.ExpiryReminderEmails.Add(new ExpiryReminderEmail
                {
                    MemberId = row.MemberId,
                    SubscriptionId = row.SubscriptionId,
                    SentOnDate = today,
                    EndDateAtSend = row.EndDate.Date,
                    DaysLeftAtSend = daysLeft,
                    EmailSent = emailed,
                    SmsSent = texted,
                });
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);

                sentToday.Add(row.MemberId);
                sent++;

                _logger.LogInformation(
                    "Expiry reminder for {Recipient} ({Channels}): {Plan} ends in {Days} day(s).",
                    MailMessageFactory.Mask(row.Email ?? row.Phone),
                    emailed && texted ? "email+sms" : emailed ? "email" : "sms",
                    row.PlanName, daysLeft);
            }
            catch (DbUpdateException)
            {
                // Another run claimed this member's row between our read and our write — exactly
                // what the unique index is for. The email may have gone twice across two racing
                // processes at worst; within one process the up-front read prevents even that.
                sentToday.Add(row.MemberId);
                _db.ChangeTracker.Clear();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Expiry reminder to member {MemberId} failed; continuing with the rest.",
                    row.MemberId);
            }
        }

        return sent;
    }

    private async Task<string> ResolveGymNameAsync(CancellationToken ct)
    {
        try
        {
            var branding = await _settings.GetGymBrandingAsync(ct).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(branding.GymName) ? "your gym" : branding.GymName.Trim();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Gym name unavailable for the reminder email; using a default.");
            return "your gym";
        }
    }

    /// <summary>How long a reminder's tap-to-pay link keeps working — the whole final window and then some.</summary>
    private const int PayLinkValidDays = 7;

    /// <summary>
    /// Creates this member's personal pay link for the day: the renewal price of the expiring
    /// plan, or the subscription's own unpaid balance when one exists. Never throws — a failed
    /// link just means the reminder goes out the way it always did, front-desk wording and all.
    /// </summary>
    private async Task<string?> TryCreatePayLinkAsync(DueRow row, string baseUrl, CancellationToken ct)
    {
        try
        {
            var dueBalance = Math.Max(0m, row.Outstanding);
            var amount = dueBalance > 0m ? dueBalance : row.PlanPrice;
            if (amount <= 0m) return null;

            var token = string.Create(16, RandomNumberGenerator.GetBytes(16), static (span, bytes) =>
            {
                const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
                for (var i = 0; i < span.Length; i++) span[i] = alphabet[bytes[i] % alphabet.Length];
            });

            _db.PaymentRequests.Add(new PaymentRequest
            {
                Token = token,
                MemberId = row.MemberId,
                // A due balance binds to its subscription; a renewal is a fresh term, so it doesn't.
                SubscriptionId = dueBalance > 0m ? row.SubscriptionId : null,
                Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
                Note = dueBalance > 0m ? $"{row.PlanName} payment due" : $"{row.PlanName} renewal",
                Reference = $"UPI{DateTime.UtcNow:yyyyMMddHHmmss}" +
                            RandomNumberGenerator.GetInt32(0, 10_000).ToString("0000", CultureInfo.InvariantCulture),
                ExpiresAtUtc = DateTime.UtcNow.AddDays(PayLinkValidDays),
            });
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            return $"{baseUrl}/pay/{token}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Pay link for member {MemberId} could not be created; reminding without it.",
                row.MemberId);
            _db.ChangeTracker.Clear();
            return null;
        }
    }

    /// <summary>
    /// The text message: one SMS segment's worth, no marketing. Gateways bill per 160-character
    /// segment and clip long bodies, so the essentials only — who, what ends, when, what to do.
    /// </summary>
    private static SmsMessage BuildSms(DueRow row, string gymName, int daysLeft, string? payLink)
    {
        var when = daysLeft switch
        {
            0 => "TODAY",
            1 => "tomorrow",
            _ => $"in {daysLeft} days",
        };

        var action = payLink is null
            ? "Renew at the front desk to keep training."
            : $"Tap to renew & pay by UPI: {payLink}";

        return new SmsMessage
        {
            To = row.Phone!,
            Text = $"Hi {FirstWord(row.MemberName)}, your {row.PlanName} membership at {gymName} " +
                   $"ends {when} ({row.EndDate:dd MMM}). {action} " +
                   $"- {gymName}",
        };
    }

    /// <summary>First name only — an SMS greeting with a full formal name reads like a bill.</summary>
    private static string FirstWord(string value)
    {
        var trimmed = value.Trim();
        var space = trimmed.IndexOf(' ');
        return space < 0 ? trimmed : trimmed[..space];
    }

    private static EmailMessage BuildMessage(DueRow row, string gymName, int daysLeft, string? payLink)
    {
        var when = daysLeft switch
        {
            0 => "today",
            1 => "tomorrow",
            _ => $"in {daysLeft} days",
        };
        var endText = row.EndDate.ToString("dd MMM yyyy");
        var subject = $"Your {row.PlanName} membership ends {when} — {gymName}";

        string E(string? v) => WebUtility.HtmlEncode(v ?? string.Empty);

        var payHtml = payLink is null
            ? "<p style=\"margin:0 0 14px\">Renew at the front desk before it lapses and your training " +
              "carries straight on — no rejoining, no new paperwork.</p>"
            : $"<p style=\"margin:0 0 18px\"><a href=\"{payLink}\" style=\"display:inline-block;" +
              "background:#4f46e5;color:#ffffff;font-weight:600;text-decoration:none;" +
              "padding:12px 26px;border-radius:10px\">Renew &amp; pay by UPI</a></p>" +
              "<p style=\"margin:0 0 14px;color:#6b7280;font-size:13px\">The link opens on your phone and " +
              "hands the payment to PhonePe, Google Pay, Paytm or any UPI app you choose. " +
              $"If the button does not work: {payLink}</p>";

        var html =
            "<div style=\"font-family:Segoe UI,Helvetica,Arial,sans-serif;font-size:15px;line-height:1.6;" +
            "color:#1f2933;max-width:560px\">" +
            $"<p style=\"margin:0 0 14px;font-size:17px;font-weight:600\">Hi {E(row.MemberName)},</p>" +
            $"<p style=\"margin:0 0 14px\">Your <strong>{E(row.PlanName)}</strong> membership at {E(gymName)} " +
            $"ends <strong>{E(when)}</strong>, on {E(endText)}.</p>" +
            payHtml +
            $"<p style=\"margin:0;color:#6b7280;font-size:13px\">{E(gymName)} · This reminder is sent daily " +
            "over the final days of a membership.</p>" +
            "</div>";

        var payText = payLink is null
            ? "Renew at the front desk before it lapses and your training carries straight on —\nno rejoining, no new paperwork."
            : $"Renew & pay by UPI (opens PhonePe / Google Pay / Paytm or any UPI app):\n{payLink}";

        var text = string.Join('\n', new[]
        {
            $"Hi {row.MemberName},",
            "",
            $"Your {row.PlanName} membership at {gymName} ends {when}, on {endText}.",
            "",
            payText,
            "",
            $"{gymName} · This reminder is sent daily over the final days of a membership.",
        });

        return new EmailMessage
        {
            To = { row.Email! },
            Subject = subject,
            HtmlBody = html,
            TextBody = text,
        };
    }

    private sealed record DueRow(
        int MemberId, int SubscriptionId, string MemberName, string? Email, string? Phone,
        string PlanName, DateTime EndDate, decimal PlanPrice, decimal Outstanding);
}
