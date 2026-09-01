using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using PosSaas.Api.Auth;
using PosSaas.Api.Dtos;
using PosSaas.Domain.Entities;
using PosSaas.Infrastructure.Persistence;

namespace PosSaas.Api.Controllers;

/// <summary>Product catalog - categories, units, products, variants, barcodes, modifiers (Section 3 Phase 1).</summary>
[ApiController]
[Authorize]
[Route("api/catalog")]
public class CatalogController : ControllerBase
{
    private readonly PosSaasStore _store;
    private readonly IMemoryCache _cache;
    public CatalogController(PosSaasStore store, IMemoryCache cache)
    {
        _store = store;
        _cache = cache;
    }

    /// <summary>
    /// Categories/products change rarely compared to how often the POS/Products screens re-fetch
    /// them (every screen focus - see mobile/src/screens/Products/ProductsScreen.tsx), so a short
    /// per-tenant cache cuts DB round-trips without noticeably delaying a newly-added product -
    /// Create* below evicts the matching key immediately rather than waiting out the TTL.
    /// </summary>
    private static readonly TimeSpan CatalogCacheDuration = TimeSpan.FromSeconds(30);

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<Category>>> GetCategories()
    {
        var tenantId = User.GetTenantId();
        var categories = await _cache.GetOrCreateAsync($"catalog:categories:{tenantId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CatalogCacheDuration;
            return await _store.Categories.GetAllAsync(tenantId);
        });
        return Ok(categories);
    }

    [HttpPost("categories")]
    [Authorize(Roles = "Owner,Manager")]
    public async Task<ActionResult<Category>> CreateCategory(CreateCategoryRequest request)
    {
        var tenantId = User.GetTenantId();
        var category = new Category { TenantId = tenantId, Name = request.Name };
        await _store.Categories.AddAsync(category);
        _cache.Remove($"catalog:categories:{tenantId}");
        return Ok(category);
    }

    [HttpGet("units")]
    public async Task<ActionResult<IReadOnlyList<Unit>>> GetUnits()
        => Ok(await _store.Units.GetAllAsync(User.GetTenantId()));

    [HttpPost("units")]
    [Authorize(Roles = "Owner,Manager")]
    public async Task<ActionResult<Unit>> CreateUnit(CreateUnitRequest request)
    {
        var unit = new Unit { TenantId = User.GetTenantId(), Name = request.Name, ShortCode = request.ShortCode };
        await _store.Units.AddAsync(unit);
        return Ok(unit);
    }

    [HttpGet("products")]
    public async Task<ActionResult<IReadOnlyList<Product>>> GetProducts()
    {
        var tenantId = User.GetTenantId();
        var products = await _cache.GetOrCreateAsync($"catalog:products:{tenantId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CatalogCacheDuration;
            return await _store.Products.GetAllAsync(tenantId);
        });
        return Ok(products);
    }

    [HttpGet("products/{id}")]
    public async Task<ActionResult<Product>> GetProduct(Guid id)
    {
        var product = await _store.Products.GetByIdAsync(id);
        return product is null || !User.BelongsToCurrentTenant(product) ? NotFound() : Ok(product);
    }

    [HttpPost("products")]
    [Authorize(Roles = "Owner,Manager")]
    public async Task<ActionResult<Product>> CreateProduct(CreateProductRequest request)
    {
        var tenantId = User.GetTenantId();
        var product = new Product
        {
            TenantId = tenantId,
            CategoryId = request.CategoryId,
            UnitId = request.UnitId,
            Name = request.Name,
            Description = request.Description,
            Sku = request.Sku,
            BasePrice = request.BasePrice,
            TaxRatePercent = request.TaxRatePercent,
            TrackInventory = request.TrackInventory
        };
        await _store.Products.AddAsync(product);
        _cache.Remove($"catalog:products:{tenantId}");
        return Ok(product);
    }

    [HttpPost("products/{productId}/variants")]
    [Authorize(Roles = "Owner,Manager")]
    public async Task<ActionResult<ProductVariant>> AddVariant(Guid productId, CreateProductVariantRequest request)
    {
        var product = await _store.Products.GetByIdAsync(productId);
        if (product is null || !User.BelongsToCurrentTenant(product)) return NotFound();

        var variant = new ProductVariant { TenantId = User.GetTenantId(), ProductId = productId, Name = request.Name, PriceDelta = request.PriceDelta };
        await _store.ProductVariants.AddAsync(variant);
        return Ok(variant);
    }

    [HttpPost("products/{productId}/barcodes")]
    [Authorize(Roles = "Owner,Manager")]
    public async Task<ActionResult<Barcode>> AddBarcode(Guid productId, CreateBarcodeRequest request)
    {
        var product = await _store.Products.GetByIdAsync(productId);
        if (product is null || !User.BelongsToCurrentTenant(product)) return NotFound();

        var barcode = new Barcode
        {
            TenantId = User.GetTenantId(),
            ProductId = productId,
            ProductVariantId = request.ProductVariantId,
            Code = request.Code
        };
        await _store.Barcodes.AddAsync(barcode);
        return Ok(barcode);
    }

    /// <summary>
    /// Looks a scanned barcode up against the product catalog - see mobile/src/screens/Products/ProductsScreen.tsx's
    /// handleScanned, which offers to register the code as a new product on a 404.
    /// </summary>
    [HttpGet("barcodes/{code}")]
    public async Task<ActionResult<Barcode>> LookupBarcode(string code)
    {
        var barcodes = await _store.Barcodes.GetAllAsync(User.GetTenantId());
        var match = barcodes.FirstOrDefault(b => b.Code == code);
        return match is null ? NotFound(new { message = $"\"{code}\" isn't registered to a product yet." }) : Ok(match);
    }

    [HttpGet("modifiers")]
    public async Task<ActionResult<IReadOnlyList<Modifier>>> GetModifiers()
        => Ok(await _store.Modifiers.GetAllAsync(User.GetTenantId()));

    [HttpPost("modifiers")]
    [Authorize(Roles = "Owner,Manager")]
    public async Task<ActionResult<Modifier>> CreateModifier(CreateModifierRequest request)
    {
        var modifier = new Modifier { TenantId = User.GetTenantId(), Name = request.Name, PriceDelta = request.PriceDelta };
        await _store.Modifiers.AddAsync(modifier);
        return Ok(modifier);
    }
}
