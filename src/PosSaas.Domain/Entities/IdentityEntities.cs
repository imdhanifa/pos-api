using PosSaas.Domain.Common;

namespace PosSaas.Domain.Entities;

/// <summary>The tenant itself - the one entity that is NOT tenant-scoped, since it IS the tenant.</summary>
public class Tenant : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? BusinessType { get; set; }
    public string DefaultCurrency { get; set; } = "INR";
    public List<Branch> Branches { get; set; } = new();

    // --- Billing/receipt template (mobile/src/screens/Billing/BillingTemplateScreen.tsx,
    // src/printing/escpos.ts's BillingTemplateOptions) - one per tenant, same as everything else
    // on this record, so it rides along on the existing GET/PUT /api/business endpoints rather
    // than needing a dedicated controller/table. ---
    public string? ReceiptAddressLine { get; set; }
    public string? ReceiptPhone { get; set; }
    public string ReceiptFooterMessage { get; set; } = "Thank you!";
    public bool ReceiptShowTaxBreakdown { get; set; } = true;
    public bool ReceiptShowDiscountLine { get; set; } = true;
    public int ReceiptPaperWidth { get; set; } = 32;
}

public class Branch : TenantScopedEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
}

public class User : TenantScopedEntity
{
    public Guid? BranchId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public Guid RoleId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Role : TenantScopedEntity
{
    public string Name { get; set; } = string.Empty;
    public bool IsSystemRole { get; set; }
    public List<RolePermission> RolePermissions { get; set; } = new();
}

/// <summary>Global, not tenant-scoped - the same fixed permission catalog applies to every tenant.</summary>
public class Permission : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>Pure join row (Role, Permission) - no audit/sync columns of its own, composite-keyed in PosSaasDbContext.</summary>
public class RolePermission
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
}

public class Device : TenantScopedEntity
{
    public Guid? BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DeviceKind Kind { get; set; } = DeviceKind.MobilePos;
    public DateTime? LastSyncedAtUtc { get; set; }
}
