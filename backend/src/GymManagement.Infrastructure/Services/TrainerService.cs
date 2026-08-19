using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Services;

/// <summary>Trainer records, member assignment and workload reporting.</summary>
public sealed class TrainerService : ITrainerService
{
    private readonly GymDbContext _db;
    private readonly ICodeGeneratorService _codes;
    private readonly IPasswordHasher _hasher;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;
    private readonly ILogger<TrainerService> _logger;

    public TrainerService(
        GymDbContext db,
        ICodeGeneratorService codes,
        IPasswordHasher hasher,
        IDateTimeProvider clock,
        ICurrentUserService currentUser,
        IAuditService audit,
        ILogger<TrainerService> logger)
    {
        _db = db;
        _codes = codes;
        _hasher = hasher;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
        _logger = logger;
    }

    // ---------------------------------------------------------------- queries

    public async Task<PagedResult<TrainerListDto>> GetPagedAsync(TrainerQueryDto query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var trainers = _db.Trainers.AsNoTracking();
        if (query.IncludeDeleted) trainers = trainers.IgnoreQueryFilters();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            trainers = trainers.Where(t =>
                t.TrainerCode.Contains(term) ||
                t.FullName.Contains(term) ||
                t.Phone.Contains(term) ||
                (t.Email != null && t.Email.Contains(term)) ||
                (t.Specialization != null && t.Specialization.Contains(term)));
        }

        if (query.Status.HasValue) trainers = trainers.Where(t => t.Status == query.Status.Value);

        if (!string.IsNullOrWhiteSpace(query.Specialization))
        {
            var spec = query.Specialization.Trim();
            trainers = trainers.Where(t => t.Specialization != null && t.Specialization.Contains(spec));
        }

        var total = await trainers.CountAsync(ct).ConfigureAwait(false);
        if (total == 0)
            return new PagedResult<TrainerListDto>(Array.Empty<TrainerListDto>(), 0, query.PageNumber, query.PageSize);

        var desc = query.SortDescending;
        trainers = (query.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "trainercode" => desc ? trainers.OrderByDescending(t => t.TrainerCode) : trainers.OrderBy(t => t.TrainerCode),
            "joiningdate" => desc
                ? trainers.OrderByDescending(t => t.JoiningDate).ThenBy(t => t.FullName)
                : trainers.OrderBy(t => t.JoiningDate).ThenBy(t => t.FullName),
            "status" => desc
                ? trainers.OrderByDescending(t => t.Status).ThenBy(t => t.FullName)
                : trainers.OrderBy(t => t.Status).ThenBy(t => t.FullName),
            _ => desc ? trainers.OrderByDescending(t => t.FullName) : trainers.OrderBy(t => t.FullName)
        };

        var items = await trainers
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(t => new TrainerListDto
            {
                Id = t.Id,
                TrainerCode = t.TrainerCode,
                FullName = t.FullName,
                Gender = t.Gender,
                Phone = t.Phone,
                Email = t.Email,
                Specialization = t.Specialization,
                ExperienceYears = t.ExperienceYears,
                JoiningDate = t.JoiningDate,
                Status = t.Status,
                PhotoPath = t.PhotoPath
            })
            .ToListAsync(ct).ConfigureAwait(false);

        // Bounded second query: assigned member counts for this page only.
        var ids = items.Select(i => i.Id).ToList();
        var counts = await _db.Members.AsNoTracking()
            .Where(m => m.AssignedTrainerId != null && ids.Contains(m.AssignedTrainerId.Value))
            .GroupBy(m => m.AssignedTrainerId!.Value)
            .Select(g => new { TrainerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TrainerId, x => x.Count, ct).ConfigureAwait(false);

        foreach (var item in items)
            item.AssignedMemberCount = counts.TryGetValue(item.Id, out var c) ? c : 0;

        return new PagedResult<TrainerListDto>(items, total, query.PageNumber, query.PageSize);
    }

    public async Task<TrainerDetailDto> GetByIdAsync(int id, CancellationToken ct = default)
        => await LoadDetailAsync(id, ct).ConfigureAwait(false) ?? throw new NotFoundAppException("Trainer", id);

