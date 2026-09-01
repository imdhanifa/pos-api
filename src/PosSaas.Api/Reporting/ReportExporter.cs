using System.Globalization;
using System.Text;
using PosSaas.Api.Dtos;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PosSaas.Api.Reporting;

/// <summary>
/// Turns the same data ReportsController.GetSales/GetBestSellers return on-screen into a
/// downloadable file - see ReportsController.Export. CSV needs no extra package (Excel opens it
/// natively); PDF uses QuestPDF, licensed under its free Community license here (Program.cs sets
/// QuestPDF.Settings.License = LicenseType.Community once at startup).
/// </summary>
public static class ReportExporter
{
    public static byte[] BuildCsv(string businessName, DateTime fromUtc, DateTime toUtc, string groupBy, IReadOnlyList<SalesBucketDto> buckets, IReadOnlyList<BestSellerDto> bestSellers)
    {
        var sb = new StringBuilder();
        void Row(params object[] cells) => sb.AppendLine(string.Join(",", cells.Select(CsvEscape)));

        Row("Sales Report", businessName);
        Row("From", fromUtc.ToString("yyyy-MM-dd"));
        Row("To", toUtc.ToString("yyyy-MM-dd"));
        Row("Grouped by", groupBy);
        sb.AppendLine();

        Row("Period", "Orders", "Total", "Discount");
        foreach (var bucket in buckets)
        {
            Row(bucket.BucketStart.ToString("yyyy-MM-dd"), bucket.OrderCount, bucket.Total.ToString(CultureInfo.InvariantCulture), bucket.DiscountTotal.ToString(CultureInfo.InvariantCulture));
        }
        Row("Total", buckets.Sum(b => b.OrderCount), buckets.Sum(b => b.Total).ToString(CultureInfo.InvariantCulture), buckets.Sum(b => b.DiscountTotal).ToString(CultureInfo.InvariantCulture));
        sb.AppendLine();

        Row("Rank", "Product", "Quantity Sold", "Revenue");
        for (var i = 0; i < bestSellers.Count; i++)
        {
            var item = bestSellers[i];
            Row(i + 1, item.ProductName, item.QuantitySold.ToString(CultureInfo.InvariantCulture), item.RevenueTotal.ToString(CultureInfo.InvariantCulture));
        }

        // UTF-8 BOM so Excel (which guesses ANSI otherwise) renders any non-ASCII product names correctly.
        return new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string CsvEscape(object value)
    {
        var text = value?.ToString() ?? "";
        return text.Contains(',') || text.Contains('"') || text.Contains('\n')
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }

    public static byte[] BuildPdf(string businessName, DateTime fromUtc, DateTime toUtc, string groupBy, IReadOnlyList<SalesBucketDto> buckets, IReadOnlyList<BestSellerDto> bestSellers)
    {
        var totalOrders = buckets.Sum(b => b.OrderCount);
        var totalSales = buckets.Sum(b => b.Total);
        var totalDiscount = buckets.Sum(b => b.DiscountTotal);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text(businessName).FontSize(18).Bold();
                    col.Item().Text("Sales Report").FontSize(12).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(2).Text($"{fromUtc:yyyy-MM-dd} to {toUtc:yyyy-MM-dd} - grouped by {groupBy}").FontColor(Colors.Grey.Darken1);
                });

                page.Content().PaddingTop(16).Column(col =>
                {
                    col.Spacing(16);

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Total Sales").FontColor(Colors.Grey.Darken1);
                            c.Item().Text(totalSales.ToString("N2", CultureInfo.InvariantCulture)).FontSize(16).Bold();
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Total Orders").FontColor(Colors.Grey.Darken1);
                            c.Item().Text(totalOrders.ToString()).FontSize(16).Bold();
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Total Discount").FontColor(Colors.Grey.Darken1);
                            c.Item().Text(totalDiscount.ToString("N2", CultureInfo.InvariantCulture)).FontSize(16).Bold();
                        });
                    });

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("Period").Bold();
                            header.Cell().AlignRight().Text("Orders").Bold();
                            header.Cell().AlignRight().Text("Total").Bold();
                            header.Cell().AlignRight().Text("Discount").Bold();
                            header.Cell().ColumnSpan(4).PaddingTop(4).BorderBottom(1).BorderColor(Colors.Grey.Lighten1);
                        });

                        foreach (var bucket in buckets)
                        {
                            table.Cell().PaddingVertical(2).Text(bucket.BucketStart.ToString("yyyy-MM-dd"));
                            table.Cell().PaddingVertical(2).AlignRight().Text(bucket.OrderCount.ToString());
                            table.Cell().PaddingVertical(2).AlignRight().Text(bucket.Total.ToString("N2", CultureInfo.InvariantCulture));
                            table.Cell().PaddingVertical(2).AlignRight().Text(bucket.DiscountTotal.ToString("N2", CultureInfo.InvariantCulture));
                        }

                        if (buckets.Count == 0)
                        {
                            table.Cell().ColumnSpan(4).PaddingVertical(6).Text("No sales in this period.").FontColor(Colors.Grey.Darken1);
                        }
                    });

                    col.Item().PaddingTop(8).Text("Best Sellers").FontSize(13).Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(28);
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("#").Bold();
                            header.Cell().Text("Product").Bold();
                            header.Cell().AlignRight().Text("Qty").Bold();
                            header.Cell().AlignRight().Text("Revenue").Bold();
                            header.Cell().ColumnSpan(4).PaddingTop(4).BorderBottom(1).BorderColor(Colors.Grey.Lighten1);
                        });

                        for (var i = 0; i < bestSellers.Count; i++)
                        {
                            var item = bestSellers[i];
                            table.Cell().PaddingVertical(2).Text((i + 1).ToString());
                            table.Cell().PaddingVertical(2).Text(item.ProductName);
                            table.Cell().PaddingVertical(2).AlignRight().Text(item.QuantitySold.ToString(CultureInfo.InvariantCulture));
                            table.Cell().PaddingVertical(2).AlignRight().Text(item.RevenueTotal.ToString("N2", CultureInfo.InvariantCulture));
                        }

                        if (bestSellers.Count == 0)
                        {
                            table.Cell().ColumnSpan(4).PaddingVertical(6).Text("No sales in this period.").FontColor(Colors.Grey.Darken1);
                        }
                    });
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Generated ").FontColor(Colors.Grey.Darken1);
                    text.Span(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'")).FontColor(Colors.Grey.Darken1);
                });
            });
        });

        return document.GeneratePdf();
    }
}
