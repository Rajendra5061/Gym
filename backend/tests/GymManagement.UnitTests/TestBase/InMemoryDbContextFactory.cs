using GymManagement.Application.Common;
using GymManagement.Domain.Constants;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GymManagement.UnitTests.TestBase;

/// <summary>
/// Reference rows every service under test needs, with the generated ids exposed so no test ever
/// hard-codes a database key.
/// </summary>
public sealed class ReferenceData
{
    /// <summary>Month/1, 2500 + 500 registration, 18% tax, 15% max discount, 3 grace days, 7 freeze days.</summary>
    public int MonthlyPlanId { get; init; }

    /// <summary>Same as Monthly but with a 0% tax rate.</summary>
    public int ZeroTaxPlanId { get; init; }

    /// <summary>Month/1 plan whose <c>MaxFreezeDays</c> is 0, so freezing must be refused.</summary>
    public int NoFreezePlanId { get; init; }

    /// <summary>Year/1, 20000, no registration fee — the "dearer plan" for upgrade paths.</summary>
    public int AnnualPlanId { get; init; }

    /// <summary>Inactive plan, so selling it must be refused.</summary>
    public int InactivePlanId { get; init; }

    public int CashMethodId { get; init; }

    /// <summary>Requires a transaction reference.</summary>
    public int UpiMethodId { get; init; }

    public int CardMethodId { get; init; }

    public int AdminRoleId { get; init; }
    public int StaffRoleId { get; init; }
    public int MemberRoleId { get; init; }
    public int TrainerRoleId { get; init; }

    public int TrainerId { get; init; }
    public int ExpenseCategoryId { get; init; }

    /// <summary>The user id the <see cref="FakeCurrentUser"/> default points at.</summary>
    public int AdminUserId { get; init; }
}

/// <summary>
/// Builds a <see cref="GymDbContext"/> on the EF Core in-memory provider. Each call gets a brand new
/// database name so no test can ever see another test's rows.
/// </summary>
public static class InMemoryDbContextFactory
{
    /// <summary>
    /// Creates an isolated context. Transactions are a no-op on this provider, so the warning the
    /// services would otherwise trip on is suppressed rather than worked around in the product.
    /// </summary>
    public static GymDbContext Create(
        ICurrentUserService? currentUser = null,
        IDateTimeProvider? clock = null,
        string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<GymDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .EnableSensitiveDataLogging()
            .Options;

        return new GymDbContext(options, currentUser, clock);
    }