    public async Task<List<LookupDto>> GetLookupAsync(bool onlyActive = true, CancellationToken ct = default)
    {
        var q = _db.Trainers.AsNoTracking();
        if (onlyActive) q = q.Where(t => t.Status == TrainerStatus.Active);

        return await q
            .OrderBy(t => t.FullName)
            .Select(t => new LookupDto
            {
                Id = t.Id,
                Name = t.FullName,
                Code = t.TrainerCode,
                Extra = t.Specialization,
                IsActive = t.Status == TrainerStatus.Active
            })
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<PagedResult<MemberListDto>> GetAssignedMembersAsync(int trainerId, PagedRequest query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var exists = await _db.Trainers.AsNoTracking().AnyAsync(t => t.Id == trainerId, ct).ConfigureAwait(false);
        if (!exists) throw new NotFoundAppException("Trainer", trainerId);

        var today = _clock.Today.Date;

        var members = _db.Members.AsNoTracking().Where(m => m.AssignedTrainerId == trainerId);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            members = members.Where(m =>
                m.MemberCode.Contains(term) ||
                m.FullName.Contains(term) ||
                m.Phone.Contains(term) ||
                (m.Email != null && m.Email.Contains(term)));
        }

        var total = await members.CountAsync(ct).ConfigureAwait(false);
        if (total == 0)
            return new PagedResult<MemberListDto>(Array.Empty<MemberListDto>(), 0, query.PageNumber, query.PageSize);

        var shaped = members.Select(m => new
        {
            m.Id,
            m.MemberCode,
            m.FullName,
            m.Gender,
            m.Phone,
            m.Email,
            m.JoiningDate,
            m.Status,
            m.ProfilePhotoPath,
            TrainerName = m.AssignedTrainer != null ? m.AssignedTrainer.FullName : null,
            CurrentPlanName = m.Subscriptions
                .OrderByDescending(s => s.Status == SubscriptionStatus.Active)
                .ThenByDescending(s => s.EndDate)
                .ThenByDescending(s => s.Id)
                .Select(s => s.MembershipPlan != null ? s.MembershipPlan.Name : null)
                .FirstOrDefault(),
            CurrentEndDate = m.Subscriptions
                .OrderByDescending(s => s.Status == SubscriptionStatus.Active)
                .ThenByDescending(s => s.EndDate)
                .ThenByDescending(s => s.Id)
                .Select(s => (DateTime?)s.EndDate)
                .FirstOrDefault(),
            CurrentStatus = m.Subscriptions
                .OrderByDescending(s => s.Status == SubscriptionStatus.Active)
                .ThenByDescending(s => s.EndDate)
                .ThenByDescending(s => s.Id)
                .Select(s => (SubscriptionStatus?)s.Status)
                .FirstOrDefault(),
            Outstanding = m.Subscriptions
                .Where(s => s.Status != SubscriptionStatus.Cancelled && s.FinalAmount > s.PaidAmount)
                .Sum(s => (decimal?)(s.FinalAmount - s.PaidAmount)) ?? 0m
        });

        var desc = query.SortDescending;
        shaped = (query.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "membercode" => desc ? shaped.OrderByDescending(x => x.MemberCode) : shaped.OrderBy(x => x.MemberCode),
            "joiningdate" => desc ? shaped.OrderByDescending(x => x.JoiningDate) : shaped.OrderBy(x => x.JoiningDate),
            "status" => desc ? shaped.OrderByDescending(x => x.Status) : shaped.OrderBy(x => x.Status),
            "subscriptionenddate" => desc
                ? shaped.OrderByDescending(x => x.CurrentEndDate)
                : shaped.OrderBy(x => x.CurrentEndDate),
            _ => desc ? shaped.OrderByDescending(x => x.FullName) : shaped.OrderBy(x => x.FullName)
        };

        var rows = await shaped.Skip(query.Skip).Take(query.PageSize)
            .ToListAsync(ct).ConfigureAwait(false);

        var items = rows.Select(x =>
        {
            var days = x.CurrentEndDate.HasValue ? (int?)(x.CurrentEndDate.Value.Date - today).Days : null;
            return new MemberListDto
            {
                Id = x.Id,
                MemberCode = x.MemberCode,
                FullName = x.FullName,
                Gender = x.Gender,
                Phone = x.Phone,
                Email = x.Email,
                JoiningDate = x.JoiningDate,
                Status = x.Status,
                ProfilePhotoPath = x.ProfilePhotoPath,
                AssignedTrainerName = x.TrainerName,
                CurrentPlanName = x.CurrentPlanName,
                SubscriptionEndDate = x.CurrentEndDate,
                DaysRemaining = days,
                SubscriptionStatus = x.CurrentStatus,
                OutstandingAmount = x.Outstanding,
                IsExpiringSoon = x.CurrentStatus == SubscriptionStatus.Active && days is >= 0 and <= 7
            };
        }).ToList();

        return new PagedResult<MemberListDto>(items, total, query.PageNumber, query.PageSize);
    }

