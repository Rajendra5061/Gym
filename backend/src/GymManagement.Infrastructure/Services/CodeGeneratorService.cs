using System.Globalization;
using System.Linq.Expressions;
using GymManagement.Application.Common;
using GymManagement.Domain.Entities;
using GymManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GymManagement.Infrastructure.Services;

/// <summary>
/// Produces human readable, per-year sequential business codes such as <c>GYM-2026-0001</c> or
/// <c>RCP-2026-0042</c>. Soft-deleted rows still reserve their code, so numbers are never reused.
/// </summary>
public sealed class CodeGeneratorService : ICodeGeneratorService
{
    private const string DefaultMemberPrefix = "GYM";
    private const string DefaultTrainerPrefix = "TRN";
    private const string DefaultPlanPrefix = "PLN";
    private const string DefaultSubscriptionPrefix = "SUB";
    private const string DefaultReceiptPrefix = "RCP";
    private const string DefaultRefundPrefix = "REF";
    private const string DefaultExpensePrefix = "EXP";

    /// <summary>Upper bound on the uniqueness probe so a corrupt table cannot spin forever.</summary>
    private const int MaxProbeAttempts = 1000;

    private readonly GymDbContext _db;
    private readonly IDateTimeProvider _clock;

    public CodeGeneratorService(GymDbContext db, IDateTimeProvider clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<string> NextMemberCodeAsync(CancellationToken ct = default)
    {
        var settings = await LoadSettingsAsync(ct).ConfigureAwait(false);
        return await NextCodeAsync<Member>(
            Normalize(settings?.MemberCodePrefix, DefaultMemberPrefix),
            m => m.MemberCode, ct).ConfigureAwait(false);
    }

    public async Task<string> NextTrainerCodeAsync(CancellationToken ct = default) =>
        await NextCodeAsync<Trainer>(DefaultTrainerPrefix, t => t.TrainerCode, ct).ConfigureAwait(false);

    public async Task<string> NextPlanCodeAsync(CancellationToken ct = default) =>
        await NextCodeAsync<MembershipPlan>(DefaultPlanPrefix, p => p.PlanCode, ct).ConfigureAwait(false);

    public async Task<string> NextSubscriptionCodeAsync(CancellationToken ct = default) =>
        await NextCodeAsync<Subscription>(DefaultSubscriptionPrefix, s => s.SubscriptionCode, ct).ConfigureAwait(false);

    public async Task<string> NextReceiptNumberAsync(CancellationToken ct = default)
    {
        var settings = await LoadSettingsAsync(ct).ConfigureAwait(false);
        return await NextCodeAsync<Payment>(
            Normalize(settings?.ReceiptPrefix, DefaultReceiptPrefix),
            p => p.ReceiptNumber, ct).ConfigureAwait(false);
    }

    public async Task<string> NextRefundNumberAsync(CancellationToken ct = default) =>
        await NextCodeAsync<PaymentRefund>(DefaultRefundPrefix, r => r.RefundNumber, ct).ConfigureAwait(false);

    public async Task<string> NextExpenseNumberAsync(CancellationToken ct = default) =>
        await NextCodeAsync<Expense>(DefaultExpensePrefix, e => e.ExpenseNumber, ct).ConfigureAwait(false);

    /// <summary>
    /// Counts the codes already issued for the current year, then probes forward until the
    /// candidate is free. The probe closes the race where two callers compute the same sequence.
    /// </summary>
    private async Task<string> NextCodeAsync<TEntity>(string prefix,
        Expression<Func<TEntity, string>> codeSelector, CancellationToken ct) where TEntity : class
    {
        var year = _clock.UtcNow.Year.ToString("0000", CultureInfo.InvariantCulture);
        var stem = $"{prefix}-{year}-";

        // IgnoreQueryFilters: soft-deleted rows keep their code reserved.
        var codes = _db.Set<TEntity>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(codeSelector);

        var used = await codes.CountAsync(c => c.StartsWith(stem), ct).ConfigureAwait(false);
        var sequence = used + 1;

        for (var attempt = 0; attempt < MaxProbeAttempts; attempt++, sequence++)
        {
            var candidate = $"{stem}{sequence.ToString("0000", CultureInfo.InvariantCulture)}";
            var taken = await codes.AnyAsync(c => c == candidate, ct).ConfigureAwait(false);
            if (!taken) return candidate;
        }

        throw new ConflictAppException(
            $"Could not allocate a free code for prefix '{prefix}' after {MaxProbeAttempts} attempts.");
    }

    /// <summary>The single gym settings row, or null when the installation is not configured yet.</summary>
    private Task<GymSetting?> LoadSettingsAsync(CancellationToken ct) =>
        _db.GymSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .OrderBy(g => g.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>Trims and upper-cases a configured prefix, falling back to the built-in default.</summary>
    private static string Normalize(string? configured, string fallback)
    {
        if (string.IsNullOrWhiteSpace(configured)) return fallback;

        var cleaned = new string(configured
            .Where(c => char.IsLetterOrDigit(c))
            .ToArray())
            .ToUpperInvariant();

        return cleaned.Length == 0 ? fallback : cleaned;
    }
}
