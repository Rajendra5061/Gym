using System.Linq.Expressions;
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

/// <summary>
/// Member registration, search, documents, measurements and progress. Every list query is
/// evaluated on the server; nothing loads a whole table into memory.
/// </summary>
public sealed class MemberService : IMemberService
{
    private const long MaxDocumentBytes = 10L * 1024 * 1024;
    private const int MaxLookupTake = 50;

    private static readonly string[] AllowedDocumentExtensions =
        { ".pdf", ".jpg", ".jpeg", ".png", ".webp" };

    /// <summary>Reused so the subscription shape stays identical everywhere it is returned.</summary>
    private static readonly Expression<Func<Subscription, SubscriptionDto>> SubscriptionProjection =
        s => new SubscriptionDto
        {
            Id = s.Id,
            SubscriptionCode = s.SubscriptionCode,
            MemberId = s.MemberId,
            MemberCode = s.Member != null ? s.Member.MemberCode : string.Empty,
            MemberName = s.Member != null ? s.Member.FullName : string.Empty,
            MemberPhone = s.Member != null ? s.Member.Phone : null,
            MembershipPlanId = s.MembershipPlanId,
            PlanName = s.MembershipPlan != null ? s.MembershipPlan.Name : string.Empty,
            StartDate = s.StartDate,
            EndDate = s.EndDate,
            PlanAmount = s.PlanAmount,
            RegistrationFee = s.RegistrationFee,
            DiscountAmount = s.DiscountAmount,
            TaxPercent = s.TaxPercent,
            TaxAmount = s.TaxAmount,
            FinalAmount = s.FinalAmount,
            PaidAmount = s.PaidAmount,
            OutstandingAmount = s.FinalAmount - s.PaidAmount > 0m ? s.FinalAmount - s.PaidAmount : 0m,
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
            CreatedAt = s.CreatedAt
        };

    private readonly GymDbContext _db;
    private readonly ICodeGeneratorService _codes;
    private readonly IPasswordHasher _hasher;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly ILicenseService _license;
    private readonly ILogger<MemberService> _logger;

    public MemberService(
        GymDbContext db,
        ICodeGeneratorService codes,
        IPasswordHasher hasher,
        IDateTimeProvider clock,
        ICurrentUserService currentUser,
        IAuditService audit,
        ISettingsService settings,
        INotificationService notifications,
        ILicenseService license,
        ILogger<MemberService> logger)
    {
        _db = db;
        _codes = codes;
        _hasher = hasher;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
        _settings = settings;
        _notifications = notifications;
        _license = license;
        _logger = logger;
    }

    // ---------------------------------------------------------------- queries

    public async Task<PagedResult<MemberListDto>> GetPagedAsync(MemberQueryDto query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var today = _clock.Today.Date;
        var expiringWithin = query.ExpiringWithinDays < 0 ? 0 : query.ExpiringWithinDays;
        var soonLimit = today.AddDays(expiringWithin);

        var members = _db.Members.AsNoTracking();
        if (query.IncludeDeleted) members = members.IgnoreQueryFilters();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            members = members.Where(m =>
                m.MemberCode.Contains(term) ||
                m.FullName.Contains(term) ||
                m.Phone.Contains(term) ||
                (m.Email != null && m.Email.Contains(term)));
        }

        if (query.Status.HasValue) members = members.Where(m => m.Status == query.Status.Value);
        if (query.Gender.HasValue) members = members.Where(m => m.Gender == query.Gender.Value);
        if (query.TrainerId.HasValue) members = members.Where(m => m.AssignedTrainerId == query.TrainerId.Value);

        if (query.MembershipPlanId.HasValue)
        {
            var planId = query.MembershipPlanId.Value;
            members = members.Where(m => m.Subscriptions.Any(s => s.MembershipPlanId == planId));
        }

        if (query.JoinedFrom.HasValue)
        {
            var from = query.JoinedFrom.Value.Date;
            members = members.Where(m => m.JoiningDate >= from);
        }

        if (query.JoinedTo.HasValue)
        {
            var to = query.JoinedTo.Value.Date;
            members = members.Where(m => m.JoiningDate <= to);
        }

        // Expired = no subscription at all, or the latest one already ended.
        if (query.OnlyExpired == true)
            members = members.Where(m => !m.Subscriptions.Any(s => s.EndDate >= today));

        if (query.OnlyExpiringSoon == true)
            members = members.Where(m => m.Subscriptions.Any(s =>
                s.Status == SubscriptionStatus.Active && s.EndDate >= today && s.EndDate <= soonLimit));

        if (query.OnlyWithOutstanding == true)
            members = members.Where(m => m.Subscriptions.Any(s =>
                s.Status != SubscriptionStatus.Cancelled && s.FinalAmount > s.PaidAmount));

        var total = await members.CountAsync(ct).ConfigureAwait(false);
        if (total == 0)
            return new PagedResult<MemberListDto>(Array.Empty<MemberListDto>(), 0, query.PageNumber, query.PageSize);

