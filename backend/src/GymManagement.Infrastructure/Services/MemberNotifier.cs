using System.Globalization;
using GymManagement.Application.Common;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using GymManagement.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Services;

/// <summary>
/// Messages a member directly on the occasions that matter to them, honouring the four rules of
/// <see cref="IMemberNotifier"/>: never throw, attempt each channel independently, at most one
/// message per occasion, nothing sensitive in the wording.
///
/// The shape follows <see cref="ExpiryReminderMailer"/>, with one deliberate inversion: the
/// <see cref="MemberNotificationLog"/> row is claimed <b>before</b> the first channel is attempted
/// rather than written after. A crash mid-send then errs on the side of silence, which for a
/// birthday wish or a receipt confirmation is the cheaper mistake — and a claim whose every
/// channel failed is released again, so a dead gateway costs a delay rather than the message.
///
/// <see cref="WhatsAppOptions"/> is injected for its template map alone: the sender only delivers
/// what it is given, and which approved template an occasion uses is a decision about the message,
/// so it is made here.
/// </summary>
public sealed class MemberNotifier : IMemberNotifier
{
    /// <summary>
    /// The template names the wording in <see cref="MemberMessageTemplates"/> documents for
    /// approval, used whenever <c>WhatsApp:Templates</c> has no entry for the occasion.
    /// </summary>
    private static readonly IReadOnlyDictionary<MemberNotificationKind, string> DefaultTemplates =
        new Dictionary<MemberNotificationKind, string>
        {
            [MemberNotificationKind.PaymentReceived] = "payment_received",
            [MemberNotificationKind.RenewalPayment] = "payment_renewal",
            [MemberNotificationKind.DietPlan] = "diet_plan_ready",
            [MemberNotificationKind.StreakMilestone] = "streak_milestone",
            [MemberNotificationKind.Birthday] = "birthday_wish",
            [MemberNotificationKind.Festival] = "festival_wish",
        };

    private readonly GymDbContext _db;
    private readonly IEmailSender _email;
    private readonly IWhatsAppSender _whatsApp;
    private readonly ISettingsService _settings;
    private readonly IDateTimeProvider _clock;
    private readonly MemberNotificationsOptions _options;
    private readonly WhatsAppOptions _whatsAppOptions;
    private readonly ILogger<MemberNotifier> _logger;

