using PosSaas.Domain.Common;

namespace PosSaas.Domain.Entities;

public class Printer : TenantScopedEntity
{
    public Guid? BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? BleServiceUuid { get; set; }
    public string? BleCharacteristicUuid { get; set; }
}

/// <summary>Global, not tenant-scoped - the same fixed set of plans is offered to every tenant.</summary>
public class SubscriptionPlan : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public decimal MonthlyPriceInr { get; set; }
    public int TrialDays { get; set; }
    public string FeaturesJson { get; set; } = "[]";
}

public class Subscription : TenantScopedEntity
{
    public Guid PlanId { get; set; }
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Trialing;
    public DateTime? TrialEndsAtUtc { get; set; }
    public DateTime? CurrentPeriodEndUtc { get; set; }
}

/// <summary>Gateway payment records for non-cash methods - see PosController.RecordPayment, README "UPI payment reconciliation".</summary>
public class PaymentTransaction : TenantScopedEntity
{
    public Guid? OrderId { get; set; }
    public string? Gateway { get; set; }
    public string? GatewayReference { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
}

/// <summary>One row per pushed change - see SyncController.Push. TenantId/DeviceId let a device's own queue be replayed/audited.</summary>
public class SyncQueueItem : TenantScopedEntity
{
    public Guid DeviceId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public SyncOperation Operation { get; set; }
    public long EntitySyncVersion { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public SyncEntityStatus Status { get; set; } = SyncEntityStatus.Pending;
    public DateTime? AppliedAtUtc { get; set; }
    public string? ConflictReason { get; set; }
}

/// <summary>Backup metadata only - see README "Google Drive backup/restore": the actual OAuth/upload happens on-device.</summary>
public class Backup : TenantScopedEntity
{
    public string? Checksum { get; set; }
    public long SizeBytes { get; set; }
    public int Version { get; set; }
    public BackupStatus Status { get; set; } = BackupStatus.Pending;
}

public class AuditLog : TenantScopedEntity
{
    public Guid? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityName { get; set; }
    public Guid? EntityId { get; set; }
    public string? DetailsJson { get; set; }
}
