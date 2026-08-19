using System.Linq.Expressions;
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
/// Walk-in, phone and website leads. An enquiry stays a lead until it is linked to the member
/// record it produced, at which point it is marked Converted.
/// </summary>
public sealed class EnquiryService : IEnquiryService
{
    private readonly GymDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;
    private readonly ILogger<EnquiryService> _logger;

    public EnquiryService(
        GymDbContext db,
        IDateTimeProvider clock,
        ICurrentUserService currentUser,
        IAuditService audit,
        ILogger<EnquiryService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private static readonly Expression<Func<Enquiry, EnquiryDetailDto>> ToDetailDto = e => new EnquiryDetailDto
    {
        Id = e.Id,
        FullName = e.FullName,
        Phone = e.Phone,
        Email = e.Email,
        Source = e.Source,
        Status = e.Status,
        InterestedPlanId = e.InterestedPlanId,
        InterestedPlanName = e.InterestedPlan != null ? e.InterestedPlan.Name : null,
        FollowUpDate = e.FollowUpDate,
        AssignedToUserId = e.AssignedToUserId,
        AssignedToUserName = e.AssignedToUser != null ? e.AssignedToUser.FullName : null,
        ConvertedMemberId = e.ConvertedMemberId,
        ConvertedMemberName = e.ConvertedMember != null ? e.ConvertedMember.FullName : null,
        Message = e.Message,
        Notes = e.Notes,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
        IsDeleted = e.IsDeleted
    };

    private static readonly Expression<Func<Enquiry, EnquiryListDto>> ToListDto = e => new EnquiryListDto
    {
        Id = e.Id,
        FullName = e.FullName,
        Phone = e.Phone,
        Email = e.Email,
        Source = e.Source,
        Status = e.Status,
        InterestedPlanId = e.InterestedPlanId,
        InterestedPlanName = e.InterestedPlan != null ? e.InterestedPlan.Name : null,
        FollowUpDate = e.FollowUpDate,
        AssignedToUserId = e.AssignedToUserId,
        AssignedToUserName = e.AssignedToUser != null ? e.AssignedToUser.FullName : null,
        ConvertedMemberId = e.ConvertedMemberId,
        ConvertedMemberName = e.ConvertedMember != null ? e.ConvertedMember.FullName : null,
        CreatedAt = e.CreatedAt,
        IsDeleted = e.IsDeleted
    };

    public async Task<PagedResult<EnquiryListDto>> GetPagedAsync(EnquiryQueryDto query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var q = (query.IncludeDeleted ? _db.Enquiries.IgnoreQueryFilters() : _db.Enquiries.AsQueryable())
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            q = q.Where(e =>
                EF.Functions.Like(e.FullName, pattern) ||
                EF.Functions.Like(e.Phone, pattern) ||
                (e.Email != null && EF.Functions.Like(e.Email, pattern)) ||
                (e.Message != null && EF.Functions.Like(e.Message, pattern)));
        }

        if (query.Status is not null) q = q.Where(e => e.Status == query.Status);
        if (query.Source is not null) q = q.Where(e => e.Source == query.Source);
        if (query.AssignedToUserId is > 0) q = q.Where(e => e.AssignedToUserId == query.AssignedToUserId);
        if (query.InterestedPlanId is > 0) q = q.Where(e => e.InterestedPlanId == query.InterestedPlanId);

        if (query.FromDate is not null)
        {
            var from = query.FromDate.Value.Date;
            q = q.Where(e => e.CreatedAt >= from);
        }

        if (query.ToDate is not null)
        {
            var toExclusive = query.ToDate.Value.Date.AddDays(1);
            q = q.Where(e => e.CreatedAt < toExclusive);
        }

        if (query.FollowUpDue == true)
        {
            var today = _clock.Today.Date;
            q = q.Where(e => e.FollowUpDate != null && e.FollowUpDate <= today &&
                             e.Status != EnquiryStatus.Converted && e.Status != EnquiryStatus.Lost);
        }

        var total = await q.CountAsync(ct).ConfigureAwait(false);

        q = (query.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "fullname" => query.SortDescending
                ? q.OrderByDescending(e => e.FullName).ThenByDescending(e => e.Id)
                : q.OrderBy(e => e.FullName).ThenBy(e => e.Id),
            "status" => query.SortDescending
                ? q.OrderByDescending(e => e.Status).ThenByDescending(e => e.Id)
                : q.OrderBy(e => e.Status).ThenBy(e => e.Id),
            "source" => query.SortDescending
                ? q.OrderByDescending(e => e.Source).ThenByDescending(e => e.Id)
                : q.OrderBy(e => e.Source).ThenBy(e => e.Id),
            "followupdate" => query.SortDescending
                ? q.OrderByDescending(e => e.FollowUpDate).ThenByDescending(e => e.Id)
                : q.OrderBy(e => e.FollowUpDate).ThenBy(e => e.Id),
            "createdat" => query.SortDescending
                ? q.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
                : q.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id),
            _ => q.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
        };