    public MemberNotifier(
        GymDbContext db,
        IEmailSender email,
        IWhatsAppSender whatsApp,
        ISettingsService settings,
        IDateTimeProvider clock,
        MemberNotificationsOptions options,
        WhatsAppOptions whatsAppOptions,
        ILogger<MemberNotifier> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _whatsApp = whatsApp ?? throw new ArgumentNullException(nameof(whatsApp));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _whatsAppOptions = whatsAppOptions ?? throw new ArgumentNullException(nameof(whatsAppOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MemberNotificationOutcome> NotifyPaymentAsync(int paymentId, CancellationToken ct = default)
    {
        try
        {
            var payment = await _db.Payments.AsNoTracking()
                .Where(p => p.Id == paymentId)
                .Select(p => new
                {
                    p.Id,
                    p.MemberId,
                    p.Status,
                    p.FinalAmount,
                    p.ReceiptNumber,
                    p.PaymentDate,
                    // The subscription's plan stays authoritative; the plan recorded directly on
                    // the payment covers money taken without one, as PaymentService reads it.
                    PlanName = p.Subscription != null && p.Subscription.MembershipPlan != null
                        ? p.Subscription.MembershipPlan.Name
                        : p.MembershipPlan != null ? p.MembershipPlan.Name : "membership",
                    SubscriptionEndDate = p.Subscription != null ? (DateTime?)p.Subscription.EndDate : null,
                    Member = new Recipient(p.Member!.Id, p.Member.FullName, p.Member.Email,
                        p.Member.Phone, p.Member.WishesOptOut, p.Member.WhatsAppOptOut),
                })
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (payment is null)
            {
                _logger.LogDebug("No payment message sent: payment {PaymentId} no longer exists.", paymentId);
                return MemberNotificationOutcome.Skipped("the payment no longer exists");
            }

            if (payment.Status is not (PaymentStatus.Paid or PaymentStatus.PartiallyPaid))
            {
                _logger.LogDebug(
                    "No payment message sent for payment {PaymentId}: status is {Status}; the member hears on confirmation.",
                    payment.Id, payment.Status);
                return MemberNotificationOutcome.Skipped("the payment is not confirmed yet");
            }

            // Whether the member reads "welcome" or "renewed" is their own history's call: any
            // earlier confirmed payment means this is not their first.
            var hasEarlier = await _db.Payments.AsNoTracking()
                .AnyAsync(p => p.MemberId == payment.MemberId
                               && p.Id != payment.Id
                               && (p.Status == PaymentStatus.Paid || p.Status == PaymentStatus.PartiallyPaid)
                               && (p.PaymentDate < payment.PaymentDate
                                   || (p.PaymentDate == payment.PaymentDate && p.Id < payment.Id)), ct)
                .ConfigureAwait(false);

            var kind = hasEarlier
                ? MemberNotificationKind.RenewalPayment
                : MemberNotificationKind.PaymentReceived;

            var (gymName, currencySymbol) = await ResolveBrandingAsync(ct).ConfigureAwait(false);
            var amount = currencySymbol + payment.FinalAmount.ToString("0.00", CultureInfo.InvariantCulture);

            // The end date the member is covered until: the paid subscription's own, or failing
            // that whatever active term they hold, or an honest dash rather than a guess.
            var validUntil = payment.SubscriptionEndDate;
            validUntil ??= await _db.Subscriptions.AsNoTracking()
                .Where(s => s.MemberId == payment.MemberId && s.Status == SubscriptionStatus.Active)
                .OrderByDescending(s => s.EndDate)
                .Select(s => (DateTime?)s.EndDate)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            var validUntilText = validUntil?.ToString("dd MMM yyyy") ?? "—";

            var rendered = kind == MemberNotificationKind.PaymentReceived
                ? MemberMessageTemplates.PaymentReceived(gymName, payment.Member.FullName,
                    payment.PlanName, amount, payment.ReceiptNumber, validUntilText)
                : MemberMessageTemplates.RenewalPayment(gymName, payment.Member.FullName,
                    payment.PlanName, amount, payment.ReceiptNumber, validUntilText);

            return await DispatchAsync(payment.Member, kind,
                    $"payment:{paymentId}", $"Receipt {payment.ReceiptNumber} · {amount}",
                    rendered, "Payment confirmation", ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Swallow(ex, "Payment confirmation", paymentId);
        }
    }

    public async Task<MemberNotificationOutcome> NotifyDietPlanAsync(int dietPlanId, CancellationToken ct = default)
    {
        try
        {
            var plan = await _db.DietPlans.AsNoTracking()
                .Where(d => d.Id == dietPlanId)
                .Select(d => new
                {
                    d.Id,
                    d.Title,
                    d.Goal,
                    d.StartDate,
                    MealCount = d.Meals.Count,
                    d.CreatedAt,
                    d.UpdatedAt,
                    Member = new Recipient(d.Member!.Id, d.Member.FullName, d.Member.Email,
                        d.Member.Phone, d.Member.WishesOptOut, d.Member.WhatsAppOptOut),
                })
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (plan is null)
            {
                _logger.LogDebug("No diet plan message sent: plan {DietPlanId} no longer exists.", dietPlanId);
                return MemberNotificationOutcome.Skipped("the diet plan no longer exists");
            }

            var isRevision = plan.UpdatedAt.HasValue && plan.UpdatedAt.Value > plan.CreatedAt;
            var (gymName, _) = await ResolveBrandingAsync(ct).ConfigureAwait(false);

            var rendered = MemberMessageTemplates.DietPlan(gymName, plan.Member.FullName,
                plan.Title, plan.Goal, plan.MealCount, plan.StartDate.ToString("dd MMM yyyy"), isRevision);

            // One message per plan per day: the date in the key lets tomorrow's revision speak
            // while a same-day edit stays silent instead of spamming the member.
            var dedupeKey = $"diet:{dietPlanId}:{_clock.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}";

            return await DispatchAsync(plan.Member, MemberNotificationKind.DietPlan,
                    dedupeKey, plan.Title, rendered, "Diet plan message", ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Swallow(ex, "Diet plan message", dietPlanId);
        }
    }

    public async Task<MemberNotificationOutcome> NotifyStreakAsync(
        int memberId, int streakDays, CancellationToken ct = default)
    {
        try
        {
            // Not a milestone: callers hand over every check-in, so silence — even in the log —
            // is the correct answer for the ordinary days in between.
            if (!_options.EffectiveStreakMilestones.Contains(streakDays)) return MemberNotificationOutcome.Nothing;

            var member = await LoadRecipientAsync(memberId, ct).ConfigureAwait(false);
            if (member is null) return MemberNotificationOutcome.Skipped("the member no longer exists");

            var (gymName, _) = await ResolveBrandingAsync(ct).ConfigureAwait(false);
            var rendered = MemberMessageTemplates.StreakMilestone(gymName, member.FullName, streakDays);

            return await DispatchAsync(member, MemberNotificationKind.StreakMilestone,
                    $"streak:{streakDays}", $"{streakDays}-day streak", rendered,
                    "Streak congratulation", ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Swallow(ex, "Streak congratulation", memberId);
        }
    }

    public async Task<MemberNotificationOutcome> NotifyBirthdayAsync(int memberId, CancellationToken ct = default)
    {
        try
        {
            var member = await LoadRecipientAsync(memberId, ct).ConfigureAwait(false);
            if (member is null) return MemberNotificationOutcome.Skipped("the member no longer exists");

            var (gymName, _) = await ResolveBrandingAsync(ct).ConfigureAwait(false);
            var rendered = MemberMessageTemplates.Birthday(gymName, member.FullName);

            return await DispatchAsync(member, MemberNotificationKind.Birthday,
                    $"birthday:{_clock.Now.Year}", $"Birthday {_clock.Now.Year}", rendered,
                    "Birthday wishes", ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Swallow(ex, "Birthday wishes", memberId);
        }
    }

    public async Task<MemberNotificationOutcome> NotifyFestivalAsync(
        int memberId, FestivalGreeting festival, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(festival);

        try
        {
            var member = await LoadRecipientAsync(memberId, ct).ConfigureAwait(false);
            if (member is null) return MemberNotificationOutcome.Skipped("the member no longer exists");

            var (gymName, _) = await ResolveBrandingAsync(ct).ConfigureAwait(false);
            var rendered = MemberMessageTemplates.Festival(gymName, member.FullName,
                festival.Name, festival.Greeting);

            return await DispatchAsync(member, MemberNotificationKind.Festival,
                    $"festival:{festival.Key}:{festival.Date.Year}", festival.Name, rendered,
                    "Festival wishes", ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Swallow(ex, "Festival wishes", memberId);
        }
    }

    public bool IsChannelEnabled(MemberNotificationKind kind, bool email) =>
        _options.IsChannelEnabled(kind, email);

    // =====================================================================
    //  The shared dispatch: gate, claim, send, record — or release
    // =====================================================================

    /// <summary>What every occasion needs to know about the member it is messaging.</summary>
    private sealed record Recipient(
        int Id, string FullName, string? Email, string? Phone, bool WishesOptOut, bool WhatsAppOptOut);

    private async Task<MemberNotificationOutcome> DispatchAsync(
        Recipient member,
        MemberNotificationKind kind,
        string deduplicationKey,
        string? detail,
        MemberMessageTemplates.Rendered rendered,
        string occasion,
        CancellationToken ct)
    {
        // The wishes opt-out covers exactly the goodwill occasions. Transactional messages are
        // unaffected: a member who declined birthday wishes still wants their payment confirmed.
        if (member.WishesOptOut && kind is MemberNotificationKind.Birthday
                or MemberNotificationKind.Festival or MemberNotificationKind.StreakMilestone)
        {
            return MemberNotificationOutcome.Skipped("the member has opted out of wishes");
        }

        var templateName = ResolveTemplateName(kind);

        var sendEmail = _options.IsChannelEnabled(kind, email: true)
                        && _email.IsEnabled
                        && !string.IsNullOrWhiteSpace(member.Email);

        var sendWhatsApp = _options.IsChannelEnabled(kind, email: false)
                           && _whatsApp.IsEnabled
                           && !member.WhatsAppOptOut
                           && !string.IsNullOrWhiteSpace(member.Phone)
                           && templateName is not null;

        if (!sendEmail && !sendWhatsApp)
            return MemberNotificationOutcome.Skipped("no channel is switched on and reachable for this occasion");

        // The fast path of the claim: a repeat call inside one process answers from a read
        // instead of an insert-and-fail. The unique index below stays the real guarantee.
        var alreadySent = await _db.MemberNotificationLogs.AsNoTracking()
            .AnyAsync(l => l.MemberId == member.Id
                           && l.Kind == kind
                           && l.DeduplicationKey == deduplicationKey, ct)
            .ConfigureAwait(false);
        if (alreadySent)
            return MemberNotificationOutcome.Duplicate("this occasion has already been sent to the member");

        // Claimed before the first channel is attempted, so a restart, a retried request or a
        // racing instance can never message the same member twice for the same occasion.
        var claim = new MemberNotificationLog
        {
            MemberId = member.Id,
            Kind = kind,
            DeduplicationKey = deduplicationKey,
            SentOnDate = _clock.Now.Date,
            Detail = detail is { Length: > 300 } ? detail[..300] : detail,
        };
        _db.MemberNotificationLogs.Add(claim);

        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Another run claimed this occasion between our read and our write — exactly what
            // the unique (MemberId, Kind, DeduplicationKey) index is for.
            _db.ChangeTracker.Clear();
            return MemberNotificationOutcome.Duplicate("another run claimed this occasion first");
        }

        var emailed = false;
        if (sendEmail)
        {
            try
            {
                var result = await _email.SendAsync(new EmailMessage
                {
                    To = { member.Email! },
                    Subject = rendered.Subject,
                    HtmlBody = rendered.HtmlBody,
                    TextBody = rendered.TextBody,
                }, ct).ConfigureAwait(false);

                emailed = result.WasSent;
                if (!emailed)
                    _logger.LogDebug("{Occasion} email for member {MemberId} skipped: {Reason}",
                        occasion, member.Id, result.Detail);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A dead relay must not cost the member their WhatsApp message.
                _logger.LogWarning(ex, "{Occasion} email to member {MemberId} failed; trying WhatsApp.",
                    occasion, member.Id);
            }
        }

        var messaged = false;
        if (sendWhatsApp)
        {
            try
            {
                var result = await _whatsApp.SendAsync(new WhatsAppMessage
                {
                    To = member.Phone!,
                    TemplateName = templateName!,
                    LanguageCode = string.IsNullOrWhiteSpace(_whatsAppOptions.LanguageCode)
                        ? "en"
                        : _whatsAppOptions.LanguageCode.Trim(),
                    Parameters = rendered.WhatsAppParameters,
                    PreviewText = rendered.WhatsAppPreview,
                }, ct).ConfigureAwait(false);

                messaged = result.WasSent;
                if (!messaged)
                    _logger.LogDebug("{Occasion} WhatsApp message for member {MemberId} skipped: {Reason}",
                        occasion, member.Id, result.Detail);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A refused gateway must not cost the member the email that may just have gone out.
                _logger.LogWarning(ex,
                    "{Occasion} WhatsApp message to member {MemberId} failed; the email still counts.",
                    occasion, member.Id);
            }
        }

        if (!emailed && !messaged)
        {
            // Release the claim: nothing left the building, so a later attempt — after the relay
            // or the gateway comes back — still owes the member this message.
            _db.MemberNotificationLogs.Remove(claim);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return MemberNotificationOutcome.Skipped("every channel failed or was declined; the claim was released");
        }

        claim.EmailSent = emailed;
        claim.WhatsAppSent = messaged;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("{Occasion} sent to {Recipient} ({Channels}).",
            occasion, MailMessageFactory.Mask(member.Email ?? member.Phone),
            emailed && messaged ? "email+whatsapp" : emailed ? "email" : "whatsapp");

        return new MemberNotificationOutcome(emailed, messaged);
    }

    private Task<Recipient?> LoadRecipientAsync(int memberId, CancellationToken ct) =>
        _db.Members.AsNoTracking()
            .Where(m => m.Id == memberId)
            .Select(m => new Recipient(m.Id, m.FullName, m.Email, m.Phone,
                m.WishesOptOut, m.WhatsAppOptOut))
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// The approved template for the occasion: an override from <c>WhatsApp:Templates</c> when one
    /// is configured, the built-in default when the key is absent, and null — WhatsApp off for the
    /// occasion — when the gym mapped it to an empty string because approval is still pending.
    /// </summary>
    private string? ResolveTemplateName(MemberNotificationKind kind)
    {
        if (_whatsAppOptions.Templates.TryGetValue(kind.ToString(), out var configured))
            return string.IsNullOrWhiteSpace(configured) ? null : configured.Trim();

        return DefaultTemplates.TryGetValue(kind, out var name) ? name : null;
    }

    private async Task<(string GymName, string CurrencySymbol)> ResolveBrandingAsync(CancellationToken ct)
    {
        try
        {
            var branding = await _settings.GetGymBrandingAsync(ct).ConfigureAwait(false);
            return (
                string.IsNullOrWhiteSpace(branding.GymName) ? "your gym" : branding.GymName.Trim(),
                string.IsNullOrWhiteSpace(branding.CurrencySymbol) ? "₹" : branding.CurrencySymbol.Trim());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Gym branding unavailable for the member message; using defaults.");
            return ("your gym", "₹");
        }
    }

    /// <summary>
    /// The never-throw guarantee, in one place: every caller has already committed a payment, a
    /// plan or a check-in, and a message is never worth undoing one. The half-tracked claim, if
    /// there is one, is dropped so the failure cannot leak into the caller's next save.
    /// </summary>
    private MemberNotificationOutcome Swallow(Exception ex, string occasion, int id)
    {
        _db.ChangeTracker.Clear();
        _logger.LogWarning(ex, "{Occasion} for id {Id} failed; nothing was sent.", occasion, id);
        return MemberNotificationOutcome.Skipped("the message could not be dispatched; the failure was logged");
    }
}
