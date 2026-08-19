using GymManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymManagement.Infrastructure.Data.Configurations;

public class ExerciseConfiguration : IEntityTypeConfiguration<Exercise>
{
    public void Configure(EntityTypeBuilder<Exercise> b)
    {
        b.ToTable("Exercises");
        b.Property(x => x.Name).HasMaxLength(160).IsRequired();
        b.Property(x => x.SecondaryMuscleGroups).HasMaxLength(256);
        b.Property(x => x.Equipment).HasMaxLength(160);
        b.Property(x => x.Instructions).HasMaxLength(4000);
        b.Property(x => x.VideoUrl).HasMaxLength(512);
        b.Property(x => x.ImagePath).HasMaxLength(512);
        b.Property(x => x.CaloriesPerMinute).HasColumnType("decimal(6,2)");
        b.Property(x => x.Category).HasConversion<int>();
        b.Property(x => x.PrimaryMuscleGroup).HasConversion<int>();
        b.Property(x => x.Difficulty).HasConversion<int>();

        b.HasIndex(x => x.Name).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x => x.Category);
        b.HasIndex(x => x.PrimaryMuscleGroup);
    }
}

public class WorkoutPlanConfiguration : IEntityTypeConfiguration<WorkoutPlan>
{
    public void Configure(EntityTypeBuilder<WorkoutPlan> b)
    {
        b.ToTable("WorkoutPlans");
        b.Property(x => x.Name).HasMaxLength(160).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.Goal).HasMaxLength(256);
        b.Property(x => x.Difficulty).HasConversion<int>();

        b.HasIndex(x => x.Name);
        b.HasIndex(x => x.IsActive);
    }
}

public class WorkoutPlanExerciseConfiguration : IEntityTypeConfiguration<WorkoutPlanExercise>
{
    public void Configure(EntityTypeBuilder<WorkoutPlanExercise> b)
    {
        b.ToTable("WorkoutPlanExercises");
        b.Property(x => x.Notes).HasMaxLength(512);
        b.Property(x => x.TargetWeightKg).HasColumnType("decimal(7,2)");

        b.HasIndex(x => new { x.WorkoutPlanId, x.DisplayOrder });

        b.HasOne(x => x.WorkoutPlan).WithMany(p => p.Exercises)
            .HasForeignKey(x => x.WorkoutPlanId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Exercise).WithMany(e => e.PlanExercises)
            .HasForeignKey(x => x.ExerciseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class MemberWorkoutPlanConfiguration : IEntityTypeConfiguration<MemberWorkoutPlan>
{
    public void Configure(EntityTypeBuilder<MemberWorkoutPlan> b)
    {
        b.ToTable("MemberWorkoutPlans");
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.StartDate).HasColumnType("date");
        b.Property(x => x.EndDate).HasColumnType("date");

        b.HasIndex(x => new { x.MemberId, x.IsActive });

        b.HasOne(x => x.Member).WithMany(m => m.WorkoutPlans)
            .HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.WorkoutPlan).WithMany(p => p.Assignments)
            .HasForeignKey(x => x.WorkoutPlanId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Trainer).WithMany(t => t.AssignedWorkoutPlans)
            .HasForeignKey(x => x.TrainerId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class WorkoutSessionConfiguration : IEntityTypeConfiguration<WorkoutSession>
{
    public void Configure(EntityTypeBuilder<WorkoutSession> b)
    {
        b.ToTable("WorkoutSessions");
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.SessionDate).HasColumnType("date");
        b.Property(x => x.CaloriesBurned).HasColumnType("decimal(8,2)");

        b.HasIndex(x => new { x.MemberId, x.SessionDate });
        b.HasIndex(x => x.SessionDate);
        b.HasIndex(x => x.TrainerId);

        b.HasOne(x => x.Member).WithMany(m => m.WorkoutSessions)
            .HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Trainer).WithMany(t => t.WorkoutSessions)
            .HasForeignKey(x => x.TrainerId).OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.WorkoutPlan).WithMany()
            .HasForeignKey(x => x.WorkoutPlanId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class WorkoutSessionExerciseConfiguration : IEntityTypeConfiguration<WorkoutSessionExercise>
{
    public void Configure(EntityTypeBuilder<WorkoutSessionExercise> b)
    {
        b.ToTable("WorkoutSessionExercises");
        b.Property(x => x.Notes).HasMaxLength(512);
        b.Property(x => x.WeightKg).HasColumnType("decimal(7,2)");
        b.Property(x => x.CaloriesBurned).HasColumnType("decimal(8,2)");

        b.HasIndex(x => new { x.WorkoutSessionId, x.DisplayOrder });

        b.HasOne(x => x.WorkoutSession).WithMany(s => s.Exercises)
            .HasForeignKey(x => x.WorkoutSessionId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Exercise).WithMany()
            .HasForeignKey(x => x.ExerciseId).OnDelete(DeleteBehavior.Restrict);
    }
}
