using GymManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymManagement.Infrastructure.Data.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("Notifications");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Message).HasMaxLength(2000).IsRequired();
        b.Property(x => x.EntityName).HasMaxLength(64);
        b.Property(x => x.DeduplicationKey).HasMaxLength(160);
        b.Property(x => x.Type).HasConversion<int>();
        b.Property(x => x.Severity).HasConversion<int>();

        b.HasIndex(x => new { x.UserId, x.IsRead });
        b.HasIndex(x => x.CreatedAtUtc);
        b.HasIndex(x => x.MemberId);
        b.HasIndex(x => x.DeduplicationKey)
            .IsUnique()
            .HasFilter("[DeduplicationKey] IS NOT NULL");

        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Member).WithMany()
            .HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs");
        b.Property(x => x.UserName).HasMaxLength(160);
        b.Property(x => x.Action).HasMaxLength(64).IsRequired();
        b.Property(x => x.EntityName).HasMaxLength(64).IsRequired();
        b.Property(x => x.OldValues).HasMaxLength(4000);
        b.Property(x => x.NewValues).HasMaxLength(4000);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.Property(x => x.DeviceInfo).HasMaxLength(256);

        b.HasIndex(x => x.ChangedAtUtc);
        b.HasIndex(x => new { x.EntityName, x.EntityId });
        b.HasIndex(x => x.UserId);
        b.HasIndex(x => x.Action);

        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> b)
    {
        b.ToTable("SystemSettings");
        b.Property(x => x.Key).HasMaxLength(120).IsRequired();
        b.Property(x => x.Value).HasMaxLength(2000);
        b.Property(x => x.DataType).HasMaxLength(32).IsRequired();
        b.Property(x => x.Category).HasMaxLength(64).IsRequired();
        b.Property(x => x.Description).HasMaxLength(512);

        b.HasIndex(x => x.Key).IsUnique();
        b.HasIndex(x => x.Category);
    }
}

public class GymSettingConfiguration : IEntityTypeConfiguration<GymSetting>
{
    public void Configure(EntityTypeBuilder<GymSetting> b)
    {
        b.ToTable("GymSettings");
        b.Property(x => x.GymName).HasMaxLength(200).IsRequired();
        b.Property(x => x.LegalName).HasMaxLength(200);
        b.Property(x => x.Address).HasMaxLength(512);
        b.Property(x => x.City).HasMaxLength(96);
        b.Property(x => x.State).HasMaxLength(96);
        b.Property(x => x.PostalCode).HasMaxLength(16);
        b.Property(x => x.Country).HasMaxLength(96);
        b.Property(x => x.Phone).HasMaxLength(24);
        b.Property(x => x.Email).HasMaxLength(160);
        b.Property(x => x.Website).HasMaxLength(256);
        b.Property(x => x.LogoPath).HasMaxLength(512);
        b.Property(x => x.TaxNumber).HasMaxLength(64);
        b.Property(x => x.CurrencyCode).HasMaxLength(8).IsRequired();
        b.Property(x => x.CurrencySymbol).HasMaxLength(8).IsRequired();
        b.Property(x => x.UpiId).HasMaxLength(128);
        b.Property(x => x.UpiPayeeName).HasMaxLength(160);
        b.Property(x => x.UpiQrImagePath).HasMaxLength(512);
        b.Property(x => x.ReceiptPrefix).HasMaxLength(16);
        b.Property(x => x.MemberCodePrefix).HasMaxLength(16);
        b.Property(x => x.ReceiptFooterText).HasMaxLength(1000);
    }
}

public class LicenseConfiguration : IEntityTypeConfiguration<License>
{
    public void Configure(EntityTypeBuilder<License> b)
    {
        b.ToTable("Licenses");
        b.Property(x => x.LicenseKey).HasMaxLength(256);
        b.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
        b.Property(x => x.GymIdentifier).HasMaxLength(120).IsRequired();
        b.Property(x => x.MachineId).HasMaxLength(200);
        b.Property(x => x.EnabledFeatures).HasMaxLength(1000);
        b.Property(x => x.Signature).HasMaxLength(1024);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.Status).HasConversion<int>();

        b.HasIndex(x => x.LicenseKey).IsUnique().HasFilter("[LicenseKey] IS NOT NULL");
    }
}

public class BackupRecordConfiguration : IEntityTypeConfiguration<BackupRecord>
{
    public void Configure(EntityTypeBuilder<BackupRecord> b)
    {
        b.ToTable("BackupRecords");
        b.Property(x => x.FileName).HasMaxLength(256).IsRequired();
        b.Property(x => x.FilePath).HasMaxLength(512).IsRequired();
        b.Property(x => x.BackupType).HasMaxLength(32).IsRequired();
        b.Property(x => x.ErrorMessage).HasMaxLength(2000);
        b.Property(x => x.Notes).HasMaxLength(512);

        b.HasIndex(x => x.CreatedAtUtc);
    }
}
