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
/// Membership plan catalogue. Plans are pure price definitions: editing a plan never rewrites the
/// money already captured on existing subscriptions, which snapshot their own amounts.
/// </summary>
public sealed class MembershipPlanService : IMembershipPlanService
{
    private const string EntityName = nameof(MembershipPlan);

    private readonly GymDbContext _db;
    private readonly ICodeGeneratorService _codes;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;
    private readonly ISettingsService _settings;
    private readonly ILogger<MembershipPlanService> _logger;

    public MembershipPlanService(
        GymDbContext db,
        ICodeGeneratorService codes,
        IDateTimeProvider clock,
        ICurrentUserService currentUser,
        IAuditService audit,
        ISettingsService settings,
        ILogger<MembershipPlanService> logger)
    {
        _db = db;
        _codes = codes;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
        _settings = settings;
        _logger = logger;
    }

    // ---------------------------------------------------------------- reads

    public async Task<PagedResult<MembershipPlanDto>> GetPagedAsync(
        MembershipPlanQueryDto query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<MembershipPlan> q = _db.MembershipPlans.AsNoTracking();
        if (query.IncludeDeleted) q = q.IgnoreQueryFilters();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(p =>
                EF.Functions.Like(p.PlanCode, $"%{term}%") ||
                EF.Functions.Like(p.Name, $"%{term}%") ||
                (p.Description != null && EF.Functions.Like(p.Description, $"%{term}%")));
        }

        if (query.Status.HasValue) q = q.Where(p => p.Status == query.Status.Value);
        if (query.DurationType.HasValue) q = q.Where(p => p.DurationType == query.DurationType.Value);

        q = ApplySort(q, query.SortBy, query.SortDescending);

        var total = await q.CountAsync(ct).ConfigureAwait(false);

        var rows = await q
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(p => new
            {
                Plan = p,
                ActiveSubscriptions = p.Subscriptions.Count(s =>
                    !s.IsDeleted && s.Status == SubscriptionStatus.Active)
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var items = rows.Select(r => Map(r.Plan, r.ActiveSubscriptions)).ToList();
        return new PagedResult<MembershipPlanDto>(items, total, query.PageNumber, query.PageSize);
    }

    public async Task<List<MembershipPlanDto>> GetActiveAsync(CancellationToken ct = default)
    {
        var rows = await _db.MembershipPlans.AsNoTracking()
            .Where(p => p.Status == PlanStatus.Active)
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Name)
            .Select(p => new
            {
                Plan = p,
                ActiveSubscriptions = p.Subscriptions.Count(s =>
                    !s.IsDeleted && s.Status == SubscriptionStatus.Active)
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.Select(r => Map(r.Plan, r.ActiveSubscriptions)).ToList();
    }

    public async Task<MembershipPlanDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var row = await _db.MembershipPlans.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new
            {
                Plan = p,
                ActiveSubscriptions = p.Subscriptions.Count(s =>
                    !s.IsDeleted && s.Status == SubscriptionStatus.Active)
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (row is null) throw new NotFoundAppException(EntityName, id);
        return Map(row.Plan, row.ActiveSubscriptions);
    }

    public async Task<List<LookupDto>> GetLookupAsync(CancellationToken ct = default)
    {
        var symbol = await GetCurrencySymbolAsync(ct).ConfigureAwait(false);

        var plans = await _db.MembershipPlans.AsNoTracking()
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.PlanCode, p.Price, p.Status })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return plans.Select(p => new LookupDto
        {
            Id = p.Id,
            Name = p.Name,
            Code = p.PlanCode,
            Extra = $"{symbol}{p.Price:N2}",
            IsActive = p.Status == PlanStatus.Active
        }).ToList();
    }

    // --------------------------------------------------------------- writes

    public async Task<MembershipPlanDto> CreateAsync(MembershipPlanDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        Validate(dto);
        await EnsureUniqueNameAsync(dto.Name, null, ct).ConfigureAwait(false);

        var entity = new MembershipPlan
        {
            PlanCode = await _codes.NextPlanCodeAsync(ct).ConfigureAwait(false),
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            Features = dto.Features?.Trim(),
            DurationType = dto.DurationType,
            DurationValue = dto.DurationValue,
            Price = dto.Price,
            RegistrationFee = dto.RegistrationFee,
            TaxPercent = dto.TaxPercent,
            MaxDiscountPercent = dto.MaxDiscountPercent,
            GracePeriodDays = dto.GracePeriodDays,
            MaxFreezeDays = dto.MaxFreezeDays,
            SessionLimit = dto.SessionLimit,
            TrainerIncluded = dto.TrainerIncluded,
            DisplayOrder = dto.DisplayOrder,
            Status = dto.Status
        };

        _db.MembershipPlans.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Create, EntityName, entity.Id,
            null,
            new { entity.PlanCode, entity.Name, entity.Price, entity.DurationType, entity.DurationValue, entity.Status },
            $"Membership plan '{entity.Name}' created.", ct).ConfigureAwait(false);

