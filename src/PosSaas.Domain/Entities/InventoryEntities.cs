using PosSaas.Domain.Common;

namespace PosSaas.Domain.Entities;

/// <summary>One row per (Branch, Product) - the on-hand quantity PosController decrements on every sale.</summary>
public class Inventory : TenantScopedEntity
{
    public Guid BranchId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public decimal QuantityOnHand { get; set; }
    public decimal ReorderLevel { get; set; }
    public decimal? AverageCost { get; set; }
}

/// <summary>Append-only movement history behind every stock change - sales, purchases, adjustments, transfers.</summary>
public class StockLedger : TenantScopedEntity
{
    public Guid? BranchId { get; set; }
    public Guid ProductId { get; set; }
    public StockMovementType MovementType { get; set; }
    public decimal QuantityDelta { get; set; }
    public string? Note { get; set; }
}

public class Purchase : TenantScopedEntity
{
    public Guid? BranchId { get; set; }
    public string? SupplierName { get; set; }
    public decimal TotalCost { get; set; }
    public List<PurchaseItem> Items { get; set; } = new();
}

public class PurchaseItem : TenantScopedEntity
{
    public Guid PurchaseId { get; set; }
    public Guid ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
}

public class StockAdjustment : TenantScopedEntity
{
    public Guid? BranchId { get; set; }
    public Guid ProductId { get; set; }
    public decimal QuantityDelta { get; set; }
    public string? Reason { get; set; }
}
