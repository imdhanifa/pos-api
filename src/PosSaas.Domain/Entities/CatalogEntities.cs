using PosSaas.Domain.Common;

namespace PosSaas.Domain.Entities;

public class Category : TenantScopedEntity
{
    public string Name { get; set; } = string.Empty;
}

public class Unit : TenantScopedEntity
{
    public string Name { get; set; } = string.Empty;
    public string ShortCode { get; set; } = string.Empty;
}

public class Product : TenantScopedEntity
{
    public Guid? CategoryId { get; set; }
    public Guid? UnitId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Sku { get; set; }
    public decimal BasePrice { get; set; }
    public decimal TaxRatePercent { get; set; }
    public bool TrackInventory { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public List<ProductVariant> Variants { get; set; } = new();
    public List<Barcode> Barcodes { get; set; } = new();
}

public class ProductVariant : TenantScopedEntity
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal PriceDelta { get; set; }
}

public class Barcode : TenantScopedEntity
{
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public string Code { get; set; } = string.Empty;
}

public class Modifier : TenantScopedEntity
{
    public string Name { get; set; } = string.Empty;
    public decimal PriceDelta { get; set; }
}

/// <summary>Pure join row (Product, Modifier) - composite-keyed in PosSaasDbContext.</summary>
public class ProductModifier
{
    public Guid ProductId { get; set; }
    public Guid ModifierId { get; set; }
}
