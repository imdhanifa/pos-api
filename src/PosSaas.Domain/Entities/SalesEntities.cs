using PosSaas.Domain.Common;

namespace PosSaas.Domain.Entities;

public class Customer : TenantScopedEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public decimal LoyaltyPoints { get; set; }
}

public class RestaurantTable : TenantScopedEntity
{
    public Guid BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public TableStatus Status { get; set; } = TableStatus.Available;
}

/// <summary>
/// POS Cart and Billing (Section 3, 9, 16) - see PosController.CreateOrder. Refunds/voids are
/// modeled as a reversal Order (<see cref="ReversalOfOrderId"/>) rather than a destructive
/// edit to the original.
/// </summary>
public class Order : TenantScopedEntity
{
    public Guid BranchId { get; set; }
    public Guid? DeviceId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public OrderType Type { get; set; }
    public OrderStatus Status { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? TableId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime OrderedAtUtc { get; set; }
    public Guid? ReversalOfOrderId { get; set; }
    public decimal SubTotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public List<OrderItem> Items { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
}

public class OrderItem : TenantScopedEntity
{
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
    public string? Notes { get; set; }
}

public class Payment : TenantScopedEntity
{
    public Guid OrderId { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; }
    public decimal Amount { get; set; }
    public decimal? TenderedAmount { get; set; }
    public decimal? ChangeGiven { get; set; }
}