        // Flatten the current subscription inside the SQL projection so paging and sorting on the
        // end date stay server-side. "Current" = the active one with the latest end date, else the
        // most recent by end date.
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
            "joiningdate" => desc
                ? shaped.OrderByDescending(x => x.JoiningDate).ThenBy(x => x.FullName)
                : shaped.OrderBy(x => x.JoiningDate).ThenBy(x => x.FullName),
            "status" => desc
                ? shaped.OrderByDescending(x => x.Status).ThenBy(x => x.FullName)
                : shaped.OrderBy(x => x.Status).ThenBy(x => x.FullName),
            "subscriptionenddate" => desc
                ? shaped.OrderByDescending(x => x.CurrentEndDate).ThenBy(x => x.FullName)
                : shaped.OrderBy(x => x.CurrentEndDate).ThenBy(x => x.FullName),
            _ => desc ? shaped.OrderByDescending(x => x.FullName) : shaped.OrderBy(x => x.FullName)
        };

        var rows = await shaped
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

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
                IsExpiringSoon = x.CurrentStatus == SubscriptionStatus.Active &&
                                 days is >= 0 && days <= expiringWithin
            };
        }).ToList();

        return new PagedResult<MemberListDto>(items, total, query.PageNumber, query.PageSize);
    }

    public async Task<MemberDetailDto> GetByIdAsync(int id, CancellationToken ct = default)
        => await LoadDetailAsync(m => m.Id == id, ct).ConfigureAwait(false)
           ?? throw new NotFoundAppException("Member", id);

    public async Task<MemberDetailDto?> GetByCodeAsync(string memberCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(memberCode)) return null;
        var code = memberCode.Trim();
        return await LoadDetailAsync(m => m.MemberCode == code, ct).ConfigureAwait(false);
    }

    public async Task<MemberHistoryDto> GetHistoryAsync(int id, CancellationToken ct = default)
    {
        var member = await LoadDetailAsync(m => m.Id == id, ct).ConfigureAwait(false)
                     ?? throw new NotFoundAppException("Member", id);

        var history = new MemberHistoryDto { Member = member };

        history.Subscriptions = await _db.Subscriptions.AsNoTracking()
            .Where(s => s.MemberId == id)
            .OrderByDescending(s => s.EndDate).ThenByDescending(s => s.Id)
            .Select(SubscriptionProjection)
            .ToListAsync(ct).ConfigureAwait(false);

        var today = _clock.Today.Date;
        foreach (var s in history.Subscriptions)
        {
            s.DaysRemaining = (s.EndDate.Date - today).Days;
            s.IsExpiringSoon = s.Status == SubscriptionStatus.Active && s.DaysRemaining is >= 0 and <= 7;
        }

        history.Payments = await _db.Payments.AsNoTracking()
            .Where(p => p.MemberId == id)
            .OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.Id)
            .Take(200)
            .Select(p => new PaymentDto
            {
                Id = p.Id,
                ReceiptNumber = p.ReceiptNumber,
                MemberId = p.MemberId,
                MemberCode = p.Member != null ? p.Member.MemberCode : string.Empty,
                MemberName = p.Member != null ? p.Member.FullName : string.Empty,
                MemberPhone = p.Member != null ? p.Member.Phone : null,
                SubscriptionId = p.SubscriptionId,
                SubscriptionCode = p.Subscription != null ? p.Subscription.SubscriptionCode : null,
                PlanName = p.Subscription != null && p.Subscription.MembershipPlan != null
                    ? p.Subscription.MembershipPlan.Name
                    : null,
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
                CreatedAt = p.CreatedAt
            })
            .ToListAsync(ct).ConfigureAwait(false);

        history.Attendance = await _db.Attendance.AsNoTracking()
            .Where(a => a.MemberId == id)
            .OrderByDescending(a => a.AttendanceDate).ThenByDescending(a => a.CheckInTime)
            .Take(200)
            .Select(a => new AttendanceDto
            {
                Id = a.Id,
                MemberId = a.MemberId,
                MemberCode = a.Member != null ? a.Member.MemberCode : string.Empty,
                MemberName = a.Member != null ? a.Member.FullName : string.Empty,
                ProfilePhotoPath = a.Member != null ? a.Member.ProfilePhotoPath : null,
                AttendanceDate = a.AttendanceDate,
                CheckInTime = a.CheckInTime,
                CheckOutTime = a.CheckOutTime,
                Status = a.Status,
                DurationMinutes = a.DurationMinutes,
                CheckInMethod = a.CheckInMethod,
                SubscriptionId = a.SubscriptionId,
                PlanName = a.Subscription != null && a.Subscription.MembershipPlan != null
                    ? a.Subscription.MembershipPlan.Name
                    : null,
                Notes = a.Notes
            })
            .ToListAsync(ct).ConfigureAwait(false);

        history.WorkoutSessions = await _db.WorkoutSessions.AsNoTracking()
            .Where(w => w.MemberId == id)
            .OrderByDescending(w => w.SessionDate).ThenByDescending(w => w.Id)
            .Take(100)
            .Select(w => new WorkoutSessionDto
            {
                Id = w.Id,
                MemberId = w.MemberId,
                MemberName = w.Member != null ? w.Member.FullName : string.Empty,
                MemberCode = w.Member != null ? w.Member.MemberCode : null,
                TrainerId = w.TrainerId,
                TrainerName = w.Trainer != null ? w.Trainer.FullName : null,
                WorkoutPlanId = w.WorkoutPlanId,
                WorkoutPlanName = w.WorkoutPlan != null ? w.WorkoutPlan.Name : null,
                SessionDate = w.SessionDate,
                StartTime = w.StartTime,
                EndTime = w.EndTime,
                DurationMinutes = w.DurationMinutes,
                CaloriesBurned = w.CaloriesBurned,
                Notes = w.Notes,
                ExerciseCount = w.Exercises.Count,
                TotalVolumeKg = w.Exercises.Sum(e =>
                    (decimal?)((decimal)e.Sets * e.Repetitions * (e.WeightKg ?? 0m))) ?? 0m
            })
            .ToListAsync(ct).ConfigureAwait(false);

        history.Measurements = await GetMeasurementsAsync(id, ct).ConfigureAwait(false);
        history.Documents = await GetDocumentsAsync(id, ct).ConfigureAwait(false);

        return history;
    }

    // ---------------------------------------------------------------- writes

    public async Task<(MemberDetailDto Member, TemporaryPasswordDto? Account)> CreateAsync(
        CreateMemberDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        await _license.EnsureCapacityAsync("members", ct).ConfigureAwait(false);

        var errors = new Dictionary<string, string[]>();
        var fullName = (dto.FullName ?? string.Empty).Trim();
        var phone = (dto.Phone ?? string.Empty).Trim();
        var email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();

        if (fullName.Length == 0) errors[nameof(dto.FullName)] = new[] { "Full name is required." };
        if (phone.Length == 0) errors[nameof(dto.Phone)] = new[] { "Phone number is required." };

        var today = _clock.Today.Date;
        if (dto.JoiningDate.Date > today.AddDays(1))
            errors[nameof(dto.JoiningDate)] = new[] { "Joining date cannot be more than one day in the future." };

        if (dto.DateOfBirth.HasValue)
        {
            var dob = dto.DateOfBirth.Value.Date;
            if (dob >= today)
                errors[nameof(dto.DateOfBirth)] = new[] { "Date of birth must be in the past." };
            else if (CalculateAge(dob, today) < 5)
                errors[nameof(dto.DateOfBirth)] = new[] { "Member must be at least 5 years old." };
        }

        if (dto.CreateUserAccount && email is null)
            errors["Email"] = new[] { "An email address is required to create a login account." };

        if (errors.Count > 0) throw new ValidationAppException(errors);

        var duplicate = await _db.Members.AsNoTracking()
            .AnyAsync(m => m.Phone == phone && m.Status == MemberStatus.Active, ct)
            .ConfigureAwait(false);
        if (duplicate)
            throw new ConflictAppException($"An active member with phone number '{phone}' already exists.");

        if (dto.AssignedTrainerId.HasValue)
            await EnsureTrainerAssignableAsync(dto.AssignedTrainerId.Value, ct).ConfigureAwait(false);

        var member = new Member
        {
            MemberCode = await _codes.NextMemberCodeAsync(ct).ConfigureAwait(false),
            FullName = fullName,
            Gender = dto.Gender,
            DateOfBirth = dto.DateOfBirth?.Date,
            Phone = phone,
            Email = email,
            Address = dto.Address,
            City = dto.City,
            PostalCode = dto.PostalCode,
            EmergencyContactName = dto.EmergencyContactName,
            EmergencyContactPhone = dto.EmergencyContactPhone,
            EmergencyContactRelation = dto.EmergencyContactRelation,
            JoiningDate = dto.JoiningDate.Date,
            ProfilePhotoPath = dto.ProfilePhotoPath,
            BloodGroup = dto.BloodGroup,
            HeightCm = dto.HeightCm,
            WeightKg = dto.WeightKg,
            Status = MemberStatus.Active,
            Notes = dto.Notes,
            AssignedTrainerId = dto.AssignedTrainerId
        };

        TemporaryPasswordDto? account = null;

        await using var tx = await BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            _db.Members.Add(member);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            if (dto.CreateUserAccount && email is not null)
            {
                account = await CreateLinkedUserAsync(
                    userName: member.MemberCode,
                    fullName: member.FullName,
                    email: email,
                    phone: member.Phone,
                    roleName: RoleNames.Member,
                    memberId: member.Id,
                    trainerId: null,
                    ct).ConfigureAwait(false);
            }

            if (tx is not null) await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }

        await _audit.LogAsync(AuditActions.Create, nameof(Member), member.Id,
            newValues: Snapshot(member),
            description: $"Registered member {member.MemberCode} – {member.FullName}.",
            ct: ct).ConfigureAwait(false);

        await SafeNotifyAsync(NotificationType.NewMemberRegistration, NotificationSeverity.Success,
            "New member registered",
            $"{member.FullName} ({member.MemberCode}) joined on {member.JoiningDate:yyyy-MM-dd}.",
            member.Id, ct).ConfigureAwait(false);

        var detail = await LoadDetailAsync(m => m.Id == member.Id, ct).ConfigureAwait(false)
                     ?? throw new NotFoundAppException("Member", member.Id);
        return (detail, account);
    }

    public async Task<MemberDetailDto> UpdateAsync(UpdateMemberDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var member = await _db.Members.FirstOrDefaultAsync(m => m.Id == dto.Id, ct).ConfigureAwait(false)
                     ?? throw new NotFoundAppException("Member", dto.Id);

        var errors = new Dictionary<string, string[]>();
        var fullName = (dto.FullName ?? string.Empty).Trim();
        var phone = (dto.Phone ?? string.Empty).Trim();
        var email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();

        if (fullName.Length == 0) errors[nameof(dto.FullName)] = new[] { "Full name is required." };
        if (phone.Length == 0) errors[nameof(dto.Phone)] = new[] { "Phone number is required." };

        var today = _clock.Today.Date;
        if (dto.JoiningDate.Date > today.AddDays(1))
            errors[nameof(dto.JoiningDate)] = new[] { "Joining date cannot be more than one day in the future." };

        if (dto.DateOfBirth.HasValue)
        {
            var dob = dto.DateOfBirth.Value.Date;
            if (dob >= today)
                errors[nameof(dto.DateOfBirth)] = new[] { "Date of birth must be in the past." };
            else if (CalculateAge(dob, today) < 5)
                errors[nameof(dto.DateOfBirth)] = new[] { "Member must be at least 5 years old." };
        }

        if (errors.Count > 0) throw new ValidationAppException(errors);

        var clash = await _db.Members.AsNoTracking()
            .AnyAsync(m => m.Id != dto.Id && m.Phone == phone && m.Status == MemberStatus.Active, ct)
            .ConfigureAwait(false);
        if (clash)
            throw new ConflictAppException($"Another active member already uses phone number '{phone}'.");

        if (dto.AssignedTrainerId.HasValue && dto.AssignedTrainerId != member.AssignedTrainerId)
            await EnsureTrainerAssignableAsync(dto.AssignedTrainerId.Value, ct).ConfigureAwait(false);

        var before = Snapshot(member);

        member.FullName = fullName;
        member.Gender = dto.Gender;
        member.DateOfBirth = dto.DateOfBirth?.Date;
        member.Phone = phone;
        member.Email = email;
        member.Address = dto.Address;
        member.City = dto.City;
        member.PostalCode = dto.PostalCode;
        member.EmergencyContactName = dto.EmergencyContactName;
        member.EmergencyContactPhone = dto.EmergencyContactPhone;
        member.EmergencyContactRelation = dto.EmergencyContactRelation;
        member.JoiningDate = dto.JoiningDate.Date;
        member.ProfilePhotoPath = dto.ProfilePhotoPath;
        member.BloodGroup = dto.BloodGroup;
        member.HeightCm = dto.HeightCm;
        member.WeightKg = dto.WeightKg;
        member.Status = dto.Status;
        member.Notes = dto.Notes;
        member.AssignedTrainerId = dto.AssignedTrainerId;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Update, nameof(Member), member.Id,
            oldValues: before, newValues: Snapshot(member),
            description: $"Updated member {member.MemberCode}.",
            ct: ct).ConfigureAwait(false);

        return await LoadDetailAsync(m => m.Id == member.Id, ct).ConfigureAwait(false)
               ?? throw new NotFoundAppException("Member", member.Id);
    }

    public async Task SetStatusAsync(int id, MemberStatus status, string? reason, CancellationToken ct = default)
    {
        var member = await _db.Members.FirstOrDefaultAsync(m => m.Id == id, ct).ConfigureAwait(false)
                     ?? throw new NotFoundAppException("Member", id);

        if (member.Status == status) return;

        var oldStatus = member.Status;
        member.Status = status;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            var stamp = $"[{_clock.Now:yyyy-MM-dd HH:mm}] {status}: {reason.Trim()}";
            member.Notes = string.IsNullOrWhiteSpace(member.Notes) ? stamp : $"{member.Notes}{Environment.NewLine}{stamp}";
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var action = status == MemberStatus.Active ? AuditActions.Reactivate : AuditActions.Deactivate;
        var description = status == MemberStatus.Active
            ? $"Reactivated member {member.MemberCode}."
            : $"Set member {member.MemberCode} to {status}. Reason: {(string.IsNullOrWhiteSpace(reason) ? "not supplied" : reason!.Trim())}";

        await _audit.LogAsync(action, nameof(Member), member.Id,
            oldValues: new { Status = oldStatus },
            newValues: new { Status = status },
            description: description, ct: ct).ConfigureAwait(false);
    }

    public async Task SoftDeleteAsync(int id, CancellationToken ct = default)
    {
        var member = await _db.Members.FirstOrDefaultAsync(m => m.Id == id, ct).ConfigureAwait(false)
                     ?? throw new NotFoundAppException("Member", id);

        var hasActiveSubscription = await _db.Subscriptions.AsNoTracking()
            .AnyAsync(s => s.MemberId == id && s.Status == SubscriptionStatus.Active, ct)
            .ConfigureAwait(false);
        if (hasActiveSubscription)
            throw new BusinessRuleAppException(
                $"Member {member.MemberCode} has an active subscription. Cancel or expire it before deleting the member.");

        var outstanding = await _db.Subscriptions.AsNoTracking()
            .Where(s => s.MemberId == id && s.Status != SubscriptionStatus.Cancelled && s.FinalAmount > s.PaidAmount)
            .SumAsync(s => (decimal?)(s.FinalAmount - s.PaidAmount), ct)
            .ConfigureAwait(false) ?? 0m;
        if (outstanding > 0m)
            throw new BusinessRuleAppException(
                $"Member {member.MemberCode} has an outstanding balance of {outstanding:0.00}. Settle or write it off before deleting.");

        var now = _clock.UtcNow;
        var userId = _currentUser.UserId;

        await using var tx = await BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            member.IsDeleted = true;
            member.DeletedAt = now;
            member.DeletedBy = userId;

            var linked = await _db.Users.Where(u => u.MemberId == id).ToListAsync(ct).ConfigureAwait(false);
            foreach (var user in linked) user.Status = UserStatus.Inactive;

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            if (tx is not null) await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }

        await _audit.LogAsync(AuditActions.SoftDelete, nameof(Member), member.Id,
            oldValues: new { IsDeleted = false },
            newValues: new { IsDeleted = true, DeletedAt = now, DeletedBy = userId },
            description: $"Moved member {member.MemberCode} – {member.FullName} to the recycle bin.",
            ct: ct).ConfigureAwait(false);
    }

    public async Task RestoreAsync(int id, CancellationToken ct = default)
    {
        var member = await _db.Members.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == id, ct).ConfigureAwait(false)
            ?? throw new NotFoundAppException("Member", id);

        if (!member.IsDeleted) return;

        member.IsDeleted = false;
        member.DeletedAt = null;
        member.DeletedBy = null;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Restore, nameof(Member), member.Id,
            oldValues: new { IsDeleted = true },
            newValues: new { IsDeleted = false },
            description: $"Restored member {member.MemberCode} – {member.FullName}.",
            ct: ct).ConfigureAwait(false);
    }

    public async Task<List<LookupDto>> SearchLookupAsync(string? term, int take = 20, CancellationToken ct = default)
    {
        var limit = take switch { < 1 => 20, > MaxLookupTake => MaxLookupTake, _ => take };

        var q = _db.Members.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            q = q.Where(m => m.MemberCode.Contains(t) || m.FullName.Contains(t) || m.Phone.Contains(t));
        }

        return await q
            .OrderBy(m => m.FullName)
            .Take(limit)
            .Select(m => new LookupDto
            {
                Id = m.Id,
                Name = m.FullName,
                Code = m.MemberCode,
                Extra = m.Phone,
                IsActive = m.Status == MemberStatus.Active
            })
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task AssignTrainerAsync(int memberId, int? trainerId, CancellationToken ct = default)
    {
        var member = await _db.Members.FirstOrDefaultAsync(m => m.Id == memberId, ct).ConfigureAwait(false)
                     ?? throw new NotFoundAppException("Member", memberId);

        string? trainerName = null;
        if (trainerId.HasValue)
            trainerName = await EnsureTrainerAssignableAsync(trainerId.Value, ct).ConfigureAwait(false);

        var oldTrainerId = member.AssignedTrainerId;
        if (oldTrainerId == trainerId) return;

        member.AssignedTrainerId = trainerId;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Update, nameof(Member), member.Id,
            oldValues: new { AssignedTrainerId = oldTrainerId },
            newValues: new { AssignedTrainerId = trainerId },
            description: trainerId.HasValue
                ? $"Assigned trainer {trainerName} to member {member.MemberCode}."
                : $"Cleared the trainer assignment for member {member.MemberCode}.",
            ct: ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- documents

    public async Task<List<MemberDocumentDto>> GetDocumentsAsync(int memberId, CancellationToken ct = default)
        => await _db.MemberDocuments.AsNoTracking()
            .Where(d => d.MemberId == memberId)
            .OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id)
            .Select(d => new MemberDocumentDto
            {
                Id = d.Id,
                MemberId = d.MemberId,
                DocumentType = d.DocumentType,
                FileName = d.FileName,
                FilePath = d.FilePath,
                ContentType = d.ContentType,
                FileSizeBytes = d.FileSizeBytes,
                Notes = d.Notes,
                CreatedAt = d.CreatedAt
            })
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task<MemberDocumentDto> AddDocumentAsync(int memberId, string documentType, string fileName,
        byte[] content, string? contentType, string? notes, CancellationToken ct = default)
    {
        var member = await _db.Members.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == memberId, ct).ConfigureAwait(false)
            ?? throw new NotFoundAppException("Member", memberId);

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(fileName))
            errors[nameof(fileName)] = new[] { "A file name is required." };
        if (content is null || content.Length == 0)
            errors[nameof(content)] = new[] { "The uploaded file is empty." };
        else if (content.Length > MaxDocumentBytes)
            errors[nameof(content)] = new[] { "The file exceeds the 10 MB upload limit." };

        var extension = string.IsNullOrWhiteSpace(fileName)
            ? string.Empty
            : Path.GetExtension(fileName).ToLowerInvariant();
        if (!errors.ContainsKey(nameof(fileName)) && !AllowedDocumentExtensions.Contains(extension))
            errors[nameof(fileName)] = new[]
            {
                $"Only the following file types are accepted: {string.Join(", ", AllowedDocumentExtensions)}."
            };

        if (errors.Count > 0) throw new ValidationAppException(errors);

        var root = await ResolveUploadsRootAsync(ct).ConfigureAwait(false);
        var safeName = SafeFileName(fileName);
        var relativePath = Path.Combine("members", memberId.ToString(), $"{Guid.NewGuid():N}_{safeName}");
        var absolutePath = Path.Combine(root, relativePath);

        var folder = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(absolutePath, content!, ct).ConfigureAwait(false);

        var document = new MemberDocument
        {
            MemberId = memberId,
            DocumentType = string.IsNullOrWhiteSpace(documentType) ? "Other" : documentType.Trim(),
            FileName = safeName,
            FilePath = relativePath,
            ContentType = contentType,
            FileSizeBytes = content!.LongLength,
            Notes = notes
        };

        try
        {
            _db.MemberDocuments.Add(document);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            TryDeleteFile(absolutePath);
            throw;
        }

        await _audit.LogAsync(AuditActions.Create, nameof(MemberDocument), document.Id,
            newValues: new { document.MemberId, document.DocumentType, document.FileName, document.FileSizeBytes },
            description: $"Uploaded document '{document.FileName}' for member {member.MemberCode}.",
            ct: ct).ConfigureAwait(false);

        return new MemberDocumentDto
        {
            Id = document.Id,
            MemberId = document.MemberId,
            DocumentType = document.DocumentType,
            FileName = document.FileName,
            FilePath = document.FilePath,
            ContentType = document.ContentType,
            FileSizeBytes = document.FileSizeBytes,
            Notes = document.Notes,
            CreatedAt = document.CreatedAt
        };
    }

    public async Task<FileExportDto> GetDocumentFileAsync(int documentId, CancellationToken ct = default)
    {
        var document = await _db.MemberDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId, ct).ConfigureAwait(false)
            ?? throw new NotFoundAppException("MemberDocument", documentId);

        var root = await ResolveUploadsRootAsync(ct).ConfigureAwait(false);
        var fullPath = Path.GetFullPath(Path.Combine(root, document.FilePath));

        // The stored path is always relative and generated by AddDocumentAsync, but re-check that it
        // still resolves inside the uploads root so a tampered row cannot read arbitrary files.
        var rootFull = Path.GetFullPath(root);
        if (!fullPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("Document {DocumentId} resolved outside the uploads root.", documentId);
            throw new NotFoundAppException("MemberDocument", documentId);
        }

        if (!File.Exists(fullPath))
            throw new NotFoundAppException($"The file for document '{document.FileName}' is no longer on disk.");

        var bytes = await File.ReadAllBytesAsync(fullPath, ct).ConfigureAwait(false);

        return new FileExportDto
        {
            FileName = document.FileName,
            ContentType = string.IsNullOrWhiteSpace(document.ContentType)
                ? "application/octet-stream"
                : document.ContentType,
            Content = bytes
        };
    }

    public async Task DeleteDocumentAsync(int documentId, CancellationToken ct = default)
    {
        var document = await _db.MemberDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId, ct).ConfigureAwait(false)
            ?? throw new NotFoundAppException("MemberDocument", documentId);

        document.IsDeleted = true;
        document.DeletedAt = _clock.UtcNow;
        document.DeletedBy = _currentUser.UserId;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        try
        {
            var root = await ResolveUploadsRootAsync(ct).ConfigureAwait(false);
            TryDeleteFile(Path.Combine(root, document.FilePath));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not remove the file for document {DocumentId}.", documentId);
        }

        await _audit.LogAsync(AuditActions.SoftDelete, nameof(MemberDocument), document.Id,
            description: $"Deleted document '{document.FileName}' of member {document.MemberId}.",
            ct: ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- measurements

    public async Task<List<MemberMeasurementDto>> GetMeasurementsAsync(int memberId, CancellationToken ct = default)
    {
        var rows = await _db.MemberMeasurements.AsNoTracking()
            .Where(m => m.MemberId == memberId)
            .OrderByDescending(m => m.MeasuredOn).ThenByDescending(m => m.Id)
            .Select(m => new
            {
                m.Id,
                m.MemberId,
                m.MeasuredOn,
                m.WeightKg,
                m.HeightCm,
                m.BodyFatPercent,
                m.MuscleMassKg,
                m.ChestCm,
                m.WaistCm,
                m.HipCm,
                m.ArmCm,
                m.ThighCm,
                m.RecordedByTrainerId,
                TrainerName = m.RecordedByTrainer != null ? m.RecordedByTrainer.FullName : null,
                m.Notes
            })
            .ToListAsync(ct).ConfigureAwait(false);

        return rows.Select(m => new MemberMeasurementDto
        {
            Id = m.Id,
            MemberId = m.MemberId,
            MeasuredOn = m.MeasuredOn,
            WeightKg = m.WeightKg,
            HeightCm = m.HeightCm,
            BodyFatPercent = m.BodyFatPercent,
            MuscleMassKg = m.MuscleMassKg,
            ChestCm = m.ChestCm,
            WaistCm = m.WaistCm,
            HipCm = m.HipCm,
            ArmCm = m.ArmCm,
            ThighCm = m.ThighCm,
            Bmi = CalculateBmi(m.WeightKg, m.HeightCm),
            RecordedByTrainerId = m.RecordedByTrainerId,
            RecordedByTrainerName = m.TrainerName,
            Notes = m.Notes
        }).ToList();
    }

    public async Task<MemberMeasurementDto> SaveMeasurementAsync(MemberMeasurementDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var hasValue = dto.WeightKg.HasValue || dto.HeightCm.HasValue || dto.BodyFatPercent.HasValue
                       || dto.MuscleMassKg.HasValue || dto.ChestCm.HasValue || dto.WaistCm.HasValue
                       || dto.HipCm.HasValue || dto.ArmCm.HasValue || dto.ThighCm.HasValue;
        if (!hasValue)
            throw new ValidationAppException(nameof(dto.WeightKg), "Supply at least one measurement value.");

        if (dto.MeasuredOn.Date > _clock.Today.Date)
            throw new ValidationAppException(nameof(dto.MeasuredOn), "The measurement date cannot be in the future.");

        var memberExists = await _db.Members.AsNoTracking()
            .AnyAsync(m => m.Id == dto.MemberId, ct).ConfigureAwait(false);
        if (!memberExists) throw new NotFoundAppException("Member", dto.MemberId);

        if (dto.RecordedByTrainerId.HasValue)
        {
            var trainerExists = await _db.Trainers.AsNoTracking()
                .AnyAsync(t => t.Id == dto.RecordedByTrainerId.Value, ct).ConfigureAwait(false);
            if (!trainerExists) throw new NotFoundAppException("Trainer", dto.RecordedByTrainerId.Value);
        }

        MemberMeasurement entity;
        var isNew = dto.Id <= 0;
        if (isNew)
        {
            entity = new MemberMeasurement { MemberId = dto.MemberId };
            _db.MemberMeasurements.Add(entity);
        }
        else
        {
            entity = await _db.MemberMeasurements
                .FirstOrDefaultAsync(m => m.Id == dto.Id, ct).ConfigureAwait(false)
                ?? throw new NotFoundAppException("MemberMeasurement", dto.Id);
        }

        entity.MeasuredOn = dto.MeasuredOn.Date;
        entity.WeightKg = dto.WeightKg;
        entity.HeightCm = dto.HeightCm;
        entity.BodyFatPercent = dto.BodyFatPercent;
        entity.MuscleMassKg = dto.MuscleMassKg;
        entity.ChestCm = dto.ChestCm;
        entity.WaistCm = dto.WaistCm;
        entity.HipCm = dto.HipCm;
        entity.ArmCm = dto.ArmCm;
        entity.ThighCm = dto.ThighCm;
        entity.RecordedByTrainerId = dto.RecordedByTrainerId;
        entity.Notes = dto.Notes;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(isNew ? AuditActions.Create : AuditActions.Update,
            nameof(MemberMeasurement), entity.Id,
            newValues: new { entity.MemberId, entity.MeasuredOn, entity.WeightKg, entity.BodyFatPercent },
            description: $"Saved body measurement for member {entity.MemberId} on {entity.MeasuredOn:yyyy-MM-dd}.",
            ct: ct).ConfigureAwait(false);

        var trainerName = entity.RecordedByTrainerId.HasValue
            ? await _db.Trainers.AsNoTracking()
                .Where(t => t.Id == entity.RecordedByTrainerId.Value)
                .Select(t => t.FullName)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : null;

        return new MemberMeasurementDto
        {
            Id = entity.Id,
            MemberId = entity.MemberId,
            MeasuredOn = entity.MeasuredOn,
            WeightKg = entity.WeightKg,
            HeightCm = entity.HeightCm,
            BodyFatPercent = entity.BodyFatPercent,
            MuscleMassKg = entity.MuscleMassKg,
            ChestCm = entity.ChestCm,
            WaistCm = entity.WaistCm,
            HipCm = entity.HipCm,
            ArmCm = entity.ArmCm,
            ThighCm = entity.ThighCm,
            Bmi = entity.Bmi,
            RecordedByTrainerId = entity.RecordedByTrainerId,
            RecordedByTrainerName = trainerName,
            Notes = entity.Notes
        };
    }

    public async Task DeleteMeasurementAsync(int measurementId, CancellationToken ct = default)
    {
        var entity = await _db.MemberMeasurements
            .FirstOrDefaultAsync(m => m.Id == measurementId, ct).ConfigureAwait(false)
            ?? throw new NotFoundAppException("MemberMeasurement", measurementId);

        _db.MemberMeasurements.Remove(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Delete, nameof(MemberMeasurement), measurementId,
            oldValues: new { entity.MemberId, entity.MeasuredOn, entity.WeightKg },
            description: $"Deleted the measurement of {entity.MeasuredOn:yyyy-MM-dd} for member {entity.MemberId}.",
            ct: ct).ConfigureAwait(false);
    }

    public async Task<MemberProgressDto> GetProgressAsync(int memberId, DateTime? from, DateTime? to,
        CancellationToken ct = default)
    {
        var member = await _db.Members.AsNoTracking()
            .Where(m => m.Id == memberId)
            .Select(m => new { m.Id, m.FullName, m.HeightCm })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new NotFoundAppException("Member", memberId);

        var toDate = (to ?? _clock.Today).Date;
        var fromDate = (from ?? toDate.AddMonths(-12)).Date;
        if (fromDate > toDate) (fromDate, toDate) = (toDate, fromDate);

        var progress = new MemberProgressDto { MemberId = member.Id, MemberName = member.FullName };

        var measurements = await _db.MemberMeasurements.AsNoTracking()
            .Where(m => m.MemberId == memberId && m.MeasuredOn >= fromDate && m.MeasuredOn <= toDate)
            .OrderBy(m => m.MeasuredOn)
            .Select(m => new { m.MeasuredOn, m.WeightKg, m.HeightCm, m.BodyFatPercent })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var m in measurements)
        {
            if (m.WeightKg.HasValue)
                progress.Weight.Add(new ProgressPointDto { Date = m.MeasuredOn, Value = m.WeightKg.Value, Label = "kg" });

            if (m.BodyFatPercent.HasValue)
                progress.BodyFat.Add(new ProgressPointDto { Date = m.MeasuredOn, Value = m.BodyFatPercent.Value, Label = "%" });

            var bmi = CalculateBmi(m.WeightKg, m.HeightCm ?? member.HeightCm);
            if (bmi.HasValue)
                progress.Bmi.Add(new ProgressPointDto { Date = m.MeasuredOn, Value = bmi.Value, Label = "BMI" });
        }

        var sessions = await _db.WorkoutSessions.AsNoTracking()
            .Where(s => s.MemberId == memberId && s.SessionDate >= fromDate && s.SessionDate <= toDate)
            .OrderBy(s => s.SessionDate)
            .Select(s => new
            {
                s.SessionDate,
                Volume = s.Exercises.Sum(e => (decimal?)((decimal)e.Sets * e.Repetitions * (e.WeightKg ?? 0m))) ?? 0m,
                s.CaloriesBurned
            })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var s in sessions)
        {
            if (s.Volume > 0m)
                progress.SessionVolume.Add(new ProgressPointDto { Date = s.SessionDate, Value = s.Volume, Label = "kg" });

            if (s.CaloriesBurned is > 0m)
                progress.CaloriesBurned.Add(new ProgressPointDto { Date = s.SessionDate, Value = s.CaloriesBurned.Value, Label = "kcal" });
        }

        return progress;
    }

    // ---------------------------------------------------------------- helpers

    private async Task<MemberDetailDto?> LoadDetailAsync(Expression<Func<Member, bool>> predicate, CancellationToken ct)
    {
        var row = await _db.Members.AsNoTracking()
            .Where(predicate)
            .Select(m => new
            {
                Member = m,
                TrainerName = m.AssignedTrainer != null ? m.AssignedTrainer.FullName : null,
                HasUserAccount = _db.Users.Any(u => u.MemberId == m.Id),
                TotalPaid = m.Payments
                    .Where(p => p.Status == PaymentStatus.Paid
                                || p.Status == PaymentStatus.PartiallyPaid
                                || p.Status == PaymentStatus.PartiallyRefunded)
                    .Sum(p => (decimal?)(p.FinalAmount - p.RefundedAmount)) ?? 0m,
                TotalOutstanding = m.Subscriptions
                    .Where(s => s.Status != SubscriptionStatus.Cancelled && s.FinalAmount > s.PaidAmount)
                    .Sum(s => (decimal?)(s.FinalAmount - s.PaidAmount)) ?? 0m,
                TotalVisits = m.AttendanceRecords.Count,
                LastVisitDate = m.AttendanceRecords.Max(a => (DateTime?)a.AttendanceDate)
            })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (row is null) return null;

        var m = row.Member;
        var today = _clock.Today.Date;

        var active = await _db.Subscriptions.AsNoTracking()
            .Where(s => s.MemberId == m.Id && s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.EndDate).ThenByDescending(s => s.Id)
            .Select(SubscriptionProjection)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (active is not null)
        {
            active.DaysRemaining = (active.EndDate.Date - today).Days;
            active.IsExpiringSoon = active.DaysRemaining is >= 0 and <= 7;
        }

        return new MemberDetailDto
        {
            Id = m.Id,
            MemberCode = m.MemberCode,
            FullName = m.FullName,
            Gender = m.Gender,
            DateOfBirth = m.DateOfBirth,
            Age = m.DateOfBirth.HasValue ? CalculateAge(m.DateOfBirth.Value.Date, today) : null,
            Phone = m.Phone,
            Email = m.Email,
            Address = m.Address,
            City = m.City,
            PostalCode = m.PostalCode,
            EmergencyContactName = m.EmergencyContactName,
            EmergencyContactPhone = m.EmergencyContactPhone,
            EmergencyContactRelation = m.EmergencyContactRelation,
            JoiningDate = m.JoiningDate,
            ProfilePhotoPath = m.ProfilePhotoPath,
            BloodGroup = m.BloodGroup,
            HeightCm = m.HeightCm,
            WeightKg = m.WeightKg,
            Status = m.Status,
            Notes = m.Notes,
            AssignedTrainerId = m.AssignedTrainerId,
            AssignedTrainerName = row.TrainerName,
            HasUserAccount = row.HasUserAccount,
            CreatedAt = m.CreatedAt,
            UpdatedAt = m.UpdatedAt,
            ActiveSubscription = active,
            TotalPaid = row.TotalPaid,
            TotalOutstanding = row.TotalOutstanding,
            TotalVisits = row.TotalVisits,
            LastVisitDate = row.LastVisitDate
        };
    }

    /// <summary>Returns the trainer name, or throws when the trainer is missing or not active.</summary>
    private async Task<string> EnsureTrainerAssignableAsync(int trainerId, CancellationToken ct)
    {
        var trainer = await _db.Trainers.AsNoTracking()
            .Where(t => t.Id == trainerId)
            .Select(t => new { t.FullName, t.Status })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new NotFoundAppException("Trainer", trainerId);

        if (trainer.Status != TrainerStatus.Active)
            throw new BusinessRuleAppException($"Trainer '{trainer.FullName}' is {trainer.Status} and cannot take new members.");

        return trainer.FullName;
    }

    private async Task<TemporaryPasswordDto> CreateLinkedUserAsync(string userName, string fullName, string email,
        string? phone, string roleName, int? memberId, int? trainerId, CancellationToken ct)
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
            MemberId = memberId,
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

    /// <summary>Starts a transaction unless one is already running or the provider has none.</summary>
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

    private async Task SafeNotifyAsync(NotificationType type, NotificationSeverity severity, string title,
        string message, int? memberId, CancellationToken ct)
    {
        try
        {
            await _notifications.NotifyAsync(type, severity, title, message, memberId, ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not raise the {NotificationType} notification for member {MemberId}.", type, memberId);
        }
    }

    private async Task<string> ResolveUploadsRootAsync(CancellationToken ct)
    {
        string? configured = null;
        try
        {
            configured = await _settings.GetValueAsync<string>("Storage.UploadsPath", null, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read Storage.UploadsPath; falling back to the application folder.");
        }

        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "uploads")
            : configured.Trim();

        return Path.GetFullPath(root);
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete the file at {Path}.", path);
        }
    }

    private static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Trim());
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Replace(' ', '_');

        if (name.Length > 120)
        {
            var ext = Path.GetExtension(name);
            name = string.Concat(Path.GetFileNameWithoutExtension(name).AsSpan(0, 120 - ext.Length), ext);
        }

        return string.IsNullOrWhiteSpace(name) ? "file" : name;
    }

    private static int CalculateAge(DateTime dateOfBirth, DateTime asOf)
    {
        var age = asOf.Year - dateOfBirth.Year;
        if (dateOfBirth.Date > asOf.AddYears(-age)) age--;
        return age < 0 ? 0 : age;
    }

    private static decimal? CalculateBmi(decimal? weightKg, decimal? heightCm)
    {
        if (weightKg is not > 0m || heightCm is not > 0m) return null;
        var metres = heightCm.Value / 100m;
        return Math.Round(weightKg.Value / (metres * metres), 2);
    }

    private static object Snapshot(Member m) => new
    {
        m.MemberCode,
        m.FullName,
        m.Gender,
        m.DateOfBirth,
        m.Phone,
        m.Email,
        m.Address,
        m.City,
        m.PostalCode,
        m.EmergencyContactName,
        m.EmergencyContactPhone,
        m.EmergencyContactRelation,
        m.JoiningDate,
        m.BloodGroup,
        m.HeightCm,
        m.WeightKg,
        m.Status,
        m.Notes,
        m.AssignedTrainerId
    };
}
