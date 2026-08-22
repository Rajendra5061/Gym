using GymManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymManagement.Infrastructure.Data.Configurations;

public class MemberNotificationLogConfiguration : IEntityTypeConfiguration<MemberNotificationLog>
{
    public void Configure(EntityTypeBuilder<MemberNotificationLog> b)
    {
        b.ToTable("MemberNotificationLogs");
        b.Property(x => x.DeduplicationKey).HasMaxLength(96).IsRequired();
        b.Property(x => x.Detail).HasMaxLength(300);
        b.Property(x => x.SentOnDate).HasColumnType("date");

        // The idempotency guarantee: one message per member per occasion, enforced by the
        // database rather than by whichever process happens to be dispatching.
        b.HasIndex(x => new { x.MemberId, x.Kind, x.DeduplicationKey }).IsUnique();

        // The operator's questions: "what went out today?" and "what did this member get?".
        b.HasIndex(x => x.SentOnDate);

        b.HasOne(x => x.Member).WithMany()
            .HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Cascade);
    }
}
