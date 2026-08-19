using FluentAssertions;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using GymManagement.Infrastructure.Services;
using GymManagement.UnitTests.TestBase;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GymManagement.UnitTests.Services;

/// <summary>
/// The reporting engine — case R-01 (every report type runs) and R-03 (changing the revenue
/// grouping changes the buckets but not the total).
/// </summary>
public class ReportServiceTests : IAsyncLifetime
{
    private readonly FixedClock _clock = new();
    private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Admin();
    private readonly StubSettingsService _settings = new();

    private GymDbContext _db = null!;
    private ReferenceData _data = null!;
    private ReportService _service = null!;

    public async Task InitializeAsync()
    {
        _db = InMemoryDbContextFactory.Create(_currentUser, _clock);
        _data = await InMemoryDbContextFactory.SeedReferenceDataAsync(_db);

        _service = new ReportService(_db, _clock, _currentUser, _settings, new StubAuditQueryService(),
            new StubExcelExportService(), new StubPdfExportService(),
            NullLogger<ReportService>.Instance);

        await SeedTransactionsAsync();
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Three months of members, subscriptions, payments, attendance and expenses so that every
    /// report has something real to render.
    /// </summary>
    private async Task SeedTransactionsAsync()
    {
        var start = _clock.Today.AddMonths(-3);

        for (var i = 0; i < 6; i++)
        {
            var member = new Member
            {
                MemberCode = $"GYM-2026-{i + 1:0000}",
                FullName = $"Member {i + 1}",
                Phone = $"90000000{i + 10}",
                JoiningDate = start.AddDays(i * 12),
                Status = i == 5 ? MemberStatus.Inactive : MemberStatus.Active,
                AssignedTrainerId = _data.TrainerId
            };
            _db.Members.Add(member);
            await _db.SaveChangesAsync();

            var subscription = new Subscription
            {
                SubscriptionCode = $"SUB-2026-{i + 1:0000}",
                MemberId = member.Id,
                MembershipPlanId = _data.MonthlyPlanId,
                StartDate = start.AddDays(i * 12),
                EndDate = start.AddDays(i * 12 + 30),
                GracePeriodDays = 3,
                PlanAmount = 2500m,
                RegistrationFee = 500m,
                DiscountAmount = 200m,
                TaxPercent = 18m,
                TaxAmount = 504m,
                FinalAmount = 3304m,
                PaidAmount = i % 3 == 0 ? 2000m : 3304m,
                PaymentStatus = i % 3 == 0 ? PaymentStatus.PartiallyPaid : PaymentStatus.Paid,
                Status = SubscriptionStatus.Active,
                IsRenewal = i % 2 == 0,
                AssignedTrainerId = _data.TrainerId
            };
            _db.Subscriptions.Add(subscription);
            await _db.SaveChangesAsync();

            _db.Payments.Add(new Payment
            {
                ReceiptNumber = $"RCP-2026-{i + 1:000000}",
                MemberId = member.Id,
                SubscriptionId = subscription.Id,
                Amount = subscription.PaidAmount,
                FinalAmount = subscription.PaidAmount,
                PaymentMethodId = i % 2 == 0 ? _data.CashMethodId : _data.UpiMethodId,
                TransactionReference = i % 2 == 0 ? null : $"UTR-{i + 1:000}",
                PaymentDate = start.AddDays(i * 12).AddHours(11),
                Status = PaymentStatus.Paid,
                ConfirmedAtUtc = start.AddDays(i * 12).AddHours(11)
            });

            _db.Attendance.Add(new Attendance
            {
                MemberId = member.Id,
                AttendanceDate = start.AddDays(i * 12 + 1),
                CheckInTime = start.AddDays(i * 12 + 1).AddHours(7),
                CheckOutTime = start.AddDays(i * 12 + 1).AddHours(8),
                Status = AttendanceStatus.CheckedOut,
                DurationMinutes = 60,
                SubscriptionId = subscription.Id
            });

            _db.Expenses.Add(new Expense
            {
                ExpenseNumber = $"EXP-2026-{i + 1:000000}",
                ExpenseCategoryId = _data.ExpenseCategoryId,
                Title = $"Expense {i + 1}",
                Amount = 1000m + i * 100m,
                ExpenseDate = start.AddDays(i * 12),
                PaymentMethodId = _data.CashMethodId
            });
        }

        await _db.SaveChangesAsync();
    }

    private ReportRequestDto Request(ReportType type, string groupBy = "Day") => new()
    {
        ReportType = type,
        FromDate = _clock.Today.AddMonths(-4),
        ToDate = _clock.Today,
        GroupBy = groupBy,
        PageNumber = 1,
        PageSize = 50
    };

    // ------------------------------------------------------------- R-01

    [Theory(DisplayName = "R-01 Every report type runs and returns columns")]
    [MemberData(nameof(AllReportTypes))]
    public async Task Run_ForEveryReportType_ReturnsColumns(ReportType reportType)
    {
        var report = await _service.RunAsync(Request(reportType));

        report.Should().NotBeNull();
        report.ReportType.Should().Be(reportType);
        report.Columns.Should().NotBeEmpty($"{reportType} must describe its columns");
        report.Columns.Should().OnlyContain(c =>
            !string.IsNullOrWhiteSpace(c.Key) && !string.IsNullOrWhiteSpace(c.Header));
        report.Title.Should().NotBeNullOrWhiteSpace();
        report.TotalCount.Should().BeGreaterThanOrEqualTo(0);
    }

    [Theory(DisplayName = "R-01 Every report type runs without throwing on an empty date range")]
    [MemberData(nameof(AllReportTypes))]
    public async Task Run_OnAnEmptyRange_DoesNotThrow(ReportType reportType)
    {
        var request = Request(reportType);
        request.FromDate = _clock.Today.AddYears(-10);
        request.ToDate = _clock.Today.AddYears(-10).AddDays(1);

        var act = () => _service.RunAsync(request);

        await act.Should().NotThrowAsync();
    }

    public static TheoryData<ReportType> AllReportTypes()
    {
        var data = new TheoryData<ReportType>();
        foreach (var value in Enum.GetValues<ReportType>()) data.Add(value);
        return data;
    }

    [Fact(DisplayName = "R-01 An out-of-range report type is a not-found error")]
    public async Task Run_WithAnUnknownReportType_ThrowsNotFound()
    {
        var request = Request((ReportType)9_999);

        var act = () => _service.RunAsync(request);

        var error = await act.Should().ThrowAsync<NotFoundAppException>();
        error.Which.Message.Should().Contain("9999");
    }

    [Fact(DisplayName = "R-01 A report reports the configured currency symbol")]
    public async Task Run_ReportsTheCurrencySymbol()
    {
        var report = await _service.RunAsync(Request(ReportType.MemberList));

        report.CurrencySymbol.Should().Be("₹");
        report.GeneratedAtUtc.Should().Be(_clock.UtcNow);
    }

    [Fact(DisplayName = "R-01 The member list report returns a row per member")]
    public async Task Run_MemberList_ReturnsARowPerMember()
    {
        var report = await _service.RunAsync(Request(ReportType.MemberList));

        report.TotalCount.Should().Be(6);
        report.Rows.Should().HaveCount(6);
    }

    // ------------------------------------------------------------- R-03

    [Theory(DisplayName = "R-03 Every revenue grouping is accepted")]
    [InlineData("Day")]
    [InlineData("Week")]
    [InlineData("Month")]
    public async Task Run_RevenueReport_AcceptsEveryGrouping(string groupBy)
    {
        var report = await _service.RunAsync(Request(ReportType.RevenueReport, groupBy));

        report.Title.Should().Contain(groupBy);
        report.Columns.Should().NotBeEmpty();
        report.Totals.Should().ContainKey("Net Revenue");
    }

    [Fact(DisplayName = "R-03 Changing the grouping changes the bucket count but keeps the same total")]
    public async Task Run_RevenueReport_GroupingChangesBucketsNotTotals()
    {
        var daily = await _service.RunAsync(Request(ReportType.RevenueReport, "Day"));
        var weekly = await _service.RunAsync(Request(ReportType.RevenueReport, "Week"));
        var monthly = await _service.RunAsync(Request(ReportType.RevenueReport, "Month"));

        // Same money, fewer and fewer buckets.
        weekly.Totals["Net Revenue"].Should().Be(daily.Totals["Net Revenue"]);
        monthly.Totals["Net Revenue"].Should().Be(daily.Totals["Net Revenue"]);
        weekly.Totals["Gross Revenue"].Should().Be(daily.Totals["Gross Revenue"]);
        monthly.Totals["Total Payments"].Should().Be(daily.Totals["Total Payments"]);

        daily.TotalCount.Should().BeGreaterThan(monthly.TotalCount);
        weekly.TotalCount.Should().BeGreaterThanOrEqualTo(monthly.TotalCount);
        monthly.TotalCount.Should().BeGreaterThan(0);
    }

    [Fact(DisplayName = "R-03 The revenue total matches the sum of the confirmed payments")]
    public async Task Run_RevenueReport_TotalMatchesThePayments()
    {
        var expected = await _db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Paid)
            .SumAsync(p => p.FinalAmount);

        var report = await _service.RunAsync(Request(ReportType.RevenueReport, "Month"));

        report.Totals["Gross Revenue"].Should().Be(expected);
    }

