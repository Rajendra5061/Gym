using GymManagement.Application.Common;
using GymManagement.Domain.Constants;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Data;

/// <summary>Populates the reference data a fresh installation needs in order to be usable.</summary>
public interface IDbSeeder
{
    Task SeedAsync(CancellationToken ct = default);
}

/// <summary>
/// Idempotent seeder: every step checks for existing rows first, so it is safe to run on every
/// application start.
/// </summary>
public sealed class DbSeeder : IDbSeeder
{
    private readonly GymDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DbSeeder> _logger;

    public DbSeeder(GymDbContext db, IPasswordHasher hasher, IConfiguration configuration,
        ILogger<DbSeeder> logger)
    {
        _db = db;
        _hasher = hasher;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedPermissionsAsync(ct);
        await SeedRolesAsync(ct);
        await SyncNewRolePermissionsAsync(ct);
        await SeedGymSettingsAsync(ct);
        await SeedSystemSettingsAsync(ct);
        await SeedPaymentMethodsAsync(ct);
        await SeedExpenseCategoriesAsync(ct);
        await SeedMembershipPlansAsync(ct);
        await SeedExercisesAsync(ct);
        await SeedEquipmentAsync(ct);
        await SeedEnquiriesAsync(ct);
        await SeedAdminUserAsync(ct);

        _logger.LogInformation("Database seeding completed.");
    }

    private async Task SeedPermissionsAsync(CancellationToken ct)
    {
        var existing = await _db.Permissions.Select(p => p.Code).ToListAsync(ct);
        var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        var toAdd = Permissions.All
            .Where(p => !existingSet.Contains(p.Code))
            .Select(p => new Permission { Code = p.Code, Module = p.Module, Description = p.Description })
            .ToList();

        if (toAdd.Count == 0) return;

        _db.Permissions.AddRange(toAdd);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded {Count} permissions.", toAdd.Count);
    }

    private async Task SeedRolesAsync(CancellationToken ct)
    {
        var permissionIds = await _db.Permissions
            .ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var roleName in RoleNames.All)
        {
            var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == roleName, ct);
            if (role is null)
            {
                role = new Role
                {
                    Name = roleName,
                    IsSystemRole = true,
                    Description = roleName switch
                    {
                        RoleNames.Admin => "Full access to every module and configuration.",
                        RoleNames.Staff => "Front-desk operations: members, attendance and payment collection.",
                        RoleNames.Trainer => "Assigned members, workout plans and progress tracking.",
                        RoleNames.Member => "Self-service access to their own membership.",
                        _ => null
                    }
                };
                _db.Roles.Add(role);
                await _db.SaveChangesAsync(ct);
            }

            // Grant the default permission set only for a role that has none yet, so an
            // administrator's later customisations are never overwritten.
            var hasPermissions = await _db.RolePermissions.AnyAsync(rp => rp.RoleId == role.Id, ct);
            if (hasPermissions) continue;

            var grants = Permissions.ForRole(roleName)
                .Where(code => permissionIds.ContainsKey(code))
                .Select(code => new RolePermission { RoleId = role.Id, PermissionId = permissionIds[code] })
                .ToList();

            if (grants.Count == 0) continue;

