using System.Net;
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
                    s.EndDate))
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
        var sent = 0;

        foreach (var row in due)
        {
            if (sentToday.Contains(row.MemberId)) continue;

            var daysLeft = (int)(row.EndDate.Date - today).TotalDays;

            try
            {
                // Each channel is attempted independently; either one succeeding claims the
                // member's day so tomorrow brings the next reminder, not a repeat of today's.
                var emailed = false;
                if (_email.IsEnabled && !string.IsNullOrWhiteSpace(row.Email))
                {
                    var result = await _email
                        .SendAsync(BuildMessage(row, gymName, daysLeft), ct)
                        .ConfigureAwait(false);
                    emailed = result.WasSent;
                    if (!emailed)
                        _logger.LogDebug("Reminder email for member {MemberId} skipped: {Reason}",
                            row.MemberId, result.Detail);
                }

                var texted = false;
                if (_sms.IsEnabled && !string.IsNullOrWhiteSpace(row.Phone))
                {
                    try
                    {
                        var result = await _sms
                            .SendAsync(BuildSms(row, gymName, daysLeft), ct)
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

    /// <summary>
    /// The text message: one SMS segment's worth, no marketing. Gateways bill per 160-character
    /// segment and clip long bodies, so the essentials only — who, what ends, when, what to do.
    /// </summary>
    private static SmsMessage BuildSms(DueRow row, string gymName, int daysLeft)
    {
        var when = daysLeft switch
        {
            0 => "TODAY",
            1 => "tomorrow",
            _ => $"in {daysLeft} days",
        };

        return new SmsMessage
        {
            To = row.Phone!,
            Text = $"Hi {FirstWord(row.MemberName)}, your {row.PlanName} membership at {gymName} " +
                   $"ends {when} ({row.EndDate:dd MMM}). Renew at the front desk to keep training. " +
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

    private static EmailMessage BuildMessage(DueRow row, string gymName, int daysLeft)
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

        var html =
            "<div style=\"font-family:Segoe UI,Helvetica,Arial,sans-serif;font-size:15px;line-height:1.6;" +
            "color:#1f2933;max-width:560px\">" +
            $"<p style=\"margin:0 0 14px;font-size:17px;font-weight:600\">Hi {E(row.MemberName)},</p>" +
            $"<p style=\"margin:0 0 14px\">Your <strong>{E(row.PlanName)}</strong> membership at {E(gymName)} " +
            $"ends <strong>{E(when)}</strong>, on {E(endText)}.</p>" +
            "<p style=\"margin:0 0 14px\">Renew at the front desk before it lapses and your training " +
            "carries straight on — no rejoining, no new paperwork.</p>" +
            $"<p style=\"margin:0;color:#6b7280;font-size:13px\">{E(gymName)} · This reminder is sent daily " +
            "over the final days of a membership.</p>" +
            "</div>";

        var text = string.Join('\n', new[]
        {
            $"Hi {row.MemberName},",
            "",
            $"Your {row.PlanName} membership at {gymName} ends {when}, on {endText}.",
            "",
            "Renew at the front desk before it lapses and your training carries straight on —",
            "no rejoining, no new paperwork.",
            "",
            $"{gymName} · This reminder is sent daily over the final days of a membership.",
        });

        return new EmailMessage
        {
            To = { row.Email },
            Subject = subject,
            HtmlBody = html,
            TextBody = text,
        };
    }

    private sealed record DueRow(
        int MemberId, int SubscriptionId, string MemberName, string? Email, string? Phone,
        string PlanName, DateTime EndDate);
}
