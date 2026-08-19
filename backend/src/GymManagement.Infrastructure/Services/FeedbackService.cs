using System.Linq.Expressions;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Services;

/// <summary>
/// Member suggestions, complaints and ratings. Staff holding <c>feedback.view</c> see every row;
/// a member sees and submits only their own, which is enforced here rather than in the controller.
/// </summary>
public sealed class FeedbackService : IFeedbackService
{
    private readonly GymDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;
    private readonly ILogger<FeedbackService> _logger;

    public FeedbackService(
        GymDbContext db,
        IDateTimeProvider clock,
        ICurrentUserService currentUser,
        IAuditService audit,
        ILogger<FeedbackService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private static readonly Expression<Func<Feedback, FeedbackDetailDto>> ToDetailDto = f => new FeedbackDetailDto
    {
        Id = f.Id,
        MemberId = f.MemberId,
        MemberName = f.Member != null ? f.Member.FullName : string.Empty,
        MemberCode = f.Member != null ? f.Member.MemberCode : null,
        Subject = f.Subject,
        Message = f.Message,
        Rating = f.Rating,
        Status = f.Status,
        IsPrivate = f.IsPrivate,
        HasResponse = f.AdminResponse != null,
        AdminResponse = f.AdminResponse,
        RespondedByUserId = f.RespondedByUserId,
        RespondedByUserName = f.RespondedByUser != null ? f.RespondedByUser.FullName : null,
        RespondedAt = f.RespondedAt,
        CreatedAt = f.CreatedAt,
        UpdatedAt = f.UpdatedAt,
        IsDeleted = f.IsDeleted
    };

    private static readonly Expression<Func<Feedback, FeedbackListDto>> ToListDto = f => new FeedbackListDto
    {
        Id = f.Id,
        MemberId = f.MemberId,
        MemberName = f.Member != null ? f.Member.FullName : string.Empty,
        MemberCode = f.Member != null ? f.Member.MemberCode : null,
        Subject = f.Subject,
        Rating = f.Rating,
        Status = f.Status,
        IsPrivate = f.IsPrivate,
        HasResponse = f.AdminResponse != null,
        CreatedAt = f.CreatedAt,
        IsDeleted = f.IsDeleted
    };

    public async Task<PagedResult<FeedbackListDto>> GetPagedAsync(FeedbackQueryDto query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // A caller without the staff view permission is limited to their own member record.
        var ownMemberId = ResolveScope();

        var q = (query.IncludeDeleted && ownMemberId is null
                ? _db.Feedback.IgnoreQueryFilters()
                : _db.Feedback.AsQueryable())
            .AsNoTracking();

        if (ownMemberId is not null) q = q.Where(f => f.MemberId == ownMemberId);
        else if (query.MemberId is > 0) q = q.Where(f => f.MemberId == query.MemberId);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            q = q.Where(f =>
                (f.Subject != null && EF.Functions.Like(f.Subject, pattern)) ||
                EF.Functions.Like(f.Message, pattern) ||
                (f.Member != null && EF.Functions.Like(f.Member.FullName, pattern)) ||
                (f.Member != null && EF.Functions.Like(f.Member.MemberCode, pattern)));
        }

        if (query.Status is not null) q = q.Where(f => f.Status == query.Status);
        if (query.MinRating is not null) q = q.Where(f => f.Rating >= query.MinRating);
        if (query.MaxRating is not null) q = q.Where(f => f.Rating <= query.MaxRating);

        if (query.FromDate is not null)
        {
            var from = query.FromDate.Value.Date;
            q = q.Where(f => f.CreatedAt >= from);
        }

        if (query.ToDate is not null)
        {
            var toExclusive = query.ToDate.Value.Date.AddDays(1);
            q = q.Where(f => f.CreatedAt < toExclusive);
        }

        var total = await q.CountAsync(ct).ConfigureAwait(false);

        q = (query.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "rating" => query.SortDescending
                ? q.OrderByDescending(f => f.Rating).ThenByDescending(f => f.Id)
                : q.OrderBy(f => f.Rating).ThenBy(f => f.Id),
            "status" => query.SortDescending
                ? q.OrderByDescending(f => f.Status).ThenByDescending(f => f.Id)
                : q.OrderBy(f => f.Status).ThenBy(f => f.Id),
            "subject" => query.SortDescending
                ? q.OrderByDescending(f => f.Subject).ThenByDescending(f => f.Id)
                : q.OrderBy(f => f.Subject).ThenBy(f => f.Id),
            "createdat" => query.SortDescending
                ? q.OrderByDescending(f => f.CreatedAt).ThenByDescending(f => f.Id)
                : q.OrderBy(f => f.CreatedAt).ThenBy(f => f.Id),
            _ => q.OrderByDescending(f => f.CreatedAt).ThenByDescending(f => f.Id)
        };

        var items = await q
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(ToListDto)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedResult<FeedbackListDto>(items, total, query.PageNumber, query.PageSize);
    }

