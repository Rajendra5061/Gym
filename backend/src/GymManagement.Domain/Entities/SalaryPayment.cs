using GymManagement.Domain.Common;

namespace GymManagement.Domain.Entities;

/// <summary>
/// One month's salary paid to a trainer. At most one live row exists per trainer and period; the
/// matching operating expense is written by the service when the payment is recorded.
/// </summary>
public class SalaryPayment : SoftDeletableEntity
{
    public int TrainerId { get; set; }
    public Trainer? Trainer { get; set; }

    public int PeriodYear { get; set; }

    /// <summary>1 = January … 12 = December.</summary>
    public int PeriodMonth { get; set; }

    public decimal BaseAmount { get; set; }
    public decimal Bonus { get; set; }
    public decimal Deduction { get; set; }

    /// <summary>Always computed on the server as Base + Bonus − Deduction.</summary>
    public decimal NetAmount { get; set; }

    public DateTime PaymentDate { get; set; } = DateTime.Today;

    public int? PaymentMethodId { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }

    public string? TransactionReference { get; set; }
    public string? Notes { get; set; }
}
