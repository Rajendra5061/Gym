using System.Globalization;
using ClosedXML.Excel;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;

namespace GymManagement.Infrastructure.Reporting;

/// <summary>
/// Renders the shape-agnostic <see cref="ReportResultDto"/> to a spreadsheet with ClosedXML.
/// The renderer is driven entirely by <see cref="ReportColumnDto.DataType"/> so every report
/// gets correct cell types, number formats and totals without report specific code.
/// </summary>
public sealed class ExcelExportService : IExcelExportService
{
    private const string HeaderFillHtml = "#1F3864";
    private const string TotalsFillHtml = "#DCE6F1";
    private const string DateFormat = "dd-MM-yyyy";
    private const string DateTimeFormat = "dd-MM-yyyy HH:mm";
    private const string MoneyFormat = "#,##0.00";
    private const string IntegerFormat = "#,##0";
    private const string PercentFormat = "0.00\"%\"";

    public byte[] Export(ReportResultDto report)
    {
        using var workbook = new XLWorkbook();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        RenderSheet(workbook, report, UniqueSheetName(SheetName(report), used));
        return ToBytes(workbook);
    }

    public byte[] ExportSheets(string title, IEnumerable<ReportResultDto> reports)
    {
        using var workbook = new XLWorkbook();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var list = (reports ?? Enumerable.Empty<ReportResultDto>())
            .Where(r => r is not null)
            .ToList();

        if (list.Count == 0)
        {
            RenderSheet(
                workbook,
                new ReportResultDto { Title = string.IsNullOrWhiteSpace(title) ? "Report" : title },
                UniqueSheetName(Sanitize(title), used));
            return ToBytes(workbook);
        }

        foreach (var report in list)
        {
            var name = UniqueSheetName(SheetName(report), used);
            RenderSheet(workbook, report, name);
        }

        return ToBytes(workbook);
    }

    // ---------------------------------------------------------------- rendering