    public async Task<List<TrainerWorkloadDto>> GetWorkloadAsync(DateTime? from, DateTime? to,
        CancellationToken ct = default)
    {
        var today = _clock.Today.Date;
        var fromDate = (from ?? new DateTime(today.Year, today.Month, 1)).Date;
        var toDate = (to ?? new DateTime(today.Year, today.Month, 1).AddMonths(1).AddDays(-1)).Date;
        if (fromDate > toDate) (fromDate, toDate) = (toDate, fromDate);

        var trainers = await _db.Trainers.AsNoTracking()
            .Where(t => t.Status == TrainerStatus.Active)
            .OrderBy(t => t.FullName)
            .Select(t => new { t.Id, t.FullName, t.Specialization })
            .ToListAsync(ct).ConfigureAwait(false);

        if (trainers.Count == 0) return new List<TrainerWorkloadDto>();

        var memberCounts = await _db.Members.AsNoTracking()
            .Where(m => m.AssignedTrainerId != null)
            .GroupBy(m => m.AssignedTrainerId!.Value)
            .Select(g => new { TrainerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TrainerId, x => x.Count, ct).ConfigureAwait(false);

        var planCounts = await _db.MemberWorkoutPlans.AsNoTracking()
            .Where(p => p.TrainerId != null && p.IsActive
                        && (p.EndDate == null || p.EndDate >= today))
            .GroupBy(p => p.TrainerId!.Value)
            .Select(g => new { TrainerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TrainerId, x => x.Count, ct).ConfigureAwait(false);

        var sessionStats = await _db.WorkoutSessions.AsNoTracking()
            .Where(s => s.TrainerId != null && s.SessionDate >= fromDate && s.SessionDate <= toDate)
            .GroupBy(s => s.TrainerId!.Value)
            .Select(g => new
            {
                TrainerId = g.Key,
                Sessions = g.Count(),
                Minutes = g.Sum(s => (decimal?)(s.DurationMinutes ?? 0)) ?? 0m
            })
            .ToDictionaryAsync(x => x.TrainerId, x => x, ct).ConfigureAwait(false);

        // Distinct members trained needs its own grouped query; DISTINCT inside an aggregate is
        // not translatable.
        var distinctMembers = await _db.WorkoutSessions.AsNoTracking()
            .Where(s => s.TrainerId != null && s.SessionDate >= fromDate && s.SessionDate <= toDate)
            .Select(s => new { TrainerId = s.TrainerId!.Value, s.MemberId })
            .Distinct()
            .GroupBy(x => x.TrainerId)
            .Select(g => new { TrainerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TrainerId, x => x.Count, ct).ConfigureAwait(false);

        return trainers.Select(t => new TrainerWorkloadDto
        {
            TrainerId = t.Id,
            TrainerName = t.FullName,
            Specialization = t.Specialization,
            AssignedMembers = memberCounts.TryGetValue(t.Id, out var mc) ? mc : 0,
            ActiveWorkoutPlans = planCounts.TryGetValue(t.Id, out var pc) ? pc : 0,
            SessionsInPeriod = sessionStats.TryGetValue(t.Id, out var ss) ? ss.Sessions : 0,
            DistinctMembersTrained = distinctMembers.TryGetValue(t.Id, out var dm) ? dm : 0,
            TotalSessionMinutes = sessionStats.TryGetValue(t.Id, out var ms) ? ms.Minutes : 0m
        }).ToList();
    }

    // ---------------------------------------------------------------- writes

    public async Task<(TrainerDetailDto Trainer, TemporaryPasswordDto? Account)> CreateAsync(
        CreateTrainerDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var errors = new Dictionary<string, string[]>();
        var fullName = (dto.FullName ?? string.Empty).Trim();
        var phone = (dto.Phone ?? string.Empty).Trim();
        var email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();

        if (fullName.Length == 0) errors[nameof(dto.FullName)] = new[] { "Full name is required." };
        if (phone.Length == 0) errors[nameof(dto.Phone)] = new[] { "Phone number is required." };

        var today = _clock.Today.Date;
        if (dto.JoiningDate.Date > today.AddDays(1))
            errors[nameof(dto.JoiningDate)] = new[] { "Joining date cannot be more than one day in the future." };

        if (dto.ExperienceYears is < 0 or > 70)
            errors[nameof(dto.ExperienceYears)] = new[] { "Experience must be between 0 and 70 years." };

        if (dto.CommissionPercent is < 0m or > 100m)
            errors[nameof(dto.CommissionPercent)] = new[] { "Commission must be between 0 and 100 percent." };

        if (dto.CreateUserAccount && email is null)
            errors["Email"] = new[] { "An email address is required to create a login account." };

        if (errors.Count > 0) throw new ValidationAppException(errors);

        var duplicate = await _db.Trainers.AsNoTracking()
            .AnyAsync(t => t.Phone == phone && t.Status != TrainerStatus.Resigned, ct).ConfigureAwait(false);
        if (duplicate)
            throw new ConflictAppException($"A trainer with phone number '{phone}' already exists.");

        var trainer = new Trainer
        {
            TrainerCode = await _codes.NextTrainerCodeAsync(ct).ConfigureAwait(false),
            FullName = fullName,
            Gender = dto.Gender,
            Phone = phone,
            Email = email,
            Address = dto.Address,
            Specialization = dto.Specialization,
            Certifications = dto.Certifications,
            ExperienceYears = dto.ExperienceYears,
            JoiningDate = dto.JoiningDate.Date,
            MonthlySalary = dto.MonthlySalary,
            CommissionPercent = dto.CommissionPercent,
            Status = TrainerStatus.Active,
            PhotoPath = dto.PhotoPath,
            Notes = dto.Notes
        };

        TemporaryPasswordDto? account = null;

        await using var tx = await BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            _db.Trainers.Add(trainer);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            if (dto.CreateUserAccount && email is not null)
            {
                account = await CreateLinkedUserAsync(trainer.TrainerCode, trainer.FullName, email,
                    trainer.Phone, RoleNames.Trainer, trainer.Id, ct).ConfigureAwait(false);
            }

            if (tx is not null) await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }

        await _audit.LogAsync(AuditActions.Create, nameof(Trainer), trainer.Id,
            newValues: Snapshot(trainer),
            description: $"Added trainer {trainer.TrainerCode} – {trainer.FullName}.",
            ct: ct).ConfigureAwait(false);

        var detail = await LoadDetailAsync(trainer.Id, ct).ConfigureAwait(false)
                     ?? throw new NotFoundAppException("Trainer", trainer.Id);
        return (detail, account);
    }

    public async Task<TrainerDetailDto> UpdateAsync(UpdateTrainerDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var trainer = await _db.Trainers.FirstOrDefaultAsync(t => t.Id == dto.Id, ct).ConfigureAwait(false)
                      ?? throw new NotFoundAppException("Trainer", dto.Id);

        var errors = new Dictionary<string, string[]>();
        var fullName = (dto.FullName ?? string.Empty).Trim();
        var phone = (dto.Phone ?? string.Empty).Trim();
        var email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();

        if (fullName.Length == 0) errors[nameof(dto.FullName)] = new[] { "Full name is required." };
        if (phone.Length == 0) errors[nameof(dto.Phone)] = new[] { "Phone number is required." };

        var today = _clock.Today.Date;
        if (dto.JoiningDate.Date > today.AddDays(1))
            errors[nameof(dto.JoiningDate)] = new[] { "Joining date cannot be more than one day in the future." };

        if (dto.ExperienceYears is < 0 or > 70)
            errors[nameof(dto.ExperienceYears)] = new[] { "Experience must be between 0 and 70 years." };

        if (dto.CommissionPercent is < 0m or > 100m)
            errors[nameof(dto.CommissionPercent)] = new[] { "Commission must be between 0 and 100 percent." };

        if (errors.Count > 0) throw new ValidationAppException(errors);

        var clash = await _db.Trainers.AsNoTracking()
            .AnyAsync(t => t.Id != dto.Id && t.Phone == phone && t.Status != TrainerStatus.Resigned, ct)
            .ConfigureAwait(false);
        if (clash)
            throw new ConflictAppException($"Another trainer already uses phone number '{phone}'.");

        var before = Snapshot(trainer);

        trainer.FullName = fullName;
        trainer.Gender = dto.Gender;
        trainer.Phone = phone;
        trainer.Email = email;
        trainer.Address = dto.Address;
        trainer.Specialization = dto.Specialization;
        trainer.Certifications = dto.Certifications;
        trainer.ExperienceYears = dto.ExperienceYears;
        trainer.JoiningDate = dto.JoiningDate.Date;
        trainer.MonthlySalary = dto.MonthlySalary;
        trainer.CommissionPercent = dto.CommissionPercent;
        trainer.Status = dto.Status;
        trainer.PhotoPath = dto.PhotoPath;
        trainer.Notes = dto.Notes;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Update, nameof(Trainer), trainer.Id,
            oldValues: before, newValues: Snapshot(trainer),
            description: $"Updated trainer {trainer.TrainerCode}.",
            ct: ct).ConfigureAwait(false);

        return await LoadDetailAsync(trainer.Id, ct).ConfigureAwait(false)
               ?? throw new NotFoundAppException("Trainer", trainer.Id);
    }

    public async Task SetStatusAsync(int id, TrainerStatus status, CancellationToken ct = default)
    {
        var trainer = await _db.Trainers.FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false)
                      ?? throw new NotFoundAppException("Trainer", id);

        if (trainer.Status == status) return;

        var oldStatus = trainer.Status;
        trainer.Status = status;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var action = status == TrainerStatus.Active ? AuditActions.Reactivate : AuditActions.Deactivate;
        await _audit.LogAsync(action, nameof(Trainer), trainer.Id,
            oldValues: new { Status = oldStatus },
            newValues: new { Status = status },
            description: $"Trainer {trainer.TrainerCode} status changed from {oldStatus} to {status}.",
            ct: ct).ConfigureAwait(false);
    }

