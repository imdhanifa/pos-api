using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using PosSaas.Api.Auth;
using PosSaas.Api.Dtos;
using PosSaas.Api.Reporting;
using PosSaas.Domain.Common;
using PosSaas.Infrastructure.Persistence;

namespace PosSaas.Api.Controllers;

/// <summary>Dashboard/sales/best-seller reporting - Section 3 Phase 7.</summary>
[ApiController]
[Authorize]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly PosSaasStore _store;
    private readonly IMemoryCache _cache;
    public ReportsController(PosSaasStore store, IMemoryCache cache)
    {
        _store = store;
        _cache = cache;
    }

    /// <summary>
    /// Unlike catalog writes (CatalogController), there's no single "orders changed" write path
    /// to evict this on - orders/payments/stock adjustments all move these numbers. A short TTL
    /// (rather than exact invalidation) keeps the dashboard cheap to poll while staying close
    /// enough to live for a summary view.
    /// </summary>
    private static readonly TimeSpan DashboardCacheDuration = TimeSpan.FromSeconds(15);

    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardSummaryDto>> GetDashboard()
    {
        var tenantId = User.GetTenantId();
        var summary = await _cache.GetOrCreateAsync($"reports:dashboard:{tenantId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = DashboardCacheDuration;
            var today = DateTime.UtcNow.Date;

            var orders = (await _store.Orders.GetAllAsync(tenantId))
                .Where(o => o.OrderedAtUtc >= today && o.Status != OrderStatus.Refunded)
                .ToList();
            var lowStockCount = (await _store.Inventory.GetAllAsync(tenantId))
                .Count(i => i.QuantityOnHand <= i.ReorderLevel);

            var orderTypeBreakdown = orders
                .GroupBy(o => o.Type)
                .Select(g => new OrderTypeBreakdownDto(g.Key.ToString(), g.Count(), g.Sum(o => o.GrandTotal)))
                .OrderByDescending(b => b.Total)
                .ToList();

            var todayOrderIds = orders.Select(o => o.Id).ToHashSet();
            var paymentMethodBreakdown = (await _store.Payments.GetAllAsync(tenantId))
                .Where(p => todayOrderIds.Contains(p.OrderId))
                .GroupBy(p => p.Method)
                .Select(g => new PaymentMethodBreakdownDto(g.Key.ToString(), g.Count(), g.Sum(p => p.Amount)))
                .OrderByDescending(b => b.TransactionCount)
                .ToList();

            return new DashboardSummaryDto(orders.Sum(o => o.GrandTotal), orders.Count, lowStockCount, orders.Sum(o => o.DiscountTotal), orderTypeBreakdown, paymentMethodBreakdown);
        });

        return Ok(summary);
    }

    /// <summary>
    /// Daily/Weekly/Monthly/Yearly/Custom (Reports screen's period chips) are all just this same
    /// endpoint with a different [fromUtc, toUtc] and groupBy - the client picks the range, this
    /// picks how coarse each bar is: "day" for a week/month view, "week"/"month" so a year view
    /// isn't 365 one-order bars.
    /// </summary>
    [HttpGet("sales")]
    public async Task<ActionResult<SalesReportDto>> GetSales([FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, [FromQuery] string groupBy = "day")
    {
        if (groupBy is not ("day" or "week" or "month"))
        {
            return BadRequest(new { message = "groupBy must be day, week or month" });
        }

        return Ok(await ComputeSalesReport(fromUtc, toUtc, groupBy));
    }

    /// <summary>
    /// Plain-value counterpart to GetSales, for Export to call directly - GetSales always wraps
    /// its result in Ok(...) rather than returning a bare SalesReportDto, which means
    /// ActionResult&lt;T&gt;.Value is never populated on success (only .Result is, holding the
    /// already-serialized OkObjectResult) - an Export that called GetSales and read .Value would
    /// silently get null and fall through to re-emitting that JSON response as the "file" instead
    /// (which is exactly what happened before this was split out).
    /// </summary>
    private async Task<SalesReportDto> ComputeSalesReport(DateTime? fromUtc, DateTime? toUtc, string groupBy)
    {
        var tenantId = User.GetTenantId();
        var from = fromUtc ?? DateTime.UtcNow.Date.AddDays(-7);
        var to = toUtc ?? DateTime.UtcNow;

        var orders = (await _store.Orders.GetAllAsync(tenantId))
            .Where(o => o.OrderedAtUtc >= from && o.OrderedAtUtc <= to && o.Status != OrderStatus.Refunded)
            .ToList();

        var buckets = orders
            .GroupBy(o => ResolveBucketStart(o.OrderedAtUtc, groupBy))
            .OrderBy(g => g.Key)
            .Select(g => new SalesBucketDto(g.Key, g.Count(), g.Sum(o => o.GrandTotal), g.Sum(o => o.DiscountTotal)))
            .ToList();

        return new SalesReportDto(from, to, groupBy, buckets);
    }

    /// <summary>Bucket start for a given order timestamp - a day is itself, a week is its ISO
    /// Monday, a month is its 1st. Matches ReportExporter's PDF/CSV bucketing 1:1 since both read
    /// from the same GetSales response shape.</summary>
    private static DateTime ResolveBucketStart(DateTime orderedAtUtc, string groupBy)
    {
        var date = orderedAtUtc.Date;
        return groupBy switch
        {
            "week" => date.AddDays(-((int)date.DayOfWeek == 0 ? 6 : (int)date.DayOfWeek - 1)), // Monday of that week
            "month" => new DateTime(date.Year, date.Month, 1),
            _ => date,
        };
    }

    [HttpGet("best-sellers")]
    public async Task<ActionResult<IReadOnlyList<BestSellerDto>>> GetBestSellers([FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, [FromQuery] int top = 10)
        => Ok(await ComputeBestSellers(fromUtc, toUtc, top));

    private async Task<List<BestSellerDto>> ComputeBestSellers(DateTime? fromUtc, DateTime? toUtc, int top)
    {
        var tenantId = User.GetTenantId();
        var from = fromUtc ?? DateTime.MinValue;
        var to = toUtc ?? DateTime.MaxValue;

        var orderIdsInRange = (await _store.Orders.GetAllAsync(tenantId))
            .Where(o => o.OrderedAtUtc >= from && o.OrderedAtUtc <= to && o.Status != OrderStatus.Refunded)
            .Select(o => o.Id)
            .ToHashSet();

        var items = (await _store.OrderItems.GetAllAsync(tenantId)).Where(i => orderIdsInRange.Contains(i.OrderId));

        return items
            .GroupBy(i => new { i.ProductId, i.ProductNameSnapshot })
            .Select(g => new BestSellerDto(g.Key.ProductId, g.Key.ProductNameSnapshot, g.Sum(i => i.Quantity), g.Sum(i => i.LineTotal)))
            .OrderByDescending(d => d.QuantitySold)
            .Take(top)
            .ToList();
    }

    /// <summary>Same data as GetSales + GetBestSellers, handed back as a file instead of JSON -
    /// see mobile/src/screens/Reports/ReportsScreen.tsx's Export buttons and ReportExporter.cs.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, [FromQuery] string groupBy = "day", [FromQuery] string format = "csv")
    {
        if (groupBy is not ("day" or "week" or "month"))
        {
            return BadRequest(new { message = "groupBy must be day, week or month" });
        }
        if (format is not ("csv" or "pdf"))
        {
            return BadRequest(new { message = "format must be csv or pdf" });
        }

        var sales = await ComputeSalesReport(fromUtc, toUtc, groupBy);
        var bestSellers = await ComputeBestSellers(fromUtc, toUtc, 10);

        var tenantId = User.GetTenantId();
        var tenant = tenantId is null ? null : await _store.Tenants.GetByIdAsync(tenantId.Value);
        var businessName = tenant?.Name ?? "Sales Report";

        var rangeLabel = $"{sales.FromUtc:yyyyMMdd}-{sales.ToUtc:yyyyMMdd}";
        if (format == "pdf")
        {
            var pdfBytes = ReportExporter.BuildPdf(businessName, sales.FromUtc, sales.ToUtc, groupBy, sales.Buckets, bestSellers);
            return File(pdfBytes, "application/pdf", $"sales-report-{rangeLabel}.pdf");
        }

        var csvBytes = ReportExporter.BuildCsv(businessName, sales.FromUtc, sales.ToUtc, groupBy, sales.Buckets, bestSellers);
        return File(csvBytes, "text/csv", $"sales-report-{rangeLabel}.csv");
    }
}
