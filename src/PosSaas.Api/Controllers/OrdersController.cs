using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSaas.Api.Auth;
using PosSaas.Domain.Common;
using PosSaas.Domain.Entities;
using PosSaas.Infrastructure.Persistence;

namespace PosSaas.Api.Controllers;

/// <summary>
/// Browsing/managing orders across dine-in/takeaway/delivery (Section 3 Phase 3). Order
/// creation/payment/refund itself is <see cref="PosController"/> - this controller is the
/// read side kitchen/floor staff use (order queue, status filtering).
/// </summary>
[ApiController]
[Authorize]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly PosSaasStore _store;
    public OrdersController(PosSaasStore store) => _store = store;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Order>>> GetOrders([FromQuery] string? status, [FromQuery] Guid? branchId, [FromQuery] int? top)
    {
        // GetAllAsync already returns newest-updated-first (EfRepository/InMemoryRepository's own
        // ordering), so `top` here is "N most recent" - e.g. Dashboard's Recent Orders list, which
        // would otherwise pull all of a tenant's order history just to show 5.
        var orders = (await _store.Orders.GetAllAsync(User.GetTenantId())).AsEnumerable();

        if (branchId is not null)
        {
            orders = orders.Where(o => o.BranchId == branchId);
        }
        if (status is not null && Enum.TryParse<OrderStatus>(status, true, out var parsedStatus))
        {
            orders = orders.Where(o => o.Status == parsedStatus);
        }
        if (top is > 0)
        {
            orders = orders.Take(top.Value);
        }

        return Ok(orders.ToList());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Order>> GetOrder(Guid id)
    {
        var order = await _store.Orders.GetByIdAsync(id);
        return order is null || !User.BelongsToCurrentTenant(order) ? NotFound() : Ok(order);
    }

    [HttpPatch("{id}/status")]
    public async Task<ActionResult<Order>> UpdateStatus(Guid id, [FromQuery] string status)
    {
        if (!Enum.TryParse<OrderStatus>(status, true, out var parsedStatus))
        {
            return BadRequest(new { message = "invalid status" });
        }

        var order = await _store.Orders.GetByIdAsync(id);
        if (order is null || !User.BelongsToCurrentTenant(order)) return NotFound();

        order.Status = parsedStatus;
        await _store.Orders.UpdateAsync(order);
        return Ok(order);
    }

    /// <summary>
    /// Cancels an order that hasn't been paid yet - unlike PosController.RefundOrder (which
    /// reverses money that already changed hands and stays Owner/Manager-only), this is abandoning
    /// unpaid work-in-progress, so any authenticated staff can do it. Puts back what
    /// PosController.CreateOrder took: restocks each line's inventory and frees the table if one
    /// was attached - a plain PATCH .../status?status=Cancelled (still available, unrestricted)
    /// would leave both of those wrong.
    /// </summary>
    [HttpPost("{id}/cancel")]
    public async Task<ActionResult<Order>> CancelOrder(Guid id)
    {
        var order = await _store.Orders.GetByIdAsync(id);
        if (order is null || !User.BelongsToCurrentTenant(order)) return NotFound();

        if (order.Status is OrderStatus.Completed or OrderStatus.Cancelled or OrderStatus.Refunded)
        {
            return BadRequest(new { message = $"Cannot cancel a {order.Status} order - once it's paid, refund it instead." });
        }

        var tenantId = User.GetTenantId();
        var items = order.Items.Any()
            ? order.Items
            : (await _store.OrderItems.GetAllAsync(tenantId)).Where(i => i.OrderId == order.Id);

        var inventoryRows = await _store.Inventory.GetAllAsync(tenantId);
        foreach (var item in items)
        {
            if (item.ProductVariantId is not null) continue; // matches CreateOrder's own skip for variant lines

            var row = inventoryRows.FirstOrDefault(i => i.BranchId == order.BranchId && i.ProductId == item.ProductId);
            if (row is not null)
            {
                row.QuantityOnHand += item.Quantity;
                await _store.Inventory.UpdateAsync(row);
            }

            await _store.StockLedger.AddAsync(new StockLedger
            {
                TenantId = tenantId,
                BranchId = order.BranchId,
                ProductId = item.ProductId,
                MovementType = StockMovementType.AdjustmentIn,
                QuantityDelta = item.Quantity,
                Note = $"Order {order.OrderNumber} cancelled",
            });
        }

        if (order.TableId is not null)
        {
            var table = await _store.Tables.GetByIdAsync(order.TableId.Value);
            if (table is not null)
            {
                table.Status = TableStatus.Available;
                await _store.Tables.UpdateAsync(table);
            }
        }

        order.Status = OrderStatus.Cancelled;
        await _store.Orders.UpdateAsync(order);
        return Ok(order);
    }
}
