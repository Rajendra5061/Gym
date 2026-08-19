using System.Globalization;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Infrastructure.Common;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace GymManagement.Infrastructure.Reporting;

/// <summary>
/// QuestPDF renderer for the generic report grid and for payment receipts.
/// Both entry points are defensive: missing optional data renders as an empty cell rather
/// than throwing, so a printing action can never take down the calling request.
/// </summary>
public sealed class PdfExportService : IPdfExportService
{
    private const string DateFormat = "dd-MM-yyyy";
    private const string DateTimeFormat = "dd-MM-yyyy HH:mm";

    static PdfExportService()
    {
        // QuestPDF requires an explicit licence declaration. This product uses the
        // Community licence, which covers the open-source / small-business tier.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // ---------------------------------------------------------------- report

    public byte[] ExportReport(ReportResultDto report, GymSettingsDto gym)
    {
        var model = report ?? new ReportResultDto();
        var settings = gym ?? new GymSettingsDto();

        var columns = (model.Columns ?? new List<ReportColumnDto>())
            .Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Key))
            .ToList();
        var rows = model.Rows ?? new List<Dictionary<string, object?>>();
        var currency = model.CurrencySymbol ?? string.Empty;

        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(20);
                page.DefaultTextStyle(t => t.FontSize(8).FontColor(Colors.Black));