    public async Task<PagedResult<FeedbackListDto>> GetMineAsync(PagedRequest query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var memberId = _currentUser.MemberId
                       ?? throw new ForbiddenAppException("Only a member account can read its own feedback.");

        return await GetPagedAsync(new FeedbackQueryDto
        {
            MemberId = memberId,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize,
            Search = query.Search,
            SortBy = query.SortBy,
            SortDescending = query.SortDescending
        }, ct).ConfigureAwait(false);
    }

    public async Task<FeedbackDetailDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var dto = await _db.Feedback
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(f => f.Id == id)
            .Select(ToDetailDto)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (dto is null) throw new NotFoundAppException("Feedback", id);

        var ownMemberId = ResolveScope();
        if (ownMemberId is not null && dto.MemberId != ownMemberId)
            throw new ForbiddenAppException("You may only view your own feedback.");

        return dto;
    }

    public async Task<FeedbackDetailDto> SaveAsync(SaveFeedbackDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var message = Normalize(dto.Message)
                      ?? throw new ValidationAppException(nameof(dto.Message), "A message is required.");

        ValidateRating(dto.Rating);

        var memberId = await RequireMemberAsync(dto.MemberId, ct).ConfigureAwait(false);

        var isNew = dto.Id <= 0;
        Feedback entity;
        object? oldValues = null;

        if (isNew)
        {
            entity = new Feedback();
            _db.Feedback.Add(entity);
        }
        else
        {
            entity = await _db.Feedback
                         .FirstOrDefaultAsync(f => f.Id == dto.Id, ct)
                         .ConfigureAwait(false)
                     ?? throw new NotFoundAppException("Feedback", dto.Id);

            oldValues = new { entity.Subject, entity.Message, entity.Rating, entity.Status, entity.IsPrivate };
        }

        entity.MemberId = memberId;
        entity.Subject = Normalize(dto.Subject);
        entity.Message = message;
        entity.Rating = dto.Rating;
        entity.Status = dto.Status;
        entity.IsPrivate = dto.IsPrivate;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(isNew ? AuditActions.Create : AuditActions.Update, nameof(Feedback), entity.Id,
            oldValues,
            new { entity.MemberId, entity.Subject, entity.Rating, entity.Status },
            $"Feedback #{entity.Id} for member {entity.MemberId} {(isNew ? "created" : "updated")}.",
            ct).ConfigureAwait(false);

        return await LoadDetailAsync(entity.Id, ct).ConfigureAwait(false);
    }

    public async Task<FeedbackDetailDto> SubmitAsync(CreateFeedbackDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var message = Normalize(dto.Message)
                      ?? throw new ValidationAppException(nameof(dto.Message), "A message is required.");

        ValidateRating(dto.Rating);

        // A member always posts as themselves; staff with the manage permission may file on behalf.
        var memberId = _currentUser.MemberId;
        if (memberId is null)
        {
            if (!_currentUser.HasPermission(Permissions.FeedbackManage) || dto.MemberId is not > 0)
                throw new ForbiddenAppException("Only a member account can submit feedback.");

            memberId = await RequireMemberAsync(dto.MemberId.Value, ct).ConfigureAwait(false);
        }
        else
        {
            memberId = await RequireMemberAsync(memberId.Value, ct).ConfigureAwait(false);
        }

        var entity = new Feedback
        {
            MemberId = memberId.Value,
            Subject = Normalize(dto.Subject),
            Message = message,
            Rating = dto.Rating,
            Status = FeedbackStatus.New,
            IsPrivate = dto.IsPrivate
        };

        _db.Feedback.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("Member {MemberId} submitted feedback {FeedbackId}.", entity.MemberId, entity.Id);

        await _audit.LogAsync(AuditActions.Create, nameof(Feedback), entity.Id, null,
            new { entity.MemberId, entity.Subject, entity.Rating },
            $"Feedback #{entity.Id} submitted by member {entity.MemberId}.", ct).ConfigureAwait(false);

        return await LoadDetailAsync(entity.Id, ct).ConfigureAwait(false);
    }

    public async Task<FeedbackDetailDto> RespondAsync(int id, string response, CancellationToken ct = default)
    {
        var text = Normalize(response)
                   ?? throw new ValidationAppException(nameof(response), "A response is required.");

        var entity = await _db.Feedback
                         .FirstOrDefaultAsync(f => f.Id == id, ct)
                         .ConfigureAwait(false)
                     ?? throw new NotFoundAppException("Feedback", id);

        var oldValues = new { entity.Status, entity.AdminResponse };

        entity.AdminResponse = text;
        entity.RespondedByUserId = _currentUser.UserId;
        entity.RespondedAt = _clock.UtcNow;
        if (entity.Status == FeedbackStatus.New) entity.Status = FeedbackStatus.Reviewed;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Update, nameof(Feedback), entity.Id, oldValues,
            new { entity.Status, entity.RespondedByUserId, entity.RespondedAt },
            $"Feedback #{entity.Id} answered.", ct).ConfigureAwait(false);

        return await LoadDetailAsync(entity.Id, ct).ConfigureAwait(false);
    }

    public async Task SoftDeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await _db.Feedback
                         .FirstOrDefaultAsync(f => f.Id == id, ct)
                         .ConfigureAwait(false)
                     ?? throw new NotFoundAppException("Feedback", id);

        _db.Feedback.Remove(entity); // The context turns this into a soft delete.
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.SoftDelete, nameof(Feedback), entity.Id,
            new { entity.MemberId, entity.Subject, entity.Status }, null,
            $"Feedback #{entity.Id} moved to the recycle bin.", ct).ConfigureAwait(false);
    }

    public async Task RestoreAsync(int id, CancellationToken ct = default)
    {
        var entity = await _db.Feedback
                         .IgnoreQueryFilters()
                         .FirstOrDefaultAsync(f => f.Id == id, ct)
                         .ConfigureAwait(false)
                     ?? throw new NotFoundAppException("Feedback", id);

        if (!entity.IsDeleted) return;

        entity.IsDeleted = false;
        entity.DeletedAt = null;
        entity.DeletedBy = null;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Restore, nameof(Feedback), entity.Id, null,
            new { entity.MemberId, entity.Subject },
            $"Feedback #{entity.Id} restored from the recycle bin.", ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the member id a caller is restricted to, or null when the caller is staff holding
    /// <c>feedback.view</c> and may therefore see every row.
    /// </summary>
    private int? ResolveScope()
    {
        if (_currentUser.HasPermission(Permissions.FeedbackView)) return null;

        return _currentUser.MemberId
               ?? throw new ForbiddenAppException("You do not have permission to read feedback.");
    }

    private async Task<int> RequireMemberAsync(int memberId, CancellationToken ct)
    {
        if (memberId <= 0)
            throw new ValidationAppException(nameof(memberId), "A member must be selected.");

        var exists = await _db.Members
            .AsNoTracking()
            .AnyAsync(m => m.Id == memberId, ct)
            .ConfigureAwait(false);

        if (!exists) throw new NotFoundAppException("Member", memberId);

        return memberId;
    }

    /// <summary>Reads a row back without applying the caller scope, for use right after a write.</summary>
    private async Task<FeedbackDetailDto> LoadDetailAsync(int id, CancellationToken ct)
    {
        var dto = await _db.Feedback
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(f => f.Id == id)
            .Select(ToDetailDto)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return dto ?? throw new NotFoundAppException("Feedback", id);
    }

    private static void ValidateRating(int? rating)
    {
        if (rating is not null && rating is < 1 or > 5)
            throw new ValidationAppException(nameof(rating), "A rating must be between 1 and 5.");
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
