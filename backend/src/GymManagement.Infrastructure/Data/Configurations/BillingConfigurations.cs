using GymManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymManagement.Infrastructure.Data.Configurations;

public class MembershipPlanConfiguration : IEntityTypeConfiguration<MembershipPlan>
{
    public void Configure(EntityTypeBuilder<MembershipPlan> b)
    {
        b.ToTable("MembershipPlans");
        b.Property(x => x.PlanCode).HasMaxLength(32).IsRequired();
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.Features).HasMaxLength(2000);
        b.Property(x => x.TaxPercent).HasColumnType("decimal(5,2)");
        b.Property(x => x.MaxDiscountPercent).HasColumnType("decimal(5,2)");
        b.Property(x => x.DurationType).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();
        b.Ignore(x => x.TotalDays);

        b.HasIndex(x => x.PlanCode).IsUnique();
        b.HasIndex(x => x.Status);
    }
}

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> b)
    {
        b.ToTable("Subscriptions");
        b.Property(x => x.SubscriptionCode).HasMaxLength(32).IsRequired();
        b.Property(x => x.StartDate).HasColumnType("date");
        b.Property(x => x.EndDate).HasColumnType("date");
        b.Property(x => x.FreezeStartDate).HasColumnType("date");
        b.Property(x => x.FreezeEndDate).HasColumnType("date");
        b.Property(x => x.TaxPercent).HasColumnType("decimal(5,2)");
        b.Property(x => x.CancellationReason).HasMaxLength(512);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.PaymentStatus).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();

        b.Ignore(x => x.OutstandingAmount);
        b.Ignore(x => x.EffectiveEndDate);

        b.HasIndex(x => x.SubscriptionCode).IsUnique();
        b.HasIndex(x => new { x.MemberId, x.Status });
        b.HasIndex(x => x.EndDate);
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.PaymentStatus);
        b.HasIndex(x => x.IsDeleted);

        b.HasOne(x => x.Member).WithMany(m => m.Subscriptions)
            .HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.MembershipPlan).WithMany(p => p.Subscriptions)
            .HasForeignKey(x => x.MembershipPlanId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.PreviousSubscription).WithMany()
            .HasForeignKey(x => x.PreviousSubscriptionId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.AssignedTrainer).WithMany()
            .HasForeignKey(x => x.AssignedTrainerId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class SubscriptionHistoryConfiguration : IEntityTypeConfiguration<SubscriptionHistory>
{
    public void Configure(EntityTypeBuilder<SubscriptionHistory> b)
    {
        b.ToTable("SubscriptionHistory");
        b.Property(x => x.Remarks).HasMaxLength(1000);
        b.Property(x => x.ActionType).HasConversion<int>();
        b.Property(x => x.OldStatus).HasConversion<int>();
        b.Property(x => x.NewStatus).HasConversion<int>();
        b.Property(x => x.OldEndDate).HasColumnType("date");
        b.Property(x => x.NewEndDate).HasColumnType("date");

        b.HasIndex(x => x.SubscriptionId);
        b.HasIndex(x => x.ActionDate);

        b.HasOne(x => x.Subscription).WithMany(s => s.History)
            .HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.PerformedByUser).WithMany()
            .HasForeignKey(x => x.PerformedBy).OnDelete(DeleteBehavior.SetNull);
    }
}

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> b)
    {
        b.ToTable("PaymentMethods");
        b.Property(x => x.Code).HasMaxLength(32).IsRequired();
        b.Property(x => x.Name).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("Payments");
        b.Property(x => x.ReceiptNumber).HasMaxLength(32).IsRequired();
        b.Property(x => x.TransactionReference).HasMaxLength(128);
        b.Property(x => x.PayerVpa).HasMaxLength(128);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.Status).HasConversion<int>();
        b.Ignore(x => x.RefundableAmount);

        b.HasIndex(x => x.ReceiptNumber).IsUnique();
        b.HasIndex(x => x.PaymentDate);
        b.HasIndex(x => new { x.MemberId, x.PaymentDate });
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.IsDeleted);
        b.HasIndex(x => x.MembershipPlanId);

        // Guards against the same UPI/bank reference being recorded twice.
        b.HasIndex(x => x.TransactionReference)
            .IsUnique()
            .HasFilter("[TransactionReference] IS NOT NULL AND [IsDeleted] = 0");

        b.HasOne(x => x.Member).WithMany(m => m.Payments)
            .HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Subscription).WithMany(s => s.Payments)
            .HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);

        // Optional and standalone: a payment may name the plan it was collected for without any
        // subscription existing. Restricted rather than cascading — deleting a plan must never
        // silently rewrite an accounting record.
        b.HasOne(x => x.MembershipPlan).WithMany()
            .HasForeignKey(x => x.MembershipPlanId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.PaymentMethod).WithMany(m => m.Payments)
            .HasForeignKey(x => x.PaymentMethodId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.CollectedByUser).WithMany()
            .HasForeignKey(x => x.CollectedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> b)
    {
        b.ToTable("PaymentRefunds");
        b.Property(x => x.RefundNumber).HasMaxLength(32).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(512).IsRequired();
        b.Property(x => x.RefundMethod).HasMaxLength(64);
        b.Property(x => x.TransactionReference).HasMaxLength(128);
        b.Property(x => x.Remarks).HasMaxLength(1000);
        b.Property(x => x.Status).HasConversion<int>();

        b.HasIndex(x => x.RefundNumber).IsUnique();
        b.HasIndex(x => x.PaymentId);
        b.HasIndex(x => x.Status);

        b.HasOne(x => x.Payment).WithMany(p => p.Refunds)
            .HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> b)
    {
        b.ToTable("ExpenseCategories");
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Description).HasMaxLength(512);
        b.HasIndex(x => x.Name).IsUnique();
    }
}

public class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> b)
    {
        b.ToTable("Expenses");
        b.Property(x => x.ExpenseNumber).HasMaxLength(32).IsRequired();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.VendorName).HasMaxLength(160);
        b.Property(x => x.ReferenceNumber).HasMaxLength(64);
        b.Property(x => x.AttachmentPath).HasMaxLength(512);
        b.Property(x => x.ExpenseDate).HasColumnType("date");

        b.HasIndex(x => x.ExpenseNumber).IsUnique();
        b.HasIndex(x => x.ExpenseDate);
        b.HasIndex(x => x.ExpenseCategoryId);

        b.HasOne(x => x.ExpenseCategory).WithMany(c => c.Expenses)
            .HasForeignKey(x => x.ExpenseCategoryId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.PaymentMethod).WithMany()
            .HasForeignKey(x => x.PaymentMethodId).OnDelete(DeleteBehavior.SetNull);
    }
}
