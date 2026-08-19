using GymManagement.Application.Common;

namespace GymManagement.Application.DTOs;

/// <summary>Report identifiers understood by the reports screen and the export endpoints.</summary>
public enum ReportType
{
    MemberList = 1,
    ActiveMembers = 2,
    ExpiredMembers = 3,
    NewRegistrations = 4,
    SubscriptionReport = 5,
    RenewalReport = 6,
    AttendanceReport = 7,
    DailyPaymentReport = 8,
    MonthlyPaymentReport = 9,
    RevenueReport = 10,
    OutstandingPaymentReport = 11,
    TrainerReport = 12,
    WorkoutActivityReport = 13,
    AuditReport = 14,
    ExpenseReport = 15,
    ProfitAndLossReport = 16
}

public class ReportRequestDto : PagedRequest
{
    public ReportType ReportType { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int? MemberId { get; set; }
    public int? TrainerId { get; set; }
    public int? MembershipPlanId { get; set; }
    public int? PaymentMethodId { get; set; }
    public int? UserId { get; set; }
    public int? Status { get; set; }
    /// <summary>Day, Week or Month bucketing for the revenue report.</summary>
    public string GroupBy { get; set; } = "Day";
}

/// <summary>
/// Shape-agnostic report payload: the columns are described by metadata and the rows are
/// dictionaries, so one client-side table component can render every report.
/// </summary>
public class ReportResultDto
{
    public ReportType ReportType { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public string? GeneratedByName { get; set; }
    public string CurrencySymbol { get; set; } = "₹";

    public List<ReportColumnDto> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();

    /// <summary>Named totals rendered in the footer, e.g. "Total Revenue" → 125000.</summary>
    public Dictionary<string, decimal> Totals { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public List<ChartSeriesDto> Chart { get; set; } = new();
}

public class ReportColumnDto
{
    public string Key { get; set; } = string.Empty;
    public string Header { get; set; } = string.Empty;
    /// <summary>string, int, decimal, currency, date, datetime, bool or percent.</summary>
    public string DataType { get; set; } = "string";
    public string? Alignment { get; set; }
    public int? Width { get; set; }
    public bool IsTotalled { get; set; }

    public ReportColumnDto() { }

    public ReportColumnDto(string key, string header, string dataType = "string", bool isTotalled = false)
    {
        Key = key;
        Header = header;
        DataType = dataType;
        IsTotalled = isTotalled;
        Alignment = dataType is "decimal" or "currency" or "int" or "percent" ? "Right"
            : dataType is "date" or "datetime" ? "Center" : "Left";
    }
}

/// <summary>A generated file returned to the client as bytes.</summary>
public class FileExportDto
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Content { get; set; } = Array.Empty<byte>();
}

public class ProfitLossDto
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalRefunds { get; set; }
    public decimal NetRevenue => TotalRevenue - TotalRefunds;
    public decimal TotalExpenses { get; set; }
    public decimal NetProfit => NetRevenue - TotalExpenses;
    public decimal OutstandingReceivables { get; set; }
    public List<ChartSeriesDto> RevenueByPlan { get; set; } = new();
    public List<ChartSeriesDto> RevenueByMethod { get; set; } = new();
    public List<ChartSeriesDto> ExpensesByCategory { get; set; } = new();
    public List<ChartSeriesDto> MonthlyTrend { get; set; } = new();
    public string CurrencySymbol { get; set; } = "₹";
}
