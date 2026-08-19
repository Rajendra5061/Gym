using System.Globalization;
using System.Linq.Expressions;
using System.Security.Cryptography;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Common;
using GymManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Services;

/// <summary>
/// Collection, confirmation and refunding of payments, plus receipts and UPI collection intents.
///
/// PCI / privacy note: this service never accepts, logs or persists a card number, CVV, card
/// expiry or UPI PIN. Only an operator-supplied transaction reference and (optionally) the payer's
/// VPA are stored, both of which are safe to keep for reconciliation.
/// </summary>
public sealed class PaymentService : IPaymentService
{
    /// <summary>
    /// Statuses that count towards a subscription's PaidAmount. A partially refunded payment is
    /// still settled, so it contributes its net value (FinalAmount - RefundedAmount); a fully
    /// refunded payment contributes zero by the same formula.
    /// </summary>
    private static readonly PaymentStatus[] SettledStatuses =
    {
        PaymentStatus.Paid, PaymentStatus.PartiallyRefunded, PaymentStatus.Refunded
    };

    private const string UpiInstructions =
        "This is a static UPI collection request. There is NO automatic verification: the gym " +
        "receives no callback when the transfer completes. Ask the member to pay using the QR code " +
        "or UPI id shown, then read the UPI transaction reference (UTR) from their payment app and " +
        "record it here. Only mark the payment as successful once the credit is visible in the gym " +
        "bank/UPI account. For automatic reconciliation, integrate a payment gateway that performs " +
        "server-side webhook verification.";

    /// <summary>
    /// Shown instead of <see cref="UpiInstructions"/> once a gateway is configured. The old text
    /// tells the operator that nothing will be verified automatically, which stops being true the
    /// moment a signed webhook can settle the payment — and an operator who confirms by hand
    /// anyway is doing work the gateway is about to do for them.
    /// </summary>
    private const string UpiGatewayInstructions =
        "A payment gateway is configured for this gym. Ask the member to pay using the QR code or " +
        "UPI id shown. Record the payment against the reference below and leave it awaiting " +
        "confirmation: when the gateway reports the transfer, its signed notification settles the " +
        "payment automatically and the receipt is emailed. Confirm by hand only if the gateway has " +
        "not reported the transfer and the credit is already visible in the gym bank/UPI account.";

    private readonly GymDbContext _db;
    private readonly ICodeGeneratorService _codes;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly IPdfExportService _pdf;
    private readonly IPaymentReceiptMailer _receiptMailer;
    private readonly ILogger<PaymentService> _logger;

    /// <summary>
    /// Whether automatic reconciliation is configured. Optional on purpose: the parameter is last
    /// and defaulted so that every existing caller — the container included — keeps working, and a
    /// null simply means "no gateway", which is the safe answer.
    /// </summary>
    private readonly IPaymentGatewayConfiguration? _gateway;

    public PaymentService(
        GymDbContext db,
        ICodeGeneratorService codes,
        IDateTimeProvider clock,
        ICurrentUserService currentUser,
        IAuditService audit,
        ISettingsService settings,
        INotificationService notifications,
        IPdfExportService pdf,
        IPaymentReceiptMailer receiptMailer,
        ILogger<PaymentService> logger,
        IPaymentGatewayConfiguration? gateway = null)
    {
        _gateway = gateway;
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _codes = codes ?? throw new ArgumentNullException(nameof(codes));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _pdf = pdf ?? throw new ArgumentNullException(nameof(pdf));
        _receiptMailer = receiptMailer ?? throw new ArgumentNullException(nameof(receiptMailer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ---------------------------------------------------------------------------------------
    // Reads
    // ---------------------------------------------------------------------------------------

    private static readonly Expression<Func<Payment, PaymentDto>> ToPaymentDto = p => new PaymentDto
    {
        Id = p.Id,
        ReceiptNumber = p.ReceiptNumber,
        MemberId = p.MemberId,
        MemberCode = p.Member != null ? p.Member.MemberCode : string.Empty,
        MemberName = p.Member != null ? p.Member.FullName : string.Empty,
        MemberPhone = p.Member != null ? p.Member.Phone : null,
        SubscriptionId = p.SubscriptionId,
        SubscriptionCode = p.Subscription != null ? p.Subscription.SubscriptionCode : null,

        // The subscription's plan stays authoritative. Only when a payment carries no subscription
        // does the plan recorded directly on it supply the name, so money collected from the
        // Record Payment screen still shows what it was for.
        PlanName = p.Subscription != null && p.Subscription.MembershipPlan != null
            ? p.Subscription.MembershipPlan.Name
            : p.MembershipPlan != null
                ? p.MembershipPlan.Name
                : null,
        MembershipPlanId = p.MembershipPlanId,

        Amount = p.Amount,
        DiscountAmount = p.DiscountAmount,
        TaxAmount = p.TaxAmount,
        FinalAmount = p.FinalAmount,
        RefundedAmount = p.RefundedAmount,
        PaymentMethodId = p.PaymentMethodId,
        PaymentMethodName = p.PaymentMethod != null ? p.PaymentMethod.Name : string.Empty,
        TransactionReference = p.TransactionReference,
        PayerVpa = p.PayerVpa,
        PaymentDate = p.PaymentDate,
        Status = p.Status,
        CollectedByUserId = p.CollectedByUserId,
        CollectedByName = p.CollectedByUser != null ? p.CollectedByUser.FullName : null,
        ConfirmedAtUtc = p.ConfirmedAtUtc,
        Notes = p.Notes,
        CreatedAt = p.CreatedAt,
        ReceiptEmailedAtUtc = p.ReceiptEmailedAtUtc
    };

    public async Task<PagedResult<PaymentDto>> GetPagedAsync(PaymentQueryDto query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var source = query.IncludeDeleted
            ? _db.Payments.IgnoreQueryFilters()
            : _db.Payments.AsQueryable();

        var q = source.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(p =>
                EF.Functions.Like(p.ReceiptNumber, $"%{term}%") ||
                (p.TransactionReference != null && EF.Functions.Like(p.TransactionReference, $"%{term}%")) ||
                (p.Member != null && EF.Functions.Like(p.Member.MemberCode, $"%{term}%")) ||
                (p.Member != null && EF.Functions.Like(p.Member.FullName, $"%{term}%")));
        }

        if (query.MemberId is > 0) q = q.Where(p => p.MemberId == query.MemberId);
        if (query.SubscriptionId is > 0) q = q.Where(p => p.SubscriptionId == query.SubscriptionId);
        if (query.PaymentMethodId is > 0) q = q.Where(p => p.PaymentMethodId == query.PaymentMethodId);
        if (query.Status is not null) q = q.Where(p => p.Status == query.Status);
        if (query.CollectedByUserId is > 0) q = q.Where(p => p.CollectedByUserId == query.CollectedByUserId);

        if (query.OnlyToday)
        {
            var today = _clock.Today;
            var tomorrow = today.AddDays(1);
            q = q.Where(p => p.PaymentDate >= today && p.PaymentDate < tomorrow);
        }
        else
        {
            if (query.FromDate is not null)
            {
                var from = query.FromDate.Value.Date;
                q = q.Where(p => p.PaymentDate >= from);
            }

            if (query.ToDate is not null)
            {
                var toExclusive = query.ToDate.Value.Date.AddDays(1);
                q = q.Where(p => p.PaymentDate < toExclusive);
            }
        }

        if (query.MinAmount is not null) q = q.Where(p => p.FinalAmount >= query.MinAmount);
        if (query.MaxAmount is not null) q = q.Where(p => p.FinalAmount <= query.MaxAmount);

        var total = await q.CountAsync(ct).ConfigureAwait(false);

        q = (query.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "finalamount" => query.SortDescending
                ? q.OrderByDescending(p => p.FinalAmount).ThenByDescending(p => p.Id)
                : q.OrderBy(p => p.FinalAmount).ThenBy(p => p.Id),
            "receiptnumber" => query.SortDescending
                ? q.OrderByDescending(p => p.ReceiptNumber)
                : q.OrderBy(p => p.ReceiptNumber),
            "membername" => query.SortDescending
                ? q.OrderByDescending(p => p.Member!.FullName).ThenByDescending(p => p.Id)
                : q.OrderBy(p => p.Member!.FullName).ThenBy(p => p.Id),
            "paymentdate" => query.SortDescending
                ? q.OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.Id)
                : q.OrderBy(p => p.PaymentDate).ThenBy(p => p.Id),
            _ => q.OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.Id)
        };

        var items = await q
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(ToPaymentDto)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedResult<PaymentDto>(items, total, query.PageNumber, query.PageSize);
    }