    /// <summary>
    /// Inserts the roles, permissions, gym settings row, payment methods, membership plans, a
    /// trainer and an expense category that the services expect to find.
    /// </summary>
    public static async Task<ReferenceData> SeedReferenceDataAsync(GymDbContext db)
    {
        var permissions = Permissions.All
            .Select(p => new Permission { Code = p.Code, Module = p.Module, Description = p.Description })
            .ToList();
        db.Permissions.AddRange(permissions);

        var roles = RoleNames.All
            .Select(name => new Role { Name = name, IsSystemRole = true, Description = $"{name} role." })
            .ToList();
        db.Roles.AddRange(roles);

        await db.SaveChangesAsync();

        foreach (var role in roles)
        {
            var granted = Permissions.ForRole(role.Name);
            db.RolePermissions.AddRange(permissions
                .Where(p => granted.Contains(p.Code))
                .Select(p => new RolePermission { RoleId = role.Id, PermissionId = p.Id }));
        }

        db.GymSettings.Add(new GymSetting
        {
            GymName = "Test Gym",
            City = "Bengaluru",
            Country = "India",
            CurrencyCode = "INR",
            CurrencySymbol = "₹",
            ReceiptPrefix = "RCP",
            MemberCodePrefix = "GYM",
            ExpiryReminderDays = 7,
            DefaultGracePeriodDays = 0,
            MaxFailedLoginAttempts = 5,
            LockoutMinutes = 15,
            OpeningTime = new TimeSpan(6, 0, 0),
            ClosingTime = new TimeSpan(22, 0, 0),
            UpiId = "testgym@upi",
            UpiPayeeName = "Test Gym",
            AllowExpiredMemberCheckIn = false
        });

        // The seeded "Monthly" plan: 2500 + 500 registration at 18% tax is the arithmetic the
        // pricing tests and the live smoke test both pin down.
        var monthly = new MembershipPlan
        {
            PlanCode = "PLN-2026-0001",
            Name = "Monthly",
            DurationType = PlanDurationType.Month,
            DurationValue = 1,
            Price = 2500m,
            RegistrationFee = 500m,
            TaxPercent = 18m,
            MaxDiscountPercent = 15m,
            GracePeriodDays = 3,
            MaxFreezeDays = 7,
            DisplayOrder = 1,
            Status = PlanStatus.Active
        };

        var zeroTax = new MembershipPlan
        {
            PlanCode = "PLN-2026-0002",
            Name = "Monthly (tax free)",
            DurationType = PlanDurationType.Month,
            DurationValue = 1,
            Price = 2500m,
            RegistrationFee = 500m,
            TaxPercent = 0m,
            MaxDiscountPercent = 15m,
            GracePeriodDays = 3,
            MaxFreezeDays = 7,
            DisplayOrder = 2,
            Status = PlanStatus.Active
        };

        var noFreeze = new MembershipPlan
        {
            PlanCode = "PLN-2026-0003",
            Name = "Monthly (no freeze)",
            DurationType = PlanDurationType.Month,
            DurationValue = 1,
            Price = 2000m,
            RegistrationFee = 0m,
            TaxPercent = 18m,
            MaxDiscountPercent = 100m,
            GracePeriodDays = 0,
            MaxFreezeDays = 0,
            DisplayOrder = 3,
            Status = PlanStatus.Active
        };

        var annual = new MembershipPlan
        {
            PlanCode = "PLN-2026-0004",
            Name = "Annual",
            DurationType = PlanDurationType.Year,
            DurationValue = 1,
            Price = 20000m,
            RegistrationFee = 0m,
            TaxPercent = 18m,
            MaxDiscountPercent = 30m,
            GracePeriodDays = 10,
            MaxFreezeDays = 45,
            DisplayOrder = 4,
            Status = PlanStatus.Active
        };

        var inactive = new MembershipPlan
        {
            PlanCode = "PLN-2026-0005",
            Name = "Withdrawn",
            DurationType = PlanDurationType.Month,
            DurationValue = 1,
            Price = 999m,
            TaxPercent = 0m,
            DisplayOrder = 5,
            Status = PlanStatus.Inactive
        };

        db.MembershipPlans.AddRange(monthly, zeroTax, noFreeze, annual, inactive);

        var cash = new PaymentMethod { Code = "CASH", Name = "Cash", RequiresReference = false, DisplayOrder = 1 };
        var upi = new PaymentMethod
        {
            Code = "UPI", Name = "UPI", RequiresReference = true, SupportsQrCode = true, DisplayOrder = 2
        };
        var card = new PaymentMethod { Code = "CARD", Name = "Card", RequiresReference = true, DisplayOrder = 3 };
        db.PaymentMethods.AddRange(cash, upi, card);

        var trainer = new Trainer
        {
            TrainerCode = "TRN-2026-0001",
            FullName = "Test Trainer",
            Phone = "9000000001",
            Status = TrainerStatus.Active
        };
        db.Trainers.Add(trainer);

        var expenseCategory = new ExpenseCategory { Name = "Rent", IsActive = true };
        db.ExpenseCategories.Add(expenseCategory);

        var admin = new User
        {
            UserName = "admin",
            Email = "admin@test.local",
            FullName = "System Administrator",
            PasswordHash = "hashed::seed::Admin@12345",
            Status = UserStatus.Active
        };
        db.Users.Add(admin);

        await db.SaveChangesAsync();

        db.UserRoles.Add(new UserRole
        {
            UserId = admin.Id,
            RoleId = roles.First(r => r.Name == RoleNames.Admin).Id
        });
        await db.SaveChangesAsync();

        return new ReferenceData
        {
            MonthlyPlanId = monthly.Id,
            ZeroTaxPlanId = zeroTax.Id,
            NoFreezePlanId = noFreeze.Id,
            AnnualPlanId = annual.Id,
            InactivePlanId = inactive.Id,
            CashMethodId = cash.Id,
            UpiMethodId = upi.Id,
            CardMethodId = card.Id,
            AdminRoleId = roles.First(r => r.Name == RoleNames.Admin).Id,
            StaffRoleId = roles.First(r => r.Name == RoleNames.Staff).Id,
            MemberRoleId = roles.First(r => r.Name == RoleNames.Member).Id,
            TrainerRoleId = roles.First(r => r.Name == RoleNames.Trainer).Id,
            TrainerId = trainer.Id,
            ExpenseCategoryId = expenseCategory.Id,
            AdminUserId = admin.Id
        };
    }

    /// <summary>Adds a member directly, bypassing the service, for arrange-only fixtures.</summary>
    public static async Task<Member> AddMemberAsync(
        GymDbContext db,
        string fullName = "Test Member",
        string? phone = null,
        MemberStatus status = MemberStatus.Active,
        DateTime? joiningDate = null)
    {
        var member = new Member
        {
            MemberCode = $"GYM-2026-{Random.Shared.Next(1000, 9999)}-{Guid.NewGuid():N}"[..18],
            FullName = fullName,
            Phone = phone ?? $"9{Random.Shared.NextInt64(100_000_000, 999_999_999)}",
            Status = status,
            JoiningDate = (joiningDate ?? FixedClock.DefaultNow).Date
        };

        db.Members.Add(member);
        await db.SaveChangesAsync();
        return member;
    }
}
