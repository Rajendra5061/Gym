using GymManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymManagement.Infrastructure.Data.Configurations;

public class EquipmentConfiguration : IEntityTypeConfiguration<Equipment>
{
    public void Configure(EntityTypeBuilder<Equipment> b)
    {
        b.ToTable("Equipment");
        b.Property(x => x.Name).HasMaxLength(160).IsRequired();
        b.Property(x => x.Code).HasMaxLength(32).IsRequired();
        b.Property(x => x.Category).HasMaxLength(64).IsRequired();
        b.Property(x => x.SerialNumber).HasMaxLength(96);
        b.Property(x => x.Manufacturer).HasMaxLength(120);
        b.Property(x => x.Location).HasMaxLength(120);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.PurchaseCost).HasColumnType("decimal(18,2)");
        b.Property(x => x.PurchaseDate).HasColumnType("date");
        b.Property(x => x.WarrantyExpiry).HasColumnType("date");
        b.Property(x => x.LastServicedOn).HasColumnType("date");
        b.Property(x => x.NextServiceDue).HasColumnType("date");
        b.Property(x => x.Condition).HasConversion<int>();

        b.HasIndex(x => x.Code).IsUnique();
        b.HasIndex(x => x.Category);
        b.HasIndex(x => x.Condition);
        b.HasIndex(x => x.NextServiceDue);
        b.HasIndex(x => x.IsActive);
        b.HasIndex(x => x.IsDeleted);
    }
}

public class EnquiryConfiguration : IEntityTypeConfiguration<Enquiry>
{
    public void Configure(EntityTypeBuilder<Enquiry> b)
    {
        b.ToTable("Enquiries");
        b.Property(x => x.FullName).HasMaxLength(160).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(24).IsRequired();
        b.Property(x => x.Email).HasMaxLength(160);
        b.Property(x => x.Message).HasMaxLength(2000);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.FollowUpDate).HasColumnType("date");
        b.Property(x => x.Source).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();

        b.HasIndex(x => x.Phone);
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.Source);
        b.HasIndex(x => x.FollowUpDate);
        b.HasIndex(x => x.AssignedToUserId);
        b.HasIndex(x => x.IsDeleted);

        b.HasOne(x => x.InterestedPlan).WithMany()
            .HasForeignKey(x => x.InterestedPlanId).OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.AssignedToUser).WithMany()
            .HasForeignKey(x => x.AssignedToUserId).OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.ConvertedMember).WithMany()
            .HasForeignKey(x => x.ConvertedMemberId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class FeedbackConfiguration : IEntityTypeConfiguration<Feedback>
{
    public void Configure(EntityTypeBuilder<Feedback> b)
    {
        b.ToTable("Feedback");
        b.Property(x => x.Subject).HasMaxLength(200);
        b.Property(x => x.Message).HasMaxLength(4000).IsRequired();
        b.Property(x => x.AdminResponse).HasMaxLength(4000);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.IsPrivate).HasDefaultValue(true);

        b.HasIndex(x => x.MemberId);
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.Rating);
        b.HasIndex(x => x.CreatedAt);
        b.HasIndex(x => x.IsDeleted);

        b.HasOne(x => x.Member).WithMany()
            .HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.RespondedByUser).WithMany()
            .HasForeignKey(x => x.RespondedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