    public async Task<PaymentDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var dto = await _db.Payments
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(ToPaymentDto)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return dto ?? throw new NotFoundAppException("Payment", id);
    }

    public async Task<int?> GetOwnerMemberIdAsync(int paymentId, CancellationToken ct = default) =>
        await _db.Payments
            .AsNoTracking()
            .Where(p => p.Id == paymentId)
            .Select(p => (int?)p.MemberId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public async Task<List<PaymentDto>> GetForMemberAsync(int memberId, CancellationToken ct = default) =>
        await _db.Payments
            .AsNoTracking()
            .Where(p => p.MemberId == memberId)
            .OrderByDescending(p => p.PaymentDate)
            .ThenByDescending(p => p.Id)
            .Select(ToPaymentDto)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    // ---------------------------------------------------------------------------------------
    // Create / confirm
    // ---------------------------------------------------------------------------------------

    public async Task<PaymentDto> CreateAsync(CreatePaymentDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var member = await _db.Members
                         .AsNoTracking()
                         .FirstOrDefaultAsync(m => m.Id == dto.MemberId, ct)
                         .ConfigureAwait(false)
                     ?? throw new NotFoundAppException("Member", dto.MemberId);

        // Amounts are recomputed here; a client supplied total is never trusted.
        if (dto.Amount <= 0m)
            throw new ValidationAppException(nameof(dto.Amount), "Amount must be greater than zero.");
        if (dto.DiscountAmount < 0m)
            throw new ValidationAppException(nameof(dto.DiscountAmount), "Discount cannot be negative.");
        if (dto.TaxAmount < 0m)
            throw new ValidationAppException(nameof(dto.TaxAmount), "Tax cannot be negative.");

        var amount = Round(dto.Amount);
        var discount = Round(dto.DiscountAmount);
        var tax = Round(dto.TaxAmount);
        var finalAmount = Round(amount - discount + tax);

        if (finalAmount <= 0m)
            throw new ValidationAppException("FinalAmount",
                "The payable amount after discount and tax must be greater than zero.");

        Subscription? subscription = null;
        if (dto.SubscriptionId is > 0)
        {
            subscription = await _db.Subscriptions
                               .FirstOrDefaultAsync(s => s.Id == dto.SubscriptionId, ct)
                               .ConfigureAwait(false)
                           ?? throw new NotFoundAppException("Subscription", dto.SubscriptionId.Value);

            if (subscription.MemberId != dto.MemberId)
                throw new ValidationAppException(nameof(dto.SubscriptionId),
                    "The subscription does not belong to the selected member.");

            if (subscription.Status == SubscriptionStatus.Cancelled)
                throw new BusinessRuleAppException(
                    $"Subscription {subscription.SubscriptionCode} is cancelled and cannot accept payments.");

            var outstanding = Round(Math.Max(0m, subscription.FinalAmount - subscription.PaidAmount));
            if (finalAmount > outstanding)
                throw new BusinessRuleAppException(
                    $"The payable amount {finalAmount:0.00} exceeds the outstanding balance " +
                    $"{outstanding:0.00} on subscription {subscription.SubscriptionCode}.");
        }

        // The plan a payment was collected for, recorded on the payment itself. This creates no
        // subscription and attaches to none: it only records what the money was for, so the
        // payments list and the receipt can name a plan when no subscription exists.
        int? membershipPlanId = null;
        if (dto.MembershipPlanId is > 0)
        {
            var planExists = await _db.MembershipPlans
                .AsNoTracking()
                .AnyAsync(p => p.Id == dto.MembershipPlanId, ct)
                .ConfigureAwait(false);

            if (!planExists)
                throw new NotFoundAppException("MembershipPlan", dto.MembershipPlanId.Value);

            membershipPlanId = dto.MembershipPlanId;
        }

        var method = await _db.PaymentMethods
                         .AsNoTracking()
                         .FirstOrDefaultAsync(m => m.Id == dto.PaymentMethodId, ct)
                         .ConfigureAwait(false)
                     ?? throw new NotFoundAppException("PaymentMethod", dto.PaymentMethodId);

        var reference = Normalize(dto.TransactionReference);
        if (method.RequiresReference && reference is null)
            throw new ValidationAppException(nameof(dto.TransactionReference),
                $"{method.Name} payments require a transaction reference.");

        if (reference is not null)
            await EnsureReferenceIsFreeAsync(reference, null, ct).ConfigureAwait(false);

        var payment = new Payment
        {
            ReceiptNumber = await _codes.NextReceiptNumberAsync(ct).ConfigureAwait(false),
            MemberId = member.Id,
            SubscriptionId = subscription?.Id,
            MembershipPlanId = membershipPlanId,
            Amount = amount,
            DiscountAmount = discount,
            TaxAmount = tax,
            FinalAmount = finalAmount,
            RefundedAmount = 0m,
            PaymentMethodId = method.Id,
            // Only a reference / VPA is stored - never a card number, CVV or UPI PIN.
            TransactionReference = reference,
            PayerVpa = Normalize(dto.PayerVpa),
            PaymentDate = dto.PaymentDate ?? _clock.Now,
            CollectedByUserId = _currentUser.UserId,
            Notes = Normalize(dto.Notes),
            Status = dto.MarkConfirmed ? PaymentStatus.Paid : PaymentStatus.AwaitingConfirmation
        };

        if (dto.MarkConfirmed)
        {
            payment.ConfirmedAtUtc = _clock.UtcNow;
            payment.ConfirmedByUserId = _currentUser.UserId;
        }

        PaymentDto result;

        var tx = await BeginTransactionIfNoneAsync(ct).ConfigureAwait(false);
        try
        {
            _db.Payments.Add(payment);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            decimal outstandingAfter = 0m;
            if (subscription is not null)
            {
                outstandingAfter = await RecalculateSubscriptionAsync(
                    subscription,
                    SubscriptionActionType.PaymentReceived,
                    payment.FinalAmount,
                    $"Payment {payment.ReceiptNumber} recorded ({method.Name}).",
                    ct).ConfigureAwait(false);
            }

            await _audit.LogAsync(AuditActions.PaymentCreated, nameof(Payment), payment.Id,
                null,
                new
                {
                    payment.ReceiptNumber,
                    payment.MemberId,
                    payment.SubscriptionId,
                    payment.FinalAmount,
                    payment.PaymentMethodId,
                    payment.Status,
                    payment.TransactionReference
                },
                $"Payment {payment.ReceiptNumber} of {finalAmount:0.00} for member {member.MemberCode}.",
                ct).ConfigureAwait(false);

            if (tx is not null) await tx.CommitAsync(ct).ConfigureAwait(false);

            await NotifyPaymentAsync(payment, member, outstandingAfter, ct).ConfigureAwait(false);

            result = await GetByIdAsync(payment.Id, ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await SafeRollbackAsync(tx, ct).ConfigureAwait(false);
            _logger.LogWarning(ex, "Duplicate transaction reference while creating a payment for member {MemberId}.",
                dto.MemberId);
            throw new ConflictAppException(
                $"A payment with transaction reference '{reference}' already exists.");
        }
        catch
        {
            await SafeRollbackAsync(tx, ct).ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync().ConfigureAwait(false);
        }

        // Outside the transaction scope entirely, not merely after the commit: the mailer stamps
        // Payment.ReceiptEmailedAtUtc with its own SaveChanges, which cannot run while a committed
        // transaction is still attached to the context. A failure in here is swallowed by the
        // mailer, so the recorded payment is returned either way.
        result.ReceiptEmailedAtUtc = await SendReceiptEmailAsync(payment.Id, ct).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Emails the receipt for a committed payment and reports the stamp the mailer recorded, so the
    /// returned DTO tells the caller whether the member was emailed.
    ///
    /// The mailer promises never to throw, and this does not take that promise on trust. By the
    /// time control reaches here the money is committed; letting anything out of this method would
    /// answer a successful collection with a failed request, which is the one outcome that must
    /// never happen.
    /// </summary>
    private async Task<DateTime?> SendReceiptEmailAsync(int paymentId, CancellationToken ct)
    {
        try
        {
            var outcome = await _receiptMailer.SendReceiptAsync(paymentId, ct).ConfigureAwait(false);

            if (outcome != ReceiptEmailOutcome.Sent && outcome != ReceiptEmailOutcome.AlreadySent)
                return null;

            return await _db.Payments
                .AsNoTracking()
                .Where(p => p.Id == paymentId)
                .Select(p => p.ReceiptEmailedAtUtc)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not email the receipt for payment {PaymentId}. The payment is recorded regardless.",
                paymentId);

            return null;
        }
    }

    public async Task<PaymentDto> ConfirmAsync(ConfirmPaymentDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var payment = await _db.Payments
                          .FirstOrDefaultAsync(p => p.Id == dto.PaymentId, ct)
                          .ConfigureAwait(false)
                      ?? throw new NotFoundAppException("Payment", dto.PaymentId);

        if (payment.Status is not (PaymentStatus.AwaitingConfirmation or PaymentStatus.Pending))
            throw new BusinessRuleAppException(
                $"Payment {payment.ReceiptNumber} is {payment.Status} and cannot be confirmed.");

        var method = await _db.PaymentMethods
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == payment.PaymentMethodId, ct)
            .ConfigureAwait(false);

        var newReference = Normalize(dto.TransactionReference);
        if (newReference is not null && !string.Equals(newReference, payment.TransactionReference, StringComparison.Ordinal))
        {
            await EnsureReferenceIsFreeAsync(newReference, payment.Id, ct).ConfigureAwait(false);
            payment.TransactionReference = newReference;
        }

        if (method?.RequiresReference == true && Normalize(payment.TransactionReference) is null)
            throw new ValidationAppException(nameof(dto.TransactionReference),
                $"{method.Name} payments require a transaction reference before confirmation.");

        var previousStatus = payment.Status;
        payment.Status = PaymentStatus.Paid;
        payment.ConfirmedAtUtc = _clock.UtcNow;
        payment.ConfirmedByUserId = _currentUser.UserId;

        var remarks = Normalize(dto.Remarks);
        if (remarks is not null)
            payment.Notes = string.IsNullOrWhiteSpace(payment.Notes) ? remarks : $"{payment.Notes}{Environment.NewLine}{remarks}";

        PaymentDto result;

        var tx = await BeginTransactionIfNoneAsync(ct).ConfigureAwait(false);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            decimal outstandingAfter = 0m;
            Subscription? subscription = null;
            if (payment.SubscriptionId is > 0)
            {
                subscription = await _db.Subscriptions
                    .FirstOrDefaultAsync(s => s.Id == payment.SubscriptionId, ct)
                    .ConfigureAwait(false);

                if (subscription is not null)
                {
                    outstandingAfter = await RecalculateSubscriptionAsync(
                        subscription,
                        SubscriptionActionType.PaymentReceived,
                        payment.FinalAmount,
                        $"Payment {payment.ReceiptNumber} confirmed.",
                        ct).ConfigureAwait(false);
                }
            }

            await _audit.LogAsync(AuditActions.PaymentConfirmed, nameof(Payment), payment.Id,
                new { Status = previousStatus },
                new { payment.Status, payment.TransactionReference, payment.ConfirmedAtUtc },
                $"Payment {payment.ReceiptNumber} confirmed.",
                ct).ConfigureAwait(false);

            if (tx is not null) await tx.CommitAsync(ct).ConfigureAwait(false);

            var member = await _db.Members.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == payment.MemberId, ct)
                .ConfigureAwait(false);

            if (member is not null)
                await NotifyPaymentAsync(payment, member, outstandingAfter, ct).ConfigureAwait(false);

            result = await GetByIdAsync(payment.Id, ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await SafeRollbackAsync(tx, ct).ConfigureAwait(false);
            throw new ConflictAppException(
                $"A payment with transaction reference '{newReference}' already exists.");
        }
        catch
        {
            await SafeRollbackAsync(tx, ct).ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync().ConfigureAwait(false);
        }

        // A payment collected as "awaiting confirmation" gets no receipt until it is confirmed —
        // this is where its one and only receipt goes out.
        result.ReceiptEmailedAtUtc = await SendReceiptEmailAsync(payment.Id, ct).ConfigureAwait(false);

        return result;
    }

    // ---------------------------------------------------------------------------------------
    // Gateway settlement
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Settles a payment on the instruction of a verified gateway webhook.
    ///
    /// This is <see cref="ConfirmAsync"/>'s sibling, not a copy of it: it reuses the same
    /// subscription recalculation, the same notification and the same send-once receipt mail, so a
    /// payment settled by the gateway is indistinguishable from one confirmed by staff except in
    /// who is recorded as having done it. Three differences are deliberate:
    ///
    /// <list type="bullet">
    ///   <item>
    ///     <b>The amount is checked first.</b> A gateway reporting an amount other than the one that
    ///     was requested is refused and reported back, never reconciled. Under- and over-payment are
    ///     both business events for a human, not something to quietly mark as settled.
    ///   </item>
    ///   <item>
    ///     <b>No user is stamped.</b> <c>ConfirmedByUserId</c> stays null and the audit entry names
    ///     <c>gateway:{provider}</c>, because attributing an automatic settlement to whichever
    ///     member of staff happened to be signed in would be a lie in the money trail.
    ///   </item>
    ///   <item>
    ///     <b>It reports rather than throws.</b> The caller is a webhook that must answer a machine
    ///     with a status code, so "not found", "already settled" and "wrong amount" come back as
    ///     values.
    ///   </item>
    /// </list>
    ///
    /// Idempotency is the webhook's job, not this method's — but settling twice is still harmless
    /// here: an already-Paid payment returns <see cref="GatewaySettlementStatus.AlreadySettled"/>
    /// and nothing is written.
    /// </summary>
    public async Task<GatewaySettlementResultDto> SettleFromGatewayAsync(
        GatewaySettlementDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var reference = Normalize(dto.PaymentReference)
                        ?? throw new ValidationAppException(nameof(dto.PaymentReference),
                            "A payment reference is required to settle a payment.");

        var payment = await _db.Payments
            .FirstOrDefaultAsync(p => p.TransactionReference == reference, ct)
            .ConfigureAwait(false);

        if (payment is null)
        {
            return new GatewaySettlementResultDto
            {
                Status = GatewaySettlementStatus.PaymentNotFound,
                Detail = $"No live payment carries reference '{reference}'."
            };
        }

        if (payment.Status is PaymentStatus.Paid or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return new GatewaySettlementResultDto
            {
                Status = GatewaySettlementStatus.AlreadySettled,
                PaymentId = payment.Id,
                ReceiptNumber = payment.ReceiptNumber,
                ExpectedAmount = payment.FinalAmount,
                Detail = $"Payment {payment.ReceiptNumber} is already {payment.Status}."
            };
        }

        if (payment.Status is not (PaymentStatus.AwaitingConfirmation or PaymentStatus.Pending))
        {
            return new GatewaySettlementResultDto
            {
                Status = GatewaySettlementStatus.NotSettleable,
                PaymentId = payment.Id,
                ReceiptNumber = payment.ReceiptNumber,
                ExpectedAmount = payment.FinalAmount,
                Detail = $"Payment {payment.ReceiptNumber} is {payment.Status} and cannot be settled by a gateway."
            };
        }

        // The amount gate. Everything below this line writes; nothing above it does.
        var reported = Round(dto.Amount);
        var expected = Round(payment.FinalAmount);

        if (reported != expected)
        {
            return new GatewaySettlementResultDto
            {
                Status = GatewaySettlementStatus.AmountMismatch,
                PaymentId = payment.Id,
                ReceiptNumber = payment.ReceiptNumber,
                ExpectedAmount = expected,
                Detail = $"Payment {payment.ReceiptNumber} was requested at {expected:0.00} but the gateway " +
                         $"reported {reported:0.00}."
            };
        }

        var previousStatus = payment.Status;

        payment.Status = PaymentStatus.Paid;
        payment.ConfirmedAtUtc = _clock.UtcNow;
        payment.ConfirmedByUserId = null;

        // The payer's VPA is only ever filled in, never overwritten: what the operator recorded
        // stands. A card number, CVV or UPI PIN is not read from the payload and could not be
        // stored here even if a gateway sent one.
        if (Normalize(payment.PayerVpa) is null && Normalize(dto.PayerVpa) is { } vpa)
            payment.PayerVpa = vpa;

        var txn = Normalize(dto.GatewayTransactionId);
        var note = txn is null
            ? $"Settled automatically by {dto.Provider} (event {dto.EventId})."
            : $"Settled automatically by {dto.Provider} (event {dto.EventId}, transaction {txn}).";

        payment.Notes = string.IsNullOrWhiteSpace(payment.Notes)
            ? note
            : $"{payment.Notes}{Environment.NewLine}{note}";

        var tx = await BeginTransactionIfNoneAsync(ct).ConfigureAwait(false);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            decimal outstandingAfter = 0m;
            if (payment.SubscriptionId is > 0)
            {
                var subscription = await _db.Subscriptions
                    .FirstOrDefaultAsync(s => s.Id == payment.SubscriptionId, ct)
                    .ConfigureAwait(false);

                if (subscription is not null)
                {
                    outstandingAfter = await RecalculateSubscriptionAsync(
                        subscription,
                        SubscriptionActionType.PaymentReceived,
                        payment.FinalAmount,
                        $"Payment {payment.ReceiptNumber} settled by {dto.Provider}.",
                        ct).ConfigureAwait(false);
                }
            }

            // Attributed to the gateway, not to a user: LogForUserAsync with a null user id is what
            // keeps a machine settlement out of some operator's name in the audit trail.
            await _audit.LogForUserAsync(
                userId: null,
                userName: $"gateway:{dto.Provider}",
                action: AuditActions.PaymentGatewaySettled,
                entityName: nameof(Payment),
                entityId: payment.Id,
                oldValues: new { Status = previousStatus },
                newValues: new
                {
                    payment.Status,
                    payment.ConfirmedAtUtc,
                    payment.FinalAmount,
                    payment.TransactionReference,
                    GatewayEventId = dto.EventId,
                    GatewayTransactionId = txn
                },
                description:
                $"Payment {payment.ReceiptNumber} of {payment.FinalAmount:0.00} settled automatically by " +
                $"{dto.Provider} from event {dto.EventId}.",
                ipAddress: null,
                deviceInfo: $"webhook:{dto.Provider}",
                ct: ct).ConfigureAwait(false);

            if (tx is not null) await tx.CommitAsync(ct).ConfigureAwait(false);

            var member = await _db.Members.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == payment.MemberId, ct)
                .ConfigureAwait(false);

            if (member is not null)
                await NotifyPaymentAsync(payment, member, outstandingAfter, ct).ConfigureAwait(false);
        }
        catch
        {
            await SafeRollbackAsync(tx, ct).ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync().ConfigureAwait(false);
        }

        // Outside the transaction, exactly as the staff-driven paths do it: the mailer stamps
        // ReceiptEmailedAtUtc with its own SaveChanges and cannot run inside a committed
        // transaction still attached to the context. This is the one and only receipt for this
        // payment — the send-once guard makes a later manual confirmation a no-op.
        await SendReceiptEmailAsync(payment.Id, ct).ConfigureAwait(false);

        return new GatewaySettlementResultDto
        {
            Status = GatewaySettlementStatus.Settled,
            PaymentId = payment.Id,
            ReceiptNumber = payment.ReceiptNumber,
            ExpectedAmount = expected,
            Detail = $"Payment {payment.ReceiptNumber} settled by {dto.Provider}."
        };
    }

    // ---------------------------------------------------------------------------------------
    // Delete / restore
    // ---------------------------------------------------------------------------------------

    public async Task SoftDeleteAsync(int id, CancellationToken ct = default)
    {
        var payment = await _db.Payments
                          .FirstOrDefaultAsync(p => p.Id == id, ct)
                          .ConfigureAwait(false)
                      ?? throw new NotFoundAppException("Payment", id);

        // A settled receipt is an accounting record: it is reversed with a refund, never deleted.
        if (payment.Status is PaymentStatus.Paid or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            throw new BusinessRuleAppException(
                $"Payment {payment.ReceiptNumber} is settled and cannot be deleted. " +
                "Raise a refund instead so that the money trail stays auditable.");

        var hasRefunds = await _db.PaymentRefunds
            .AsNoTracking()
            .AnyAsync(r => r.PaymentId == payment.Id, ct)
            .ConfigureAwait(false);

        if (hasRefunds)
            throw new BusinessRuleAppException(
                $"Payment {payment.ReceiptNumber} has refund records attached and cannot be deleted.");

        _db.Payments.Remove(payment); // The context converts this into a soft delete.
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.SoftDelete, nameof(Payment), payment.Id,
            new { payment.ReceiptNumber, payment.Status, payment.FinalAmount }, null,
            $"Payment {payment.ReceiptNumber} moved to the recycle bin.", ct).ConfigureAwait(false);
    }

    public async Task RestoreAsync(int id, CancellationToken ct = default)
    {
        var payment = await _db.Payments
                          .IgnoreQueryFilters()
                          .FirstOrDefaultAsync(p => p.Id == id, ct)
                          .ConfigureAwait(false)
                      ?? throw new NotFoundAppException("Payment", id);

        if (!payment.IsDeleted) return;

        if (Normalize(payment.TransactionReference) is { } reference)
            await EnsureReferenceIsFreeAsync(reference, payment.Id, ct).ConfigureAwait(false);

        payment.IsDeleted = false;
        payment.DeletedAt = null;
        payment.DeletedBy = null;

        var tx = await BeginTransactionIfNoneAsync(ct).ConfigureAwait(false);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            if (payment.SubscriptionId is > 0)
            {
                var subscription = await _db.Subscriptions
                    .FirstOrDefaultAsync(s => s.Id == payment.SubscriptionId, ct)
                    .ConfigureAwait(false);

                if (subscription is not null)
                {
                    await RecalculateSubscriptionAsync(subscription, null, null, null, ct).ConfigureAwait(false);
                }
            }

            await _audit.LogAsync(AuditActions.Restore, nameof(Payment), payment.Id, null,
                new { payment.ReceiptNumber, payment.Status },
                $"Payment {payment.ReceiptNumber} restored from the recycle bin.", ct).ConfigureAwait(false);

            if (tx is not null) await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await SafeRollbackAsync(tx, ct).ConfigureAwait(false);
            throw new ConflictAppException(
                $"Payment {payment.ReceiptNumber} cannot be restored: its transaction reference is now used by another payment.");
        }
        catch
        {
            await SafeRollbackAsync(tx, ct).ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync().ConfigureAwait(false);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Receipts
    // ---------------------------------------------------------------------------------------

    public async Task<PaymentReceiptDto> GetReceiptAsync(int paymentId, CancellationToken ct = default) =>
        await PaymentReceiptFactory.TryBuildAsync(_db, _settings, paymentId, ct).ConfigureAwait(false)
        ?? throw new NotFoundAppException("Payment", paymentId);

    public async Task<FileExportDto> GetReceiptPdfAsync(int paymentId, CancellationToken ct = default)
    {
        var receipt = await GetReceiptAsync(paymentId, ct).ConfigureAwait(false);
        var content = _pdf.ExportReceipt(receipt);

        return new FileExportDto
        {
            FileName = PaymentReceiptFactory.PdfFileName(receipt.ReceiptNumber),
            ContentType = "application/pdf",
            Content = content
        };
    }

    // ---------------------------------------------------------------------------------------
    // UPI collection intent
    // ---------------------------------------------------------------------------------------

    public async Task<UpiPaymentIntentDto> CreateUpiIntentAsync(UpiPaymentRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var member = await _db.Members
                         .AsNoTracking()
                         .Where(m => m.Id == request.MemberId)
                         .Select(m => new { m.Id, m.FullName, m.MemberCode })
                         .FirstOrDefaultAsync(ct)
                         .ConfigureAwait(false)
                     ?? throw new NotFoundAppException("Member", request.MemberId);

        if (request.Amount <= 0m)
            throw new ValidationAppException(nameof(request.Amount), "Amount must be greater than zero.");

        var amount = Round(request.Amount);

        if (request.SubscriptionId is > 0)
        {
            var subscription = await _db.Subscriptions
                                   .AsNoTracking()
                                   .Where(s => s.Id == request.SubscriptionId)
                                   .Select(s => new { s.Id, s.MemberId, s.SubscriptionCode, s.Status, s.FinalAmount, s.PaidAmount })
                                   .FirstOrDefaultAsync(ct)
                                   .ConfigureAwait(false)
                               ?? throw new NotFoundAppException("Subscription", request.SubscriptionId.Value);

            if (subscription.MemberId != member.Id)
                throw new ValidationAppException(nameof(request.SubscriptionId),
                    "The subscription does not belong to the selected member.");

            if (subscription.Status == SubscriptionStatus.Cancelled)
                throw new BusinessRuleAppException(
                    $"Subscription {subscription.SubscriptionCode} is cancelled and cannot accept payments.");

            var outstanding = Round(Math.Max(0m, subscription.FinalAmount - subscription.PaidAmount));
            if (amount > outstanding)
                throw new BusinessRuleAppException(
                    $"The requested amount {amount:0.00} exceeds the outstanding balance " +
                    $"{outstanding:0.00} on subscription {subscription.SubscriptionCode}.");
        }

        var gym = await _settings.GetGymSettingsAsync(ct).ConfigureAwait(false);

        var upiId = Normalize(gym.UpiId);
        if (upiId is null)
            throw new BusinessRuleAppException(
                "No UPI id is configured for this gym. Add one under Settings before collecting UPI payments.");

        var payeeName = Normalize(gym.UpiPayeeName) ?? Normalize(gym.GymName) ?? "Gym";
        var note = Normalize(request.Notes) ?? $"Membership payment {member.MemberCode}";
        var reference = await NextUpiReferenceAsync(ct).ConfigureAwait(false);

        var deepLink =
            $"upi://pay?pa={Uri.EscapeDataString(upiId)}" +
            $"&pn={Uri.EscapeDataString(payeeName)}" +
            $"&am={amount.ToString("F2", CultureInfo.InvariantCulture)}" +
            "&cu=INR" +
            $"&tn={Uri.EscapeDataString(note)}" +
            $"&tr={Uri.EscapeDataString(reference)}";

        // Whether anything will reconcile this transfer on its own. With no gateway configured the
        // answer is no and the screen must keep saying so; with one configured the old wording
        // ("there is NO automatic verification") becomes a false statement to the operator.
        var gatewayConfigured = _gateway?.IsEnabled == true;

        // No Payment row is created here: a static QR gives no confirmation, so the payment is
        // only recorded once an operator has verified the transfer.
        return new UpiPaymentIntentDto
        {
            MemberId = member.Id,
            MemberName = member.FullName,
            SubscriptionId = request.SubscriptionId,
            Amount = amount,
            CurrencySymbol = string.IsNullOrWhiteSpace(gym.CurrencySymbol) ? "₹" : gym.CurrencySymbol,
            UpiId = upiId,
            PayeeName = payeeName,
            UpiDeepLink = deepLink,
            QrImagePath = Normalize(gym.UpiQrImagePath),
            PaymentReference = reference,
            RequiresManualVerification = !gatewayConfigured,
            Instructions = gatewayConfigured ? UpiGatewayInstructions : UpiInstructions
        };
    }

    /// <summary>Allocates a collection token that is not already recorded against a payment.</summary>
    private async Task<string> NextUpiReferenceAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 25; attempt++)
        {
            var candidate =
                $"UPI{_clock.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}" +
                RandomNumberGenerator.GetInt32(0, 10_000).ToString("0000", CultureInfo.InvariantCulture);

            var taken = await _db.Payments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(p => p.TransactionReference == candidate, ct)
                .ConfigureAwait(false);

            if (!taken) return candidate;
        }

        throw new ConflictAppException("Could not allocate a unique UPI payment reference. Please retry.");
    }

    // ---------------------------------------------------------------------------------------
    // Refunds
    // ---------------------------------------------------------------------------------------

    public async Task<PaymentRefundDto> CreateRefundAsync(CreateRefundDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var payment = await _db.Payments
                          .FirstOrDefaultAsync(p => p.Id == dto.PaymentId, ct)
                          .ConfigureAwait(false)
                      ?? throw new NotFoundAppException("Payment", dto.PaymentId);

        // Paid, or already partially refunded (a second partial refund is still legitimate).
        if (payment.Status is not (PaymentStatus.Paid or PaymentStatus.PartiallyRefunded))
            throw new BusinessRuleAppException(
                $"Payment {payment.ReceiptNumber} is {payment.Status}; only a settled payment can be refunded.");

        if (dto.Amount <= 0m)
            throw new ValidationAppException(nameof(dto.Amount), "Refund amount must be greater than zero.");

        var reason = Normalize(dto.Reason)
                     ?? throw new ValidationAppException(nameof(dto.Reason), "A refund reason is required.");

        var amount = Round(dto.Amount);
        var refundable = Round(Math.Max(0m, payment.FinalAmount - payment.RefundedAmount));

        if (amount > refundable)
            throw new BusinessRuleAppException(
                $"The refund of {amount:0.00} exceeds the refundable balance {refundable:0.00} " +
                $"on payment {payment.ReceiptNumber}.");

        var canApprove = _currentUser.HasPermission(Permissions.PaymentsRefund);
        var approveNow = dto.ApproveImmediately && canApprove;

        var remarks = Normalize(dto.Remarks);
        if (dto.ApproveImmediately && !canApprove)
        {
            const string note = "Immediate approval was requested but the operator does not hold the " +
                                "'payments.refund' permission; the refund is left pending approval.";
            remarks = remarks is null ? note : $"{remarks}{Environment.NewLine}{note}";
        }

        var refund = new PaymentRefund
        {
            RefundNumber = await _codes.NextRefundNumberAsync(ct).ConfigureAwait(false),
            PaymentId = payment.Id,
            Amount = amount,
            Reason = reason,
            Status = approveNow ? RefundStatus.Completed : RefundStatus.Pending,
            RequestedAt = _clock.UtcNow,
            RequestedByUserId = _currentUser.UserId,
            RefundMethod = Normalize(dto.RefundMethod),
            TransactionReference = Normalize(dto.TransactionReference),
            Remarks = remarks
        };

        if (approveNow)
        {
            refund.ApprovedAt = _clock.UtcNow;
            refund.ApprovedByUserId = _currentUser.UserId;
        }

        var tx = await BeginTransactionIfNoneAsync(ct).ConfigureAwait(false);
        try
        {
            _db.PaymentRefunds.Add(refund);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            if (approveNow)
                await ApplyRefundCompletionAsync(payment, refund, ct).ConfigureAwait(false);

            await _audit.LogAsync(AuditActions.PaymentRefunded, nameof(PaymentRefund), refund.Id, null,
                new { refund.RefundNumber, refund.PaymentId, refund.Amount, refund.Status, refund.Reason },
                $"Refund {refund.RefundNumber} of {amount:0.00} raised against payment {payment.ReceiptNumber} " +
                $"({refund.Status}).",
                ct).ConfigureAwait(false);

            if (tx is not null) await tx.CommitAsync(ct).ConfigureAwait(false);

            return await GetRefundDtoAsync(refund.Id, ct).ConfigureAwait(false);
        }
        catch
        {
            await SafeRollbackAsync(tx, ct).ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task<PaymentRefundDto> ApproveRefundAsync(ApproveRefundDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var refund = await _db.PaymentRefunds
                         .FirstOrDefaultAsync(r => r.Id == dto.RefundId, ct)
                         .ConfigureAwait(false)
                     ?? throw new NotFoundAppException("PaymentRefund", dto.RefundId);

        if (refund.Status != RefundStatus.Pending)
            throw new BusinessRuleAppException(
                $"Refund {refund.RefundNumber} is {refund.Status} and can no longer be decided.");

        var payment = await _db.Payments
                          .IgnoreQueryFilters()
                          .FirstOrDefaultAsync(p => p.Id == refund.PaymentId, ct)
                          .ConfigureAwait(false)
                      ?? throw new NotFoundAppException("Payment", refund.PaymentId);

        var remarks = Normalize(dto.Remarks);
        if (remarks is not null)
            refund.Remarks = string.IsNullOrWhiteSpace(refund.Remarks)
                ? remarks
                : $"{refund.Remarks}{Environment.NewLine}{remarks}";

        refund.ApprovedAt = _clock.UtcNow;
        refund.ApprovedByUserId = _currentUser.UserId;

        var tx = await BeginTransactionIfNoneAsync(ct).ConfigureAwait(false);
        try
        {
            if (dto.Approve)
            {
                var refundable = Round(Math.Max(0m, payment.FinalAmount - payment.RefundedAmount));
                if (refund.Amount > refundable)
                    throw new BusinessRuleAppException(
                        $"The refund of {refund.Amount:0.00} exceeds the refundable balance " +
                        $"{refundable:0.00} on payment {payment.ReceiptNumber}.");

                refund.Status = RefundStatus.Completed;
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                await ApplyRefundCompletionAsync(payment, refund, ct).ConfigureAwait(false);
            }
            else
            {
                refund.Status = RefundStatus.Rejected;
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            await _audit.LogAsync(AuditActions.PaymentRefunded, nameof(PaymentRefund), refund.Id,
                new { Status = RefundStatus.Pending },
                new { refund.Status, refund.Amount, refund.Remarks },
                $"Refund {refund.RefundNumber} {(dto.Approve ? "approved" : "rejected")} " +
                $"for payment {payment.ReceiptNumber}.",
                ct).ConfigureAwait(false);

            if (tx is not null) await tx.CommitAsync(ct).ConfigureAwait(false);

            return await GetRefundDtoAsync(refund.Id, ct).ConfigureAwait(false);
        }
        catch
        {
            await SafeRollbackAsync(tx, ct).ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Applies the money effects of a completed refund: the payment's refunded total grows, its
    /// status becomes Refunded / PartiallyRefunded and the parent subscription is recalculated.
    /// </summary>
    private async Task ApplyRefundCompletionAsync(Payment payment, PaymentRefund refund, CancellationToken ct)
    {
        payment.RefundedAmount = Round(payment.RefundedAmount + refund.Amount);
        payment.Status = payment.RefundedAmount >= payment.FinalAmount
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (payment.SubscriptionId is > 0)
        {
            var subscription = await _db.Subscriptions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Id == payment.SubscriptionId, ct)
                .ConfigureAwait(false);

            if (subscription is not null)
            {
                await RecalculateSubscriptionAsync(
                    subscription,
                    SubscriptionActionType.PaymentReceived,
                    -refund.Amount,
                    $"Refund {refund.RefundNumber} of {refund.Amount:0.00} applied to payment {payment.ReceiptNumber}.",
                    ct).ConfigureAwait(false);
            }
        }
    }

    public async Task<PagedResult<PaymentRefundDto>> GetRefundsAsync(PagedRequest query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Filtering happens on the entity before the join/projection so that every predicate
        // translates to SQL on an indexed column.
        var refunds = _db.PaymentRefunds.IgnoreQueryFilters().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            refunds = refunds.Where(r =>
                EF.Functions.Like(r.RefundNumber, pattern) ||
                _db.Payments.Any(p => p.Id == r.PaymentId &&
                                      (EF.Functions.Like(p.ReceiptNumber, pattern) ||
                                       (p.Member != null && EF.Functions.Like(p.Member.FullName, pattern)))));
        }

        var total = await refunds.CountAsync(ct).ConfigureAwait(false);

        var page = refunds
            .OrderByDescending(r => r.RequestedAt)
            .ThenByDescending(r => r.Id)
            .Skip(query.Skip)
            .Take(query.PageSize);

        var items = await ProjectRefunds(page).ToListAsync(ct).ConfigureAwait(false);

        // The join above does not carry the inner ordering, so the page is re-ordered here.
        var ordered = items
            .OrderByDescending(x => x.RequestedAt)
            .ThenByDescending(x => x.Id)
            .ToList();

        return new PagedResult<PaymentRefundDto>(ordered, total, query.PageNumber, query.PageSize);
    }

    private async Task<PaymentRefundDto> GetRefundDtoAsync(int refundId, CancellationToken ct) =>
        await ProjectRefunds(_db.PaymentRefunds.IgnoreQueryFilters().AsNoTracking().Where(r => r.Id == refundId))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false)
        ?? throw new NotFoundAppException("PaymentRefund", refundId);

    /// <summary>Refund projection joined to its payment and member. Deleted payments stay visible.</summary>
    private IQueryable<PaymentRefundDto> ProjectRefunds(IQueryable<PaymentRefund> refunds) =>
        from refund in refunds
        join payment in _db.Payments.IgnoreQueryFilters() on refund.PaymentId equals payment.Id
        join member in _db.Members.IgnoreQueryFilters() on payment.MemberId equals member.Id
        select new PaymentRefundDto
        {
            Id = refund.Id,
            RefundNumber = refund.RefundNumber,
            PaymentId = refund.PaymentId,
            ReceiptNumber = payment.ReceiptNumber,
            MemberId = member.Id,
            MemberName = member.FullName,
            Amount = refund.Amount,
            Reason = refund.Reason,
            Status = refund.Status,
            RequestedAt = refund.RequestedAt,
            RequestedByName = _db.Users
                .Where(u => u.Id == refund.RequestedByUserId)
                .Select(u => u.FullName)
                .FirstOrDefault(),
            ApprovedAt = refund.ApprovedAt,
            ApprovedByName = _db.Users
                .Where(u => u.Id == refund.ApprovedByUserId)
                .Select(u => u.FullName)
                .FirstOrDefault(),
            RefundMethod = refund.RefundMethod,
            TransactionReference = refund.TransactionReference,
            Remarks = refund.Remarks
        };

    // ---------------------------------------------------------------------------------------
    // Outstanding balances
    // ---------------------------------------------------------------------------------------

    public async Task<PagedResult<OutstandingBalanceDto>> GetOutstandingAsync(PagedRequest query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var q = _db.Subscriptions
            .AsNoTracking()
            .Where(s => s.Status != SubscriptionStatus.Cancelled && s.FinalAmount > s.PaidAmount);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(s => s.Member != null && (
                EF.Functions.Like(s.Member.MemberCode, $"%{term}%") ||
                EF.Functions.Like(s.Member.FullName, $"%{term}%") ||
                EF.Functions.Like(s.Member.Phone, $"%{term}%")));
        }

        var total = await q.CountAsync(ct).ConfigureAwait(false);

        // The oldest EndDate is the most overdue, so ordering by EndDate ascending is exactly
        // "DaysOverdue descending" without needing a provider specific date-diff in SQL.
        var rows = await q
            .OrderBy(s => s.EndDate)
            .ThenBy(s => s.Id)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(s => new OutstandingBalanceDto
            {
                MemberId = s.MemberId,
                MemberCode = s.Member != null ? s.Member.MemberCode : string.Empty,
                MemberName = s.Member != null ? s.Member.FullName : string.Empty,
                Phone = s.Member != null ? s.Member.Phone : null,
                SubscriptionId = s.Id,
                SubscriptionCode = s.SubscriptionCode,
                PlanName = s.MembershipPlan != null ? s.MembershipPlan.Name : string.Empty,
                StartDate = s.StartDate,
                EndDate = s.EndDate,
                FinalAmount = s.FinalAmount,
                PaidAmount = s.PaidAmount
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var today = _clock.Today;
        foreach (var row in rows)
        {
            row.OutstandingAmount = Round(Math.Max(0m, row.FinalAmount - row.PaidAmount));
            row.DaysOverdue = Math.Max(0, (today.Date - row.EndDate.Date).Days);
        }

        return new PagedResult<OutstandingBalanceDto>(rows, total, query.PageNumber, query.PageSize);
    }

    // ---------------------------------------------------------------------------------------
    // Payment methods
    // ---------------------------------------------------------------------------------------

    public async Task<List<PaymentMethodDto>> GetPaymentMethodsAsync(bool onlyActive = true, CancellationToken ct = default)
    {
        var q = _db.PaymentMethods.AsNoTracking();
        if (onlyActive) q = q.Where(m => m.IsActive);

        return await q
            .OrderBy(m => m.DisplayOrder)
            .ThenBy(m => m.Name)
            .Select(m => new PaymentMethodDto
            {
                Id = m.Id,
                Code = m.Code,
                Name = m.Name,
                RequiresReference = m.RequiresReference,
                SupportsQrCode = m.SupportsQrCode,
                IsActive = m.IsActive,
                DisplayOrder = m.DisplayOrder
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<PaymentMethodDto> SavePaymentMethodAsync(PaymentMethodDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var code = Normalize(dto.Code)?.ToUpperInvariant()
                   ?? throw new ValidationAppException(nameof(dto.Code), "A payment method code is required.");
        var name = Normalize(dto.Name)
                   ?? throw new ValidationAppException(nameof(dto.Name), "A payment method name is required.");

        var duplicate = await _db.PaymentMethods
            .AsNoTracking()
            .AnyAsync(m => m.Code == code && m.Id != dto.Id, ct)
            .ConfigureAwait(false);

        if (duplicate)
            throw new ConflictAppException($"A payment method with code '{code}' already exists.");

        PaymentMethod entity;
        var isNew = dto.Id <= 0;

        if (isNew)
        {
            entity = new PaymentMethod();
            _db.PaymentMethods.Add(entity);
        }
        else
        {
            entity = await _db.PaymentMethods
                         .FirstOrDefaultAsync(m => m.Id == dto.Id, ct)
                         .ConfigureAwait(false)
                     ?? throw new NotFoundAppException("PaymentMethod", dto.Id);
        }

        entity.Code = code;
        entity.Name = name;
        entity.RequiresReference = dto.RequiresReference;
        entity.SupportsQrCode = dto.SupportsQrCode;
        entity.IsActive = dto.IsActive;
        entity.DisplayOrder = dto.DisplayOrder;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.ConfigurationChanged, nameof(PaymentMethod), entity.Id, null,
            new { entity.Code, entity.Name, entity.RequiresReference, entity.SupportsQrCode, entity.IsActive },
            $"Payment method '{entity.Name}' {(isNew ? "created" : "updated")}.", ct).ConfigureAwait(false);

        return new PaymentMethodDto
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            RequiresReference = entity.RequiresReference,
            SupportsQrCode = entity.SupportsQrCode,
            IsActive = entity.IsActive,
            DisplayOrder = entity.DisplayOrder
        };
    }

    // ---------------------------------------------------------------------------------------
    // Shared helpers
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Single source of truth for subscription money state.
    /// PaidAmount = Σ (FinalAmount - RefundedAmount) over the subscription's settled payments;
    /// PaymentStatus = Paid when PaidAmount >= FinalAmount, PartiallyPaid when > 0, else Pending;
    /// a Pending subscription is promoted to Active as soon as any money has been received.
    /// Returns the outstanding amount left on the subscription.
    /// </summary>
    private async Task<decimal> RecalculateSubscriptionAsync(
        Subscription subscription,
        SubscriptionActionType? historyAction,
        decimal? historyAmount,
        string? historyRemarks,
        CancellationToken ct)
    {
        var paid = await _db.Payments
            .AsNoTracking()
            .Where(p => p.SubscriptionId == subscription.Id && SettledStatuses.Contains(p.Status))
            .Select(p => p.FinalAmount - p.RefundedAmount)
            .SumAsync(ct)
            .ConfigureAwait(false);

        paid = Round(Math.Max(0m, paid));

        subscription.PaidAmount = paid;
        subscription.PaymentStatus = paid >= subscription.FinalAmount
            ? PaymentStatus.Paid
            : paid > 0m
                ? PaymentStatus.PartiallyPaid
                : PaymentStatus.Pending;

        var oldStatus = subscription.Status;
        if (subscription.Status == SubscriptionStatus.Pending && paid > 0m)
            subscription.Status = SubscriptionStatus.Active;

        if (historyAction is not null)
        {
            _db.SubscriptionHistory.Add(new SubscriptionHistory
            {
                SubscriptionId = subscription.Id,
                ActionType = historyAction.Value,
                ActionDate = _clock.UtcNow,
                OldStatus = oldStatus,
                NewStatus = subscription.Status,
                Amount = historyAmount,
                Remarks = historyRemarks,
                PerformedBy = _currentUser.UserId
            });
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Round(Math.Max(0m, subscription.FinalAmount - subscription.PaidAmount));
    }

    /// <summary>
    /// Rejects a transaction reference that is already recorded against another live payment.
    /// The database also enforces this with a unique filtered index.
    /// </summary>
    private async Task EnsureReferenceIsFreeAsync(string reference, int? excludePaymentId, CancellationToken ct)
    {
        var taken = await _db.Payments
            .AsNoTracking()
            .AnyAsync(p => p.TransactionReference == reference &&
                           (excludePaymentId == null || p.Id != excludePaymentId), ct)
            .ConfigureAwait(false);

        if (taken)
            throw new ConflictAppException($"A payment with transaction reference '{reference}' already exists.");
    }

    private async Task NotifyPaymentAsync(Payment payment, Member member, decimal outstanding, CancellationToken ct)
    {
        try
        {
            var symbol = await GetCurrencySymbolAsync(ct).ConfigureAwait(false);
            var settled = payment.Status == PaymentStatus.Paid;
            var balanceRemains = outstanding > 0m;

            if (settled && !balanceRemains)
            {
                await _notifications.NotifyAsync(
                    NotificationType.PaymentSuccessful,
                    NotificationSeverity.Success,
                    "Payment received",
                    $"Payment {payment.ReceiptNumber} of {symbol}{payment.FinalAmount:N2} was received from {member.FullName}.",
                    memberId: member.Id,
                    entityName: nameof(Payment),
                    entityId: payment.Id,
                    deduplicationKey: $"payment:{payment.Id}:paid",
                    ct: ct).ConfigureAwait(false);
            }
            else
            {
                var reason = settled
                    ? $"{symbol}{outstanding:N2} is still outstanding."
                    : "The transaction is awaiting confirmation.";

                await _notifications.NotifyAsync(
                    NotificationType.PaymentPending,
                    NotificationSeverity.Warning,
                    "Payment pending",
                    $"Payment {payment.ReceiptNumber} of {symbol}{payment.FinalAmount:N2} from {member.FullName}: {reason}",
                    memberId: member.Id,
                    entityName: nameof(Payment),
                    entityId: payment.Id,
                    deduplicationKey: $"payment:{payment.Id}:pending",
                    ct: ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // A notification must never fail a committed payment.
            _logger.LogWarning(ex, "Could not raise a notification for payment {PaymentId}.", payment.Id);
        }
    }

    private async Task<string> GetCurrencySymbolAsync(CancellationToken ct)
    {
        try
        {
            var gym = await _settings.GetGymSettingsAsync(ct).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(gym.CurrencySymbol) ? "₹" : gym.CurrencySymbol;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Gym settings unavailable while formatting a currency amount.");
            return "₹";
        }
    }

    /// <summary>Joins an ambient transaction when one exists, otherwise starts and owns a new one.</summary>
    private async Task<IDbContextTransaction?> BeginTransactionIfNoneAsync(CancellationToken ct) =>
        _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false)
            : null;

    private async Task SafeRollbackAsync(IDbContextTransaction? tx, CancellationToken ct)
    {
        if (tx is null) return;

        try
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rolling back the payment transaction failed.");
        }
    }

    /// <summary>True for SQL Server unique index / unique constraint violations (2601, 2627).</summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            // Read SqlException.Number without taking a compile-time dependency on the provider.
            if (inner.GetType().GetProperty("Number")?.GetValue(inner) is int number && number is 2601 or 2627)
                return true;

            if (inner.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
                inner.Message.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

}