                page.Header().Element(container => ComposeReportHeader(container, model, settings));
                page.Content().PaddingVertical(8)
                    .Element(container => ComposeReportBody(container, columns, rows, currency));
                page.Footer().Element(container => ComposeReportFooter(container, settings));
            });
        }).GeneratePdf();
    }

    private static void ComposeReportHeader(IContainer container, ReportResultDto report, GymSettingsDto gym)
    {
        container.Column(column =>
        {
            column.Item().Text(string.IsNullOrWhiteSpace(gym.GymName) ? "Gym" : gym.GymName)
                .FontSize(16).Bold().FontColor(Colors.Blue.Darken3);

            column.Item().PaddingTop(2)
                .Text(string.IsNullOrWhiteSpace(report.Title) ? report.ReportType.ToString() : report.Title)
                .FontSize(11).SemiBold();

            if (!string.IsNullOrWhiteSpace(report.Subtitle))
                column.Item().Text(report.Subtitle!).FontSize(8).FontColor(Colors.Grey.Darken2);

            if (report.FromDate.HasValue || report.ToDate.HasValue)
            {
                var from = report.FromDate?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? "-";
                var to = report.ToDate?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? "-";
                column.Item().Text($"Period: {from} to {to}").FontSize(8).FontColor(Colors.Grey.Darken2);
            }

            var stamp = report.GeneratedAtUtc == default
                ? DateTime.Now
                : report.GeneratedAtUtc.ToLocalTime();
            var by = string.IsNullOrWhiteSpace(report.GeneratedByName) ? "system" : report.GeneratedByName!;
            column.Item().Text($"Generated {stamp.ToString(DateTimeFormat, CultureInfo.InvariantCulture)} by {by}")
                .FontSize(7).FontColor(Colors.Grey.Darken1);

            column.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Medium);
        });
    }

    private static void ComposeReportBody(IContainer container, List<ReportColumnDto> columns,
        List<Dictionary<string, object?>> rows, string currency)
    {
        if (columns.Count == 0 || rows.Count == 0)
        {
            container.AlignCenter().AlignMiddle()
                .Text("No records found").FontSize(11).Italic().FontColor(Colors.Grey.Darken1);
            return;
        }

        var totalled = columns.Any(c => c.IsTotalled);
        var totals = new decimal[columns.Count];

        container.Table(table =>
        {
            table.ColumnsDefinition(definition =>
            {
                foreach (var column in columns) definition.RelativeColumn(RelativeWidth(column));
            });

            table.Header(header =>
            {
                foreach (var column in columns)
                {
                    var cell = header.Cell().Element(HeaderCell);
                    if (IsRightAligned(column)) cell = cell.AlignRight();
                    cell.Text(string.IsNullOrWhiteSpace(column.Header) ? column.Key : column.Header)
                        .FontSize(8).Bold();
                }
            });

            for (var r = 0; r < rows.Count; r++)
            {
                var row = rows[r] ?? new Dictionary<string, object?>();
                var shaded = r % 2 == 1;

                for (var c = 0; c < columns.Count; c++)
                {
                    var column = columns[c];
                    row.TryGetValue(column.Key, out var value);
                    if (column.IsTotalled) totals[c] += ToDecimal(value) ?? 0m;

                    var cell = table.Cell().Element(x => BodyCell(x, shaded));
                    if (IsRightAligned(column)) cell = cell.AlignRight();
                    cell.Text(FormatValue(value, column.DataType, currency)).FontSize(7.5f);
                }
            }

            if (!totalled) return;

            var labelWritten = false;
            for (var c = 0; c < columns.Count; c++)
            {
                var column = columns[c];
                var cell = table.Cell().Element(TotalsCell);
                if (IsRightAligned(column)) cell = cell.AlignRight();

                if (column.IsTotalled)
                {
                    cell.Text(FormatValue(Math.Round(totals[c], 2), column.DataType, currency)).FontSize(8).Bold();
                }
                else if (!labelWritten)
                {
                    labelWritten = true;
                    cell.Text("TOTAL").FontSize(8).Bold();
                }
                else
                {
                    cell.Text(string.Empty);
                }
            }
        });
    }

    private static void ComposeReportFooter(IContainer container, GymSettingsDto gym)
    {
        var contact = BuildContactLine(gym);

        container.BorderTop(1).BorderColor(Colors.Grey.Lighten1).PaddingTop(4).Row(row =>
        {
            row.RelativeItem().Text(contact).FontSize(7).FontColor(Colors.Grey.Darken1);
            row.ConstantItem(120).AlignRight().Text(text =>
            {
                text.DefaultTextStyle(s => s.FontSize(7).FontColor(Colors.Grey.Darken1));
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        });
    }

    // ---------------------------------------------------------------- receipt

    /// <summary>
    /// A4 portrait receipt.
    ///
    /// Designed for a black and white office printer: every distinction is carried by rules,
    /// weight, borders and wording, never by colour alone. The paid / outstanding verdict is a
    /// bordered box containing the words "PAID IN FULL" or "PART PAYMENT RECEIVED", so a greyscale
    /// print says exactly what a colour one does. Money is right-aligned in a fixed column, and the
    /// breakdown is written so that the arithmetic is visible: amount, less discount, add tax, total.
    /// </summary>
    public byte[] ExportReceipt(PaymentReceiptDto receipt)
    {
        var model = receipt ?? new PaymentReceiptDto();
        var currency = model.CurrencySymbol ?? string.Empty;
        var logo = TryLoadImage(model.GymLogoPath);

        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4.Portrait());
                page.Margin(32);
                // QuestPDF 2024.3+ resolves a missing glyph against the installed fonts on its own,
                // which is what carries the currency symbol: the bundled default face has no Rupee
                // sign, and without that fallback every amount would print with a tofu box.
                page.DefaultTextStyle(t => t.FontSize(10).FontColor(Colors.Black));

                page.Header().Element(container => ComposeReceiptHeader(container, model, logo));
                page.Content().PaddingTop(14)
                    .Element(container => ComposeReceiptBody(container, model, currency));
                page.Footer().Element(container => ComposeReceiptFooter(container, model));
            });
        }).GeneratePdf();
    }

    private static void ComposeReceiptHeader(IContainer container, PaymentReceiptDto model, byte[]? logo)
    {
        container.Column(header =>
        {
            header.Item().Row(row =>
            {
                row.Spacing(12);

                if (logo is not null)
                {
                    try
                    {
                        row.ConstantItem(64).Height(52).AlignMiddle().Image(logo).FitArea();
                    }
                    catch
                    {
                        // A corrupt or unsupported logo must never break a receipt.
                    }
                }

                row.RelativeItem().Column(gym =>
                {
                    gym.Item().Text(string.IsNullOrWhiteSpace(model.GymName) ? "Gym" : model.GymName)
                        .FontSize(19).Bold();

                    if (!string.IsNullOrWhiteSpace(model.GymAddress))
                        gym.Item().PaddingTop(3).Text(model.GymAddress!)
                            .FontSize(8.5f).FontColor(Colors.Grey.Darken2);

                    var contact = JoinNonEmpty("   ·   ",
                        string.IsNullOrWhiteSpace(model.GymPhone) ? null : $"Phone: {model.GymPhone}",
                        string.IsNullOrWhiteSpace(model.GymEmail) ? null : $"Email: {model.GymEmail}",
                        string.IsNullOrWhiteSpace(model.TaxNumber) ? null : $"Tax No: {model.TaxNumber}");

                    if (contact.Length > 0)
                        gym.Item().PaddingTop(2).Text(contact)
                            .FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                });

                // The receipt number and date are what somebody looks for first, so they get their
                // own bordered block in the top right rather than being buried in a run of text.
                row.ConstantItem(186).Border(1).BorderColor(Colors.Black).Column(stamp =>
                {
                    stamp.Item().Background(Colors.Grey.Lighten2).PaddingVertical(4).PaddingHorizontal(8)
                        .Text("PAYMENT RECEIPT").FontSize(10).Bold().LetterSpacing(0.12f);

                    stamp.Item().PaddingHorizontal(8).PaddingVertical(6).Column(detail =>
                    {
                        detail.Item().Element(c => StampLine(c, "Receipt No",
                            ReceiptPresentation.OrDash(model.ReceiptNumber), bold: true));

                        detail.Item().PaddingTop(3).Element(c => StampLine(c, "Date",
                            ReceiptPresentation.DateTimeStamp(model.PaymentDate), bold: false));
                    });
                });
            });

            header.Item().PaddingTop(10).LineHorizontal(1.6f).LineColor(Colors.Black);
        });
    }

    private static void StampLine(IContainer container, string label, string value, bool bold)
    {
        container.Column(column =>
        {
            column.Item().Text(label).FontSize(7.5f).FontColor(Colors.Grey.Darken2).LetterSpacing(0.08f);

            var text = column.Item().Text(value).FontSize(bold ? 11 : 9.5f);
            if (bold) text.Bold();
        });
    }

    private static void ComposeReceiptBody(IContainer container, PaymentReceiptDto model, string currency)
    {
        container.Column(body =>
        {
            body.Spacing(12);

            // ---- who paid, and what for
            body.Item().Row(row =>
            {
                row.Spacing(12);

                row.RelativeItem().Element(c => Panel(c, "Received from", new[]
                {
                    ("Name", ReceiptPresentation.OrDash(model.MemberName)),
                    ("Member ID", ReceiptPresentation.OrDash(model.MemberCode)),
                    ("Phone", ReceiptPresentation.OrDash(model.MemberPhone))
                }));

                var membership = new List<(string, string)>
                {
                    ("Plan", ReceiptPresentation.OrDash(model.PlanName))
                };

                if (model.SubscriptionStartDate.HasValue || model.SubscriptionEndDate.HasValue)
                    membership.Add(("Valid", ReceiptPresentation.Term(model)));

                membership.Add(("Received by", ReceiptPresentation.OrDash(model.CollectedByName)));

                row.RelativeItem().Element(c => Panel(c, "Membership", membership));
            });

            // ---- the itemised breakdown, written so the arithmetic is followable
            body.Item().Table(table =>
            {
                table.ColumnsDefinition(definition =>
                {
                    definition.RelativeColumn(3);
                    definition.ConstantColumn(150);
                });

                table.Header(header =>
                {
                    header.Cell().Element(ItemHeaderCell).Text("DESCRIPTION")
                        .FontSize(8).Bold().LetterSpacing(0.1f);
                    header.Cell().Element(ItemHeaderCell).AlignRight().Text("AMOUNT")
                        .FontSize(8).Bold().LetterSpacing(0.1f);
                });

                ItemRow(table, ReceiptPresentation.LineItemDescription(model),
                    ReceiptPresentation.Money(model.Amount, currency));

                if (model.DiscountAmount > 0m)
                    ItemRow(table, "Less: discount",
                        ReceiptPresentation.MoneyDeducted(model.DiscountAmount, currency));

                if (model.TaxAmount > 0m)
                    ItemRow(table, "Add: tax",
                        ReceiptPresentation.MoneyAdded(model.TaxAmount, currency));

                ItemRow(table, "TOTAL PAYABLE",
                    ReceiptPresentation.Money(model.FinalAmount, currency), emphasise: true);

                ItemRow(table, "Amount paid — this receipt",
                    ReceiptPresentation.Money(model.FinalAmount, currency));

                if (model.PaidAmount != model.FinalAmount)
                    ItemRow(table, "Paid to date",
                        ReceiptPresentation.Money(model.PaidAmount, currency));

                ItemRow(table, "Balance outstanding",
                    ReceiptPresentation.Money(model.OutstandingAmount, currency),
                    emphasise: model.OutstandingAmount > 0m);
            });

            // ---- amount in words
            if (!string.IsNullOrWhiteSpace(model.AmountInWords))
            {
                body.Item().Border(0.8f).BorderColor(Colors.Grey.Darken1)
                    .PaddingVertical(6).PaddingHorizontal(9).Text(text =>
                    {
                        text.Span("Amount in words:  ").FontSize(9).FontColor(Colors.Grey.Darken2);
                        text.Span(model.AmountInWords).FontSize(9.5f).SemiBold();
                    });
            }

            // ---- how it was paid, and where that leaves the member
            body.Item().Row(row =>
            {
                row.Spacing(12);

                row.RelativeItem().Element(c => Panel(c, "Payment details", new[]
                {
                    ("Method", ReceiptPresentation.OrDash(model.PaymentMethodName)),
                    ("Reference", ReceiptPresentation.OrDash(model.TransactionReference)),
                    ("Recorded as", ReceiptPresentation.Humanise(model.StatusText))
                }));

                var (headline, detail) = ReceiptPresentation.Status(model);

                // Two-point border and the verdict spelled out: this has to survive a monochrome
                // printer, so nothing here depends on a colour being visible.
                row.RelativeItem().Border(2).BorderColor(Colors.Black)
                    .PaddingVertical(8).PaddingHorizontal(10).Column(status =>
                    {
                        status.Item().Text(headline).FontSize(12).Bold().LetterSpacing(0.1f);
                        status.Item().PaddingTop(3).Text(detail)
                            .FontSize(8.5f).FontColor(Colors.Grey.Darken3);
                    });
            });

            if (!string.IsNullOrWhiteSpace(model.Notes))
                body.Item().Text(text =>
                {
                    text.Span("Notes:  ").FontSize(8.5f).SemiBold().FontColor(Colors.Grey.Darken2);
                    text.Span(model.Notes!).FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                });

            // ---- signature
            body.Item().PaddingTop(26).Row(row =>
            {
                row.RelativeItem().AlignBottom()
                    .Text("Received with thanks.").FontSize(9).Italic().FontColor(Colors.Grey.Darken2);

                row.ConstantItem(170).Column(sign =>
                {
                    sign.Item().PaddingBottom(3).LineHorizontal(0.8f).LineColor(Colors.Black);
                    sign.Item().AlignCenter().Text("Authorised signature").FontSize(8.5f);
                });
            });
        });
    }

    private static void ComposeReceiptFooter(IContainer container, PaymentReceiptDto model)
    {
        container.BorderTop(0.8f).BorderColor(Colors.Grey.Medium).PaddingTop(5).Column(footer =>
        {
            if (!string.IsNullOrWhiteSpace(model.FooterText))
                footer.Item().AlignCenter().Text(model.FooterText!)
                    .FontSize(8.5f).Italic().FontColor(Colors.Grey.Darken2);

            footer.Item().PaddingTop(2).Row(row =>
            {
                row.RelativeItem()
                    .Text("This is a computer-generated receipt and is valid without a signature.")
                    .FontSize(7.5f).FontColor(Colors.Grey.Darken1);

                row.ConstantItem(90).AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(Colors.Grey.Darken1));
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        });
    }

    /// <summary>One line of the itemised breakdown. The amount column is always right-aligned.</summary>
    private static void ItemRow(TableDescriptor table, string label, string amount, bool emphasise = false)
    {
        var labelCell = table.Cell().Element(x => ItemCell(x, emphasise));
        var amountCell = table.Cell().Element(x => ItemCell(x, emphasise)).AlignRight();

        if (emphasise)
        {
            labelCell.Text(label).FontSize(10).Bold();
            amountCell.Text(amount).FontSize(10.5f).Bold();
        }
        else
        {
            labelCell.Text(label).FontSize(9.5f);
            amountCell.Text(amount).FontSize(9.5f);
        }
    }

    private static IContainer ItemHeaderCell(IContainer container) => container
        .Background(Colors.Grey.Lighten2)
        .BorderBottom(1).BorderColor(Colors.Black)
        .PaddingVertical(5).PaddingHorizontal(9);

    private static IContainer ItemCell(IContainer container, bool emphasise) => (emphasise
            ? container.Background(Colors.Grey.Lighten3).BorderTop(0.8f).BorderColor(Colors.Grey.Darken1)
            : container)
        .BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1)
        .PaddingVertical(5).PaddingHorizontal(9);

    /// <summary>A bordered block of caption plus label/value lines, used for the side-by-side panels.</summary>
    private static void Panel(IContainer container, string caption, IEnumerable<(string Label, string Value)> lines)
    {
        container.Border(0.8f).BorderColor(Colors.Grey.Darken1).Column(column =>
        {
            column.Item().Background(Colors.Grey.Lighten3)
                .BorderBottom(0.8f).BorderColor(Colors.Grey.Darken1)
                .PaddingVertical(3).PaddingHorizontal(9)
                .Text(caption.ToUpperInvariant())
                .FontSize(7.5f).Bold().LetterSpacing(0.1f).FontColor(Colors.Grey.Darken3);

            column.Item().PaddingVertical(6).PaddingHorizontal(9).Column(rows =>
            {
                rows.Spacing(2);

                foreach (var line in lines)
                {
                    rows.Item().Row(row =>
                    {
                        row.ConstantItem(74).Text(line.Label)
                            .FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                        row.RelativeItem().Text(line.Value).FontSize(9.5f);
                    });
                }
            });
        });
    }

    // ---------------------------------------------------------------- styling helpers

    private static IContainer HeaderCell(IContainer container) => container
        .Background(Colors.Grey.Lighten2)
        .BorderBottom(1).BorderColor(Colors.Grey.Medium)
        .PaddingVertical(4).PaddingHorizontal(3);

    private static IContainer BodyCell(IContainer container, bool shaded) => (shaded
            ? container.Background(Colors.Grey.Lighten4)
            : container)
        .BorderBottom(0.4f).BorderColor(Colors.Grey.Lighten2)
        .PaddingVertical(3).PaddingHorizontal(3);

    private static IContainer TotalsCell(IContainer container) => container
        .Background(Colors.Grey.Lighten3)
        .BorderTop(1).BorderColor(Colors.Grey.Medium)
        .PaddingVertical(4).PaddingHorizontal(3);

    private static float RelativeWidth(ReportColumnDto column)
    {
        if (column.Width is > 0) return Math.Min(column.Width!.Value, 400) / 60f;

        return (column.DataType ?? "string").ToLowerInvariant() switch
        {
            "int" => 1f,
            "bool" => 1f,
            "percent" => 1f,
            "date" => 1.3f,
            "datetime" => 1.6f,
            "decimal" => 1.3f,
            "currency" => 1.3f,
            _ => 2f
        };
    }

    private static bool IsRightAligned(ReportColumnDto column) =>
        string.Equals(column.Alignment, "Right", StringComparison.OrdinalIgnoreCase)
        || (column.Alignment is null
            && (column.DataType ?? string.Empty).ToLowerInvariant() is "int" or "decimal" or "currency" or "percent");

    // ---------------------------------------------------------------- value formatting

    private static string FormatValue(object? value, string? dataType, string currency)
    {
        if (value is null) return string.Empty;

        switch ((dataType ?? "string").ToLowerInvariant())
        {
            case "date":
            {
                var date = ToDateTime(value);
                return date.HasValue ? date.Value.ToString(DateFormat, CultureInfo.InvariantCulture) : AsText(value);
            }

            case "datetime":
            {
                var date = ToDateTime(value);
                return date.HasValue ? date.Value.ToString(DateTimeFormat, CultureInfo.InvariantCulture) : AsText(value);
            }

            case "currency":
            {
                var number = ToDecimal(value);
                return number.HasValue ? FormatMoney(number.Value, currency) : AsText(value);
            }

            case "decimal":
            {
                var number = ToDecimal(value);
                return number.HasValue
                    ? Math.Round(number.Value, 2).ToString("N2", CultureInfo.InvariantCulture)
                    : AsText(value);
            }

            case "int":
            {
                var number = ToDecimal(value);
                return number.HasValue
                    ? Math.Round(number.Value, 0).ToString("N0", CultureInfo.InvariantCulture)
                    : AsText(value);
            }

            case "percent":
            {
                var number = ToDecimal(value);
                return number.HasValue
                    ? Math.Round(number.Value, 2).ToString("0.00", CultureInfo.InvariantCulture) + "%"
                    : AsText(value);
            }

            case "bool":
                return ToBool(value) ? "Yes" : "No";

            default:
                return AsText(value);
        }
    }

    private static string FormatMoney(decimal value, string currency) =>
        (string.IsNullOrEmpty(currency) ? string.Empty : currency + " ")
        + Math.Round(value, 2).ToString("N2", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTime? value) =>
        value.HasValue ? value.Value.ToString(DateFormat, CultureInfo.InvariantCulture) : "-";

    private static string BuildContactLine(GymSettingsDto gym)
    {
        var location = JoinNonEmpty(", ", gym.Address, gym.City, gym.State, gym.PostalCode);
        var phone = string.IsNullOrWhiteSpace(gym.Phone) ? null : $"Ph: {gym.Phone}";
        return JoinNonEmpty("  |  ", location.Length == 0 ? null : location, phone);
    }

    private static string JoinNonEmpty(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

    private static byte[]? TryLoadImage(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch
        {
            // Missing / locked / unreadable logo: the receipt is printed without it.
            return null;
        }
    }

    private static string AsText(object value) => value switch
    {
        string s => s,
        DateTime dt => dt.ToString(dt.TimeOfDay == TimeSpan.Zero ? DateFormat : DateTimeFormat, CultureInfo.InvariantCulture),
        decimal d => Math.Round(d, 2).ToString("N2", CultureInfo.InvariantCulture),
        double d => d.ToString("N2", CultureInfo.InvariantCulture),
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