            _db.RolePermissions.AddRange(grants);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Granted {Count} permissions to the {Role} role.", grants.Count, roleName);
        }
    }

    /// <summary>
    /// Ensures permissions added after an installation went live are granted to the built-in roles
    /// that need them. <see cref="SeedRolesAsync"/> only grants defaults to a role with zero rows,
    /// so an existing database would never pick the new codes up without this step. Add-only and
    /// idempotent: it never removes a grant an administrator made, and it is a no-op once the rows
    /// exist. Runs on every boot.
    /// </summary>
    private async Task SyncNewRolePermissionsAsync(CancellationToken ct)
    {
        var required = new (string RoleName, string[] Codes)[]
        {
            (RoleNames.Trainer, new[]
            {
                Permissions.DietView, Permissions.DietManage,
                Permissions.MeasurementsManage, Permissions.AttendanceManage
            }),
            (RoleNames.Staff, new[] { Permissions.MeasurementsManage })
        };

        var permissionIds = await _db.Permissions
            .ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.OrdinalIgnoreCase, ct);

        var added = 0;

        foreach (var (roleName, codes) in required)
        {
            var role = await _db.Roles
                .FirstOrDefaultAsync(r => r.Name == roleName && r.IsSystemRole, ct);
            if (role is null) continue;

            var held = await _db.RolePermissions
                .Where(rp => rp.RoleId == role.Id)
                .Select(rp => rp.PermissionId)
                .ToListAsync(ct);
            var heldSet = new HashSet<int>(held);

            foreach (var code in codes)
            {
                if (!permissionIds.TryGetValue(code, out var permissionId)) continue;
                if (heldSet.Contains(permissionId)) continue;

                _db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permissionId });
                added++;
            }
        }

        if (added == 0) return;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Granted {Count} newly introduced permission(s) to existing system roles.", added);
    }

    private async Task SeedGymSettingsAsync(CancellationToken ct)
    {
        if (await _db.GymSettings.AnyAsync(ct)) return;

        _db.GymSettings.Add(new GymSetting
        {
            GymName = "My Fitness Gym",
            Address = "123 Fitness Street",
            City = "Bengaluru",
            State = "Karnataka",
            Country = "India",
            Phone = "+91 98765 43210",
            Email = "info@myfitnessgym.example",
            CurrencyCode = "INR",
            CurrencySymbol = "₹",
            ReceiptPrefix = "RCP",
            MemberCodePrefix = "GYM",
            ReceiptFooterText = "Thank you for training with us!",
            ExpiryReminderDays = 7,
            DefaultGracePeriodDays = 3,
            MaxFailedLoginAttempts = 5,
            LockoutMinutes = 15,
            OpeningTime = new TimeSpan(6, 0, 0),
            ClosingTime = new TimeSpan(22, 0, 0)
        });

        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedSystemSettingsAsync(CancellationToken ct)
    {
        var defaults = new (string Key, string? Value, string Type, string Category, string Description)[]
        {
            ("Storage.UploadsPath", null, "string", "Storage", "Folder for member photos and documents. Empty uses the application folder."),
            ("Backup.Folder", null, "string", "Backup", "Folder SQL Server writes database backups to. Must be writable by the SQL Server service account."),
            ("Notifications.AutoGenerateOnStartup", "true", "bool", "Notifications", "Raise expiry and pending-payment alerts when the API starts."),
            ("Subscriptions.AutoExpireOnStartup", "true", "bool", "Subscriptions", "Run the auto-expiry job when the API starts."),
            ("Reports.DefaultPageSize", "50", "int", "Reports", "Rows per page in the reports screen."),
            ("Attendance.AllowedMethods", "Manual,QrCode,Barcode,Rfid,Biometric", "string", "Attendance", "Check-in methods the front desk may use.")
        };

        var existing = await _db.SystemSettings.Select(s => s.Key).ToListAsync(ct);
        var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        var toAdd = defaults
            .Where(d => !existingSet.Contains(d.Key))
            .Select(d => new SystemSetting
            {
                Key = d.Key,
                Value = d.Value,
                DataType = d.Type,
                Category = d.Category,
                Description = d.Description
            })
            .ToList();

        if (toAdd.Count == 0) return;

        _db.SystemSettings.AddRange(toAdd);
        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedPaymentMethodsAsync(CancellationToken ct)
    {
        if (await _db.PaymentMethods.AnyAsync(ct)) return;

        _db.PaymentMethods.AddRange(
            new PaymentMethod { Code = "CASH", Name = "Cash", RequiresReference = false, DisplayOrder = 1 },
            new PaymentMethod { Code = "UPI", Name = "UPI", RequiresReference = true, SupportsQrCode = true, DisplayOrder = 2 },
            new PaymentMethod { Code = "CARD", Name = "Card", RequiresReference = true, DisplayOrder = 3 },
            new PaymentMethod { Code = "BANK", Name = "Bank Transfer", RequiresReference = true, DisplayOrder = 4 },
            new PaymentMethod { Code = "CHEQUE", Name = "Cheque", RequiresReference = true, DisplayOrder = 5 },
            new PaymentMethod { Code = "WALLET", Name = "Wallet", RequiresReference = true, DisplayOrder = 6 });

        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedExpenseCategoriesAsync(CancellationToken ct)
    {
        if (await _db.ExpenseCategories.AnyAsync(ct)) return;

        _db.ExpenseCategories.AddRange(
            new ExpenseCategory { Name = "Rent", Description = "Premises rent and maintenance charges." },
            new ExpenseCategory { Name = "Salaries", Description = "Trainer and staff salaries." },
            new ExpenseCategory { Name = "Equipment", Description = "Purchase and repair of gym equipment." },
            new ExpenseCategory { Name = "Utilities", Description = "Electricity, water and internet." },
            new ExpenseCategory { Name = "Marketing", Description = "Advertising and promotions." },
            new ExpenseCategory { Name = "Housekeeping", Description = "Cleaning and consumables." },
            new ExpenseCategory { Name = "Miscellaneous", Description = "Anything not covered elsewhere." });

        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedMembershipPlansAsync(CancellationToken ct)
    {
        if (await _db.MembershipPlans.IgnoreQueryFilters().AnyAsync(ct)) return;

        var year = DateTime.UtcNow.Year;
        var plans = new[]
        {
            new MembershipPlan
            {
                PlanCode = $"PLN-{year}-0001", Name = "Day Pass", DurationType = PlanDurationType.Day,
                DurationValue = 1, Price = 300m, TaxPercent = 18m, DisplayOrder = 1,
                Description = "Single day access to the gym floor.",
                Features = "Gym floor access\nLocker"
            },
            new MembershipPlan
            {
                PlanCode = $"PLN-{year}-0002", Name = "Weekly", DurationType = PlanDurationType.Week,
                DurationValue = 1, Price = 1200m, TaxPercent = 18m, DisplayOrder = 2,
                Description = "One week of unlimited access.",
                Features = "Gym floor access\nLocker\nGroup classes"
            },
            new MembershipPlan
            {
                PlanCode = $"PLN-{year}-0003", Name = "Monthly", DurationType = PlanDurationType.Month,
                DurationValue = 1, Price = 2500m, RegistrationFee = 500m, TaxPercent = 18m,
                MaxDiscountPercent = 15m, GracePeriodDays = 3, MaxFreezeDays = 7, DisplayOrder = 3,
                Description = "Our most popular plan.",
                Features = "Gym floor access\nLocker\nGroup classes\nDiet consultation"
            },
            new MembershipPlan
            {
                PlanCode = $"PLN-{year}-0004", Name = "Quarterly", DurationType = PlanDurationType.Quarter,
                DurationValue = 1, Price = 6500m, RegistrationFee = 500m, TaxPercent = 18m,
                MaxDiscountPercent = 20m, GracePeriodDays = 5, MaxFreezeDays = 15, DisplayOrder = 4,
                Description = "Three months with a better monthly rate.",
                Features = "Gym floor access\nLocker\nGroup classes\nDiet consultation\n1 personal training session"
            },
            new MembershipPlan
            {
                PlanCode = $"PLN-{year}-0005", Name = "Half Yearly", DurationType = PlanDurationType.HalfYear,
                DurationValue = 1, Price = 12000m, RegistrationFee = 500m, TaxPercent = 18m,
                MaxDiscountPercent = 25m, GracePeriodDays = 7, MaxFreezeDays = 30, TrainerIncluded = true,
                DisplayOrder = 5, Description = "Six months including a dedicated trainer.",
                Features = "Everything in Quarterly\nDedicated trainer\nMonthly body composition check"
            },
            new MembershipPlan
            {
                PlanCode = $"PLN-{year}-0006", Name = "Annual", DurationType = PlanDurationType.Year,
                DurationValue = 1, Price = 20000m, RegistrationFee = 0m, TaxPercent = 18m,
                MaxDiscountPercent = 30m, GracePeriodDays = 10, MaxFreezeDays = 45, TrainerIncluded = true,
                DisplayOrder = 6, Description = "Best value — twelve months, no registration fee.",
                Features = "Everything in Half Yearly\nNo registration fee\nGuest passes\nPriority class booking"
            }
        };

        _db.MembershipPlans.AddRange(plans);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded {Count} membership plans.", plans.Length);
    }

    private async Task SeedExercisesAsync(CancellationToken ct)
    {
        if (await _db.Exercises.IgnoreQueryFilters().AnyAsync(ct)) return;

        var exercises = new (string Name, ExerciseCategory Category, MuscleGroup Muscle,
            DifficultyLevel Difficulty, string Equipment, decimal Calories)[]
        {
            ("Barbell Bench Press", ExerciseCategory.Strength, MuscleGroup.Chest, DifficultyLevel.Intermediate, "Barbell, Bench", 6m),
            ("Incline Dumbbell Press", ExerciseCategory.Strength, MuscleGroup.Chest, DifficultyLevel.Intermediate, "Dumbbells, Bench", 6m),
            ("Push Up", ExerciseCategory.Strength, MuscleGroup.Chest, DifficultyLevel.Beginner, "Bodyweight", 7m),
            ("Deadlift", ExerciseCategory.Strength, MuscleGroup.Back, DifficultyLevel.Advanced, "Barbell", 9m),
            ("Lat Pulldown", ExerciseCategory.Strength, MuscleGroup.Back, DifficultyLevel.Beginner, "Cable Machine", 5m),
            ("Bent Over Row", ExerciseCategory.Strength, MuscleGroup.Back, DifficultyLevel.Intermediate, "Barbell", 7m),
            ("Overhead Press", ExerciseCategory.Strength, MuscleGroup.Shoulders, DifficultyLevel.Intermediate, "Barbell", 6m),
            ("Lateral Raise", ExerciseCategory.Strength, MuscleGroup.Shoulders, DifficultyLevel.Beginner, "Dumbbells", 4m),
            ("Barbell Curl", ExerciseCategory.Strength, MuscleGroup.Biceps, DifficultyLevel.Beginner, "Barbell", 4m),
            ("Hammer Curl", ExerciseCategory.Strength, MuscleGroup.Biceps, DifficultyLevel.Beginner, "Dumbbells", 4m),
            ("Triceps Pushdown", ExerciseCategory.Strength, MuscleGroup.Triceps, DifficultyLevel.Beginner, "Cable Machine", 4m),
            ("Skull Crusher", ExerciseCategory.Strength, MuscleGroup.Triceps, DifficultyLevel.Intermediate, "EZ Bar", 5m),
            ("Barbell Squat", ExerciseCategory.Strength, MuscleGroup.Legs, DifficultyLevel.Intermediate, "Barbell, Rack", 9m),
            ("Leg Press", ExerciseCategory.Strength, MuscleGroup.Legs, DifficultyLevel.Beginner, "Leg Press Machine", 7m),
            ("Lunge", ExerciseCategory.Strength, MuscleGroup.Legs, DifficultyLevel.Beginner, "Dumbbells", 7m),
            ("Romanian Deadlift", ExerciseCategory.Strength, MuscleGroup.Glutes, DifficultyLevel.Intermediate, "Barbell", 7m),
            ("Hip Thrust", ExerciseCategory.Strength, MuscleGroup.Glutes, DifficultyLevel.Beginner, "Barbell, Bench", 6m),
            ("Standing Calf Raise", ExerciseCategory.Strength, MuscleGroup.Calves, DifficultyLevel.Beginner, "Machine", 3m),
            ("Plank", ExerciseCategory.Functional, MuscleGroup.Core, DifficultyLevel.Beginner, "Bodyweight", 4m),
            ("Crunch", ExerciseCategory.Strength, MuscleGroup.Core, DifficultyLevel.Beginner, "Bodyweight", 4m),
            ("Russian Twist", ExerciseCategory.Functional, MuscleGroup.Core, DifficultyLevel.Beginner, "Medicine Ball", 5m),
            ("Hanging Leg Raise", ExerciseCategory.Strength, MuscleGroup.Core, DifficultyLevel.Advanced, "Pull Up Bar", 6m),
            ("Treadmill Run", ExerciseCategory.Cardio, MuscleGroup.FullBody, DifficultyLevel.Beginner, "Treadmill", 11m),
            ("Stationary Cycling", ExerciseCategory.Cardio, MuscleGroup.Legs, DifficultyLevel.Beginner, "Exercise Bike", 8m),
            ("Rowing Machine", ExerciseCategory.Cardio, MuscleGroup.FullBody, DifficultyLevel.Intermediate, "Rower", 10m),
            ("Jump Rope", ExerciseCategory.Cardio, MuscleGroup.FullBody, DifficultyLevel.Beginner, "Skipping Rope", 12m),
            ("Burpee", ExerciseCategory.Functional, MuscleGroup.FullBody, DifficultyLevel.Advanced, "Bodyweight", 13m),
            ("Kettlebell Swing", ExerciseCategory.Functional, MuscleGroup.FullBody, DifficultyLevel.Intermediate, "Kettlebell", 10m),
            ("Hamstring Stretch", ExerciseCategory.Flexibility, MuscleGroup.Legs, DifficultyLevel.Beginner, "Mat", 2m),
            ("Single Leg Balance", ExerciseCategory.Balance, MuscleGroup.Core, DifficultyLevel.Beginner, "Bodyweight", 2m)
        };

        _db.Exercises.AddRange(exercises.Select(e => new Exercise
        {
            Name = e.Name,
            Category = e.Category,
            PrimaryMuscleGroup = e.Muscle,
            Difficulty = e.Difficulty,
            Equipment = e.Equipment,
            CaloriesPerMinute = e.Calories,
            IsActive = true,
            Instructions = $"Perform {e.Name} with controlled form. Keep the movement smooth and breathe steadily."
        }));

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded {Count} exercises.", exercises.Length);
    }

    private async Task SeedEquipmentAsync(CancellationToken ct)
    {
        if (await _db.Equipment.IgnoreQueryFilters().AnyAsync(ct)) return;

        var today = DateTime.Today;

        var items = new[]
        {
            new Equipment
            {
                Code = "EQP-CRD-001", Name = "Treadmill", Category = "Cardio",
                Manufacturer = "Generic", Quantity = 4, Condition = EquipmentCondition.Good,
                Location = "Cardio Zone", PurchaseCost = 185000m,
                PurchaseDate = today.AddYears(-1), NextServiceDue = today.AddMonths(2)
            },
            new Equipment
            {
                Code = "EQP-CRD-002", Name = "Elliptical Cross Trainer", Category = "Cardio",
                Manufacturer = "Generic", Quantity = 2, Condition = EquipmentCondition.Good,
                Location = "Cardio Zone", PurchaseCost = 96000m,
                PurchaseDate = today.AddYears(-1), NextServiceDue = today.AddMonths(3)
            },
            new Equipment
            {
                Code = "EQP-STR-001", Name = "Smith Machine", Category = "Strength",
                Manufacturer = "Generic", Quantity = 1, Condition = EquipmentCondition.New,
                Location = "Strength Zone", PurchaseCost = 142000m,
                PurchaseDate = today.AddMonths(-3)
            },
            new Equipment
            {
                Code = "EQP-STR-002", Name = "Lat Pulldown Machine", Category = "Strength",
                Manufacturer = "Generic", Quantity = 1, Condition = EquipmentCondition.NeedsService,
                Location = "Strength Zone", PurchaseCost = 78000m,
                PurchaseDate = today.AddYears(-2), LastServicedOn = today.AddMonths(-8),
                NextServiceDue = today.AddDays(-5)
            },
            new Equipment
            {
                Code = "EQP-FRW-001", Name = "Olympic Barbell 20kg", Category = "Free Weights",
                Quantity = 6, Condition = EquipmentCondition.Good, Location = "Free Weights Area",
                PurchaseCost = 12500m, PurchaseDate = today.AddYears(-1)
            },
            new Equipment
            {
                Code = "EQP-FRW-002", Name = "Rubber Dumbbell Set 2.5-30kg", Category = "Free Weights",
                Quantity = 1, Condition = EquipmentCondition.Good, Location = "Free Weights Area",
                PurchaseCost = 165000m, PurchaseDate = today.AddYears(-1)
            },
            new Equipment
            {
                Code = "EQP-ACC-001", Name = "Yoga Mat", Category = "Accessories",
                Quantity = 20, Condition = EquipmentCondition.Good, Location = "Studio",
                PurchaseCost = 9000m, PurchaseDate = today.AddMonths(-6)
            }
        };

        _db.Equipment.AddRange(items);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded {Count} equipment records.", items.Length);
    }

    private async Task SeedEnquiriesAsync(CancellationToken ct)
    {
        if (await _db.Enquiries.IgnoreQueryFilters().AnyAsync(ct)) return;

        var planId = await _db.MembershipPlans
            .OrderBy(p => p.Id)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(ct);

        var today = DateTime.Today;

        _db.Enquiries.AddRange(
            new Enquiry
            {
                FullName = "Ravi Kumar",
                Phone = "9800000001",
                Email = "ravi.kumar@example.com",
                Source = EnquirySource.WalkIn,
                InterestedPlanId = planId,
                Status = EnquiryStatus.New,
                FollowUpDate = today.AddDays(2),
                Message = "Asked about morning batch timings and the quarterly plan price."
            },
            new Enquiry
            {
                FullName = "Anita Sharma",
                Phone = "9800000002",
                Email = "anita.sharma@example.com",
                Source = EnquirySource.Phone,
                InterestedPlanId = planId,
                Status = EnquiryStatus.Contacted,
                FollowUpDate = today.AddDays(5),
                Message = "Wants a trial session with a personal trainer before signing up."
            });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded 2 sample enquiries.");
    }

    /// <summary>
    /// Creates the first administrator. The password comes from configuration
    /// (<c>Seed:AdminPassword</c>) or falls back to a documented default that the account is
    /// forced to change at first sign-in.
    /// </summary>
    private async Task SeedAdminUserAsync(CancellationToken ct)
    {
        if (await _db.Users.IgnoreQueryFilters().AnyAsync(ct)) return;

        var adminRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == RoleNames.Admin, ct);
        if (adminRole is null)
        {
            _logger.LogError("The Admin role is missing, so the administrator account was not created.");
            return;
        }

        var userName = _configuration["Seed:AdminUserName"] ?? "admin";
        var email = _configuration["Seed:AdminEmail"] ?? "admin@myfitnessgym.example";
        var password = _configuration["Seed:AdminPassword"];
        var isDefaultPassword = string.IsNullOrWhiteSpace(password);
        if (isDefaultPassword) password = "Admin@12345";

        var admin = new User
        {
            UserName = userName,
            Email = email,
            FullName = _configuration["Seed:AdminFullName"] ?? "System Administrator",
            PasswordHash = _hasher.Hash(password!),
            Status = UserStatus.Active,
            // Force a change when the fallback password was used.
            MustChangePassword = isDefaultPassword
        };

        _db.Users.Add(admin);
        await _db.SaveChangesAsync(ct);

        _db.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync(ct);

        if (isDefaultPassword)
        {
            _logger.LogWarning(
                "Created the administrator account '{UserName}' with the default password. " +
                "Sign in and change it immediately, or set Seed:AdminPassword before first run.",
                userName);
        }
        else
        {
            _logger.LogInformation("Created the administrator account '{UserName}'.", userName);
        }
    }
}
