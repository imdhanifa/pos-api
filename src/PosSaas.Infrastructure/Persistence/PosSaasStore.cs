using PosSaas.Domain.Common;
using PosSaas.Domain.Entities;

namespace PosSaas.Infrastructure.Persistence;

/// <summary>
/// Aggregates one <see cref="IRepository{T}"/> per entity - the single seam every
/// controller reaches persistence through. Two backing implementations are wired here:
///
///   - <see cref="PosSaasStore(PosSaasDbContext)"/> - the real path: every property is an
///     <see cref="EfRepository{T}"/> wrapping a shared, DI-scoped <see cref="PosSaasDbContext"/>
///     (SQL Server via EF Core). Register `AddDbContext&lt;PosSaasDbContext&gt;(...)` and
///     `AddScoped&lt;PosSaasStore&gt;()` in Program.cs - this is what a normal `dotnet run` on
///     a machine with SQL Server does, and is what's wired up by default.
///   - <see cref="PosSaasStore()"/> - the no-args fallback: every property is an
///     <see cref="InMemoryRepository{T}"/>, identical to how this class worked before the
///     EF Core swap. Useful for a quick demo or a test with no SQL Server available -
///     register it with `builder.Services.AddSingleton(new PosSaasStore())` (or just
///     `new PosSaasStore()` directly in a unit test) instead of the DbContext-based
///     registration if you want this mode. Data does not persist across restarts here.
///
/// Every controller depends on IRepository&lt;T&gt; through these properties, never on the
/// concrete repository type or on PosSaasDbContext, so switching between the two
/// constructors above is the only change needed anywhere in the app.
/// </summary>
public class PosSaasStore
{
    public IRepository<Tenant> Tenants { get; }
    public IRepository<Branch> Branches { get; }
    public IRepository<User> Users { get; }
    public IRepository<Role> Roles { get; }
    public IRepository<Permission> Permissions { get; }
    public IRepository<Device> Devices { get; }

    public IRepository<Category> Categories { get; }
    public IRepository<Unit> Units { get; }
    public IRepository<Product> Products { get; }
    public IRepository<ProductVariant> ProductVariants { get; }
    public IRepository<Barcode> Barcodes { get; }
    public IRepository<Modifier> Modifiers { get; }

    public IRepository<Customer> Customers { get; }
    public IRepository<RestaurantTable> Tables { get; }
    public IRepository<Order> Orders { get; }
    public IRepository<OrderItem> OrderItems { get; }
    public IRepository<Payment> Payments { get; }

    public IRepository<Inventory> Inventory { get; }
    public IRepository<StockLedger> StockLedger { get; }
    public IRepository<Purchase> Purchases { get; }
    public IRepository<PurchaseItem> PurchaseItems { get; }
    public IRepository<StockAdjustment> StockAdjustments { get; }

    public IRepository<Printer> Printers { get; }
    public IRepository<SubscriptionPlan> SubscriptionPlans { get; }
    public IRepository<Subscription> Subscriptions { get; }
    public IRepository<PaymentTransaction> PaymentTransactions { get; }
    public IRepository<SyncQueueItem> SyncQueue { get; }
    public IRepository<Backup> Backups { get; }
    public IRepository<AuditLog> AuditLogs { get; }

    /// <summary>Real path: EF Core + SQL Server via a DI-scoped PosSaasDbContext.</summary>
    public PosSaasStore(PosSaasDbContext dbContext)
    {
        Tenants = new EfRepository<Tenant>(dbContext);
        Branches = new EfRepository<Branch>(dbContext);
        Users = new EfRepository<User>(dbContext);
        Roles = new EfRepository<Role>(dbContext);
        Permissions = new EfRepository<Permission>(dbContext);
        Devices = new EfRepository<Device>(dbContext);

        Categories = new EfRepository<Category>(dbContext);
        Units = new EfRepository<Unit>(dbContext);
        Products = new EfRepository<Product>(dbContext);
        ProductVariants = new EfRepository<ProductVariant>(dbContext);
        Barcodes = new EfRepository<Barcode>(dbContext);
        Modifiers = new EfRepository<Modifier>(dbContext);

        Customers = new EfRepository<Customer>(dbContext);
        Tables = new EfRepository<RestaurantTable>(dbContext);
        Orders = new EfRepository<Order>(dbContext);
        OrderItems = new EfRepository<OrderItem>(dbContext);
        Payments = new EfRepository<Payment>(dbContext);

        Inventory = new EfRepository<Inventory>(dbContext);
        StockLedger = new EfRepository<StockLedger>(dbContext);
        Purchases = new EfRepository<Purchase>(dbContext);
        PurchaseItems = new EfRepository<PurchaseItem>(dbContext);
        StockAdjustments = new EfRepository<StockAdjustment>(dbContext);

        Printers = new EfRepository<Printer>(dbContext);
        SubscriptionPlans = new EfRepository<SubscriptionPlan>(dbContext);
        Subscriptions = new EfRepository<Subscription>(dbContext);
        PaymentTransactions = new EfRepository<PaymentTransaction>(dbContext);
        SyncQueue = new EfRepository<SyncQueueItem>(dbContext);
        Backups = new EfRepository<Backup>(dbContext);
        AuditLogs = new EfRepository<AuditLog>(dbContext);
    }

    /// <summary>
    /// Fallback path: in-memory only, no SQL Server / EF Core needed at all. See class doc
    /// comment above for when to use this instead of the DbContext-based constructor.
    /// </summary>
    public PosSaasStore()
    {
        Tenants = new InMemoryRepository<Tenant>();
        Branches = new InMemoryRepository<Branch>();
        Users = new InMemoryRepository<User>();
        Roles = new InMemoryRepository<Role>();
        Permissions = new InMemoryRepository<Permission>();
        Devices = new InMemoryRepository<Device>();

        Categories = new InMemoryRepository<Category>();
        Units = new InMemoryRepository<Unit>();
        Products = new InMemoryRepository<Product>();
        ProductVariants = new InMemoryRepository<ProductVariant>();
        Barcodes = new InMemoryRepository<Barcode>();
        Modifiers = new InMemoryRepository<Modifier>();

        Customers = new InMemoryRepository<Customer>();
        Tables = new InMemoryRepository<RestaurantTable>();
        Orders = new InMemoryRepository<Order>();
        OrderItems = new InMemoryRepository<OrderItem>();
        Payments = new InMemoryRepository<Payment>();

        Inventory = new InMemoryRepository<Inventory>();
        StockLedger = new InMemoryRepository<StockLedger>();
        Purchases = new InMemoryRepository<Purchase>();
        PurchaseItems = new InMemoryRepository<PurchaseItem>();
        StockAdjustments = new InMemoryRepository<StockAdjustment>();

        Printers = new InMemoryRepository<Printer>();
        SubscriptionPlans = new InMemoryRepository<SubscriptionPlan>();
        Subscriptions = new InMemoryRepository<Subscription>();
        PaymentTransactions = new InMemoryRepository<PaymentTransaction>();
        SyncQueue = new InMemoryRepository<SyncQueueItem>();
        Backups = new InMemoryRepository<Backup>();
        AuditLogs = new InMemoryRepository<AuditLog>();
    }
}