    public async Task SoftDeleteAsync(int id, CancellationToken ct = default)
    {
        var trainer = await _db.Trainers.FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false)
                      ?? throw new NotFoundAppException("Trainer", id);

        var assignedMembers = await _db.Members.AsNoTracking()
            .CountAsync(m => m.AssignedTrainerId == id, ct).ConfigureAwait(false);
        if (assignedMembers > 0)
            throw new BusinessRuleAppException(
                $"Trainer {trainer.TrainerCode} still has {assignedMembers} assigned member(s). Reassign them before deleting the trainer.");

        var today = _clock.Today.Date;
        var openPlans = await _db.MemberWorkoutPlans.AsNoTracking()
            .CountAsync(p => p.TrainerId == id && p.IsActive && (p.EndDate == null || p.EndDate >= today), ct)
            .ConfigureAwait(false);
        if (openPlans > 0)
            throw new BusinessRuleAppException(
                $"Trainer {trainer.TrainerCode} is responsible for {openPlans} current or future workout plan(s). Reassign them before deleting the trainer.");

        var now = _clock.UtcNow;
        var userId = _currentUser.UserId;

        await using var tx = await BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            trainer.IsDeleted = true;
            trainer.DeletedAt = now;
            trainer.DeletedBy = userId;

            var linked = await _db.Users.Where(u => u.TrainerId == id).ToListAsync(ct).ConfigureAwait(false);
            foreach (var user in linked) user.Status = UserStatus.Inactive;

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            if (tx is not null) await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }

        await _audit.LogAsync(AuditActions.SoftDelete, nameof(Trainer), trainer.Id,
            oldValues: new { IsDeleted = false },
            newValues: new { IsDeleted = true, DeletedAt = now, DeletedBy = userId },
            description: $"Moved trainer {trainer.TrainerCode} – {trainer.FullName} to the recycle bin.",
            ct: ct).ConfigureAwait(false);
    }

    public async Task RestoreAsync(int id, CancellationToken ct = default)
    {
        var trainer = await _db.Trainers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false)
            ?? throw new NotFoundAppException("Trainer", id);

        if (!trainer.IsDeleted) return;

        trainer.IsDeleted = false;
        trainer.DeletedAt = null;
        trainer.DeletedBy = null;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Restore, nameof(Trainer), trainer.Id,
            oldValues: new { IsDeleted = true },
            newValues: new { IsDeleted = false },
            description: $"Restored trainer {trainer.TrainerCode} – {trainer.FullName}.",
            ct: ct).ConfigureAwait(false);
    }

    public async Task AssignMembersAsync(AssignTrainerDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var memberIds = dto.MemberIds?.Where(i => i > 0).Distinct().ToList() ?? new List<int>();
        if (memberIds.Count == 0)
            throw new ValidationAppException(nameof(dto.MemberIds), "Select at least one member to assign.");

        var trainer = await _db.Trainers.AsNoTracking()
            .Where(t => t.Id == dto.TrainerId)
            .Select(t => new { t.Id, t.TrainerCode, t.FullName, t.Status })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new NotFoundAppException("Trainer", dto.TrainerId);

        if (trainer.Status != TrainerStatus.Active)
            throw new BusinessRuleAppException($"Trainer '{trainer.FullName}' is {trainer.Status} and cannot take new members.");

        var members = await _db.Members.Where(m => memberIds.Contains(m.Id)).ToListAsync(ct).ConfigureAwait(false);
        if (members.Count != memberIds.Count)
        {
            var missing = memberIds.Except(members.Select(m => m.Id)).ToList();
            throw new NotFoundAppException($"Member(s) with identifier(s) '{string.Join(", ", missing)}' were not found.");
        }

        await using var tx = await BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var member in members) member.AssignedTrainerId = trainer.Id;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            if (tx is not null) await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }

        await _audit.LogAsync(AuditActions.Update, nameof(Trainer), trainer.Id,
            newValues: new { TrainerId = trainer.Id, MemberIds = memberIds },
            description: $"Assigned {memberIds.Count} member(s) to trainer {trainer.TrainerCode}: {string.Join(", ", memberIds)}.",
            ct: ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<TrainerDetailDto?> LoadDetailAsync(int id, CancellationToken ct)
    {
        var monthStart = new DateTime(_clock.Today.Year, _clock.Today.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        return await _db.Trainers.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new TrainerDetailDto
            {
                Id = t.Id,
                TrainerCode = t.TrainerCode,
                FullName = t.FullName,
                Gender = t.Gender,
                Phone = t.Phone,
                Email = t.Email,
                Specialization = t.Specialization,
                ExperienceYears = t.ExperienceYears,
                JoiningDate = t.JoiningDate,
                Status = t.Status,
                PhotoPath = t.PhotoPath,
                Address = t.Address,
                Certifications = t.Certifications,
                MonthlySalary = t.MonthlySalary,
                CommissionPercent = t.CommissionPercent,
                Notes = t.Notes,
                CreatedAt = t.CreatedAt,
                AssignedMemberCount = _db.Members.Count(m => m.AssignedTrainerId == t.Id),
                HasUserAccount = _db.Users.Any(u => u.TrainerId == t.Id),
                SessionsThisMonth = _db.WorkoutSessions
                    .Count(s => s.TrainerId == t.Id && s.SessionDate >= monthStart && s.SessionDate <= monthEnd)
            })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    private async Task<TemporaryPasswordDto> CreateLinkedUserAsync(string userName, string fullName, string email,
        string? phone, string roleName, int trainerId, CancellationToken ct)
    {
        var normalizedEmail = email.Trim();

        var emailTaken = await _db.Users.IgnoreQueryFilters()
            .AnyAsync(u => u.Email == normalizedEmail, ct).ConfigureAwait(false);
        if (emailTaken)
            throw new ConflictAppException($"A login account already uses the email address '{normalizedEmail}'.");

        var nameTaken = await _db.Users.IgnoreQueryFilters()
            .AnyAsync(u => u.UserName == userName, ct).ConfigureAwait(false);
        if (nameTaken)
            throw new ConflictAppException($"A login account named '{userName}' already exists.");

        var role = await _db.Roles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Name == roleName, ct).ConfigureAwait(false)
            ?? throw new NotFoundAppException("Role", roleName);

        var temporaryPassword = _hasher.GenerateTemporaryPassword();

        var user = new User
        {
            UserName = userName,
            Email = normalizedEmail,
            Phone = phone,
            FullName = fullName,
            PasswordHash = _hasher.Hash(temporaryPassword),
            Status = UserStatus.Active,
            MustChangePassword = true,
            TrainerId = trainerId
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        _db.UserRoles.Add(new UserRole
        {
            UserId = user.Id,
            RoleId = role.Id,
            AssignedAt = _clock.UtcNow,
            AssignedBy = _currentUser.UserId
        });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new TemporaryPasswordDto
        {
            UserId = user.Id,
            UserName = user.UserName,
            TemporaryPassword = temporaryPassword
        };
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken ct)
    {
        if (_db.Database.CurrentTransaction is not null) return null;
        try
        {
            return await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "The configured provider does not support transactions; continuing without one.");
            return null;
        }
    }

    private static object Snapshot(Trainer t) => new
    {
        t.TrainerCode,
        t.FullName,
        t.Gender,
        t.Phone,
        t.Email,
        t.Address,
        t.Specialization,
        t.Certifications,
        t.ExperienceYears,
        t.JoiningDate,
        t.MonthlySalary,
        t.CommissionPercent,
        t.Status,
        t.PhotoPath,
        t.Notes
    };
}
