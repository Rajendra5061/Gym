using GymManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymManagement.Infrastructure.Data.Configurations;

public class SalaryPaymentConfiguration : IEntityTypeConfiguration<SalaryPayment>
{
    public void Configure(EntityTypeBuilder<SalaryPayment> b)
    {
        b.ToTable("SalaryPayments");
        b.Property(x => x.TransactionReference).HasMaxLength(128);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.PaymentDate).HasColumnType("date");

        // Enforces "one live salary row per trainer and period" at the database level; soft-deleted
        // rows fall outside the filter so a period can be re-recorded after a delete.
        b.HasIndex(x => new { x.TrainerId, x.PeriodYear, x.PeriodMonth })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        b.HasIndex(x => new { x.PeriodYear, x.PeriodMonth });
        b.HasIndex(x => x.IsDeleted);

        b.HasOne(x => x.Trainer).WithMany()
            .HasForeignKey(x => x.TrainerId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.PaymentMethod).WithMany()
            .HasForeignKey(x => x.PaymentMethodId).OnDelete(DeleteBehavior.SetNull);
    }
}
