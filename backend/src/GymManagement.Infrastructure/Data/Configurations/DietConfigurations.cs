using GymManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymManagement.Infrastructure.Data.Configurations;

public class DietPlanConfiguration : IEntityTypeConfiguration<DietPlan>
{
    public void Configure(EntityTypeBuilder<DietPlan> b)
    {
        b.ToTable("DietPlans");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Goal).HasMaxLength(256);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.StartDate).HasColumnType("date");
        b.Property(x => x.EndDate).HasColumnType("date");
        b.Property(x => x.Status).HasConversion<int>();

        b.HasIndex(x => new { x.MemberId, x.Status });
        b.HasIndex(x => x.TrainerId);
        b.HasIndex(x => x.IsDeleted);

        b.HasOne(x => x.Member).WithMany()
            .HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Trainer).WithMany()
            .HasForeignKey(x => x.TrainerId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class DietPlanMealConfiguration : IEntityTypeConfiguration<DietPlanMeal>
{
    public void Configure(EntityTypeBuilder<DietPlanMeal> b)
    {
        b.ToTable("DietPlanMeals");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.MealType).HasConversion<int>();

        b.HasIndex(x => new { x.DietPlanId, x.DisplayOrder });

        b.HasOne(x => x.DietPlan).WithMany(p => p.Meals)
            .HasForeignKey(x => x.DietPlanId).OnDelete(DeleteBehavior.Cascade);
    }
}