        var items = await q
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(ToListDto)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedResult<EnquiryListDto>(items, total, query.PageNumber, query.PageSize);
    }

    public async Task<EnquiryDetailDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var dto = await _db.Enquiries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e => e.Id == id)
            .Select(ToDetailDto)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return dto ?? throw new NotFoundAppException("Enquiry", id);
    }

    public async Task<EnquiryDetailDto> SaveAsync(SaveEnquiryDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var fullName = Normalize(dto.FullName)
                       ?? throw new ValidationAppException(nameof(dto.FullName), "A name is required.");

        var phone = Normalize(dto.Phone)
                    ?? throw new ValidationAppException(nameof(dto.Phone), "A phone number is required.");

        var email = Normalize(dto.Email);
        if (email is not null && !email.Contains('@'))
            throw new ValidationAppException(nameof(dto.Email), "Enter a valid email address.");

        if (dto.InterestedPlanId is > 0)
        {
            var planExists = await _db.MembershipPlans
                .AsNoTracking()
                .AnyAsync(p => p.Id == dto.InterestedPlanId, ct)
                .ConfigureAwait(false);

            if (!planExists) throw new NotFoundAppException("MembershipPlan", dto.InterestedPlanId.Value);
        }

        if (dto.AssignedToUserId is > 0)
        {
            var userExists = await _db.Users
                .AsNoTracking()
                .AnyAsync(u => u.Id == dto.AssignedToUserId, ct)
                .ConfigureAwait(false);

            if (!userExists) throw new NotFoundAppException("User", dto.AssignedToUserId.Value);
        }

        var isNew = dto.Id <= 0;
        Enquiry entity;
        object? oldValues = null;

        if (isNew)
        {
            entity = new Enquiry();
            _db.Enquiries.Add(entity);
        }
        else
        {
            entity = await _db.Enquiries
                         .FirstOrDefaultAsync(e => e.Id == dto.Id, ct)
                         .ConfigureAwait(false)
                     ?? throw new NotFoundAppException("Enquiry", dto.Id);

            oldValues = new
            {
                entity.FullName,
                entity.Phone,
                entity.Status,
                entity.FollowUpDate,
                entity.AssignedToUserId
            };

            if (entity.Status == EnquiryStatus.Converted && dto.Status != EnquiryStatus.Converted)
            {
                throw new BusinessRuleAppException(
                    "A converted enquiry cannot be moved back to another status.");
            }
        }

        entity.FullName = fullName;
        entity.Phone = phone;
        entity.Email = email;
        entity.Source = dto.Source;
        entity.InterestedPlanId = dto.InterestedPlanId is > 0 ? dto.InterestedPlanId : null;
        entity.Message = Normalize(dto.Message);
        entity.Status = dto.Status;
        entity.FollowUpDate = dto.FollowUpDate?.Date;
        entity.AssignedToUserId = dto.AssignedToUserId is > 0 ? dto.AssignedToUserId : _currentUser.UserId;
        entity.Notes = Normalize(dto.Notes);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(isNew ? AuditActions.Create : AuditActions.Update, nameof(Enquiry), entity.Id,
            oldValues,
            new { entity.FullName, entity.Phone, entity.Source, entity.Status, entity.FollowUpDate },
            $"Enquiry from {entity.FullName} {(isNew ? "recorded" : "updated")}.",
            ct).ConfigureAwait(false);

        return await GetByIdAsync(entity.Id, ct).ConfigureAwait(false);
    }

    public async Task SoftDeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await _db.Enquiries
                         .FirstOrDefaultAsync(e => e.Id == id, ct)
                         .ConfigureAwait(false)
                     ?? throw new NotFoundAppException("Enquiry", id);

        _db.Enquiries.Remove(entity); // The context turns this into a soft delete.
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.SoftDelete, nameof(Enquiry), entity.Id,
            new { entity.FullName, entity.Phone, entity.Status }, null,
            $"Enquiry from {entity.FullName} moved to the recycle bin.", ct).ConfigureAwait(false);
    }

    public async Task RestoreAsync(int id, CancellationToken ct = default)
    {
        var entity = await _db.Enquiries
                         .IgnoreQueryFilters()
                         .FirstOrDefaultAsync(e => e.Id == id, ct)
                         .ConfigureAwait(false)
                     ?? throw new NotFoundAppException("Enquiry", id);

        if (!entity.IsDeleted) return;

        entity.IsDeleted = false;
        entity.DeletedAt = null;
        entity.DeletedBy = null;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Restore, nameof(Enquiry), entity.Id, null,
            new { entity.FullName, entity.Phone },
            $"Enquiry from {entity.FullName} restored from the recycle bin.", ct).ConfigureAwait(false);
    }

    public async Task<EnquiryDetailDto> ConvertToMemberAsync(int enquiryId, int memberId,
        CancellationToken ct = default)
    {
        var entity = await _db.Enquiries
                         .FirstOrDefaultAsync(e => e.Id == enquiryId, ct)
                         .ConfigureAwait(false)
                     ?? throw new NotFoundAppException("Enquiry", enquiryId);

        if (entity.Status == EnquiryStatus.Converted && entity.ConvertedMemberId is not null)
        {
            throw new ConflictAppException(
                $"This enquiry was already converted to member {entity.ConvertedMemberId}.");
        }

        var member = await _db.Members
                         .AsNoTracking()
                         .Where(m => m.Id == memberId)
                         .Select(m => new { m.Id, m.FullName, m.MemberCode })
                         .FirstOrDefaultAsync(ct)
                         .ConfigureAwait(false)
                     ?? throw new NotFoundAppException("Member", memberId);

        var oldValues = new { entity.Status, entity.ConvertedMemberId };

        entity.ConvertedMemberId = member.Id;
        entity.Status = EnquiryStatus.Converted;
        entity.FollowUpDate = null;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("Enquiry {EnquiryId} converted to member {MemberCode}.",
            entity.Id, member.MemberCode);

        await _audit.LogAsync(AuditActions.Update, nameof(Enquiry), entity.Id, oldValues,
            new { entity.Status, entity.ConvertedMemberId },
            $"Enquiry from {entity.FullName} converted to member {member.MemberCode} ({member.FullName}).",
            ct).ConfigureAwait(false);

        return await GetByIdAsync(entity.Id, ct).ConfigureAwait(false);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
