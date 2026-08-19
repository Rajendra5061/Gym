using GymManagement.Application.Common;

namespace GymManagement.Application.DTOs;

public class ExpenseCategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal TotalAmount { get; set; }
    public int ExpenseCount { get; set; }
}

public class ExpenseDto
{
    public int Id { get; set; }
    public string ExpenseNumber { get; set; } = string.Empty;
    public int ExpenseCategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public DateTime ExpenseDate { get; set; }
    public int? PaymentMethodId { get; set; }
    public string? PaymentMethodName { get; set; }
    public string? VendorName { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? AttachmentPath { get; set; }
    public string? RecordedByName { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SaveExpenseDto
{
    public int Id { get; set; }
    public int ExpenseCategoryId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public DateTime ExpenseDate { get; set; } = DateTime.Today;
    public int? PaymentMethodId { get; set; }
    public string? VendorName { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? AttachmentPath { get; set; }
}

public class ExpenseQueryDto : PagedRequest
{
    public int? ExpenseCategoryId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public bool IncludeDeleted { get; set; }
}