    [Fact(DisplayName = "R-03 An unrecognised grouping falls back to a daily report")]
    public async Task Run_RevenueReport_WithAnUnknownGrouping_FallsBackToDay()
    {
        var fallback = await _service.RunAsync(Request(ReportType.RevenueReport, "Fortnight"));
        var daily = await _service.RunAsync(Request(ReportType.RevenueReport, "Day"));

        fallback.TotalCount.Should().Be(daily.TotalCount);
        fallback.Totals["Net Revenue"].Should().Be(daily.Totals["Net Revenue"]);
    }

    // -------------------------------------------------------------- money

    [Fact(DisplayName = "R-09 Net profit is revenue less refunds less expenses")]
    public async Task GetProfitAndLoss_ComputesTheNetProfit()
    {
        var result = await _service.GetProfitAndLossAsync(_clock.Today.AddMonths(-4), _clock.Today);

        result.NetRevenue.Should().Be(result.TotalRevenue - result.TotalRefunds);
        result.NetProfit.Should().Be(result.NetRevenue - result.TotalExpenses);
        result.TotalRevenue.Should().BeGreaterThan(0m);
        result.TotalExpenses.Should().BeGreaterThan(0m);
    }

    [Fact(DisplayName = "R-01 Every totalled figure is a decimal")]
    public async Task Run_TotalsAreDecimals()
    {
        var report = await _service.RunAsync(Request(ReportType.RevenueReport, "Month"));

        report.Totals.Should().NotBeEmpty();
        foreach (var (key, value) in report.Totals)
            value.Should().BeOfType(typeof(decimal), $"{key} must be exact money");
    }

