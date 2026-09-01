using PosSaas.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSaas.Api.Dtos;
using PosSaas.Domain.Common;
using PosSaas.Domain.Entities;
using PosSaas.Infrastructure.Persistence;

namespace PosSaas.Api.Controllers;

/// <summary>
/// POS Cart and Billing - Section 3, 9, 16. Order creation is idempotent: the client
/// generates the Order's Guid on-device (offline-first) and sends it as part of the
/// request; replaying the same request (e.g. after a flaky connection) returns the
/// existing order instead of creating a duplicate bill (Section 16 "idempotent APIs").
/// </summary>
[ApiController]
[Authorize]
[Route("api/pos")]
public class PosController : ControllerBase
{
    private readonly PosSaasStore _store;
    public PosController(PosSaasStore store) => _store = store;

    [HttpPost("orders")]
    public async Task<ActionResult<OrderDto>> CreateOrder([FromQuery] Guid? clientOrderId, CreateOrderRequest request)
    {
        // Idempotency: if the caller already sent this exact order id, return it as-is. Guarded
        // by tenant so a Guid collision with (or a guess at) another tenant's order id can't leak
        // that order back to this caller - it just falls through and creates a fresh one instead,
        // same as if clientOrderId had never been seen.
        if (clientOrderId is not null)
        {
            var existing = await _store.Orders.GetByIdAsync(clientOrderId.Value);
            if (existing is not null && User.BelongsToCurrentTenant(existing))
            {
                return Ok(await ToDto(existing));
            }
        }

        if (!Enum.TryParse<OrderType>(request.Type, true, out var orderType))
        {
            return BadRequest(new { message = "type must be DineIn, Takeaway or Delivery" });
        }

        var tenantId = User.GetTenantId();
        var userId = User.GetUserId();

        // TableId is client-supplied - without this check a request naming another tenant's
        // table would mark THAT tenant's table Occupied (a cross-tenant write), not just fail to
        // find it.
        RestaurantTable? requestedTable = null;
        if (request.TableId is not null)
        {
            requestedTable = await _store.Tables.GetByIdAsync(request.TableId.Value);
            if (!User.BelongsToCurrentTenant(requestedTable))
            {
                return BadRequest(new { message = "table not found" });
            }
        }

        // Same reasoning as TableId - CustomerId is client-supplied too. Not currently sent by
        // the mobile app (no customer-picker in PosScreen.tsx yet), but the API itself shouldn't
        // trust it unchecked once one exists.
        if (request.CustomerId is not null && !User.BelongsToCurrentTenant(await _store.Customers.GetByIdAsync(request.CustomerId.Value)))
        {
            return BadRequest(new { message = "customer not found" });
        }

        var order = new Order
        {
            Id = clientOrderId ?? Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = request.BranchId,
            DeviceId = request.DeviceId,
            OrderNumber = GenerateOrderNumber(),
            Type = orderType,
            Status = OrderStatus.Open,
            CustomerId = request.CustomerId,
            TableId = request.TableId,
            CreatedByUserId = userId ?? Guid.Empty,
            OrderedAtUtc = DateTime.UtcNow
        };

        decimal subTotal = 0, taxTotal = 0;
        foreach (var item in request.Items)
        {
            var lineTotal = OrderMath.CalculateLineTotal(item.Quantity, item.UnitPrice, item.DiscountAmount, item.TaxAmount);
            subTotal += item.Quantity * item.UnitPrice;
            taxTotal += item.TaxAmount;

            order.Items.Add(new OrderItem
            {
                TenantId = tenantId,
                OrderId = order.Id,
                ProductId = item.ProductId,
                ProductVariantId = item.ProductVariantId,
                ProductNameSnapshot = item.ProductName,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                DiscountAmount = item.DiscountAmount,
                TaxAmount = item.TaxAmount,
                LineTotal = lineTotal,
                Notes = item.Notes
            });
        }

        order.SubTotal = subTotal;
        order.DiscountTotal = request.DiscountTotal;
        order.TaxTotal = taxTotal;
        order.GrandTotal = OrderMath.CalculateGrandTotal(subTotal, request.DiscountTotal, taxTotal);
        order.Status = OrderStatus.Confirmed;

        await _store.Orders.AddAsync(order);
        foreach (var item in order.Items)
        {
            await _store.OrderItems.AddAsync(item);
            if (item.ProductVariantId is null) // simple stock decrement demo; full costing lives in Inventory module
            {
                await AdjustStockForSale(tenantId, request.BranchId, item.ProductId, item.Quantity);
            }
        }

        if (requestedTable is not null)
        {
            requestedTable.Status = TableStatus.Occupied;
            await _store.Tables.UpdateAsync(requestedTable);
        }

        return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, await ToDto(order));
    }

    [HttpGet("orders/{id}")]
    public async Task<ActionResult<OrderDto>> GetOrder(Guid id)
    {
        var order = await _store.Orders.GetByIdAsync(id);
        if (order is null || !User.BelongsToCurrentTenant(order)) return NotFound();
        return Ok(await ToDto(order));
    }

    /// <summary>Cash and UPI Payments - Section 3, 9.</summary>
    [HttpPost("payments")]
    public async Task<ActionResult<Payment>> RecordPayment(PaymentRequest request)
    {
        if (!Enum.TryParse<PaymentMethod>(request.Method, true, out var method))
        {
            return BadRequest(new { message = "method must be Cash, Upi, Card or Wallet" });
        }

        // Idempotency, same reasoning/pattern as CreateOrder's clientOrderId: if this exact
        // payment was already recorded (e.g. an offline-queued retry whose first attempt actually
        // reached the server but the response was lost), return it as-is instead of charging twice.
        if (request.ClientPaymentId is not null)
        {
            var existingPayment = await _store.Payments.GetByIdAsync(request.ClientPaymentId.Value);
            if (existingPayment is not null && User.BelongsToCurrentTenant(existingPayment))
            {
                return Ok(existingPayment);
            }
        }

        var order = await _store.Orders.GetByIdAsync(request.OrderId);
        if (order is null || !User.BelongsToCurrentTenant(order)) return NotFound(new { message = "order not found" });

        var payment = new Payment
        {
            Id = request.ClientPaymentId ?? Guid.NewGuid(),
            TenantId = User.GetTenantId(),
            OrderId = order.Id,
            Method = method,
            Status = method == PaymentMethod.Cash ? PaymentStatus.Success : PaymentStatus.Pending,
            Amount = request.Amount,
            TenderedAmount = request.TenderedAmount,
            ChangeGiven = method == PaymentMethod.Cash && request.TenderedAmount is not null
                ? Math.Max(0, request.TenderedAmount.Value - request.Amount)
                : null
        };
        await _store.Payments.AddAsync(payment);

        if (method == PaymentMethod.Cash)
        {
            order.Status = OrderStatus.Completed;
            await _store.Orders.UpdateAsync(order);
        }

        return Ok(payment);
    }

    /// <summary>Void/refund via a reversal order, never a destructive edit (Section 16).</summary>
    [HttpPost("orders/{id}/refund")]
    [Authorize(Roles = "Owner,Manager")]
    public async Task<ActionResult<OrderDto>> RefundOrder(Guid id)
    {
        var original = await _store.Orders.GetByIdAsync(id);
        if (original is null || !User.BelongsToCurrentTenant(original)) return NotFound();

        var reversal = new Order
        {
            TenantId = original.TenantId,
            BranchId = original.BranchId,
            DeviceId = original.DeviceId,
            OrderNumber = GenerateOrderNumber(),
            Type = original.Type,
            Status = OrderStatus.Refunded,
            CreatedByUserId = User.GetUserId() ?? Guid.Empty,
            ReversalOfOrderId = original.Id,
            SubTotal = -original.SubTotal,
            DiscountTotal = -original.DiscountTotal,
            TaxTotal = -original.TaxTotal,
            GrandTotal = -original.GrandTotal
        };
        await _store.Orders.AddAsync(reversal);

        original.Status = OrderStatus.Refunded;
        await _store.Orders.UpdateAsync(original);

        return Ok(await ToDto(reversal));
    }

    private async Task AdjustStockForSale(Guid? tenantId, Guid branchId, Guid productId, decimal quantity)
    {
        var allInventory = await _store.Inventory.GetAllAsync(tenantId);
        var row = allInventory.FirstOrDefault(i => i.BranchId == branchId && i.ProductId == productId);
        if (row is not null)
        {
            row.QuantityOnHand -= quantity;
            await _store.Inventory.UpdateAsync(row);
        }

        await _store.StockLedger.AddAsync(new StockLedger
        {
            TenantId = tenantId,
            BranchId = branchId,
            ProductId = productId,
            MovementType = StockMovementType.SaleOut,
            QuantityDelta = -quantity
        });
    }

    private async Task<OrderDto> ToDto(Order order)
    {
        var items = order.Items.Any()
            ? order.Items
            : (await _store.OrderItems.GetAllAsync(order.TenantId)).Where(i => i.OrderId == order.Id);

        return new OrderDto(
            order.Id, order.OrderNumber, order.Type.ToString(), order.Status.ToString(),
            order.SubTotal, order.DiscountTotal, order.TaxTotal, order.GrandTotal, order.OrderedAtUtc,
            items.Select(i => new CartItemDto(i.ProductId, i.ProductVariantId, i.ProductNameSnapshot, i.Quantity, i.UnitPrice, i.DiscountAmount, i.TaxAmount, i.Notes)).ToList());
    }

    private static string GenerateOrderNumber() => $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
}
