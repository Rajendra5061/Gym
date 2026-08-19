using System.Globalization;
using System.Text;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GymManagement.Infrastructure.Services;

/// <summary>
/// Supplies the id/name pairs used by every combo box in the UI: database driven lists plus the
/// domain enumerations rendered with friendly display names.
/// </summary>
public sealed class LookupService : ILookupService
{
    private readonly GymDbContext _db;

    public LookupService(GymDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<Dictionary<string, List<LookupDto>>> GetAllAsync(CancellationToken ct = default)
    {
        var plans = await _db.MembershipPlans
            .AsNoTracking()
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.PlanCode, p.Price, p.Status })
            .ToListAsync(ct).ConfigureAwait(false);

        var trainers = await _db.Trainers
            .AsNoTracking()
            .OrderBy(t => t.FullName)
            .Select(t => new { t.Id, t.FullName, t.TrainerCode, t.Specialization, t.Status })
            .ToListAsync(ct).ConfigureAwait(false);

        var paymentMethods = await _db.PaymentMethods
            .AsNoTracking()
            .OrderBy(m => m.DisplayOrder).ThenBy(m => m.Name)
            .Select(m => new { m.Id, m.Name, m.Code, m.RequiresReference, m.SupportsQrCode, m.IsActive })
            .ToListAsync(ct).ConfigureAwait(false);

        var expenseCategories = await _db.ExpenseCategories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.IsActive })
            .ToListAsync(ct).ConfigureAwait(false);

        var exercises = await _db.Exercises
            .AsNoTracking()
            .OrderBy(e => e.Name)
            .Select(e => new { e.Id, e.Name, e.Category, e.PrimaryMuscleGroup, e.IsActive })
            .ToListAsync(ct).ConfigureAwait(false);

        var workoutPlans = await _db.WorkoutPlans
            .AsNoTracking()
            .OrderBy(w => w.Name)
            .Select(w => new { w.Id, w.Name, w.Difficulty, w.IsActive })
            .ToListAsync(ct).ConfigureAwait(false);

        var roles = await _db.Roles
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new { r.Id, r.Name, r.Description })
            .ToListAsync(ct).ConfigureAwait(false);

        return new Dictionary<string, List<LookupDto>>(StringComparer.OrdinalIgnoreCase)
        {
            ["MembershipPlans"] = plans.Select(p => new LookupDto
            {
                Id = p.Id,
                Name = p.Name,
                Code = p.PlanCode,
                Extra = p.Price.ToString("0.00", CultureInfo.InvariantCulture),
                IsActive = p.Status == PlanStatus.Active
            }).ToList(),

            ["Trainers"] = trainers.Select(t => new LookupDto
            {
                Id = t.Id,
                Name = t.FullName,
                Code = t.TrainerCode,
                Extra = t.Specialization,
                IsActive = t.Status == TrainerStatus.Active
            }).ToList(),

            ["PaymentMethods"] = paymentMethods.Select(m => new LookupDto
            {
                Id = m.Id,
                Name = m.Name,
                Code = m.Code,
                Extra = m.SupportsQrCode ? "Qr" : m.RequiresReference ? "Reference" : null,
                IsActive = m.IsActive
            }).ToList(),

            ["ExpenseCategories"] = expenseCategories.Select(c => new LookupDto
            {
                Id = c.Id,
                Name = c.Name,
                IsActive = c.IsActive
            }).ToList(),

            ["Exercises"] = exercises.Select(e => new LookupDto
            {
                Id = e.Id,
                Name = e.Name,
                Code = Humanize(e.Category.ToString()),
                Extra = Humanize(e.PrimaryMuscleGroup.ToString()),
                IsActive = e.IsActive
            }).ToList(),

            ["WorkoutPlans"] = workoutPlans.Select(w => new LookupDto
            {
                Id = w.Id,
                Name = w.Name,
                Extra = Humanize(w.Difficulty.ToString()),
                IsActive = w.IsActive
            }).ToList(),

            ["Roles"] = roles.Select(r => new LookupDto
            {
                Id = r.Id,
                Name = r.Name,
                Extra = r.Description,
                IsActive = true
            }).ToList()
        };
    }

    public Task<List<LookupDto>> GetEnumAsync(string enumName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var name = (enumName ?? string.Empty).Trim();

        var values = name.ToLowerInvariant() switch
        {
            "gender" => FromEnum<Gender>(),
            "memberstatus" => FromEnum<MemberStatus>(),
            "userstatus" => FromEnum<UserStatus>(),
            "trainerstatus" => FromEnum<TrainerStatus>(),
            "plandurationtype" => FromEnum<PlanDurationType>(),
            "planstatus" => FromEnum<PlanStatus>(),
            "subscriptionstatus" => FromEnum<SubscriptionStatus>(),
            "paymentstatus" => FromEnum<PaymentStatus>(),
            "attendancestatus" => FromEnum<AttendanceStatus>(),
            "notificationtype" => FromEnum<NotificationType>(),
            "notificationseverity" => FromEnum<NotificationSeverity>(),
            "refundstatus" => FromEnum<RefundStatus>(),
            "exercisecategory" => FromEnum<ExerciseCategory>(),
            "musclegroup" => FromEnum<MuscleGroup>(),
            "difficultylevel" => FromEnum<DifficultyLevel>(),
            "licensestatus" => FromEnum<LicenseStatus>(),
            _ => null
        };

        if (values is null)
            throw new NotFoundAppException($"Enumeration '{name}' is not a known lookup.");

        return Task.FromResult(values);
    }

    private static List<LookupDto> FromEnum<TEnum>() where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>()
            .Select(value => new LookupDto
            {
                Id = Convert.ToInt32(value, CultureInfo.InvariantCulture),
                Name = Humanize(value.ToString()),
                Code = value.ToString(),
                IsActive = true
            })
            .OrderBy(l => l.Id)
            .ToList();

    /// <summary>Turns <c>MembershipExpiringSoon</c> into <c>Membership Expiring Soon</c>.</summary>
    private static string Humanize(string? pascalCase)
    {
        if (string.IsNullOrWhiteSpace(pascalCase)) return string.Empty;

        var builder = new StringBuilder(pascalCase.Length + 8);

        for (var i = 0; i < pascalCase.Length; i++)
        {
            var current = pascalCase[i];

            if (i > 0 && char.IsUpper(current))
            {
                var previous = pascalCase[i - 1];
                var nextIsLower = i + 1 < pascalCase.Length && char.IsLower(pascalCase[i + 1]);

                // Break before a new word, but keep acronyms such as "UPI" together.
                if (!char.IsUpper(previous) || nextIsLower)
                    builder.Append(' ');
            }
            else if (i > 0 && char.IsDigit(current) && !char.IsDigit(pascalCase[i - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(current);
        }

        return builder.ToString();
    }
}
