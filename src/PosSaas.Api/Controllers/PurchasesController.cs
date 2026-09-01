using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSaas.Api.Auth;
using PosSaas.Api.Dtos;
using PosSaas.Domain.Common;
using PosSaas.Domain.Entities;
using PosSaas.Infrastructure.Persistence;

namespace PosSaas.Api.Controllers;

/// <summary>Purchasing/receiving stock from a supplier - Section 3 Phase 4. Receiving a purchase increments Inventory and writes a StockLedger row per line, mirroring PosController's sale-side decrement.</summary>
[ApiController]
[Authorize]
[Route("api/purchases")]
public class PurchasesController : ControllerBase
{
    private readonly PosSaasStore _store;
    public PurchasesController(PosSaasStore store) => _store = store;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Purchase>>> GetPurchases()
        => Ok(await _store.Purchases.GetAllAsync(User.GetTenantId()));

    [HttpGet("{id}")]
    public async Task<ActionResult<Purchase>> GetPurchase(Guid id)
    {
        var purchase = await _store.Purchases.GetByIdAsync(id);
        return purchase is null || !User.BelongsToCurrentTenant(purchase) ? NotFound() : Ok(purchase);
    }

    [HttpPost]
    [Authorize(Roles = "Owner,Manager")]
    public async Task<ActionResult<Purchase>> CreatePurchase(CreatePurchaseRequest request)
    {
        var tenantId = User.GetTenantId();

        var purchase = new Purchase
        {
            TenantId = tenantId,
            BranchId = request.BranchId,
            SupplierName = request.SupplierName,
            TotalCost = request.Items.Sum(i => i.Quantity * i.UnitCost)
        };
        await _store.Purchases.AddAsync(purchase);

        foreach (var item in request.Items)
        {
            purchase.Items.Add(await AddPurchaseItemAndReceiveStock(tenantId, purchase, item, request.BranchId));
        }

        return Ok(purchase);
    }

    private async Task<PurchaseItem> AddPurchaseItemAndReceiveStock(Guid? tenantId, Purchase purchase, CreatePurchaseItemRequest item, Guid? branchId)
    {
        var purchaseItem = new PurchaseItem
        {
            TenantId = tenantId,
            PurchaseId = purchase.Id,
            ProductId = item.ProductId,
            Quantity = item.Quantity,
            UnitCost = item.UnitCost
        };
        await _store.PurchaseItems.AddAsync(purchaseItem);

        if (branchId is not null)
        {
            var rows = await _store.Inventory.GetAllAsync(tenantId);
            var row = rows.FirstOrDefault(i => i.BranchId == branchId && i.ProductId == item.ProductId);
            if (row is not null)
            {
                row.QuantityOnHand += item.Quantity;
                await _store.Inventory.UpdateAsync(row);
            }
            else
            {
                await _store.Inventory.AddAsync(new Inventory
                {
                    TenantId = tenantId,
                    BranchId = branchId.Value,
                    ProductId = item.ProductId,
                    QuantityOnHand = item.Quantity
                });
            }
        }

        await _store.StockLedger.AddAsync(new StockLedger
        {
            TenantId = tenantId,
            BranchId = branchId,
            ProductId = item.ProductId,
            MovementType = StockMovementType.PurchaseIn,
            QuantityDelta = item.Quantity
        });

        return purchaseItem;
    }
}
