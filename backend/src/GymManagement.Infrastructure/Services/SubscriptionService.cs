using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Services;

/// <summary>
/// Subscription lifecycle: sell, renew, upgrade/downgrade, freeze/resume, cancel and expire.
/// <para>
/// The server is the ONLY authority on money. Every path that produces amounts funnels through
/// <see cref="CalculatePricing"/> so a quote and the subscription that follows it can never diverge,
/// and no client supplied total is ever persisted. All arithmetic is <see cref="decimal"/> and
/// every rounding step uses <see cref="MidpointRounding.AwayFromZero"/> at two decimal places.
/// </para>
/// </summary>
public sealed class SubscriptionService : ISubscriptionService
{
    private const string EntityName = nameof(Subscription);

    /// <summary>How far in the past an operator may back-date a new term.</summary>
    private const int MaxBackdateDays = 30;

    /// <summary>How far in the future an operator may pre-sell a term.</summary>
    private const int MaxFutureDays = 90;

    /// <summary>Rows touched per round trip by <see cref="ProcessExpiriesAsync"/>.</summary>
    private const int BatchSize = 200;

    private readonly GymDbContext _db;
    private readonly ICodeGeneratorService _codes;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly IPaymentReceiptMailer _receiptMailer;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        GymDbContext db,
        ICodeGeneratorService codes,
        IDateTimeProvider clock,
        ICurrentUserService currentUser,
        IAuditService audit,
        ISettingsService settings,
        INotificationService notifications,
        IPaymentReceiptMailer receiptMailer,
        ILogger<SubscriptionService> logger)
    {
        _db = db;
        _codes = codes;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
        _settings = settings;
        _notifications = notifications;
        _receiptMailer = receiptMailer;
        _logger = logger;
    }

    /// <summary>
    /// Emails the receipt for an inline payment, once the subscription transaction has committed.
    /// The mailer refuses to send twice for the same payment and promises never to throw; the catch
    /// here does not rely on that promise, because the subscription and the money are already
    /// committed and nothing about a receipt may turn that into a failed request.
    /// </summary>
    private async Task SendReceiptEmailAsync(Payment? payment, CancellationToken ct)
    {
        if (payment is null) return;

        try
        {
            await _receiptMailer.SendReceiptAsync(payment.Id, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not email the receipt for payment {PaymentId}. The subscription and payment are unaffected.",
                payment.Id);
        }
    }

    // ======================================================================
    //  Pricing — the single source of truth for every amount in this module
    // ======================================================================

    /// <summary>Immutable, fully rounded price breakdown for one subscription term.</summary>
    private sealed record PricingResult(
        decimal PlanAmount,
        decimal RegistrationFee,
        decimal ProratedCredit,
        decimal DiscountAmount,
        decimal MaxAllowedDiscount,
        decimal TaxPercent,
        decimal TaxAmount,
        decimal FinalAmount,
        DateTime StartDate,
        DateTime EndDate,
        int TotalDays,
        int GracePeriodDays);

    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The one and only price calculator. Used by <see cref="GetQuoteAsync"/>, <see cref="CreateAsync"/>,
    /// <see cref="RenewAsync"/> and <see cref="ChangePlanAsync"/> so the quoted numbers and the stored
    /// numbers are produced by identical code.
    /// </summary>
    /// <param name="plan">Plan being sold. Supplies price, tax, discount ceiling and term length.</param>
    /// <param name="startDate">First day of the new term (inclusive).</param>
    /// <param name="requestedDiscount">Discount asked for by the operator; validated, never trusted.</param>
    /// <param name="chargeRegistrationFee">False for renewals and plan changes.</param>
    /// <param name="proratedCredit">Unused value carried over from a replaced subscription.</param>
    /// <param name="defaultGracePeriodDays">Gym-wide fallback when the plan defines no grace period.</param>
    private static PricingResult CalculatePricing(
        MembershipPlan plan,
        DateTime startDate,
        decimal requestedDiscount,
        bool chargeRegistrationFee,
        decimal proratedCredit,
        int defaultGracePeriodDays)
    {
        var planAmount = Round2(plan.Price);
        var registrationFee = chargeRegistrationFee ? Round2(plan.RegistrationFee ?? 0m) : 0m;
        var credit = Round2(Math.Max(0m, proratedCredit));

        var maxAllowedDiscount = Round2((planAmount + registrationFee) * plan.MaxDiscountPercent / 100m);

        // clamp(requestedDiscount, 0, maxAllowedDiscount) — anything above the ceiling is rejected
        // outright rather than silently trimmed, so the operator sees why the total changed.
        var discount = requestedDiscount < 0m ? 0m : Round2(requestedDiscount);
        if (discount > maxAllowedDiscount)
            throw new ValidationAppException("DiscountAmount",
                $"The discount of {discount:0.00} exceeds the maximum of {maxAllowedDiscount:0.00} allowed by plan '{plan.Name}' " +
                $"({plan.MaxDiscountPercent:0.##}%).");

        var taxableBase = planAmount + registrationFee - discount - credit;
        if (taxableBase < 0m) taxableBase = 0m;

        var taxAmount = Round2(taxableBase * plan.TaxPercent / 100m);
        var finalAmount = Round2(taxableBase + taxAmount);

        var start = startDate.Date;
        var gracePeriodDays = plan.GracePeriodDays > 0
            ? plan.GracePeriodDays
            : Math.Max(0, defaultGracePeriodDays);

        return new PricingResult(
            planAmount,
            registrationFee,
            credit,
            discount,
            maxAllowedDiscount,
            plan.TaxPercent,
            taxAmount,
            finalAmount,
            start,
            plan.CalculateEndDate(start).Date,
            plan.TotalDays,
            gracePeriodDays);
    }

    /// <summary>
    /// Unused value of a subscription that is being replaced:
    /// <c>unusedDays / totalDays * PlanAmount</c>, rounded to 2dp and capped at the amount actually
    /// charged for the plan.
    /// </summary>
    private static decimal CalculateProratedCredit(Subscription current, MembershipPlan? currentPlan, DateTime asOf)
    {
        var totalDays = currentPlan?.TotalDays ?? 0;
        if (totalDays <= 0)
            totalDays = Math.Max(1, (current.EndDate.Date - current.StartDate.Date).Days + 1);

        // EndDate is inclusive, so the day the replacement starts is still an unused day.
        var unusedDays = (current.EndDate.Date - asOf.Date).Days + 1;
        if (unusedDays <= 0) return 0m;
        if (unusedDays > totalDays) unusedDays = totalDays;

        var credit = Round2(unusedDays / (decimal)totalDays * current.PlanAmount);
        return Math.Min(credit, Round2(current.PlanAmount));
    }

    public async Task<SubscriptionQuoteDto> GetQuoteAsync(QuoteRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plan = await _db.MembershipPlans.AsNoTracking()
                       .FirstOrDefaultAsync(p => p.Id == request.MembershipPlanId, ct).ConfigureAwait(false)
                   ?? throw new NotFoundAppException(nameof(MembershipPlan), request.MembershipPlanId);

        var credit = 0m;
        if (request.ProrateUnusedAmount && request.CurrentSubscriptionId is > 0)
        {
            var current = await _db.Subscriptions.AsNoTracking()
                .Include(s => s.MembershipPlan)
                .FirstOrDefaultAsync(s => s.Id == request.CurrentSubscriptionId!.Value, ct)
                .ConfigureAwait(false);

            if (current is not null)
                credit = CalculateProratedCredit(current, current.MembershipPlan, request.StartDate);
        }

        var gym = await _settings.GetGymSettingsAsync(ct).ConfigureAwait(false);
        var p = CalculatePricing(plan, request.StartDate, request.DiscountAmount,
            request.ChargeRegistrationFee, credit, gym.DefaultGracePeriodDays);

        return new SubscriptionQuoteDto
        {
            MembershipPlanId = plan.Id,
            PlanName = plan.Name,
            StartDate = p.StartDate,
            EndDate = p.EndDate,
            TotalDays = p.TotalDays,
            PlanAmount = p.PlanAmount,
            RegistrationFee = p.RegistrationFee,
            ProratedCredit = p.ProratedCredit,
            DiscountAmount = p.DiscountAmount,
            MaxAllowedDiscount = p.MaxAllowedDiscount,
            TaxPercent = p.TaxPercent,
            TaxAmount = p.TaxAmount,
            FinalAmount = p.FinalAmount
        };
    }

    // ======================================================================
    //  Create
    // ======================================================================

    public async Task<SubscriptionDto> CreateAsync(CreateSubscriptionDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var today = _clock.Today.Date;
        var gym = await _settings.GetGymSettingsAsync(ct).ConfigureAwait(false);

        await using var tx = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // 1. member must exist, must not be in the recycle bin and must not be suspended
            var member = await LoadSellableMemberAsync(dto.MemberId, ct).ConfigureAwait(false);

            // 2. plan must exist and be on sale
            var plan = await LoadActivePlanAsync(dto.MembershipPlanId, ct).ConfigureAwait(false);

            var startDate = dto.StartDate.Date;
            var endDate = plan.CalculateEndDate(startDate).Date;

            // 3. no overlapping active term — the operator must renew instead
            await EnsureNoOverlapAsync(member.Id, startDate, endDate, null, ct).ConfigureAwait(false);

            // 4. sane start date window
            ValidateStartDate(startDate, today);

            await EnsureTrainerExistsAsync(dto.AssignedTrainerId, ct).ConfigureAwait(false);

            // 5. amounts are computed here, never taken from the client
            var pricing = CalculatePricing(plan, startDate, dto.DiscountAmount,
                dto.ChargeRegistrationFee, 0m, gym.DefaultGracePeriodDays);

            var subscription = NewSubscription(member.Id, plan.Id, pricing, dto.AssignedTrainerId, dto.Notes);
            subscription.SubscriptionCode = await _codes.NextSubscriptionCodeAsync(ct).ConfigureAwait(false);

            _db.Subscriptions.Add(subscription);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            // 6. optional inline payment
            var payment = await AddInlinePaymentAsync(subscription, member.Id, dto.Payment, ct).ConfigureAwait(false);

            // 7. paid amount is derived from CONFIRMED payments only
            await ApplyPaymentTotalsAsync(subscription, ct).ConfigureAwait(false);
            PromoteMemberIfNeeded(member, subscription);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            // 8. append-only history
            AddHistory(subscription.Id, SubscriptionActionType.Created, null, subscription.Status,
                null, subscription.EndDate, subscription.FinalAmount,
                $"Subscription {subscription.SubscriptionCode} created on plan '{plan.Name}'.");

            if (payment is not null)
                AddHistory(subscription.Id, SubscriptionActionType.PaymentReceived, null, subscription.Status,
                    null, null, payment.FinalAmount,
                    $"Payment {payment.ReceiptNumber} of {payment.FinalAmount:0.00} recorded ({payment.Status}).");

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            var result = await LoadDtoAsync(subscription.Id, gym.ExpiryReminderDays, today, ct).ConfigureAwait(false);

            // 9. audit + notifications
            await _audit.LogAsync(AuditActions.SubscriptionCreated, EntityName, subscription.Id, null,
                new
                {
                    subscription.SubscriptionCode, subscription.MemberId, subscription.MembershipPlanId,
                    subscription.StartDate, subscription.EndDate, subscription.PlanAmount,
                    subscription.RegistrationFee, subscription.DiscountAmount, subscription.TaxAmount,
                    subscription.FinalAmount, subscription.PaidAmount, subscription.Status
                },
                $"Subscription {subscription.SubscriptionCode} created for member {member.MemberCode}.",
                ct).ConfigureAwait(false);

            await NotifyPaymentOutcomeAsync(subscription, member, ct).ConfigureAwait(false);

            await tx.CommitAsync(ct).ConfigureAwait(false);

            await SendReceiptEmailAsync(payment, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Subscription {Code} created for member {MemberId}: final {Final}, paid {Paid}, status {Status}.",
                subscription.SubscriptionCode, member.Id, subscription.FinalAmount, subscription.PaidAmount,
                subscription.Status);

            return result;
        }
        catch (Exception ex)
        {
            await SafeRollbackAsync(tx, ex, nameof(CreateAsync)).ConfigureAwait(false);
            throw;
        }
    }

    // ======================================================================
    //  Renew
    // ======================================================================

    public async Task<SubscriptionDto> RenewAsync(RenewSubscriptionDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var today = _clock.Today.Date;
        var gym = await _settings.GetGymSettingsAsync(ct).ConfigureAwait(false);

        await using var tx = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            var previous = await ResolvePreviousSubscriptionAsync(dto, ct).ConfigureAwait(false);
            var member = await LoadSellableMemberAsync(previous.MemberId, ct).ConfigureAwait(false);
            var plan = await LoadActivePlanAsync(dto.MembershipPlanId ?? previous.MembershipPlanId, ct)
                .ConfigureAwait(false);

            // Default: the day after the current term. If that term is already over, start today so
            // the member does not pay for days that have already elapsed.
            var naturalStart = previous.EndDate.Date < today ? today : previous.EndDate.Date.AddDays(1);
            var startDate = (dto.StartDate?.Date ?? naturalStart).Date;

            var endDate = plan.CalculateEndDate(startDate).Date;

            await EnsureNoOverlapAsync(member.Id, startDate, endDate, previous.Id, ct).ConfigureAwait(false);

            // Renewing early is normal: an annual term ending next August continues the day after,
            // which is far beyond the usual 90-day ceiling. Allow up to that natural continuation
            // so the service cannot reject the very date it just computed, while still catching a
            // mistyped year beyond it.
            ValidateStartDate(startDate, today, allowUpTo: naturalStart);
            await EnsureTrainerExistsAsync(dto.AssignedTrainerId, ct).ConfigureAwait(false);

            var pricing = CalculatePricing(plan, startDate, dto.DiscountAmount,
                chargeRegistrationFee: false, proratedCredit: 0m,
                defaultGracePeriodDays: gym.DefaultGracePeriodDays);

            var subscription = NewSubscription(member.Id, plan.Id, pricing,
                dto.AssignedTrainerId ?? previous.AssignedTrainerId, dto.Notes);
            subscription.SubscriptionCode = await _codes.NextSubscriptionCodeAsync(ct).ConfigureAwait(false);
            subscription.IsRenewal = true;
            subscription.PreviousSubscriptionId = previous.Id;

            _db.Subscriptions.Add(subscription);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            var payment = await AddInlinePaymentAsync(subscription, member.Id, dto.Payment, ct).ConfigureAwait(false);

            await ApplyPaymentTotalsAsync(subscription, ct).ConfigureAwait(false);
            PromoteMemberIfNeeded(member, subscription);

            // The previous term is deliberately left alone: an Active one runs to its own end date and
            // is picked up by ProcessExpiriesAsync, an already Expired one stays Expired.
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            AddHistory(subscription.Id, SubscriptionActionType.Created, null, subscription.Status,
                null, subscription.EndDate, subscription.FinalAmount,
                $"Subscription {subscription.SubscriptionCode} created on plan '{plan.Name}'.");

            AddHistory(subscription.Id, SubscriptionActionType.Renewed, null, subscription.Status,
                previous.EndDate, subscription.EndDate, subscription.FinalAmount,
                $"Renewal of {previous.SubscriptionCode} ({previous.StartDate:yyyy-MM-dd} to {previous.EndDate:yyyy-MM-dd}).");

            if (payment is not null)
                AddHistory(subscription.Id, SubscriptionActionType.PaymentReceived, null, subscription.Status,
                    null, null, payment.FinalAmount,
                    $"Payment {payment.ReceiptNumber} of {payment.FinalAmount:0.00} recorded ({payment.Status}).");

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            var result = await LoadDtoAsync(subscription.Id, gym.ExpiryReminderDays, today, ct).ConfigureAwait(false);

            await _audit.LogAsync(AuditActions.SubscriptionRenewed, EntityName, subscription.Id,
                new { previous.Id, previous.SubscriptionCode, previous.EndDate, previous.Status },
                new
                {
                    subscription.SubscriptionCode, subscription.StartDate, subscription.EndDate,
                    subscription.FinalAmount, subscription.PaidAmount, subscription.Status
                },
                $"Subscription {previous.SubscriptionCode} renewed as {subscription.SubscriptionCode}.",
                ct).ConfigureAwait(false);

            await NotifyPaymentOutcomeAsync(subscription, member, ct).ConfigureAwait(false);

            await tx.CommitAsync(ct).ConfigureAwait(false);

            await SendReceiptEmailAsync(payment, ct).ConfigureAwait(false);

            return result;
        }
        catch (Exception ex)
        {
            await SafeRollbackAsync(tx, ex, nameof(RenewAsync)).ConfigureAwait(false);
            throw;
        }
    }

    // ======================================================================
    //  Change plan (upgrade / downgrade)
    // ======================================================================

    public async Task<SubscriptionDto> ChangePlanAsync(ChangePlanDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var today = _clock.Today.Date;
        var gym = await _settings.GetGymSettingsAsync(ct).ConfigureAwait(false);

        await using var tx = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            var current = await _db.Subscriptions
                              .Include(s => s.MembershipPlan)
                              .FirstOrDefaultAsync(s => s.Id == dto.SubscriptionId, ct).ConfigureAwait(false)
                          ?? throw new NotFoundAppException(EntityName, dto.SubscriptionId);

            if (current.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Frozen))
                throw new BusinessRuleAppException(
                    $"Only an active or frozen subscription can change plan. {current.SubscriptionCode} is {current.Status}.");

            if (dto.NewMembershipPlanId == current.MembershipPlanId)
                throw new ValidationAppException(nameof(dto.NewMembershipPlanId),
                    "The new plan must be different from the current plan.");

            var member = await LoadSellableMemberAsync(current.MemberId, ct).ConfigureAwait(false);
            var newPlan = await LoadActivePlanAsync(dto.NewMembershipPlanId, ct).ConfigureAwait(false);

            var effectiveDate = dto.EffectiveDate.Date;
            if (effectiveDate <= current.StartDate.Date)
                throw new ValidationAppException(nameof(dto.EffectiveDate),
                    $"The effective date must be after the current term started ({current.StartDate:yyyy-MM-dd}).");

            ValidateStartDate(effectiveDate, today);

            var credit = dto.ProrateUnusedAmount
                ? CalculateProratedCredit(current, current.MembershipPlan, effectiveDate)
                : 0m;

            // A plan change never re-charges the registration fee.
            var pricing = CalculatePricing(newPlan, effectiveDate, dto.DiscountAmount,
                chargeRegistrationFee: false, proratedCredit: credit,
                defaultGracePeriodDays: gym.DefaultGracePeriodDays);

            var isUpgrade = Round2(newPlan.Price) > Round2(current.PlanAmount);
            var closingStatus = isUpgrade ? SubscriptionStatus.Upgraded : SubscriptionStatus.Downgraded;
            var closingAction = isUpgrade ? SubscriptionActionType.Upgraded : SubscriptionActionType.Downgraded;

            var oldStatus = current.Status;
            var oldEndDate = current.EndDate;

            current.Status = closingStatus;
            current.EndDate = effectiveDate.AddDays(-1).Date;
            if (current.FreezeStartDate is not null || current.FreezeEndDate is not null)
            {
                current.FreezeStartDate = null;
                current.FreezeEndDate = null;
            }

            var replacement = NewSubscription(member.Id, newPlan.Id, pricing,
                current.AssignedTrainerId, dto.Reason ?? current.Notes);
            replacement.SubscriptionCode = await _codes.NextSubscriptionCodeAsync(ct).ConfigureAwait(false);
            replacement.PreviousSubscriptionId = current.Id;

            _db.Subscriptions.Add(replacement);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            var payment = await AddInlinePaymentAsync(replacement, member.Id, dto.Payment, ct).ConfigureAwait(false);

            await ApplyPaymentTotalsAsync(replacement, ct).ConfigureAwait(false);
            PromoteMemberIfNeeded(member, replacement);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            AddHistory(current.Id, closingAction, oldStatus, closingStatus, oldEndDate, current.EndDate, credit,
                $"Closed on {closingStatus.ToString().ToLowerInvariant()} to plan '{newPlan.Name}' " +
                $"({replacement.SubscriptionCode}); prorated credit {credit:0.00}. {dto.Reason}".TrimEnd());

            AddHistory(replacement.Id, closingAction, null, replacement.Status, null, replacement.EndDate,
                replacement.FinalAmount,
                $"Replaces {current.SubscriptionCode} from {effectiveDate:yyyy-MM-dd}; " +
                $"credit applied {credit:0.00}, payable {replacement.FinalAmount:0.00}.");

            if (payment is not null)
                AddHistory(replacement.Id, SubscriptionActionType.PaymentReceived, null, replacement.Status,
                    null, null, payment.FinalAmount,
                    $"Payment {payment.ReceiptNumber} of {payment.FinalAmount:0.00} recorded ({payment.Status}).");

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            var result = await LoadDtoAsync(replacement.Id, gym.ExpiryReminderDays, today, ct).ConfigureAwait(false);

            await _audit.LogAsync(AuditActions.SubscriptionChanged, EntityName, replacement.Id,
                new { current.Id, current.SubscriptionCode, OldStatus = oldStatus, OldEndDate = oldEndDate, current.PlanAmount },
                new
                {
                    replacement.SubscriptionCode, replacement.MembershipPlanId, replacement.StartDate,
                    replacement.EndDate, ProratedCredit = credit, replacement.FinalAmount, replacement.Status
                },
                $"Subscription {current.SubscriptionCode} {closingStatus.ToString().ToLowerInvariant()} to " +
                $"{replacement.SubscriptionCode} on plan '{newPlan.Name}'.", ct).ConfigureAwait(false);

            await NotifyPaymentOutcomeAsync(replacement, member, ct).ConfigureAwait(false);

            await tx.CommitAsync(ct).ConfigureAwait(false);

            await SendReceiptEmailAsync(payment, ct).ConfigureAwait(false);

            return result;
        }
        catch (Exception ex)
        {
            await SafeRollbackAsync(tx, ex, nameof(ChangePlanAsync)).ConfigureAwait(false);
            throw;
        }
    }

    // ======================================================================
    //  Freeze / resume
    // ======================================================================

    public async Task<SubscriptionDto> FreezeAsync(FreezeSubscriptionDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var today = _clock.Today.Date;
        var gym = await _settings.GetGymSettingsAsync(ct).ConfigureAwait(false);

        await using var tx = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            var sub = await _db.Subscriptions
                          .Include(s => s.MembershipPlan)
                          .FirstOrDefaultAsync(s => s.Id == dto.SubscriptionId, ct).ConfigureAwait(false)
                      ?? throw new NotFoundAppException(EntityName, dto.SubscriptionId);

            if (sub.Status != SubscriptionStatus.Active)
                throw new BusinessRuleAppException(
                    $"Only an active subscription can be frozen. {sub.SubscriptionCode} is {sub.Status}.");

            var plan = sub.MembershipPlan
                       ?? throw new NotFoundAppException(nameof(MembershipPlan), sub.MembershipPlanId);

            if (plan.MaxFreezeDays <= 0)
                throw new BusinessRuleAppException($"Plan '{plan.Name}' does not allow freezing.");

            var freezeStart = dto.FreezeStartDate.Date;
            var freezeEnd = dto.FreezeEndDate.Date;

            if (freezeEnd <= freezeStart)
                throw new ValidationAppException(nameof(dto.FreezeEndDate),
                    "The freeze end date must be after the freeze start date.");

            if (freezeStart < today)
                throw new ValidationAppException(nameof(dto.FreezeStartDate),
                    "A freeze cannot start in the past.");

            var requestedDays = (freezeEnd - freezeStart).Days;
            var remainingAllowance = plan.MaxFreezeDays - sub.FrozenDaysUsed;
            if (requestedDays > remainingAllowance)
                throw new BusinessRuleAppException(
                    $"The requested freeze of {requestedDays} day(s) exceeds the {remainingAllowance} day(s) still " +
                    $"available on plan '{plan.Name}' (limit {plan.MaxFreezeDays}, already used {sub.FrozenDaysUsed}).");

            var oldStatus = sub.Status;
            var oldEndDate = sub.EndDate;

            sub.Status = SubscriptionStatus.Frozen;
            sub.FreezeStartDate = freezeStart;
            sub.FreezeEndDate = freezeEnd;
            // The member keeps every paid day: the term slides out by the frozen day count.
            sub.EndDate = sub.EndDate.Date.AddDays(requestedDays);

            AddHistory(sub.Id, SubscriptionActionType.Frozen, oldStatus, sub.Status, oldEndDate, sub.EndDate, null,
                $"Frozen {freezeStart:yyyy-MM-dd} to {freezeEnd:yyyy-MM-dd} ({requestedDays} day(s)). {dto.Reason}".TrimEnd());

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            var result = await LoadDtoAsync(sub.Id, gym.ExpiryReminderDays, today, ct).ConfigureAwait(false);

            await _audit.LogAsync(AuditActions.SubscriptionFrozen, EntityName, sub.Id,
                new { OldStatus = oldStatus, OldEndDate = oldEndDate },
                new { sub.Status, sub.EndDate, sub.FreezeStartDate, sub.FreezeEndDate, FrozenDays = requestedDays },
                $"Subscription {sub.SubscriptionCode} frozen for {requestedDays} day(s).", ct).ConfigureAwait(false);

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            await SafeRollbackAsync(tx, ex, nameof(FreezeAsync)).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<SubscriptionDto> ResumeAsync(ResumeSubscriptionDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var today = _clock.Today.Date;
        var gym = await _settings.GetGymSettingsAsync(ct).ConfigureAwait(false);

        await using var tx = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            var sub = await _db.Subscriptions.FirstOrDefaultAsync(s => s.Id == dto.SubscriptionId, ct)
                          .ConfigureAwait(false)
                      ?? throw new NotFoundAppException(EntityName, dto.SubscriptionId);

            if (sub.Status != SubscriptionStatus.Frozen)
                throw new BusinessRuleAppException(
                    $"Only a frozen subscription can be resumed. {sub.SubscriptionCode} is {sub.Status}.");

            var oldStatus = sub.Status;
            var oldEndDate = sub.EndDate;
            var (actualDays, plannedDays) = ResolveFreezeDays(sub, dto.ResumeDate);

            // Freezing extended EndDate by plannedDays; give back the days that were not used.
            var overExtension = plannedDays - actualDays;
            if (overExtension > 0) sub.EndDate = sub.EndDate.Date.AddDays(-overExtension);

            sub.FrozenDaysUsed += actualDays;
            sub.FreezeStartDate = null;
            sub.FreezeEndDate = null;
            sub.Status = SubscriptionStatus.Active;

            AddHistory(sub.Id, SubscriptionActionType.Resumed, oldStatus, sub.Status, oldEndDate, sub.EndDate, null,
                $"Resumed on {dto.ResumeDate:yyyy-MM-dd} after {actualDays} of {plannedDays} frozen day(s). {dto.Remarks}"
                    .TrimEnd());

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            var result = await LoadDtoAsync(sub.Id, gym.ExpiryReminderDays, today, ct).ConfigureAwait(false);

            await _audit.LogAsync(AuditActions.SubscriptionResumed, EntityName, sub.Id,
                new { OldStatus = oldStatus, OldEndDate = oldEndDate, PlannedFrozenDays = plannedDays },
                new { sub.Status, sub.EndDate, ActualFrozenDays = actualDays, sub.FrozenDaysUsed },
                $"Subscription {sub.SubscriptionCode} resumed after {actualDays} frozen day(s).",
                ct).ConfigureAwait(false);

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            await SafeRollbackAsync(tx, ex, nameof(ResumeAsync)).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Actual vs planned frozen days for a resume, floored at zero.</summary>
    private static (int Actual, int Planned) ResolveFreezeDays(Subscription sub, DateTime resumeDate)
    {
        var start = (sub.FreezeStartDate ?? sub.StartDate).Date;
        var end = (sub.FreezeEndDate ?? start).Date;

        var planned = Math.Max(0, (end - start).Days);
        var effectiveResume = resumeDate.Date < end ? resumeDate.Date : end;
        var actual = Math.Max(0, (effectiveResume - start).Days);
        if (actual > planned) actual = planned;

        return (actual, planned);
    }

    // ======================================================================
    //  Cancel
    // ======================================================================

    public async Task<SubscriptionDto> CancelAsync(CancelSubscriptionDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.Reason))
            throw new ValidationAppException(nameof(dto.Reason), "A cancellation reason is required.");

        var today = _clock.Today.Date;
        var gym = await _settings.GetGymSettingsAsync(ct).ConfigureAwait(false);

        await using var tx = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            var sub = await _db.Subscriptions.FirstOrDefaultAsync(s => s.Id == dto.SubscriptionId, ct)
                          .ConfigureAwait(false)
                      ?? throw new NotFoundAppException(EntityName, dto.SubscriptionId);

            if (sub.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Frozen or SubscriptionStatus.Pending))
                throw new BusinessRuleAppException(
                    $"Only an active, frozen or pending subscription can be cancelled. {sub.SubscriptionCode} is {sub.Status}.");

            var oldStatus = sub.Status;
            sub.Status = SubscriptionStatus.Cancelled;
            sub.CancelledAt = _clock.UtcNow;
            sub.CancellationReason = Truncate(dto.Reason.Trim(), 512);
            sub.FreezeStartDate = null;
            sub.FreezeEndDate = null;

            var refundTotal = 0m;
            var refundRows = 0;
            if (dto.RefundRemainingAmount)
                (refundTotal, refundRows) = await CreatePendingRefundsAsync(sub, today, ct).ConfigureAwait(false);

            AddHistory(sub.Id, SubscriptionActionType.Cancelled, oldStatus, sub.Status, sub.EndDate, sub.EndDate,
                refundTotal > 0m ? refundTotal : null,
                refundRows > 0
                    ? $"Cancelled: {sub.CancellationReason} Refund of {refundTotal:0.00} raised across {refundRows} payment(s), pending approval."
                    : $"Cancelled: {sub.CancellationReason}");

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            var result = await LoadDtoAsync(sub.Id, gym.ExpiryReminderDays, today, ct).ConfigureAwait(false);

            await _audit.LogAsync(AuditActions.SubscriptionCancelled, EntityName, sub.Id,
                new { OldStatus = oldStatus, sub.PaidAmount, sub.FinalAmount },
                new { sub.Status, sub.CancelledAt, sub.CancellationReason, RefundRequested = refundTotal },
                $"Subscription {sub.SubscriptionCode} cancelled." +
                (refundRows > 0 ? $" Pending refund {refundTotal:0.00}." : string.Empty),
                ct).ConfigureAwait(false);

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            await SafeRollbackAsync(tx, ex, nameof(CancelAsync)).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Raises <see cref="RefundStatus.Pending"/> refund rows for the unused, prorated share of the
    /// CONFIRMED payments. Nothing is ever refunded automatically: an operator holding
    /// <c>payments.refund</c> must approve, which is also what moves <c>Payment.RefundedAmount</c>.
    /// </summary>
    private async Task<(decimal Total, int Rows)> CreatePendingRefundsAsync(
        Subscription sub, DateTime today, CancellationToken ct)
    {
        var payments = await _db.Payments
            .Where(p => p.SubscriptionId == sub.Id && p.Status == PaymentStatus.Paid)
            .OrderBy(p => p.PaymentDate).ThenBy(p => p.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (payments.Count == 0) return (0m, 0);

        var confirmedPaid = Round2(payments.Sum(p => p.FinalAmount));
        var alreadyRefunded = Round2(payments.Sum(p => p.RefundedAmount));

        // Refunds already awaiting approval must not be raised twice.
        var paymentIds = payments.Select(p => p.Id).ToList();
        var pendingRefunds = await _db.PaymentRefunds
            .Where(r => paymentIds.Contains(r.PaymentId) &&
                        (r.Status == RefundStatus.Pending || r.Status == RefundStatus.Approved))
            .SumAsync(r => (decimal?)r.Amount, ct)
            .ConfigureAwait(false) ?? 0m;

        var totalDays = Math.Max(1, (sub.EndDate.Date - sub.StartDate.Date).Days + 1);
        var unusedDays = (sub.EndDate.Date - today).Days + 1;
        if (unusedDays <= 0) return (0m, 0);
        if (unusedDays > totalDays) unusedDays = totalDays;

        var prorated = Round2(unusedDays / (decimal)totalDays * confirmedPaid);
        var ceiling = Round2(confirmedPaid - alreadyRefunded - Round2(pendingRefunds));
        var refundable = Math.Min(prorated, ceiling);
        if (refundable <= 0m) return (0m, 0);

        const string approvalNote =
            "Raised automatically on subscription cancellation. An operator with the payments.refund " +
            "permission must approve this refund before any money leaves the till.";

        var remaining = refundable;
        var rows = 0;
        foreach (var payment in payments)
        {
            if (remaining <= 0m) break;

            var share = Math.Min(remaining, Round2(payment.RefundableAmount));
            if (share <= 0m) continue;

            _db.PaymentRefunds.Add(new PaymentRefund
            {
                RefundNumber = await _codes.NextRefundNumberAsync(ct).ConfigureAwait(false),
                PaymentId = payment.Id,
                Amount = share,
                Reason = Truncate(
                    $"Subscription {sub.SubscriptionCode} cancelled: {sub.CancellationReason}", 512)!,
                Status = RefundStatus.Pending,
                RequestedAt = _clock.UtcNow,
                RequestedByUserId = _currentUser.UserId,
                Remarks = Truncate(
                    $"{approvalNote} Unused {unusedDays} of {totalDays} day(s) on receipt {payment.ReceiptNumber}.",
                    1000)
            });

            remaining -= share;
            rows++;
        }

        return (Round2(refundable - remaining), rows);
    }

    // ======================================================================
    //  Reads
    // ======================================================================

    public async Task<PagedResult<SubscriptionDto>> GetPagedAsync(
        SubscriptionQueryDto query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var today = _clock.Today.Date;
        var gym = await _settings.GetGymSettingsAsync(ct).ConfigureAwait(false);

        IQueryable<Subscription> q = _db.Subscriptions.AsNoTracking();
        if (query.IncludeDeleted) q = q.IgnoreQueryFilters();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(s =>
                EF.Functions.Like(s.SubscriptionCode, $"%{term}%") ||
                EF.Functions.Like(s.Member!.MemberCode, $"%{term}%") ||
                EF.Functions.Like(s.Member!.FullName, $"%{term}%"));
        }

        if (query.MemberId is > 0) q = q.Where(s => s.MemberId == query.MemberId!.Value);
        if (query.MembershipPlanId is > 0) q = q.Where(s => s.MembershipPlanId == query.MembershipPlanId!.Value);
        if (query.Status.HasValue) q = q.Where(s => s.Status == query.Status.Value);
        if (query.PaymentStatus.HasValue) q = q.Where(s => s.PaymentStatus == query.PaymentStatus.Value);

        if (query.StartFrom.HasValue)
        {
            var from = query.StartFrom.Value.Date;
            q = q.Where(s => s.StartDate >= from);
        }

        if (query.StartTo.HasValue)
        {
            var to = query.StartTo.Value.Date;
            q = q.Where(s => s.StartDate <= to);
        }

        if (query.EndFrom.HasValue)
        {
            var from = query.EndFrom.Value.Date;
            q = q.Where(s => s.EndDate >= from);
        }

        if (query.EndTo.HasValue)
        {
            var to = query.EndTo.Value.Date;
            q = q.Where(s => s.EndDate <= to);
        }

        if (query.OnlyRenewals == true) q = q.Where(s => s.IsRenewal);

        // The set the renewal-reminder emails target: expiring rows whose member holds no later
        // Active/Pending term. Matches the mailer's own exclusion word for word.
        if (query.ExcludeRenewed == true)
        {
            q = q.Where(s => !_db.Subscriptions.Any(o =>
                o.MemberId == s.MemberId
                && o.Id != s.Id
                && (o.Status == SubscriptionStatus.Active || o.Status == SubscriptionStatus.Pending)
                && o.EndDate > s.EndDate));
        }

        if (query.OnlyExpiringSoon == true)
        {
            var horizon = today.AddDays(Math.Max(0, query.ExpiringWithinDays));
            q = q.Where(s => s.Status == SubscriptionStatus.Active &&
                             s.EndDate >= today && s.EndDate <= horizon);
        }

        if (query.OnlyWithOutstanding == true) q = q.Where(s => s.FinalAmount > s.PaidAmount);

        q = ApplySort(q, query.SortBy, query.SortDescending);

        var total = await q.CountAsync(ct).ConfigureAwait(false);

        var items = await Project(q.Skip(query.Skip).Take(query.PageSize))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var item in items) Decorate(item, today, gym.ExpiryReminderDays);

        return new PagedResult<SubscriptionDto>(items, total, query.PageNumber, query.PageSize);
    }

    public async Task<SubscriptionDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var gym = await _settings.GetGymSettingsAsync(ct).ConfigureAwait(false);
        return await LoadDtoAsync(id, gym.ExpiryReminderDays, _clock.Today.Date, ct).ConfigureAwait(false);
    }

    public async Task<List<SubscriptionDto>> GetForMemberAsync(int memberId, CancellationToken ct = default)
    {
        var today = _clock.Today.Date;
        var gym = await _settings.GetGymSettingsAsync(ct).ConfigureAwait(false);

        var items = await Project(_db.Subscriptions.AsNoTracking()
                .Where(s => s.MemberId == memberId)
                .OrderByDescending(s => s.StartDate).ThenByDescending(s => s.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var item in items) Decorate(item, today, gym.ExpiryReminderDays);
        return items;
    }

    public async Task<SubscriptionDto?> GetActiveForMemberAsync(int memberId, CancellationToken ct = default)
    {
        var today = _clock.Today.Date;
        var gym = await _settings.GetGymSettingsAsync(ct).ConfigureAwait(false);

        var candidates = await _db.Subscriptions.AsNoTracking()
            .Where(s => s.MemberId == memberId &&
                        s.Status == SubscriptionStatus.Active &&
                        s.StartDate <= today &&
                        today <= s.EndDate.AddDays(s.GracePeriodDays))
            .OrderByDescending(s => s.EndDate).ThenByDescending(s => s.Id)
            .Select(s => s.Id)
            .Take(1)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (candidates.Count == 0) return null;

        var dto = await Project(_db.Subscriptions.AsNoTracking().Where(s => s.Id == candidates[0]))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (dto is null) return null;
        Decorate(dto, today, gym.ExpiryReminderDays);
        return dto;
    }

    public async Task<List<SubscriptionHistoryDto>> GetHistoryAsync(int subscriptionId, CancellationToken ct = default)
    {
        var exists = await _db.Subscriptions.AsNoTracking().IgnoreQueryFilters()
            .AnyAsync(s => s.Id == subscriptionId, ct)
            .ConfigureAwait(false);
        if (!exists) throw new NotFoundAppException(EntityName, subscriptionId);

        return await _db.SubscriptionHistory.AsNoTracking()
            .Where(h => h.SubscriptionId == subscriptionId)
            .OrderByDescending(h => h.ActionDate).ThenByDescending(h => h.Id)
            .Select(h => new SubscriptionHistoryDto
            {
                Id = h.Id,
                SubscriptionId = h.SubscriptionId,
                ActionType = h.ActionType,
                ActionDate = h.ActionDate,
                OldStatus = h.OldStatus,
                NewStatus = h.NewStatus,
                OldEndDate = h.OldEndDate,
                NewEndDate = h.NewEndDate,
                Amount = h.Amount,
                PerformedByName = h.PerformedByUser != null ? h.PerformedByUser.FullName : null,
                Remarks = h.Remarks
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    // ======================================================================
    //  Soft delete / restore
    // ======================================================================

    public async Task SoftDeleteAsync(int id, CancellationToken ct = default)
    {
        var sub = await _db.Subscriptions.FirstOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false)
                  ?? throw new NotFoundAppException(EntityName, id);

        var confirmedPayments = await _db.Payments.AsNoTracking()
            .CountAsync(p => p.SubscriptionId == id && p.Status == PaymentStatus.Paid, ct)
            .ConfigureAwait(false);

        if (confirmedPayments > 0)
            throw new BusinessRuleAppException(
                $"Subscription {sub.SubscriptionCode} has {confirmedPayments} confirmed payment(s) and cannot be deleted. " +
                "Cancel it instead so the money trail stays intact.");

        _db.Subscriptions.Remove(sub); // the context converts this into a soft delete
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.SoftDelete, EntityName, id,
            new { sub.SubscriptionCode, sub.MemberId, sub.Status }, null,
            $"Subscription {sub.SubscriptionCode} moved to the recycle bin.", ct).ConfigureAwait(false);
    }

    public async Task RestoreAsync(int id, CancellationToken ct = default)
    {
        var sub = await _db.Subscriptions.IgnoreQueryFilters()
                      .FirstOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false)
                  ?? throw new NotFoundAppException(EntityName, id);

        if (!sub.IsDeleted) return;

        sub.IsDeleted = false;
        sub.DeletedAt = null;
        sub.DeletedBy = null;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Restore, EntityName, id, null,
            new { sub.SubscriptionCode, sub.MemberId, sub.Status },
            $"Subscription {sub.SubscriptionCode} restored.", ct).ConfigureAwait(false);
    }

    // ======================================================================
    //  Maintenance job
    // ======================================================================

    public async Task<ExpiryProcessingResultDto> ProcessExpiriesAsync(CancellationToken ct = default)
    {
        var today = _clock.Today.Date;
        var gym = await _settings.GetGymSettingsAsync(ct).ConfigureAwait(false);
        var stamp = today.ToString("yyyyMMdd");

        var result = new ExpiryProcessingResultDto { ProcessedAtUtc = _clock.UtcNow };

        result.ResumedFromFreeze = await AutoResumeExpiredFreezesAsync(today, ct).ConfigureAwait(false);
        if (result.ResumedFromFreeze > 0)
            result.Messages.Add($"Auto-resumed {result.ResumedFromFreeze} subscription(s) whose freeze window ended.");

        var expiredIds = await ExpireFinishedSubscriptionsAsync(today, ct).ConfigureAwait(false);
        result.ExpiredCount = expiredIds.Count;
        if (expiredIds.Count > 0)
            result.Messages.Add($"Expired {expiredIds.Count} subscription(s) past their end date and grace period.");

        var (demoted, reactivated) = await SyncMemberStatusesAsync(today, ct).ConfigureAwait(false);
        if (demoted > 0) result.Messages.Add($"Marked {demoted} member(s) as Expired.");
        if (reactivated > 0) result.Messages.Add($"Reactivated {reactivated} member(s) with a usable subscription.");

        var expiringNotices = await NotifyExpiringSoonAsync(today, gym.ExpiryReminderDays, stamp, ct)
            .ConfigureAwait(false);
        var expiredNotices = await NotifyExpiredAsync(expiredIds, stamp, ct).ConfigureAwait(false);

        result.NotificationsCreated = expiringNotices + expiredNotices;
        if (expiringNotices > 0)
            result.Messages.Add($"Raised {expiringNotices} expiring-soon reminder(s) within {gym.ExpiryReminderDays} day(s).");
        if (expiredNotices > 0)
            result.Messages.Add($"Raised {expiredNotices} membership-expired alert(s).");

        if (result.Messages.Count == 0)
            result.Messages.Add("Nothing to process: no freezes ended, no subscriptions expired and no reminders were due.");

        _logger.LogInformation(
            "Expiry job for {Today:yyyy-MM-dd}: expired {Expired}, resumed {Resumed}, notifications {Notifications}.",
            today, result.ExpiredCount, result.ResumedFromFreeze, result.NotificationsCreated);

        return result;
    }

    private async Task<int> AutoResumeExpiredFreezesAsync(DateTime today, CancellationToken ct)
    {
        var processed = 0;

        while (true)
        {
            var batch = await _db.Subscriptions
                .Where(s => s.Status == SubscriptionStatus.Frozen &&
                            s.FreezeEndDate != null && s.FreezeEndDate < today)
                .OrderBy(s => s.Id)
                .Take(BatchSize)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (batch.Count == 0) break;

            await using var tx = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            try
            {
                foreach (var sub in batch)
                {
                    var oldStatus = sub.Status;
                    var oldEndDate = sub.EndDate;
                    // The whole window elapsed, so the planned extension was fully earned.
                    var (actual, planned) = ResolveFreezeDays(sub, sub.FreezeEndDate!.Value);

                    sub.FrozenDaysUsed += actual;
                    sub.FreezeStartDate = null;
                    sub.FreezeEndDate = null;
                    sub.Status = SubscriptionStatus.Active;

                    AddHistory(sub.Id, SubscriptionActionType.Resumed, oldStatus, sub.Status, oldEndDate, sub.EndDate,
                        null, $"Auto-resumed by the expiry job: freeze window of {planned} day(s) ended.");
                }

                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await SafeRollbackAsync(tx, ex, nameof(AutoResumeExpiredFreezesAsync)).ConfigureAwait(false);
                throw;
            }

            processed += batch.Count;
            if (batch.Count < BatchSize) break;
        }

        return processed;
    }

    private async Task<List<int>> ExpireFinishedSubscriptionsAsync(DateTime today, CancellationToken ct)
    {
        var expired = new List<int>();

        while (true)
        {
            // EndDate + GracePeriodDays < today  <=>  (today - EndDate) > GracePeriodDays
            var batch = await _db.Subscriptions
                .Where(s => s.Status == SubscriptionStatus.Active &&
                            today > s.EndDate.AddDays(s.GracePeriodDays))
                .OrderBy(s => s.Id)
                .Take(BatchSize)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (batch.Count == 0) break;

            await using var tx = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            try
            {
                foreach (var sub in batch)
                {
                    var oldStatus = sub.Status;
                    sub.Status = SubscriptionStatus.Expired;

                    AddHistory(sub.Id, SubscriptionActionType.Expired, oldStatus, sub.Status,
                        sub.EndDate, sub.EndDate, sub.OutstandingAmount > 0m ? sub.OutstandingAmount : null,
                        $"Expired automatically: term ended {sub.EndDate:yyyy-MM-dd} " +
                        $"and the {sub.GracePeriodDays} day grace period has passed.");
                }

                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await SafeRollbackAsync(tx, ex, nameof(ExpireFinishedSubscriptionsAsync)).ConfigureAwait(false);
                throw;
            }

            expired.AddRange(batch.Select(s => s.Id));
            if (batch.Count < BatchSize) break;
        }

        return expired;
    }

    private async Task<(int Demoted, int Reactivated)> SyncMemberStatusesAsync(DateTime today, CancellationToken ct)
    {
        var demoted = 0;
        var reactivated = 0;

        // Members who have bought at least once but hold nothing usable today.
        while (true)
        {
            var batch = await _db.Members
                .Where(m => m.Status == MemberStatus.Active &&
                            _db.Subscriptions.Any(s => s.MemberId == m.Id) &&
                            !_db.Subscriptions.Any(s => s.MemberId == m.Id &&
                                                        s.Status == SubscriptionStatus.Active &&
                                                        s.StartDate <= today &&
                                                        today <= s.EndDate.AddDays(s.GracePeriodDays)))
                .OrderBy(m => m.Id)
                .Take(BatchSize)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (batch.Count == 0) break;

            foreach (var member in batch) member.Status = MemberStatus.Expired;
            await SaveInTransactionAsync(nameof(SyncMemberStatusesAsync), ct).ConfigureAwait(false);

            demoted += batch.Count;
            if (batch.Count < BatchSize) break;
        }

        // Members flagged Expired who have since bought a usable subscription.
        while (true)
        {
            var batch = await _db.Members
                .Where(m => m.Status == MemberStatus.Expired &&
                            _db.Subscriptions.Any(s => s.MemberId == m.Id &&
                                                       s.Status == SubscriptionStatus.Active &&
                                                       s.StartDate <= today &&
                                                       today <= s.EndDate.AddDays(s.GracePeriodDays)))
                .OrderBy(m => m.Id)
                .Take(BatchSize)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (batch.Count == 0) break;

            foreach (var member in batch) member.Status = MemberStatus.Active;
            await SaveInTransactionAsync(nameof(SyncMemberStatusesAsync), ct).ConfigureAwait(false);

            reactivated += batch.Count;
            if (batch.Count < BatchSize) break;
        }

        return (demoted, reactivated);
    }

    private async Task<int> NotifyExpiringSoonAsync(DateTime today, int reminderDays, string stamp, CancellationToken ct)
    {
        var horizon = today.AddDays(Math.Max(0, reminderDays));
        var raised = 0;
        var page = 0;

        while (true)
        {
            var batch = await _db.Subscriptions.AsNoTracking()
                .Where(s => s.Status == SubscriptionStatus.Active &&
                            s.EndDate >= today && s.EndDate <= horizon)
                .OrderBy(s => s.Id)
                .Skip(page * BatchSize)
                .Take(BatchSize)
                .Select(s => new
                {
                    s.Id, s.SubscriptionCode, s.MemberId, s.EndDate,
                    MemberName = s.Member!.FullName,
                    PlanName = s.MembershipPlan!.Name
                })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (batch.Count == 0) break;

            foreach (var row in batch)
            {
                var days = (row.EndDate.Date - today).Days;
                await SafeNotifyAsync(
                    NotificationType.MembershipExpiringSoon,
                    days <= 2 ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                    "Membership expiring soon",
                    $"{row.MemberName}'s '{row.PlanName}' membership ({row.SubscriptionCode}) " +
                    (days == 0 ? "expires today." : $"expires in {days} day(s) on {row.EndDate:yyyy-MM-dd}."),
                    row.MemberId, row.Id, $"expiring:{row.Id}:{stamp}", ct).ConfigureAwait(false);
                raised++;
            }

            page++;
            if (batch.Count < BatchSize) break;
        }

        return raised;
    }

    private async Task<int> NotifyExpiredAsync(List<int> expiredIds, string stamp, CancellationToken ct)
    {
        if (expiredIds.Count == 0) return 0;

        var raised = 0;
        foreach (var chunk in expiredIds.Chunk(BatchSize))
        {
            var ids = chunk.ToList();
            var rows = await _db.Subscriptions.AsNoTracking()
                .Where(s => ids.Contains(s.Id))
                .Select(s => new
                {
                    s.Id, s.SubscriptionCode, s.MemberId, s.EndDate,
                    MemberName = s.Member!.FullName,
                    PlanName = s.MembershipPlan!.Name
                })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            foreach (var row in rows)
            {
                await SafeNotifyAsync(
                    NotificationType.MembershipExpired,
                    NotificationSeverity.Critical,
                    "Membership expired",
                    $"{row.MemberName}'s '{row.PlanName}' membership ({row.SubscriptionCode}) " +
                    $"expired on {row.EndDate:yyyy-MM-dd}. Renew to restore access.",
                    row.MemberId, row.Id, $"expired:{row.Id}:{stamp}", ct).ConfigureAwait(false);
                raised++;
            }
        }

        return raised;
    }

    // ======================================================================
    //  Shared internals
    // ======================================================================

    private async Task<Member> LoadSellableMemberAsync(int memberId, CancellationToken ct)
    {
        // The global query filter already hides soft-deleted members.
        var member = await _db.Members.FirstOrDefaultAsync(m => m.Id == memberId, ct).ConfigureAwait(false)
                     ?? throw new NotFoundAppException(nameof(Member), memberId);

        if (member.Status == MemberStatus.Suspended)
            throw new BusinessRuleAppException(
                $"Member {member.MemberCode} is suspended and cannot be sold a subscription. " +
                "Lift the suspension first.");

        return member;
    }

    private async Task<MembershipPlan> LoadActivePlanAsync(int planId, CancellationToken ct)
    {
        var plan = await _db.MembershipPlans.FirstOrDefaultAsync(p => p.Id == planId, ct).ConfigureAwait(false)
                   ?? throw new NotFoundAppException(nameof(MembershipPlan), planId);

        if (plan.Status != PlanStatus.Active)
            throw new BusinessRuleAppException($"Plan '{plan.Name}' is not active and cannot be sold.");

        return plan;
    }

    private async Task EnsureTrainerExistsAsync(int? trainerId, CancellationToken ct)
    {
        if (trainerId is not > 0) return;

        var exists = await _db.Trainers.AsNoTracking()
            .AnyAsync(t => t.Id == trainerId.Value, ct)
            .ConfigureAwait(false);

        if (!exists)
            throw new ValidationAppException("AssignedTrainerId", $"Trainer {trainerId} was not found.");
    }

    /// <param name="allowUpTo">
    /// A date beyond the usual ceiling that is legitimate for this operation — a renewal
    /// continuing an existing term. Without it, renewing an annual membership early was
    /// impossible: the service computed "the day after the current term" and then rejected it.
    /// </param>
    private static void ValidateStartDate(DateTime startDate, DateTime today, DateTime? allowUpTo = null)
    {
        if (startDate < today.AddDays(-MaxBackdateDays))
            throw new ValidationAppException("StartDate",
                $"The start date cannot be more than {MaxBackdateDays} days in the past.");

        var ceiling = today.AddDays(MaxFutureDays);
        if (allowUpTo.HasValue && allowUpTo.Value.Date > ceiling) ceiling = allowUpTo.Value.Date;

        if (startDate > ceiling)
            throw new ValidationAppException("StartDate",
                allowUpTo.HasValue
                    ? $"The start date cannot be later than {ceiling:d MMM yyyy}, the day the current term ends."
                    : $"The start date cannot be more than {MaxFutureDays} days in the future.");
    }

    private async Task EnsureNoOverlapAsync(
        int memberId, DateTime startDate, DateTime endDate, int? excludeSubscriptionId, CancellationToken ct)
    {
        var clash = await _db.Subscriptions.AsNoTracking()
            .Where(s => s.MemberId == memberId &&
                        s.Status == SubscriptionStatus.Active &&
                        (excludeSubscriptionId == null || s.Id != excludeSubscriptionId.Value) &&
                        s.StartDate <= endDate && s.EndDate >= startDate)
            .Select(s => new { s.SubscriptionCode, s.StartDate, s.EndDate })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (clash is not null)
            throw new BusinessRuleAppException(
                $"The member already has an active subscription ({clash.SubscriptionCode}, " +
                $"{clash.StartDate:yyyy-MM-dd} to {clash.EndDate:yyyy-MM-dd}) that overlaps " +
                $"{startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}. Renew that subscription instead of creating a new one.");
    }

    private async Task<Subscription> ResolvePreviousSubscriptionAsync(RenewSubscriptionDto dto, CancellationToken ct)
    {
        Subscription? previous;

        if (dto.PreviousSubscriptionId is > 0)
        {
            previous = await _db.Subscriptions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == dto.PreviousSubscriptionId!.Value, ct)
                .ConfigureAwait(false);

            if (previous is null)
                throw new NotFoundAppException(EntityName, dto.PreviousSubscriptionId!.Value);

            if (dto.MemberId > 0 && previous.MemberId != dto.MemberId)
                throw new ValidationAppException(nameof(dto.PreviousSubscriptionId),
                    "The subscription being renewed belongs to a different member.");
        }
        else
        {
            if (dto.MemberId <= 0)
                throw new ValidationAppException(nameof(dto.MemberId), "A member is required to renew.");

            previous = await _db.Subscriptions.AsNoTracking()
                .Where(s => s.MemberId == dto.MemberId)
                .OrderByDescending(s => s.EndDate).ThenByDescending(s => s.Id)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (previous is null)
                throw new NotFoundAppException(
                    $"Member {dto.MemberId} has no subscription to renew. Create a new subscription instead.");
        }

        return previous;
    }

    private Subscription NewSubscription(
        int memberId, int planId, PricingResult p, int? trainerId, string? notes) => new()
    {
        MemberId = memberId,
        MembershipPlanId = planId,
        StartDate = p.StartDate,
        EndDate = p.EndDate,
        PlanAmount = p.PlanAmount,
        RegistrationFee = p.RegistrationFee,
        // The prorated credit is folded into DiscountAmount so the stored total always satisfies
        // FinalAmount = PlanAmount + RegistrationFee - DiscountAmount + TaxAmount.
        DiscountAmount = Round2(p.DiscountAmount + p.ProratedCredit),
        TaxPercent = p.TaxPercent,
        TaxAmount = p.TaxAmount,
        FinalAmount = p.FinalAmount,
        PaidAmount = 0m,
        PaymentStatus = PaymentStatus.Pending,
        Status = SubscriptionStatus.Pending,
        GracePeriodDays = p.GracePeriodDays,
        AssignedTrainerId = trainerId,
        Notes = Truncate(notes, 1000)
    };

    /// <summary>
    /// Records the optional inline payment. Amount, receipt number, collector and confirmation state
    /// are all decided here; the caller only supplies the tender details.
    /// </summary>
    private async Task<Payment?> AddInlinePaymentAsync(
        Subscription subscription, int memberId, CollectPaymentInlineDto? dto, CancellationToken ct)
    {
        if (dto is null) return null;

        if (dto.Amount <= 0m)
            throw new ValidationAppException("Payment.Amount", "The payment amount must be greater than zero.");

        var amount = Round2(dto.Amount);
        var outstanding = Round2(subscription.FinalAmount - subscription.PaidAmount);
        if (amount > outstanding)
            throw new ValidationAppException("Payment.Amount",
                $"The payment of {amount:0.00} exceeds the outstanding amount of {outstanding:0.00}.");

        var method = await _db.PaymentMethods.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == dto.PaymentMethodId, ct)
            .ConfigureAwait(false);

        if (method is null)
            throw new ValidationAppException("Payment.PaymentMethodId",
                $"Payment method {dto.PaymentMethodId} was not found.");
        if (!method.IsActive)
            throw new ValidationAppException("Payment.PaymentMethodId",
                $"Payment method '{method.Name}' is not active.");

        var reference = string.IsNullOrWhiteSpace(dto.TransactionReference)
            ? null
            : dto.TransactionReference.Trim();

        if (method.RequiresReference && reference is null)
            throw new ValidationAppException("Payment.TransactionReference",
                $"Payment method '{method.Name}' requires a transaction reference.");

        if (reference is not null)
        {
            var duplicate = await _db.Payments.AsNoTracking()
                .AnyAsync(p => p.TransactionReference == reference, ct)
                .ConfigureAwait(false);

            if (duplicate)
                throw new ConflictAppException(
                    $"A payment with transaction reference '{reference}' has already been recorded.");
        }

        var payment = new Payment
        {
            ReceiptNumber = await _codes.NextReceiptNumberAsync(ct).ConfigureAwait(false),
            MemberId = memberId,
            SubscriptionId = subscription.Id,
            // Amount and FinalAmount both hold the tendered value: discount and tax already live on
            // the subscription, so repeating them here would double count the money.
            Amount = amount,
            DiscountAmount = 0m,
            TaxAmount = 0m,
            FinalAmount = amount,
            RefundedAmount = 0m,
            PaymentMethodId = method.Id,
            TransactionReference = Truncate(reference, 128),
            PayerVpa = Truncate(string.IsNullOrWhiteSpace(dto.PayerVpa) ? null : dto.PayerVpa.Trim(), 128),
            PaymentDate = (dto.PaymentDate ?? _clock.Now),
            Status = dto.MarkConfirmed ? PaymentStatus.Paid : PaymentStatus.AwaitingConfirmation,
            CollectedByUserId = _currentUser.UserId,
            ConfirmedAtUtc = dto.MarkConfirmed ? _clock.UtcNow : null,
            ConfirmedByUserId = dto.MarkConfirmed ? _currentUser.UserId : null,
            Notes = Truncate(dto.Notes, 1000)
        };

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return payment;
    }

    /// <summary>
    /// Recomputes <c>PaidAmount</c> from CONFIRMED payments only, then derives the payment status and
    /// activates the subscription when money has actually landed (or when there is nothing to pay).
    /// </summary>
    private async Task ApplyPaymentTotalsAsync(Subscription subscription, CancellationToken ct)
    {
        var paid = await _db.Payments
            .Where(p => p.SubscriptionId == subscription.Id && p.Status == PaymentStatus.Paid)
            .SumAsync(p => (decimal?)p.FinalAmount, ct)
            .ConfigureAwait(false) ?? 0m;

        subscription.PaidAmount = Round2(paid);

        subscription.PaymentStatus = subscription.PaidAmount >= subscription.FinalAmount
            ? PaymentStatus.Paid
            : subscription.PaidAmount > 0m
                ? PaymentStatus.PartiallyPaid
                : PaymentStatus.Pending;

        // Active as soon as any confirmed money is in, or when the term costs nothing; a subscription
        // with an untouched balance stays Pending until the first payment is confirmed.
        subscription.Status = subscription.PaidAmount > 0m || subscription.FinalAmount == 0m
            ? SubscriptionStatus.Active
            : SubscriptionStatus.Pending;
    }

    private static void PromoteMemberIfNeeded(Member member, Subscription subscription)
    {
        if (subscription.Status == SubscriptionStatus.Active && member.Status == MemberStatus.Expired)
            member.Status = MemberStatus.Active;
    }

    private void AddHistory(
        int subscriptionId,
        SubscriptionActionType action,
        SubscriptionStatus? oldStatus,
        SubscriptionStatus? newStatus,
        DateTime? oldEndDate,
        DateTime? newEndDate,
        decimal? amount,
        string? remarks) =>
        _db.SubscriptionHistory.Add(new SubscriptionHistory
        {
            SubscriptionId = subscriptionId,
            ActionType = action,
            ActionDate = _clock.UtcNow,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            OldEndDate = oldEndDate?.Date,
            NewEndDate = newEndDate?.Date,
            Amount = amount,
            Remarks = Truncate(remarks, 1000),
            PerformedBy = _currentUser.UserId
        });

    private async Task NotifyPaymentOutcomeAsync(Subscription subscription, Member member, CancellationToken ct)
    {
        if (subscription.PaidAmount > 0m)
            await SafeNotifyAsync(NotificationType.PaymentSuccessful, NotificationSeverity.Success,
                "Payment received",
                $"{member.FullName} paid {subscription.PaidAmount:0.00} towards subscription {subscription.SubscriptionCode}.",
                member.Id, subscription.Id,
                $"paid:{subscription.Id}:{subscription.PaidAmount:0.00}", ct).ConfigureAwait(false);

        if (subscription.OutstandingAmount > 0m)
            await SafeNotifyAsync(NotificationType.PaymentPending, NotificationSeverity.Warning,
                "Payment pending",
                $"{member.FullName} has an outstanding balance of {subscription.OutstandingAmount:0.00} on " +
                $"subscription {subscription.SubscriptionCode}.",
                member.Id, subscription.Id,
                $"outstanding:{subscription.Id}:{_clock.Today:yyyyMMdd}", ct).ConfigureAwait(false);
    }

    /// <summary>Notifications are advisory: a failure here must never undo a booked subscription.</summary>
    private async Task SafeNotifyAsync(
        NotificationType type, NotificationSeverity severity, string title, string message,
        int? memberId, int? subscriptionId, string deduplicationKey, CancellationToken ct)
    {
        try
        {
            await _notifications.NotifyAsync(type, severity, title, message,
                memberId: memberId, userId: null, entityName: EntityName, entityId: subscriptionId,
                deduplicationKey: deduplicationKey, ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Notification {Type} for subscription {SubscriptionId} could not be raised.",
                type, subscriptionId);
        }
    }

    private async Task SaveInTransactionAsync(string operation, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await SafeRollbackAsync(tx, ex, operation).ConfigureAwait(false);
            throw;
        }
    }

    private async Task SafeRollbackAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, Exception cause, string operation)
    {
        try
        {
            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception rollbackFailure)
        {
            _logger.LogError(rollbackFailure, "Rolling back {Operation} failed after {Cause}.",
                operation, cause.GetType().Name);
            return;
        }

        if (cause is AppException)
            _logger.LogInformation("{Operation} rolled back: {Message}", operation, cause.Message);
        else
            _logger.LogError(cause, "{Operation} rolled back after an unexpected failure.", operation);
    }

    private static IQueryable<Subscription> ApplySort(IQueryable<Subscription> q, string? sortBy, bool desc) =>
        (sortBy?.Trim().ToLowerInvariant()) switch
        {
            "startdate" => desc
                ? q.OrderByDescending(s => s.StartDate).ThenByDescending(s => s.Id)
                : q.OrderBy(s => s.StartDate).ThenBy(s => s.Id),
            "finalamount" => desc
                ? q.OrderByDescending(s => s.FinalAmount).ThenByDescending(s => s.Id)
                : q.OrderBy(s => s.FinalAmount).ThenBy(s => s.Id),
            "status" => desc
                ? q.OrderByDescending(s => s.Status).ThenByDescending(s => s.EndDate)
                : q.OrderBy(s => s.Status).ThenByDescending(s => s.EndDate),
            "membername" => desc
                ? q.OrderByDescending(s => s.Member!.FullName).ThenByDescending(s => s.EndDate)
                : q.OrderBy(s => s.Member!.FullName).ThenByDescending(s => s.EndDate),
            "enddate" => desc
                ? q.OrderByDescending(s => s.EndDate).ThenByDescending(s => s.Id)
                : q.OrderBy(s => s.EndDate).ThenBy(s => s.Id),
            _ => q.OrderByDescending(s => s.EndDate).ThenByDescending(s => s.Id)
        };

    private IQueryable<SubscriptionDto> Project(IQueryable<Subscription> source) =>
        source.Select(s => new SubscriptionDto
        {
            Id = s.Id,
            SubscriptionCode = s.SubscriptionCode,
            MemberId = s.MemberId,
            MemberCode = s.Member!.MemberCode,
            MemberName = s.Member!.FullName,
            MemberPhone = s.Member!.Phone,
            MembershipPlanId = s.MembershipPlanId,
            PlanName = s.MembershipPlan!.Name,
            StartDate = s.StartDate,
            EndDate = s.EndDate,
            PlanAmount = s.PlanAmount,
            RegistrationFee = s.RegistrationFee,
            DiscountAmount = s.DiscountAmount,
            TaxPercent = s.TaxPercent,
            TaxAmount = s.TaxAmount,
            FinalAmount = s.FinalAmount,
            PaidAmount = s.PaidAmount,
            PaymentStatus = s.PaymentStatus,
            Status = s.Status,
            GracePeriodDays = s.GracePeriodDays,
            IsRenewal = s.IsRenewal,
            PreviousSubscriptionId = s.PreviousSubscriptionId,
            FreezeStartDate = s.FreezeStartDate,
            FreezeEndDate = s.FreezeEndDate,
            FrozenDaysUsed = s.FrozenDaysUsed,
            CancelledAt = s.CancelledAt,
            CancellationReason = s.CancellationReason,
            AssignedTrainerId = s.AssignedTrainerId,
            AssignedTrainerName = s.AssignedTrainer != null ? s.AssignedTrainer.FullName : null,
            Notes = s.Notes,
            CreatedAt = s.CreatedAt,
            CreatedByName = _db.Users.Where(u => u.Id == s.CreatedBy).Select(u => u.FullName).FirstOrDefault()
        });

    /// <summary>Fills the members the database cannot compute for us.</summary>
    private static void Decorate(SubscriptionDto dto, DateTime today, int reminderDays)
    {
        dto.OutstandingAmount = Math.Max(0m, Round2(dto.FinalAmount - dto.PaidAmount));
        dto.DaysRemaining = (dto.EndDate.Date - today).Days;
        dto.IsExpiringSoon = dto.Status == SubscriptionStatus.Active &&
                             dto.DaysRemaining >= 0 &&
                             dto.DaysRemaining <= Math.Max(0, reminderDays);
    }

    private async Task<SubscriptionDto> LoadDtoAsync(int id, int reminderDays, DateTime today, CancellationToken ct)
    {
        var dto = await Project(_db.Subscriptions.AsNoTracking().IgnoreQueryFilters().Where(s => s.Id == id))
                      .FirstOrDefaultAsync(ct).ConfigureAwait(false)
                  ?? throw new NotFoundAppException(EntityName, id);

        Decorate(dto, today, reminderDays);
        return dto;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
