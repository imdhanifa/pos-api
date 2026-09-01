using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSaas.Api.Auth;
using PosSaas.Api.Dtos;
using PosSaas.Domain.Common;
using PosSaas.Domain.Entities;
using PosSaas.Infrastructure.Persistence;

namespace PosSaas.Api.Controllers;

/// <summary>Stock levels, adjustments and the movement ledger - Section 3 Phase 4.</summary>
[ApiController]
[Authorize]
[Route("api/inventory")]
public class InventoryController : ControllerBase
{
    private readonly PosSaasStore _store;
    public InventoryController(PosSaasStore store) => _store = store;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Inventory>>> GetInventory([FromQuery] Guid? branchId)
    {
        var rows = (await _store.Inventory.GetAllAsync(User.GetTenantId())).AsEnumerable();
        if (branchId is not null)
        {
            rows = rows.Where(i => i.BranchId == branchId);
        }
        return Ok(rows.ToList());
    }

    [HttpGet("low-stock")]
    public async Task<ActionResult<IReadOnlyList<Inventory>>> GetLowStock()
    {
        var rows = await _store.Inventory.GetAllAsync(User.GetTenantId());
        return Ok(rows.Where(i => i.QuantityOnHand <= i.ReorderLevel).ToList());
    }

    [HttpGet("ledger")]
    public async Task<ActionResult<IReadOnlyList<StockLedger>>> GetLedger([FromQuery] Guid? productId)
    {
        var rows = (await _store.StockLedger.GetAllAsync(User.GetTenantId())).AsEnumerable();
        if (productId is not null)
        {
            rows = rows.Where(l => l.ProductId == productId);
        }
        return Ok(rows.ToList());
    }

    /// <summary>Manual correction (breakage, stock count, etc.) - not a sale or purchase, so it goes through the ledger directly.</summary>
    [HttpPost("adjustments")]
    [Authorize(Roles = "Owner,Manager")]
    public async Task<ActionResult<StockAdjustment>> CreateAdjustment(CreateStockAdjustmentRequest request)
    {
        var tenantId = User.GetTenantId();
        var rows = await _store.Inventory.GetAllAsync(tenantId);
        var row = rows.FirstOrDefault(i => i.BranchId == request.BranchId && i.ProductId == request.ProductId);
        if (row is not null)
        {
            row.QuantityOnHand += request.QuantityDelta;
            await _store.Inventory.UpdateAsync(row);
        }
        else
        {
            await _store.Inventory.AddAsync(new Inventory
            {
                TenantId = tenantId,
                BranchId = request.BranchId,
                ProductId = request.ProductId,
                QuantityOnHand = request.QuantityDelta
            });
        }

        await _store.StockLedger.AddAsync(new StockLedger
        {
            TenantId = tenantId,
            BranchId = request.BranchId,
            ProductId = request.ProductId,
            MovementType = request.QuantityDelta >= 0 ? StockMovementType.AdjustmentIn : StockMovementType.AdjustmentOut,
            QuantityDelta = request.QuantityDelta,
            Note = request.Reason
        });

        var adjustment = new StockAdjustment
        {
            TenantId = tenantId,
            BranchId = request.BranchId,
            ProductId = request.ProductId,
            QuantityDelta = request.QuantityDelta,
            Reason = request.Reason
        };
        await _store.StockAdjustments.AddAsync(adjustment);
        return Ok(adjustment);
    }
}
