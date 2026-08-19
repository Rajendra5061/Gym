using GymManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymManagement.Infrastructure.Data.Configurations;

public class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    public void Configure(EntityTypeBuilder<Member> b)
    {
        b.ToTable("Members");
        b.Property(x => x.MemberCode).HasMaxLength(32).IsRequired();
        b.Property(x => x.FullName).HasMaxLength(160).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(24).IsRequired();
        b.Property(x => x.Email).HasMaxLength(160);
        b.Property(x => x.Address).HasMaxLength(512);
        b.Property(x => x.City).HasMaxLength(96);
        b.Property(x => x.PostalCode).HasMaxLength(16);
        b.Property(x => x.EmergencyContactName).HasMaxLength(160);
        b.Property(x => x.EmergencyContactPhone).HasMaxLength(24);
        b.Property(x => x.EmergencyContactRelation).HasMaxLength(64);
        b.Property(x => x.ProfilePhotoPath).HasMaxLength(512);
        b.Property(x => x.BloodGroup).HasMaxLength(8);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.HeightCm).HasColumnType("decimal(6,2)");
        b.Property(x => x.WeightKg).HasColumnType("decimal(6,2)");
        b.Property(x => x.Gender).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.JoiningDate).HasColumnType("date");
        b.Property(x => x.DateOfBirth).HasColumnType("date");

        b.HasIndex(x => x.MemberCode).IsUnique();
        b.HasIndex(x => x.Phone);
        b.HasIndex(x => x.FullName);
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.IsDeleted);
        b.HasIndex(x => x.JoiningDate);

        b.HasOne(x => x.AssignedTrainer)
            .WithMany(t => t.AssignedMembers)
            .HasForeignKey(x => x.AssignedTrainerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class MemberDocumentConfiguration : IEntityTypeConfiguration<MemberDocument>
{
    public void Configure(EntityTypeBuilder<MemberDocument> b)
    {
        b.ToTable("MemberDocuments");
        b.Property(x => x.DocumentType).HasMaxLength(64).IsRequired();
        b.Property(x => x.FileName).HasMaxLength(256).IsRequired();
        b.Property(x => x.FilePath).HasMaxLength(512).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(128);
        b.Property(x => x.Notes).HasMaxLength(512);

        b.HasIndex(x => x.MemberId);

        b.HasOne(x => x.Member).WithMany(m => m.Documents)
            .HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class MemberMeasurementConfiguration : IEntityTypeConfiguration<MemberMeasurement>
{
    public void Configure(EntityTypeBuilder<MemberMeasurement> b)
    {
        b.ToTable("MemberMeasurements");
        b.Property(x => x.MeasuredOn).HasColumnType("date");
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Ignore(x => x.Bmi);

        foreach (var p in new[] { nameof(MemberMeasurement.WeightKg), nameof(MemberMeasurement.HeightCm),
            nameof(MemberMeasurement.BodyFatPercent), nameof(MemberMeasurement.MuscleMassKg),
            nameof(MemberMeasurement.ChestCm), nameof(MemberMeasurement.WaistCm),
            nameof(MemberMeasurement.HipCm), nameof(MemberMeasurement.ArmCm), nameof(MemberMeasurement.ThighCm) })
        {
            b.Property(p).HasColumnType("decimal(6,2)");
        }

        b.HasIndex(x => new { x.MemberId, x.MeasuredOn });

        b.HasOne(x => x.Member).WithMany(m => m.Measurements)
            .HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.RecordedByTrainer).WithMany()
            .HasForeignKey(x => x.RecordedByTrainerId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class TrainerConfiguration : IEntityTypeConfiguration<Trainer>
{
    public void Configure(EntityTypeBuilder<Trainer> b)
    {
        b.ToTable("Trainers");
        b.Property(x => x.TrainerCode).HasMaxLength(32).IsRequired();
        b.Property(x => x.FullName).HasMaxLength(160).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(24).IsRequired();
        b.Property(x => x.Email).HasMaxLength(160);
        b.Property(x => x.Address).HasMaxLength(512);
        b.Property(x => x.Specialization).HasMaxLength(256);
        b.Property(x => x.Certifications).HasMaxLength(1000);
        b.Property(x => x.PhotoPath).HasMaxLength(512);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.CommissionPercent).HasColumnType("decimal(5,2)");
        b.Property(x => x.Gender).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.JoiningDate).HasColumnType("date");

        b.HasIndex(x => x.TrainerCode).IsUnique();
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.IsDeleted);
    }
}

public class AttendanceConfiguration : IEntityTypeConfiguration<Attendance>
{
    public void Configure(EntityTypeBuilder<Attendance> b)
    {
        b.ToTable("Attendance");
        b.Property(x => x.AttendanceDate).HasColumnType("date");
        b.Property(x => x.CheckInMethod).HasMaxLength(32).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(512);
        b.Property(x => x.Status).HasConversion<int>();

        // Enforces the duplicate check-in rule at the database level.
        b.HasIndex(x => new { x.MemberId, x.AttendanceDate }).IsUnique();
        b.HasIndex(x => x.AttendanceDate);
        b.HasIndex(x => x.Status);

        b.HasOne(x => x.Member).WithMany(m => m.AttendanceRecords)
            .HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Subscription).WithMany()
            .HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.SetNull);
    }
}
