using GymManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymManagement.Infrastructure.Data.Configurations;

public class ExpiryReminderEmailConfiguration : IEntityTypeConfiguration<ExpiryReminderEmail>
{
    public void Configure(EntityTypeBuilder<ExpiryReminderEmail> b)
    {
        b.ToTable("ExpiryReminderEmails");
        b.Property(x => x.SentOnDate).HasColumnType("date");
        b.Property(x => x.EndDateAtSend).HasColumnType("date");

        // The idempotency guarantee: one reminder per member per day, enforced by the database
        // rather than by whichever process happens to run the mailer.
        b.HasIndex(x => new { x.MemberId, x.SentOnDate }).IsUnique();

        b.Property(x => x.EmailSent).HasDefaultValue(false);
        b.Property(x => x.SmsSent).HasDefaultValue(false);

        b.HasOne(x => x.Member).WithMany()
            .HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Cascade);

        // No cascade from the subscription: the log outlives a renewal that replaces the term.
        b.HasOne(x => x.Subscription).WithMany()
            .HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.NoAction);
    }
}
