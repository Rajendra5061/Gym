using GymManagement.Application.Common;
using GymManagement.Application.DTOs;

namespace GymManagement.Application.Interfaces;

public interface IMembershipPlanService
{
    Task<PagedResult<MembershipPlanDto>> GetPagedAsync(MembershipPlanQueryDto query, CancellationToken ct = default);
    Task<List<MembershipPlanDto>> GetActiveAsync(CancellationToken ct = default);
    Task<MembershipPlanDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<MembershipPlanDto> CreateAsync(MembershipPlanDto dto, CancellationToken ct = default);
    Task<MembershipPlanDto> UpdateAsync(MembershipPlanDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(int id, CancellationToken ct = default);
    Task RestoreAsync(int id, CancellationToken ct = default);
    Task<List<LookupDto>> GetLookupAsync(CancellationToken ct = default);
}

public interface ISubscriptionService
{
    /// <summary>Server-side price calculation. The client never decides the payable amount.</summary>
    Task<SubscriptionQuoteDto> GetQuoteAsync(QuoteRequestDto request, CancellationToken ct = default);

    Task<PagedResult<SubscriptionDto>> GetPagedAsync(SubscriptionQueryDto query, CancellationToken ct = default);
    Task<SubscriptionDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<List<SubscriptionDto>> GetForMemberAsync(int memberId, CancellationToken ct = default);
    Task<SubscriptionDto?> GetActiveForMemberAsync(int memberId, CancellationToken ct = default);
    Task<List<SubscriptionHistoryDto>> GetHistoryAsync(int subscriptionId, CancellationToken ct = default);

    /// <summary>Creates subscription + optional payment inside one database transaction.</summary>
    Task<SubscriptionDto> CreateAsync(CreateSubscriptionDto dto, CancellationToken ct = default);
    Task<SubscriptionDto> RenewAsync(RenewSubscriptionDto dto, CancellationToken ct = default);
    Task<SubscriptionDto> ChangePlanAsync(ChangePlanDto dto, CancellationToken ct = default);
    Task<SubscriptionDto> FreezeAsync(FreezeSubscriptionDto dto, CancellationToken ct = default);
    Task<SubscriptionDto> ResumeAsync(ResumeSubscriptionDto dto, CancellationToken ct = default);
    Task<SubscriptionDto> CancelAsync(CancelSubscriptionDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(int id, CancellationToken ct = default);
    Task RestoreAsync(int id, CancellationToken ct = default);

    /// <summary>Expires finished subscriptions, auto-resumes freezes and raises reminders.</summary>
    Task<ExpiryProcessingResultDto> ProcessExpiriesAsync(CancellationToken ct = default);
}

public interface IPaymentService
{
    Task<PagedResult<PaymentDto>> GetPagedAsync(PaymentQueryDto query, CancellationToken ct = default);
    Task<PaymentDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<List<PaymentDto>> GetForMemberAsync(int memberId, CancellationToken ct = default);

    /// <summary>
    /// The member a payment belongs to, or <see langword="null"/> when no such payment is visible.
    /// Used to decide whether a signed-in member owns a receipt; it returns null instead of throwing
    /// so that a missing payment cannot be told apart from somebody else's.
    /// </summary>
    Task<int?> GetOwnerMemberIdAsync(int paymentId, CancellationToken ct = default);

    Task<PaymentDto> CreateAsync(CreatePaymentDto dto, CancellationToken ct = default);
    Task<PaymentDto> ConfirmAsync(ConfirmPaymentDto dto, CancellationToken ct = default);

