using GymManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymManagement.Infrastructure.Data.Configurations;

/// <summary>
/// The webhook idempotency ledger.
///
/// The unique index over <c>(Provider, EventId)</c> is the whole mechanism: the settlement path
/// inserts its claim row first, so two concurrent deliveries of one gateway event race on the
/// index and exactly one of them can win. Nothing here depends on the application checking first.
/// </summary>
public class PaymentGatewayEventConfiguration : IEntityTypeConfiguration<PaymentGatewayEvent>
{
    public void Configure(EntityTypeBuilder<PaymentGatewayEvent> b)
    {
        b.ToTable("PaymentGatewayEvents");

        b.Property(x => x.Provider).HasMaxLength(64).IsRequired();
        b.Property(x => x.EventId).HasMaxLength(128).IsRequired();
        b.Property(x => x.EventType).HasMaxLength(96);
        b.Property(x => x.PaymentReference).HasMaxLength(128);
        b.Property(x => x.Currency).HasMaxLength(8);
        b.Property(x => x.GatewayStatus).HasMaxLength(48);
        b.Property(x => x.GatewayTransactionId).HasMaxLength(128);
        b.Property(x => x.PayloadDigest).HasMaxLength(64);
        b.Property(x => x.Detail).HasMaxLength(512);
        b.Property(x => x.Outcome).HasConversion<int>();

        b.HasIndex(x => new { x.Provider, x.EventId }).IsUnique();
        b.HasIndex(x => x.PaymentReference);
        b.HasIndex(x => x.ReceivedAtUtc);

        // A settled payment must never be deletable out from under its evidence.
        b.HasOne(x => x.Payment).WithMany()
            .HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
    }
}