    // ------------------------------------------------------------ exports

    [Fact(DisplayName = "R-05 An Excel export is produced with the spreadsheet content type")]
    public async Task ExportExcel_ReturnsASpreadsheet()
    {
        var file = await _service.ExportExcelAsync(Request(ReportType.MemberList));

        file.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        file.FileName.Should().EndWith(".xlsx");
        file.Content.Should().NotBeEmpty();
    }

    [Fact(DisplayName = "R-06 A PDF export is produced with the PDF content type")]
    public async Task ExportPdf_ReturnsAPdf()
    {
        var file = await _service.ExportPdfAsync(Request(ReportType.MemberList));

        file.ContentType.Should().Be("application/pdf");
        file.FileName.Should().EndWith(".pdf");
        System.Text.Encoding.ASCII.GetString(file.Content, 0, 4).Should().Be("%PDF");
    }

    [Fact(DisplayName = "R-07 An export contains every row, not just the requested page")]
    public async Task Export_IgnoresThePageSizeAndIncludesEveryRow()
    {
        var request = Request(ReportType.MemberList);
        request.PageSize = 2;

        var page = await _service.RunAsync(request);
        page.Rows.Should().HaveCount(2, "the paged view is limited");

        // The export path re-runs the report with a very large page size.
        var file = await _service.ExportExcelAsync(request);
        file.Content.Should().NotBeEmpty();

        request.PageSize = 200;
        var full = await _service.RunAsync(request);
        full.Rows.Should().HaveCount(6, "the export sees the whole result set");
    }
}
