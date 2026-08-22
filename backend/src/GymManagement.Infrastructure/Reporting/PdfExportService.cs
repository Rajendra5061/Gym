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

    /// <summary>Horizontal inset for every section. The header band bleeds past it on purpose.</summary>
    private const float ReceiptPad = 32;

    /// <summary>
    /// The receipt palette.
    ///
    /// Colour is layered on top of the monochrome structure, never in place of it. Every
    /// distinction the receipt makes is still carried by weight, rule and wording — the verdict is
    /// the words "PAID IN FULL", not a green box — so a black and white print says exactly what a
    /// colour one does. The tints are chosen dark enough to stay distinguishable once a printer
    /// has flattened them to grey.
    /// </summary>
    private static readonly Color BrandDeep = Color.FromHex("#312E81");
    private static readonly Color Brand = Color.FromHex("#4F46E5");
    private static readonly Color BrandSoft = Color.FromHex("#EEF2FF");
    private static readonly Color OnBrandMuted = Color.FromHex("#C7D2FE");
    private static readonly Color Ink = Color.FromHex("#111827");
    private static readonly Color InkMuted = Color.FromHex("#6B7280");
    private static readonly Color Hairline = Color.FromHex("#E5E7EB");
    private static readonly Color ZebraTint = Color.FromHex("#FAFAFB");
    private static readonly Color PaidInk = Color.FromHex("#166534");
    private static readonly Color PaidSoft = Color.FromHex("#ECFDF5");
    private static readonly Color PendingInk = Color.FromHex("#92400E");
    private static readonly Color PendingSoft = Color.FromHex("#FFFBEB");
    private static readonly Color RefundInk = Color.FromHex("#991B1B");
    private static readonly Color RefundSoft = Color.FromHex("#FEF2F2");

    /// <summary>
    /// A4 portrait receipt.
    ///
    /// The order is: a brand masthead, then the one figure the member is looking for — what they
    /// paid, and whether anything is still owed — then who and what it was for, then the itemised
    /// arithmetic, then how it was paid.
    ///
    /// Still designed to survive a black and white office printer. Colour reinforces the hierarchy
    /// but never carries a fact on its own: the paid / outstanding verdict is spelled out in words
    /// inside a bordered box, money is right-aligned in a fixed column, and the breakdown reads
    /// amount, less discount, add tax, total, so the arithmetic can be followed either way.
    /// </summary>
    public byte[] ExportReceipt(PaymentReceiptDto receipt) =>
        BuildReceiptDocument(receipt).GeneratePdf();

    /// <summary>
    /// Composes the receipt without committing to an output format. Kept separate from
    /// <see cref="ExportReceipt"/> so the same layout can be rendered to a raster image — which is
    /// how the design is proof-read — without a second copy of it drifting out of step.
    /// </summary>
    private static IDocument BuildReceiptDocument(PaymentReceiptDto receipt)
    {
        var model = receipt ?? new PaymentReceiptDto();
        var currency = model.CurrencySymbol ?? string.Empty;
        var logo = TryLoadImage(model.GymLogoPath);

        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4.Portrait());

                // No page margin: the masthead and the footer rule run edge to edge, and every
                // other section supplies its own inset. A band held off the paper by a margin
                // reads as a box that failed to line up rather than as a masthead.
                page.Margin(0);

                // QuestPDF 2024.3+ resolves a missing glyph against the installed fonts on its own,
                // which is what carries the currency symbol: the bundled default face has no Rupee
                // sign, and without that fallback every amount would print with a tofu box. It is
                // also why no font family is named here — naming one would lose that fallback.
                page.DefaultTextStyle(t => t.FontSize(10).FontColor(Ink));

                page.Header().Element(container => ComposeReceiptHeader(container, model, logo));
                page.Content().PaddingHorizontal(ReceiptPad).PaddingTop(16)
                    .Element(container => ComposeReceiptBody(container, model, currency));
                page.Footer().Element(container => ComposeReceiptFooter(container, model));
            });
        });
    }

    /// <summary>
    /// The masthead: gym identity reversed out of a deep brand band, with the receipt number and
    /// date in a white card on top of it. Those two are what somebody looks for first, so they sit
    /// in the highest-contrast element on the page.
    /// </summary>
    private static void ComposeReceiptHeader(IContainer container, PaymentReceiptDto model, byte[]? logo)
    {
        container.Background(BrandDeep)
            .PaddingHorizontal(ReceiptPad).PaddingVertical(20)
            .Row(row =>
            {
                row.Spacing(14);

                if (logo is not null)
                {
                    try
                    {
                        row.ConstantItem(50).Height(50).AlignMiddle().Image(logo).FitArea();
                    }
                    catch
                    {
                        // A corrupt or unsupported logo must never break a receipt.
                    }
                }

                row.RelativeItem().AlignMiddle().Column(gym =>
                {
                    gym.Item()
                        .Text(string.IsNullOrWhiteSpace(model.GymName) ? "Gym" : model.GymName)
                        .FontSize(20).Bold().FontColor(Colors.White);

                    if (!string.IsNullOrWhiteSpace(model.GymAddress))
                        gym.Item().PaddingTop(4).Text(model.GymAddress!)
                            .FontSize(8.5f).FontColor(OnBrandMuted);

                    var contact = JoinNonEmpty("   ·   ",
                        string.IsNullOrWhiteSpace(model.GymPhone) ? null : model.GymPhone,
                        string.IsNullOrWhiteSpace(model.GymEmail) ? null : model.GymEmail,
                        string.IsNullOrWhiteSpace(model.TaxNumber) ? null : $"Tax No: {model.TaxNumber}");

                    if (contact.Length > 0)
                        gym.Item().PaddingTop(2).Text(contact)
                            .FontSize(8.5f).FontColor(OnBrandMuted);
                });

                row.ConstantItem(176).AlignMiddle().Background(Colors.White).Column(stamp =>
                {
                    stamp.Item().Background(BrandSoft)
                        .BorderBottom(1).BorderColor(Brand)
                        .PaddingVertical(4).PaddingHorizontal(10)
                        .Text("PAYMENT RECEIPT")
                        .FontSize(9).Bold().LetterSpacing(0.14f).FontColor(BrandDeep);

                    stamp.Item().PaddingHorizontal(10).PaddingVertical(7).Column(detail =>
                    {
                        detail.Item().Element(c => StampLine(c, "Receipt No",
                            ReceiptPresentation.OrDash(model.ReceiptNumber), bold: true));

                        detail.Item().PaddingTop(4).Element(c => StampLine(c, "Date",
                            ReceiptPresentation.DateTimeStamp(model.PaymentDate), bold: false));
                    });
                });
            });
    }

    private static void StampLine(IContainer container, string label, string value, bool bold)
    {
        container.Column(column =>
        {
            column.Item().Text(label.ToUpperInvariant())
                .FontSize(6.5f).SemiBold().FontColor(InkMuted).LetterSpacing(0.12f);

            var text = column.Item().PaddingTop(1).Text(value).FontSize(bold ? 12 : 9.5f);
            if (bold) text.Bold().FontColor(BrandDeep);
        });
    }

    private static void ComposeReceiptBody(IContainer container, PaymentReceiptDto model, string currency)
    {
        var (headline, detail) = ReceiptPresentation.Status(model);
        var (statusInk, statusSoft) = ReceiptPresentation.Tone(model) switch
        {
            ReceiptTone.Paid => (PaidInk, PaidSoft),
            ReceiptTone.Refunded => (RefundInk, RefundSoft),
            _ => (PendingInk, PendingSoft),
        };

        container.Column(body =>
        {
            body.Spacing(11);

            // ---- the headline figure and the verdict, side by side
            // A receipt answers two questions: how much, and is anything still owed. Both used to
            // be findable only by reading a row of the breakdown table, so they now lead the page.
            body.Item().Border(1).BorderColor(Hairline).Row(row =>
            {
                row.RelativeItem().Background(BrandSoft)
                    .PaddingVertical(13).PaddingHorizontal(15).Column(amount =>
                    {
                        amount.Item().Text("AMOUNT PAID")
                            .FontSize(7).Bold().LetterSpacing(0.14f).FontColor(Brand);

                        amount.Item().PaddingTop(3)
                            .Text(ReceiptPresentation.Money(model.FinalAmount, currency))
                            .FontSize(25).Bold().FontColor(BrandDeep);

                        amount.Item().PaddingTop(2)
                            .Text(ReceiptPresentation.LineItemDescription(model))
                            .FontSize(8.5f).FontColor(InkMuted);
                    });

                row.ConstantItem(232).Background(statusSoft)
                    .BorderLeft(1).BorderColor(Hairline)
                    .PaddingVertical(13).PaddingHorizontal(15).Column(status =>
                    {
                        status.Item().Text(headline)
                            .FontSize(12).Bold().LetterSpacing(0.08f).FontColor(statusInk);

                        status.Item().PaddingTop(4).Text(detail)
                            .FontSize(8.5f).LineHeight(1.35f).FontColor(Ink);
                    });
            });

            // ---- who paid, and what for
            body.Item().Row(row =>
            {
                row.Spacing(11);

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
            body.Item().Border(1).BorderColor(Hairline).Table(table =>
            {
                table.ColumnsDefinition(definition =>
                {
                    definition.RelativeColumn(3);
                    definition.ConstantColumn(150);
                });

                table.Header(header =>
                {
                    header.Cell().Element(ItemHeaderCell).Text("DESCRIPTION")
                        .FontSize(7.5f).Bold().LetterSpacing(0.12f).FontColor(Colors.White);
                    header.Cell().Element(ItemHeaderCell).AlignRight().Text("AMOUNT")
                        .FontSize(7.5f).Bold().LetterSpacing(0.12f).FontColor(Colors.White);
                });

                var zebra = false;

                ItemRow(table, ReceiptPresentation.LineItemDescription(model),
                    ReceiptPresentation.Money(model.Amount, currency), shaded: Alternate(ref zebra));

                if (model.DiscountAmount > 0m)
                    ItemRow(table, "Less: discount",
                        ReceiptPresentation.MoneyDeducted(model.DiscountAmount, currency),
                        shaded: Alternate(ref zebra));

                if (model.TaxAmount > 0m)
                    ItemRow(table, "Add: tax",
                        ReceiptPresentation.MoneyAdded(model.TaxAmount, currency),
                        shaded: Alternate(ref zebra));

                ItemRow(table, "TOTAL PAYABLE",
                    ReceiptPresentation.Money(model.FinalAmount, currency), emphasise: true);

                // The zebra restarts below the total: the two halves of the table are separate
                // statements — what was charged, and what has been settled against it.
                zebra = false;

                ItemRow(table, "Amount paid — this receipt",
                    ReceiptPresentation.Money(model.FinalAmount, currency), shaded: Alternate(ref zebra));

                if (model.PaidAmount != model.FinalAmount)
                    ItemRow(table, "Paid to date",
                        ReceiptPresentation.Money(model.PaidAmount, currency), shaded: Alternate(ref zebra));

                ItemRow(table, "Balance outstanding",
                    ReceiptPresentation.Money(model.OutstandingAmount, currency),
                    emphasise: model.OutstandingAmount > 0m,
                    shaded: Alternate(ref zebra));
            });

            // ---- amount in words
            if (!string.IsNullOrWhiteSpace(model.AmountInWords))
            {
                body.Item().Background(ZebraTint)
                    .BorderLeft(3).BorderColor(Brand)
                    .PaddingVertical(7).PaddingHorizontal(11).Text(text =>
                    {
                        text.Span("Amount in words:  ")
                            .FontSize(8.5f).SemiBold().FontColor(InkMuted);
                        text.Span(model.AmountInWords).FontSize(9.5f).SemiBold().FontColor(Ink);
                    });
            }

            // ---- how it was paid
            body.Item().Element(c => WidePanel(c, "Payment details", new[]
            {
                ("Method", ReceiptPresentation.OrDash(model.PaymentMethodName)),
                ("Reference", ReceiptPresentation.OrDash(model.TransactionReference)),
                ("Recorded as", ReceiptPresentation.Humanise(model.StatusText))
            }));

            if (!string.IsNullOrWhiteSpace(model.Notes))
                body.Item().Text(text =>
                {
                    text.Span("Notes:  ").FontSize(8.5f).SemiBold().FontColor(InkMuted);
                    text.Span(model.Notes!).FontSize(8.5f).FontColor(InkMuted);
                });

            // ---- signature
            body.Item().PaddingTop(22).Row(row =>
            {
                row.RelativeItem().AlignBottom()
                    .Text("Received with thanks.")
                    .FontSize(9.5f).Italic().FontColor(InkMuted);

                row.ConstantItem(170).Column(sign =>
                {
                    sign.Item().PaddingBottom(3).LineHorizontal(0.8f).LineColor(InkMuted);
                    sign.Item().AlignCenter().Text("Authorised signature")
                        .FontSize(8.5f).FontColor(InkMuted);
                });
            });
        });
    }

    /// <summary>Flips the zebra flag and returns the shading the current row should use.</summary>
    private static bool Alternate(ref bool zebra)
    {
        zebra = !zebra;
        return !zebra;
    }

    private static void ComposeReceiptFooter(IContainer container, PaymentReceiptDto model)
    {
        container.PaddingHorizontal(ReceiptPad).PaddingBottom(16).Column(footer =>
        {
            footer.Item().PaddingBottom(6).LineHorizontal(2).LineColor(Brand);

            if (!string.IsNullOrWhiteSpace(model.FooterText))
                footer.Item().PaddingBottom(3).AlignCenter().Text(model.FooterText!)
                    .FontSize(8.5f).Italic().FontColor(InkMuted);

            footer.Item().Row(row =>
            {
                row.RelativeItem()
                    .Text("This is a computer-generated receipt and is valid without a signature.")
                    .FontSize(7.5f).FontColor(InkMuted);

                row.ConstantItem(90).AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(InkMuted));
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        });
    }

    /// <summary>One line of the itemised breakdown. The amount column is always right-aligned.</summary>
    private static void ItemRow(TableDescriptor table, string label, string amount,
        bool emphasise = false, bool shaded = false)
    {
        var labelCell = table.Cell().Element(x => ItemCell(x, emphasise, shaded));
        var amountCell = table.Cell().Element(x => ItemCell(x, emphasise, shaded)).AlignRight();

        if (emphasise)
        {
            labelCell.Text(label).FontSize(10).Bold().FontColor(BrandDeep);
            amountCell.Text(amount).FontSize(11.5f).Bold().FontColor(BrandDeep);
        }
        else
        {
            labelCell.Text(label).FontSize(9.5f);
            amountCell.Text(amount).FontSize(9.5f);
        }
    }

    private static IContainer ItemHeaderCell(IContainer container) => container
        .Background(BrandDeep)
        .PaddingVertical(6).PaddingHorizontal(11);

    private static IContainer ItemCell(IContainer container, bool emphasise, bool shaded) => (emphasise
            ? container.Background(BrandSoft).BorderTop(1).BorderColor(Brand)
            : shaded ? container.Background(ZebraTint) : container)
        .BorderBottom(0.5f).BorderColor(Hairline)
        .PaddingVertical(6).PaddingHorizontal(11);

    /// <summary>
    /// A bordered block of caption plus label/value lines, used for the side-by-side panels. The
    /// caption bar carries the brand tint so the two panels read as a pair rather than as two
    /// unrelated boxes.
    /// </summary>
    private static void Panel(IContainer container, string caption, IEnumerable<(string Label, string Value)> lines)
    {
        container.Border(1).BorderColor(Hairline).Column(column =>
        {
            column.Item().Background(BrandSoft)
                .BorderBottom(1).BorderColor(Hairline)
                .PaddingVertical(4).PaddingHorizontal(11)
                .Text(caption.ToUpperInvariant())
                .FontSize(7).Bold().LetterSpacing(0.12f).FontColor(Brand);

            column.Item().PaddingVertical(7).PaddingHorizontal(11).Column(rows =>
            {
                rows.Spacing(3);

                foreach (var line in lines)
                {
                    rows.Item().Row(row =>
                    {
                        row.ConstantItem(74).Text(line.Label)
                            .FontSize(8.5f).FontColor(InkMuted);
                        row.RelativeItem().Text(line.Value).FontSize(9.5f);
                    });
                }
            });
        });
    }

    /// <summary>
    /// The same panel laid out across the page: the values sit side by side rather than stacked,
    /// which suits a short run of facts that would otherwise leave half the width empty.
    /// </summary>
    private static void WidePanel(IContainer container, string caption, (string Label, string Value)[] lines)
    {
        container.Border(1).BorderColor(Hairline).Column(column =>
        {
            column.Item().Background(BrandSoft)
                .BorderBottom(1).BorderColor(Hairline)
                .PaddingVertical(4).PaddingHorizontal(11)
                .Text(caption.ToUpperInvariant())
                .FontSize(7).Bold().LetterSpacing(0.12f).FontColor(Brand);

            column.Item().PaddingVertical(8).PaddingHorizontal(11).Row(row =>
            {
                row.Spacing(11);

                foreach (var line in lines)
                {
                    row.RelativeItem().Column(cell =>
                    {
                        cell.Item().Text(line.Label.ToUpperInvariant())
                            .FontSize(6.5f).SemiBold().LetterSpacing(0.1f).FontColor(InkMuted);
                        cell.Item().PaddingTop(2).Text(line.Value).FontSize(9.5f);
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
        DateTimeOffs