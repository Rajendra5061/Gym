using GymManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymManagement.Infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.Property(x => x.UserName).HasMaxLength(64).IsRequired();
        b.Property(x => x.Email).HasMaxLength(160).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(24);
        b.Property(x => x.FullName).HasMaxLength(160).IsRequired();
        b.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
        b.Property(x => x.ProfilePhotoPath).HasMaxLength(512);
        b.Property(x => x.PasswordResetTokenHash).HasMaxLength(256);
        b.Property(x => x.Status).HasConversion<int>();

        b.HasIndex(x => x.UserName).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x => x.Email).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.IsDeleted);

        b.HasOne(x => x.Member)
            .WithMany()
            .HasForeignKey(x => x.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Trainer)
            .WithMany()
            .HasForeignKey(x => x.TrainerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("Roles");
        b.Property(x => x.Name).HasMaxLength(64).IsRequired();
        b.Property(x => x.Description).HasMaxLength(256);
        b.HasIndex(x => x.Name).IsUnique();
    }
}

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b)
    {
        b.ToTable("Permissions");
        b.Property(x => x.Code).HasMaxLength(80).IsRequired();
        b.Property(x => x.Module).HasMaxLength(64).IsRequired();
        b.Property(x => x.Description).HasMaxLength(256).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.HasIndex(x => x.Module);
    }
}

public class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.ToTable("UserRoles");
        b.HasIndex(x => new { x.UserId, x.RoleId }).IsUnique();

        b.HasOne(x => x.User).WithMany(u => u.UserRoles)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Role).WithMany(r => r.UserRoles)
            .HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.ToTable("RolePermissions");
        b.HasIndex(x => new { x.RoleId, x.PermissionId }).IsUnique();

        b.HasOne(x => x.Role).WithMany(r => r.RolePermissions)
            .HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Permission).WithMany(p => p.RolePermissions)
            .HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("RefreshTokens");
        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
        b.Property(x => x.CreatedByIp).HasMaxLength(64);
        b.Property(x => x.RevokedReason).HasMaxLength(256);
        b.Ignore(x => x.IsActive);

        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });

        b.HasOne(x => x.User).WithMany(u => u.RefreshTokens)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class LoginAttemptConfiguration : IEntityTypeConfiguration<LoginAttempt>
{
    public void Configure(EntityTypeBuilder<LoginAttempt> b)
    {
        b.ToTable("LoginAttempts");
        b.Property(x => x.UserNameOrEmail).HasMaxLength(160).IsRequired();
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.Property(x => x.DeviceInfo).HasMaxLength(256);
        b.Property(x => x.FailureReason).HasMaxLength(256);
        b.Property(x => x.Result).HasConversion<int>();

        b.HasIndex(x => x.AttemptedAtUtc);
        b.HasIndex(x => new { x.UserNameOrEmail, x.AttemptedAtUtc });

        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
    }
}
