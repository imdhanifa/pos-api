namespace PosSaas.Domain.Common;

/// <summary>
/// Marker base for every entity that belongs to exactly one tenant - i.e. everything except
/// the small set of platform-global rows (<c>Tenant</c> itself, <c>SubscriptionPlan</c>,
/// <c>Permission</c>). <c>PosSaasDbContext.OnModelCreating</c> uses this distinction to
/// decide which tables get an index on <see cref="BaseEntity.TenantId"/> - every
/// tenant-scoped table does, since every controller filters "rows for my tenant" constantly.
/// </summary>
public abstract class TenantScopedEntity : BaseEntity
{
}
