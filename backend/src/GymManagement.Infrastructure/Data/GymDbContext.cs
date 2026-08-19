using GymManagement.Application.Common;
using GymManagement.Domain.Common;
using GymManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GymManagement.Infrastructure.Data;

/// <summary>
/// EF Core context for the whole application. Soft-deleted rows are hidden by global query
/// filters; the recycle bin uses <c>IgnoreQueryFilters()</c> to see them.
/// </summary>
public class GymDbContext : DbContext
{
    private readonly ICurrentUserService? _currentUser;
    private readonly IDateTimeProvider? _clock;

    public GymDbContext(DbContextOptions<GymDbContext> options) : base(options) { }

    public GymDbContext(DbContextOptions<GymDbContext> options,
        ICurrentUserService? currentUser,
        IDateTimeProvider? clock) : base(options)
    {
        _currentUser = currentUser;
        _clock = clock;
    }

    // Identity & security
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();

    // Members & trainers
    public DbSet<Member> Members => Set<Member>();
    public DbSet<MemberDocument> MemberDocuments => Set<MemberDocument>();
    public DbSet<MemberMeasurement> MemberMeasurements => Set<MemberMeasurement>();
    public DbSet<Trainer> Trainers => Set<Trainer>();

    // Workouts
    public DbSet<Exercise> Exercises => Set<Exercise>();
    public DbSet<WorkoutPlan> WorkoutPlans => Set<WorkoutPlan>();
    public DbSet<WorkoutPlanExercise> WorkoutPlanExercises => Set<WorkoutPlanExercise>();
    public DbSet<MemberWorkoutPlan> MemberWorkoutPlans => Set<MemberWorkoutPlan>();
    public DbSet<WorkoutSession> WorkoutSessions => Set<WorkoutSession>();
    public DbSet<WorkoutSessionExercise> WorkoutSessionExercises => Set<WorkoutSessionExercise>();

    // Diet
    public DbSet<DietPlan> DietPlans => Set<DietPlan>();
    public DbSet<DietPlanMeal> DietPlanMeals => Set<DietPlanMeal>();

    // Attendance
    public DbSet<Attendance> Attendance => Set<Attendance>();

    // Billing
    public DbSet<MembershipPlan> MembershipPlans => Set<MembershipPlan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionHistory> SubscriptionHistory => Set<SubscriptionHistory>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentRefund> PaymentRefunds => Set<PaymentRefund>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();

    /// <summary>Verified gateway webhook deliveries. Doubles as the idempotency ledger.</summary>
    public DbSet<PaymentGatewayEvent> PaymentGatewayEvents => Set<PaymentGatewayEvent>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<SalaryPayment> SalaryPayments => Set<SalaryPayment>();

    // Operations
    public DbSet<Equipment> Equipment => Set<Equipment>();
    public DbSet<Enquiry> Enquiries => Set<Enquiry>();
    public DbSet<Feedback> Feedback => Set<Feedback>();

    // System
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<GymSetting> GymSettings => Set<GymSetting>();
    public DbSet<License> Licenses => Set<License>();
    public DbSet<BackupRecord> BackupRecords => Set<BackupRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GymDbContext).Assembly);

        // Money: every decimal defaults to 18,2 unless a configuration overrides it.
        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            if (property.GetColumnType() is null)
                property.SetColumnType("decimal(18,2)");
        }

        // DateTime2 keeps millisecond precision and avoids the SQL Server datetime range trap.
        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)))
        {
            if (property.GetColumnType() is null)
                property.SetColumnType("datetime2");
        }

        ApplySoftDeleteFilters(modelBuilder);
    }

    /// <summary>Adds <c>WHERE IsDeleted = 0</c> to every soft-deletable entity.</summary>
    private static void ApplySoftDeleteFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType)) continue;

            var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
            var body = System.Linq.Expressions.Expression.Equal(
                System.Linq.Expressions.Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted)),
                System.Linq.Expressions.Expression.Constant(false));

            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter(System.Linq.Expressions.Expression.Lambda(body, parameter));
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAuditColumns();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        StampAuditColumns();
        return base.SaveChanges();
    }

    /// <summary>Fills CreatedAt/CreatedBy/UpdatedAt/UpdatedBy and converts hard deletes to soft deletes.</summary>
    private void StampAuditColumns()
    {
        var now = _clock?.UtcNow ?? DateTime.UtcNow;
        var userId = _currentUser?.UserId;

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy ??= userId;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = userId ?? entry.Entity.UpdatedBy;
                    entry.Property(nameof(AuditableEntity.CreatedAt)).IsModified = false;
                    entry.Property(nameof(AuditableEntity.CreatedBy)).IsModified = false;
                    break;
            }
        }

        // A caller that removes a soft-deletable entity gets a soft delete instead. Permanent
        // deletion goes through the recycle bin service, which uses ExecuteDelete.
        foreach (var entry in ChangeTracker.Entries<SoftDeletableEntity>()
                     .Where(e => e.State == EntityState.Deleted))
        {
            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;
            entry.Entity.DeletedAt = now;
            entry.Entity.DeletedBy = userId;
        }
    }
}
