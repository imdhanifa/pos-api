namespace PosSaas.Domain.Common;

/// <summary>
/// Common columns every persisted entity carries (Section 16, Phase 0): a client-issuable
/// Guid primary key, so an entity created offline (e.g. an Order rung up on a phone with no
/// network) never needs a server round-trip just to get an id; tenant scoping; audit
/// timestamps; a monotonically-increasing <see cref="SyncVersion"/> that
/// SyncController's push endpoint uses for conflict detection; and a soft-delete flag, so
/// nothing is ever hard-deleted out from under a sync in flight.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? TenantId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public long SyncVersion { get; set; }
    public bool IsDeleted { get; set; }
}
