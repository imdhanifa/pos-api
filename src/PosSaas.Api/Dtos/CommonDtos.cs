namespace PosSaas.Api.Dtos;

// --- Auth ---
/// <summary>DeviceId/DeviceName are optional and, when sent, get upserted into the Devices table
/// (AuthController.UpsertDevice) - see mobile/src/device/deviceInfo.ts, which generates and
/// persists one DeviceId per install and sends it on every login/register call.</summary>
public record LoginRequest(string Email, string Password, Guid? DeviceId = null, string? DeviceName = null);
public record LoginResponse(string Token, Guid UserId, Guid TenantId, string Role, string FullName);
/// <summary>New merchant sign-up (Section 3 Phase 1's onboarding) - creates a Tenant, its default
/// Branch, an Owner role/user and starts a trial on the cheapest plan, mirroring
/// PosSaas.Infrastructure.Persistence.SeedData's demo-tenant shape but for one real merchant
/// instead of canned data.</summary>
public record RegisterRequest(string BusinessName, string OwnerFullName, string Email, string Password, string? DefaultCurrency = null, Guid? DeviceId = null, string? DeviceName = null);

// --- Business (BusinessController) ---
/// <summary>The billing-template fields (Receipt*) are all optional so existing callers that only
/// ever touched Name/LegalName/BusinessType/DefaultCurrency keep working unchanged - when omitted,
/// UpdateCurrentTenant leaves that field on the Tenant untouched rather than blanking it out.
/// See mobile/src/screens/Billing/BillingTemplateScreen.tsx and printing/escpos.ts's
/// BillingTemplateOptions, which this shape mirrors.</summary>
public record UpdateBusinessRequest(
    string Name,
    string? LegalName,
    string? BusinessType,
    string DefaultCurrency,
    string? ReceiptAddressLine = null,
    string? ReceiptPhone = null,
    string? ReceiptFooterMessage = null,
    bool? ReceiptShowTaxBreakdown = null,
    bool? ReceiptShowDiscountLine = null,
    int? ReceiptPaperWidth = null);
public record CreateBranchRequest(string Name, string? Address);

// --- Users (UsersController) ---
public record CreateUserRequest(string FullName, string Email, string Password, Guid RoleId, Guid? BranchId);
public record CreateRoleRequest(string Name);

// --- Catalog (CatalogController) ---
public record CreateCategoryRequest(string Name);
public record CreateUnitRequest(string Name, string ShortCode);
public record CreateProductRequest(string Name, Guid? CategoryId, Guid? UnitId, decimal BasePrice, decimal TaxRatePercent, string? Sku, string? Description, bool TrackInventory = true);
public record CreateProductVariantRequest(string Name, decimal PriceDelta);
public record CreateBarcodeRequest(string Code, Guid? ProductVariantId = null);
public record CreateModifierRequest(string Name, decimal PriceDelta);

// --- Tables (TablesController) ---
public record CreateTableRequest(Guid BranchId, string Name, int Capacity);
public record UpdateTableStatusRequest(string Status);

// --- Customers (CustomersController) ---
public record CreateCustomerRequest(string Name, string? Phone, string? Email);

// --- Inventory (InventoryController) ---
public record CreateStockAdjustmentRequest(Guid BranchId, Guid ProductId, decimal QuantityDelta, string? Reason);

// --- Purchases (PurchasesController) ---
public record CreatePurchaseItemRequest(Guid ProductId, decimal Quantity, decimal UnitCost);
public record CreatePurchaseRequest(Guid? BranchId, string? SupplierName, List<CreatePurchaseItemRequest> Items);

// --- Reports (ReportsController) ---
/// <summary>OrderTypeBreakdown powers Dashboard's pie chart (mobile/src/components/PieChart.tsx) -
/// today's order count/total split by DineIn/Takeaway/Delivery.</summary>
public record DashboardSummaryDto(decimal TodaySales, int TodayOrderCount, int LowStockCount, decimal TodayDiscountTotal, List<OrderTypeBreakdownDto> OrderTypeBreakdown, List<PaymentMethodBreakdownDto> PaymentMethodBreakdown);
public record OrderTypeBreakdownDto(string Type, int OrderCount, decimal Total);
/// <summary>Today's transaction count/total per Cash/Upi/Card/Wallet - powers Dashboard's second
/// pie chart (mobile/src/screens/Dashboard/DashboardScreen.tsx). Keyed off the paid order's own
/// OrderedAtUtc, not Payment.CreatedAtUtc - repository inserts always stamp CreatedAtUtc to the
/// real insert-time "now" (see EfRepository's doc comment), which for seeded historical orders is
/// whenever the seed ran, not the order's actual (backdated) date.</summary>
public record PaymentMethodBreakdownDto(string Method, int TransactionCount, decimal Total);
public record BestSellerDto(Guid ProductId, string ProductName, decimal QuantitySold, decimal RevenueTotal);
/// <summary>One row of GetSales's breakdown - BucketStart is a day, an ISO week's Monday, or a
/// month's 1st depending on the request's groupBy (see ReportsController.ResolveBucketStart).</summary>
public record SalesBucketDto(DateTime BucketStart, int OrderCount, decimal Total, decimal DiscountTotal);
public record SalesReportDto(DateTime FromUtc, DateTime ToUtc, string GroupBy, List<SalesBucketDto> Buckets);

// --- Backups (BackupsController) ---
public record RecordBackupRequest(string? Checksum, long SizeBytes, int Version);

// --- Restore (RestoreController) ---
public record RestoreStatusDto(string Status);

// --- Subscription (SubscriptionController) ---
public record StartTrialRequest(Guid PlanId);
/// <summary>EndDateUtc is TrialEndsAtUtc while Status is Trialing, else CurrentPeriodEndUtc - whichever governs
/// when this subscription actually stops working. ExpiringSoon flips true at 5 days out (see
/// mobile/src/components/SubscriptionBanner.tsx, which polls this once per screen focus and
/// nudges the owner at most once per calendar day while it's true).</summary>
public record SubscriptionStatusDto(string Status, DateTime? EndDateUtc, int? DaysRemaining, bool ExpiringSoon);