        _logger.LogInformation("Membership plan {PlanCode} ({PlanId}) created by user {UserId}.",
            entity.PlanCode, entity.Id, _currentUser.UserId);

        return Map(entity, 0);
    }

    public async Task<MembershipPlanDto> UpdateAsync(MembershipPlanDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        Validate(dto);

        var entity = await _db.MembershipPlans.FirstOrDefaultAsync(p => p.Id == dto.Id, ct)
                         .ConfigureAwait(false)
                     ?? throw new NotFoundAppException(EntityName, dto.Id);

        await EnsureUniqueNameAsync(dto.Name, entity.Id, ct).ConfigureAwait(false);

        var before = new
        {
            entity.Name, entity.Price, entity.RegistrationFee, entity.TaxPercent,
            entity.MaxDiscountPercent, entity.DurationType, entity.DurationValue,
            entity.GracePeriodDays, entity.MaxFreezeDays, entity.Status
        };

        entity.Name = dto.Name.Trim();
        entity.Description = dto.Description?.Trim();
        entity.Features = dto.Features?.Trim();
        entity.DurationType = dto.DurationType;
        entity.DurationValue = dto.DurationValue;
        // Price changes affect FUTURE subscriptions only. Existing subscriptions snapshot their own
        // PlanAmount/TaxPercent/FinalAmount and are deliberately left untouched here.
        entity.Price = dto.Price;
        entity.RegistrationFee = dto.RegistrationFee;
        entity.TaxPercent = dto.TaxPercent;
        entity.MaxDiscountPercent = dto.MaxDiscountPercent;
        entity.GracePeriodDays = dto.GracePeriodDays;
        entity.MaxFreezeDays = dto.MaxFreezeDays;
        entity.SessionLimit = dto.SessionLimit;
        entity.TrainerIncluded = dto.TrainerIncluded;
        entity.DisplayOrder = dto.DisplayOrder;
        entity.Status = dto.Status;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Update, EntityName, entity.Id,
            before,
            new
            {
                entity.Name, entity.Price, entity.RegistrationFee, entity.TaxPercent,
                entity.MaxDiscountPercent, entity.DurationType, entity.DurationValue,
                entity.GracePeriodDays, entity.MaxFreezeDays, entity.Status
            },
            $"Membership plan '{entity.Name}' updated. Existing subscriptions were not repriced.",
            ct).ConfigureAwait(false);

        var activeCount = await _db.Subscriptions.AsNoTracking()
            .CountAsync(s => s.MembershipPlanId == entity.Id && s.Status == SubscriptionStatus.Active, ct)
            .ConfigureAwait(false);

        return Map(entity, activeCount);
    }

    public async Task SoftDeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await _db.MembershipPlans.FirstOrDefaultAsync(p => p.Id == id, ct).ConfigureAwait(false)
                     ?? throw new NotFoundAppException(EntityName, id);

        var blocking = await _db.Subscriptions.AsNoTracking()
            .CountAsync(s => s.MembershipPlanId == id &&
                             (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Pending),
                ct)
            .ConfigureAwait(false);

        if (blocking > 0)
            throw new BusinessRuleAppException(
                $"Plan '{entity.Name}' cannot be deleted because {blocking} active or pending subscription(s) still use it. " +
                "Set the plan to Inactive instead so it stays out of the sales screens.");

        _db.MembershipPlans.Remove(entity); // converted to a soft delete by the context
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.SoftDelete, EntityName, id,
            new { entity.PlanCode, entity.Name }, null,
            $"Membership plan '{entity.Name}' moved to the recycle bin.", ct).ConfigureAwait(false);
    }

    public async Task RestoreAsync(int id, CancellationToken ct = default)
    {
        var entity = await _db.MembershipPlans
                         .IgnoreQueryFilters()
                         .FirstOrDefaultAsync(p => p.Id == id, ct)
                         .ConfigureAwait(false)
                     ?? throw new NotFoundAppException(EntityName, id);

        if (entity.IsDeleted)
        {
            // A restored plan must not collide with a name taken while it was in the bin.
            var clash = await _db.MembershipPlans.AsNoTracking()
                .AnyAsync(p => p.Id != entity.Id && p.Name == entity.Name, ct)
                .ConfigureAwait(false);
            if (clash)
                throw new ConflictAppException(
                    $"Another membership plan named '{entity.Name}' already exists. Rename it before restoring this one.");

            entity.IsDeleted = false;
            entity.DeletedAt = null;
            entity.DeletedBy = null;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            await _audit.LogAsync(AuditActions.Restore, EntityName, id, null,
                new { entity.PlanCode, entity.Name },
                $"Membership plan '{entity.Name}' restored.", ct).ConfigureAwait(false);
        }
    }

    // -------------------------------------------------------------- helpers

    private static IQueryable<MembershipPlan> ApplySort(IQueryable<MembershipPlan> q, string? sortBy, bool desc) =>
        (sortBy?.Trim().ToLowerInvariant()) switch
        {
            "name" => desc ? q.OrderByDescending(p => p.Name) : q.OrderBy(p => p.Name),
            "price" => desc
                ? q.OrderByDescending(p => p.Price).ThenBy(p => p.Name)
                : q.OrderBy(p => p.Price).ThenBy(p => p.Name),
            "durationvalue" => desc
                ? q.OrderByDescending(p => p.DurationValue).ThenBy(p => p.Name)
                : q.OrderBy(p => p.DurationValue).ThenBy(p => p.Name),
            "displayorder" => desc
                ? q.OrderByDescending(p => p.DisplayOrder).ThenBy(p => p.Name)
                : q.OrderBy(p => p.DisplayOrder).ThenBy(p => p.Name),
            _ => q.OrderBy(p => p.DisplayOrder).ThenBy(p => p.Name)
        };

    private async Task EnsureUniqueNameAsync(string name, int? excludeId, CancellationToken ct)
    {
        var trimmed = (name ?? string.Empty).Trim();
        var exists = await _db.MembershipPlans.AsNoTracking()
            .AnyAsync(p => p.Name == trimmed && (excludeId == null || p.Id != excludeId.Value), ct)
            .ConfigureAwait(false);

        if (exists)
            throw new ConflictAppException($"A membership plan named '{trimmed}' already exists.");
    }

    private static void Validate(MembershipPlanDto dto)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(dto.Name))
            errors[nameof(dto.Name)] = new[] { "Plan name is required." };
        else if (dto.Name.Trim().Length > 120)
            errors[nameof(dto.Name)] = new[] { "Plan name cannot exceed 120 characters." };

        if (dto.Price < 0m)
            errors[nameof(dto.Price)] = new[] { "Price cannot be negative." };

        if (dto.RegistrationFee is < 0m)
            errors[nameof(dto.RegistrationFee)] = new[] { "Registration fee cannot be negative." };

        if (dto.DurationValue < 1)
            errors[nameof(dto.DurationValue)] = new[] { "Duration must be at least 1." };

        if (dto.TaxPercent is < 0m or > 100m)
            errors[nameof(dto.TaxPercent)] = new[] { "Tax percent must be between 0 and 100." };

        if (dto.MaxDiscountPercent is < 0m or > 100m)
            errors[nameof(dto.MaxDiscountPercent)] = new[] { "Maximum discount percent must be between 0 and 100." };

        if (dto.GracePeriodDays < 0)
            errors[nameof(dto.GracePeriodDays)] = new[] { "Grace period days cannot be negative." };

        if (dto.MaxFreezeDays < 0)
            errors[nameof(dto.MaxFreezeDays)] = new[] { "Maximum freeze days cannot be negative." };

        if (dto.SessionLimit is < 1)
            errors[nameof(dto.SessionLimit)] = new[] { "Session limit must be at least 1 when specified." };

        if (errors.Count > 0) throw new ValidationAppException(errors);
    }

    private async Task<string> GetCurrencySymbolAsync(CancellationToken ct)
    {
        try
        {
            var settings = await _settings.GetGymSettingsAsync(ct).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(settings.CurrencySymbol) ? "₹" : settings.CurrencySymbol;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Gym settings unavailable while building the plan lookup; using the default currency symbol.");
            return "₹";
        }
    }

    private static MembershipPlanDto Map(MembershipPlan p, int activeSubscriptionCount) => new()
    {
        Id = p.Id,
        PlanCode = p.PlanCode,
        Name = p.Name,
        Description = p.Description,
        Features = p.Features,
        DurationType = p.DurationType,
        DurationValue = p.DurationValue,
        TotalDays = p.TotalDays,
        Price = p.Price,
        RegistrationFee = p.RegistrationFee,
        TaxPercent = p.TaxPercent,
        MaxDiscountPercent = p.MaxDiscountPercent,
        GracePeriodDays = p.GracePeriodDays,
        MaxFreezeDays = p.MaxFreezeDays,
        SessionLimit = p.SessionLimit,
        TrainerIncluded = p.TrainerIncluded,
        DisplayOrder = p.DisplayOrder,
        Status = p.Status,
        ActiveSubscriptionCount = activeSubscriptionCount
    };
}