    /// <summary>
    /// Settles a payment on the instruction of a gateway webhook whose signature has already been
    /// verified. Reports what it did instead of throwing, because the caller has to answer a
    /// machine, and refuses outright when the amount reported is not the amount requested.
    /// </summary>
    Task<GatewaySettlementResultDto> SettleFromGatewayAsync(
        GatewaySettlementDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(int id, CancellationToken ct = default);
    Task RestoreAsync(int id, CancellationToken ct = default);

    Task<PaymentReceiptDto> GetReceiptAsync(int paymentId, CancellationToken ct = default);
    Task<FileExportDto> GetReceiptPdfAsync(int paymentId, CancellationToken ct = default);

    Task<UpiPaymentIntentDto> CreateUpiIntentAsync(UpiPaymentRequestDto request, CancellationToken ct = default);

    Task<PaymentRefundDto> CreateRefundAsync(CreateRefundDto dto, CancellationToken ct = default);
    Task<PaymentRefundDto> ApproveRefundAsync(ApproveRefundDto dto, CancellationToken ct = default);
    Task<PagedResult<PaymentRefundDto>> GetRefundsAsync(PagedRequest query, CancellationToken ct = default);

    Task<PagedResult<OutstandingBalanceDto>> GetOutstandingAsync(PagedRequest query, CancellationToken ct = default);
    Task<List<PaymentMethodDto>> GetPaymentMethodsAsync(bool onlyActive = true, CancellationToken ct = default);
    Task<PaymentMethodDto> SavePaymentMethodAsync(PaymentMethodDto dto, CancellationToken ct = default);
}

/// <summary>Why a receipt email was, or was not, sent.</summary>
public enum ReceiptEmailOutcome
{
    /// <summary>No mail provider is configured. The normal state of a fresh checkout.</summary>
    Disabled = 0,

    /// <summary>The payment no longer exists (deleted between the commit and the send).</summary>
    PaymentNotFound = 1,

    /// <summary>The payment is not settled yet; the receipt goes out when it is confirmed.</summary>
    NotSettled = 2,

    /// <summary>The member has no email address on file.</summary>
    NoRecipient = 3,

    /// <summary>A receipt for this payment has already gone out. Nothing was sent again.</summary>
    AlreadySent = 4,

    Sent = 5,

    /// <summary>The provider threw. Logged and swallowed — the payment itself stands.</summary>
    Failed = 6
}

/// <summary>
/// Emails a payment receipt to the member it belongs to.
///
/// Called only <b>after</b> the payment transaction has committed: the money is recorded whether
/// or not the message goes out, so no implementation may throw. At most one receipt is ever sent
/// per payment — <c>Payment.ReceiptEmailedAtUtc</c> is stamped on success and checked first.
/// </summary>
public interface IPaymentReceiptMailer
{
    Task<ReceiptEmailOutcome> SendReceiptAsync(int paymentId, CancellationToken ct = default);
}

/// <summary>
/// Emails renewal reminders to members whose active subscription ends within the configured
/// window (default three days), one email per member per day.
///
/// Runs from the daily scheduler and from an admin-triggered endpoint, so it must be safe to
/// call any number of times: the send log's unique (member, day) row is claimed before each
/// message goes out, and a member with no address, or whose reminder already went today, is
/// skipped silently. Never throws for a mail failure; each member is attempted independently.
/// </summary>
public interface IExpiryReminderMailer
{
    /// <summary>Sends what is due today. Returns how many emails actually went out.</summary>
    Task<int> SendDueRemindersAsync(CancellationToken ct = default);
}

public interface IExpenseService
{
    Task<PagedResult<ExpenseDto>> GetPagedAsync(ExpenseQueryDto query, CancellationToken ct = default);
    Task<ExpenseDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<ExpenseDto> SaveAsync(SaveExpenseDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(int id, CancellationToken ct = default);
    Task RestoreAsync(int id, CancellationToken ct = default);
    Task<List<ExpenseCategoryDto>> GetCategoriesAsync(CancellationToken ct = default);
    Task<ExpenseCategoryDto> SaveCategoryAsync(ExpenseCategoryDto dto, CancellationToken ct = default);
}

public interface ISalaryPaymentService
{
    Task<PagedResult<SalaryPaymentDto>> GetPagedAsync(SalaryQueryDto query, CancellationToken ct = default);
    Task<SalarySummaryDto> GetSummaryAsync(int? year, CancellationToken ct = default);

    /// <summary>Records the payment and writes the matching operating expense in one transaction.</summary>
    Task<SalaryPaymentDto> CreateAsync(SaveSalaryPaymentDto dto, CancellationToken ct = default);

    Task SoftDeleteAsync(int id, CancellationToken ct = default);
}