    private static void RenderSheet(XLWorkbook workbook, ReportResultDto? report, string sheetName)
    {
        report ??= new ReportResultDto();
        var ws = workbook.Worksheets.Add(sheetName);

        var columns = report.Columns?.Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Key)).ToList()
                      ?? new List<ReportColumnDto>();
        var rows = report.Rows ?? new List<Dictionary<string, object?>>();
        var currency = string.IsNullOrWhiteSpace(report.CurrencySymbol) ? string.Empty : report.CurrencySymbol;
        var currencyFormat = currency.Length == 0 ? MoneyFormat : $"\"{currency}\" {MoneyFormat}";
        var span = Math.Max(columns.Count, 1);

        // Title.
        var titleCell = ws.Cell(1, 1);
        titleCell.Value = string.IsNullOrWhiteSpace(report.Title) ? "Report" : report.Title;
        titleCell.Style.Font.Bold = true;
        titleCell.Style.Font.FontSize = 14;
        ws.Range(1, 1, 1, span).Merge();
        ws.Range(1, 1, 1, span).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        // Subtitle / date range / provenance.
        var subtitleCell = ws.Cell(2, 1);
        subtitleCell.Value = BuildSubtitle(report);
        subtitleCell.Style.Font.Italic = true;
        subtitleCell.Style.Font.FontSize = 9;
        ws.Range(2, 1, 2, span).Merge();
        ws.Range(2, 1, 2, span).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        // Row 3 is intentionally left blank; the header row always lands on row 4.
        const int headerRow = 4;

        if (columns.Count == 0)
        {
            var empty = ws.Cell(headerRow, 1);
            empty.Value = "No data";
            empty.Style.Font.Italic = true;
            ws.Columns().AdjustToContents();
            return;
        }

        for (var c = 0; c < columns.Count; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = string.IsNullOrWhiteSpace(columns[c].Header) ? columns[c].Key : columns[c].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml(HeaderFillHtml);
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.WrapText = true;
        }

        ws.SheetView.FreezeRows(headerRow);

        if (rows.Count == 0)
        {
            var noData = ws.Cell(headerRow + 1, 1);
            noData.Value = "No records found";
            noData.Style.Font.Italic = true;
            ws.Range(headerRow, 1, headerRow, columns.Count).SetAutoFilter();
            ws.Columns().AdjustToContents();
            return;
        }

        var firstDataRow = headerRow + 1;
        var totals = new decimal[columns.Count];

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r] ?? new Dictionary<string, object?>();
            for (var c = 0; c < columns.Count; c++)
            {
                var column = columns[c];
                row.TryGetValue(column.Key, out var value);
                var cell = ws.Cell(firstDataRow + r, c + 1);
                WriteValue(cell, value, column.DataType, currencyFormat);

                if (column.IsTotalled) totals[c] += ToDecimal(value) ?? 0m;
            }
        }

        var lastDataRow = firstDataRow + rows.Count - 1;

        if (columns.Any(c => c.IsTotalled))
        {
            var totalsRow = lastDataRow + 1;
            var labelWritten = false;

            for (var c = 0; c < columns.Count; c++)
            {
                var cell = ws.Cell(totalsRow, c + 1);
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml(TotalsFillHtml);

                if (columns[c].IsTotalled)
                {
                    cell.Value = (double)Math.Round(totals[c], 2);
                    cell.Style.NumberFormat.Format = columns[c].DataType switch
                    {
                        "int" => IntegerFormat,
                        "currency" => currencyFormat,
                        _ => MoneyFormat
                    };
                }
                else if (!labelWritten)
                {
                    cell.Value = "TOTAL";
                    labelWritten = true;
                }
            }
        }

        ws.Range(headerRow, 1, lastDataRow, columns.Count).SetAutoFilter();
        ws.Columns().AdjustToContents();

        // AdjustToContents can produce very wide columns for long note fields.
        foreach (var column in ws.ColumnsUsed())
        {
            if (column.Width > 45) column.Width = 45;
        }
    }

    private static string BuildSubtitle(ReportResultDto report)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(report.Subtitle)) parts.Add(report.Subtitle!);

        if (report.FromDate.HasValue || report.ToDate.HasValue)
        {
            var from = report.FromDate?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? "-";
            var to = report.ToDate?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? "-";
            parts.Add($"Period: {from} to {to}");
        }

        parts.Add($"Generated: {report.GeneratedAtUtc.ToLocalTime().ToString(DateTimeFormat, CultureInfo.InvariantCulture)}");

        if (!string.IsNullOrWhiteSpace(report.GeneratedByName)) parts.Add($"By: {report.GeneratedByName}");

        return string.Join("   |   ", parts);
    }

    private static void WriteValue(IXLCell cell, object? value, string? dataType, string currencyFormat)
    {
        if (value is null) return;

        switch ((dataType ?? "string").ToLowerInvariant())
        {
            case "date":
            {
                var date = ToDateTime(value);
                if (date.HasValue)
                {
                    cell.Value = date.Value.Date;
                    cell.Style.NumberFormat.Format = DateFormat;
                }
                else cell.Value = AsText(value);
                break;
            }

            case "datetime":
            {
                var date = ToDateTime(value);
                if (date.HasValue)
                {
                    cell.Value = date.Value;
                    cell.Style.NumberFormat.Format = DateTimeFormat;
                }
                else cell.Value = AsText(value);
                break;
            }

            case "currency":
            case "decimal":
            {
                var number = ToDecimal(value);
                if (number.HasValue)
                {
                    cell.Value = (double)Math.Round(number.Value, 2);
                    cell.Style.NumberFormat.Format = dataType!.Equals("currency", StringComparison.OrdinalIgnoreCase)
                        ? currencyFormat
                        : MoneyFormat;
                }
                else cell.Value = AsText(value);
                break;
            }

            case "int":
            {
                var number = ToDecimal(value);
                if (number.HasValue)
                {
                    cell.Value = (double)number.Value;
                    cell.Style.NumberFormat.Format = IntegerFormat;
                }
                else cell.Value = AsText(value);
                break;
            }

            case "percent":
            {
                var number = ToDecimal(value);
                if (number.HasValue)
                {
                    cell.Value = (double)Math.Round(number.Value, 2);
                    cell.Style.NumberFormat.Format = PercentFormat;
                }
                else cell.Value = AsText(value);
                break;
            }

            case "bool":
            {
                cell.Value = ToBool(value) ? "Yes" : "No";
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                break;
            }

            default:
                cell.Value = AsText(value);
                break;
        }
    }

    // ---------------------------------------------------------------- helpers

    private static byte[] ToBytes(XLWorkbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static string SheetName(ReportResultDto? report)
    {
        if (report is null) return "Report";
        var name = !string.IsNullOrWhiteSpace(report.Title) ? report.Title! : report.ReportType.ToString();
        return Sanitize(name);
    }

    /// <summary>Excel forbids <c>[]:*?/\</c> in sheet names and caps them at 31 characters.</summary>
    private static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Report";

        var invalid = new[] { '[', ']', ':', '*', '?', '/', '\\' };
        var cleaned = new string(name!.Where(ch => !invalid.Contains(ch)).ToArray()).Trim();

        if (cleaned.Length == 0) return "Report";
        return cleaned.Length > 31 ? cleaned[..31].Trim() : cleaned;
    }

    private static string UniqueSheetName(string name, HashSet<string> used)
    {
        var candidate = Sanitize(name);
        var suffix = 2;

        while (!used.Add(candidate))
        {
            var tail = $" ({suffix++})";
            var head = Sanitize(name);
            if (head.Length + tail.Length > 31) head = head[..(31 - tail.Length)];
            candidate = head + tail;
        }

        return candidate;
    }

    private static string AsText(object value) => value switch
    {
        string s => s,
        DateTime dt => dt.ToString(dt.TimeOfDay == TimeSpan.Zero ? DateFormat : DateTimeFormat, CultureInfo.InvariantCulture),
        decimal d => d.ToString("0.##", CultureInfo.InvariantCulture),
        double d => d.ToString("0.##", CultureInfo.InvariantCulture),
        bool b => b ? "Yes" : "No",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static decimal? ToDecimal(object? value) => value switch
    {
        null => null,
        decimal d => d,
        double d => (decimal)d,
        float f => (decimal)f,
        int i => i,
        long l => l,
        short s => s,
        byte b => b,
        string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null
    };

    private static DateTime? ToDateTime(object? value) => value switch
    {
        null => null,
        DateTime dt => dt,
        DateTimeOffset dto => dto.LocalDateTime,
        string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) => parsed,
        _ => null
    };

    private static bool ToBool(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => bool.TryParse(s, out var parsed) ? parsed : s.Equals("yes", StringComparison.OrdinalIgnoreCase),
        _ => ToDecimal(value) is > 0m
    };
}
