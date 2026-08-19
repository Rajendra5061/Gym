using GymManagement.Application.Common;

namespace GymManagement.Application.DTOs;

public class SalaryPaymentDto
{
    public int Id { get; set; }
    public int TrainerId { get; set; }
    public string TrainerName { get; set; } = string.Empty;
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal Bonus { get; set; }
    public decimal Deduction { get; set; }
    public decimal NetAmount { get; set; }
    public DateTime PaymentDate { get; set; }
    public int? PaymentMethodId { get; set; }
    public string? PaymentMethodName { get; set; }
    public string? TransactionReference { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SaveSalaryPaymentDto
{
    public int TrainerId { get; set; }
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal Bonus { get; set; }
    public decimal Deduction { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.Today;
    public int? PaymentMethodId { get; set; }
    public string? TransactionReference { get; set; }
    public string? Notes { get; set; }
}

public class SalaryQueryDto : PagedRequest
{
    public int? Year { get; set; }
    public int? Month { get; set; }
    public int? TrainerId { get; set; }
}

/// <summary>Per-month and per-trainer salary totals for one calendar year.</summary>
public class SalarySummaryDto
{
    public int Year { get; set; }
    public List<SalaryMonthTotalDto> Months { get; set; } = new();
    public decimal TotalYear { get; set; }
    public List<SalaryTrainerTotalDto> PerTrainer { get; set; } = new();
}

public class SalaryMonthTotalDto
{
    public int Month { get; set; }
    public decimal TotalNet { get; set; }
    public int Payments { get; set; }
}

public class SalaryTrainerTotalDto
{
    public int TrainerId { get; set; }
    public string TrainerName { get; set; } = string.Empty;
    public decimal? MonthlySalary { get; set; }
    public int PaidCount { get; set; }
    public decimal TotalNet { get; set; }
}
