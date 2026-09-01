using Microsoft.EntityFrameworkCore;
using PosSaas.Domain.Common;
using PosSaas.Domain.Entities;

namespace PosSaas.Infrastructure.Persistence;

/// <summary>
/// Real EF Core + SQL Server persistence - the implementation the README's "Swapping in EF
/// Core + SQL Server" section describes, and the backing store <see cref="EfRepository{T}"/>
/// wraps. One `DbSet&lt;T&gt;` per entity exposed on <see cref="PosSaasStore"/>, plus the two
/// pure join tables (`RolePermission`, `ProductModifier`) that don't inherit
/// <see cref="BaseEntity"/> and so aren't reachable through <see cref="IRepository{T}"/>, but
/// still need to exist in the schema for `Role.RolePermissions` / `Product`'s modifier
/// relationships to work.
///
/// Kept alongside (not replacing) <see cref="InMemoryRepository{T}"/> - see that file and
/// <see cref="PosSaasStore"/>'s doc comment for why both persistence paths are wired in.
/// </summary>
public class PosSaasDbContext : DbContext
{
    public PosSaasDbContext(DbContextOptions<PosSaasDbContext> options) : base(options)
    {
    }

    // --- Identity / tenancy ---
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Device> Devices => Set<Device>();

    // --- Catalog ---
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<Barcode> Barcodes => Set<Barcode>();
    public DbSet<Modifier> Modifiers => Set<Modifier>();
    public DbSet<ProductModifier> ProductModifiers => Set<ProductModifier>();

    // --- Sales ---
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<RestaurantTable> Tables => Set<RestaurantTable>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();

    // --- Inventory / purchasing ---
    // Named InventoryRows (not "Inventory") to avoid clashing with the `Inventory` entity
    // type it's a DbSet of - `DbSet<Inventory> Inventory` would compile, but it reads badly
    // and shadows the type name everywhere this context is used.
    public DbSet<Inventory> InventoryRows => Set<Inventory>();
    public DbSet<StockLedger> StockLedgers => Set<StockLedger>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<PurchaseItem> PurchaseItems => Set<PurchaseItem>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();

    // --- Platform / billing / sync ---
    public DbSet<Printer> Printers => Set<Printer>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<SyncQueueItem> SyncQueueItems => Set<SyncQueueItem>();
    public DbSet<Backup> Backups => Set<Backup>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // --- Money precision (Section 16): every decimal/decimal? column across every
        // entity gets HasPrecision(18, 2), without hand-listing each one. .ToList() first
        // because we're mutating facets on the same mutable model we're enumerating.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?))
                {
                    modelBuilder.Entity(entityType.ClrType)
                        .Property(property.Name)
                        .HasPrecision(18, 2);
                }
            }
        }

        // --- Pure join tables that don't inherit BaseEntity: composite keys. ---
        modelBuilder.Entity<RolePermission>(b =>
        {
            b.HasKey(x => new { x.RoleId, x.PermissionId });
        });

        modelBuilder.Entity<ProductModifier>(b =>
        {
            b.HasKey(x => new { x.ProductId, x.ModifierId });
        });

        // --- Explicit collection navigations, wired to the plain Guid FK already on the
        // child entity, so EF doesn't have to guess a shadow FK. These are the ONLY real
        // entity-to-entity relationships configured in this model - see the note below
        // about TenantId deliberately NOT becoming a Tenant navigation everywhere.
        modelBuilder.Entity<Tenant>()
            .HasMany(t => t.Branches)
            .WithOne()
            .HasForeignKey(b => b.TenantId);

        modelBuilder.Entity<Product>()
            .HasMany(p => p.Variants)
            .WithOne()
            .HasForeignKey(v => v.ProductId);

        modelBuilder.Entity<Product>()
            .HasMany(p => p.Barcodes)
            .WithOne()
            .HasForeignKey(b => b.ProductId);

        modelBuilder.Entity<Order>()
            .HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey(i => i.OrderId);

        modelBuilder.Entity<Order>()
            .HasMany(o => o.Payments)
            .WithOne()
            .HasForeignKey(p => p.OrderId);

        modelBuilder.Entity<Purchase>()
            .HasMany(p => p.Items)
            .WithOne()
            .HasForeignKey(i => i.PurchaseId);

        modelBuilder.Entity<Role>()
            .HasMany(r => r.RolePermissions)
            .WithOne()
            .HasForeignKey(rp => rp.RoleId);

        // --- TenantId indexing, deliberately NOT a Tenant relationship. ---
        // BaseEntity.TenantId is a plain `Guid?`, not a navigation to Tenant, on every
        // entity in the system. We do not configure a HasOne<Tenant>() from every entity -
        // only Tenant.Branches above is a real FK relationship to Tenant. Every other
        // tenant-scoped entity just gets an index on TenantId for query performance
        // (every controller filters "all rows for my tenant" constantly).
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            if (typeof(TenantScopedEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType).HasIndex(nameof(BaseEntity.TenantId));
            }
        }
    }
}
