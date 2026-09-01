using System.Text.Json;
using PosSaas.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSaas.Api.Dtos;
using PosSaas.Domain.Common;
using PosSaas.Domain.Entities;
using PosSaas.Infrastructure.Persistence;

namespace PosSaas.Api.Controllers;

/// <summary>
/// Cloud Synchronization (Section 3, 4, 8, 16, 17 Phase 6).
///
/// What's real now: Push compares the incoming <c>EntitySyncVersion</c> against the
/// CURRENT server-side row's <see cref="BaseEntity.SyncVersion"/> for (EntityName,
/// EntityId) before accepting a change - a genuinely new entity is always applied, an
/// existing one is applied only if the device's version is strictly newer, and anything
/// else (device's view is stale or equal) is recorded as <see cref="SyncEntityStatus.Conflict"/>
/// instead of silently overwritten. This was the actually-dangerous bug the old comment on
/// this class called out: the push endpoint used to accept every change as Applied with no
/// version check at all.
///
/// What's still simplified: applying an accepted, non-conflicting push only deep-merges
/// fields for "Inventory" (<c>QuantityOnHand</c>) - the entity type most likely to actually
/// conflict across two devices selling the same product concurrently. For every other
/// entity type, an accepted push just bumps the server row's SyncVersion/UpdatedAtUtc to
/// acknowledge it as the newer version, without merging the rest of the payload's fields in
/// yet. Full per-field payload merge for every entity type (all 29 tables) is the next
/// increment - see the TODO on <see cref="ApplyChangeAsync"/>. Section 8's "synchronize/
/// merge missing cloud transactions rather than replacing newer local data" is satisfied at
/// the conflict-*detection* level by this pass; per-field/CRDT-style merge on the apply side
/// is still open.
///
/// Pull is unchanged - it returns rows changed since a watermark; applying those rows back
/// into the mobile client's local SQLite is owned by the mobile side (mobile/src/sync).
/// </summary>
[ApiController]
[Authorize]
[Route("api/sync")]
public class SyncController : ControllerBase
{
    private readonly PosSaasStore _store;
    public SyncController(PosSaasStore store) => _store = store;

    [HttpPost("push")]
    public async Task<ActionResult<List<SyncPushResult>>> Push(SyncPushRequest request)
    {
        var results = new List<SyncPushResult>();

        foreach (var change in request.Changes)
        {
            if (!Enum.TryParse<SyncOperation>(change.Operation, true, out var op))
            {
                results.Add(new SyncPushResult(change.EntityId, "Failed", "unknown operation"));
                continue;
            }

            var queueItem = new SyncQueueItem
            {
                TenantId = User.GetTenantId(),
                DeviceId = request.DeviceId,
                EntityName = change.EntityName,
                EntityId = change.EntityId,
                Operation = op,
                EntitySyncVersion = change.EntitySyncVersion,
                PayloadJson = change.PayloadJson
            };

            var currentEntity = await LookupCurrentEntityAsync(change.EntityName, change.EntityId);

            // A row that exists but belongs to a different tenant must be treated exactly like
            // "no server-side row for THIS tenant yet" (the branch just below), never surfaced or
            // merged into - LookupCurrentEntityAsync's GetByIdAsync calls have no tenant awareness
            // of their own (see EfRepository's doc comment), so without this a device could push a
            // change referencing another tenant's entity id and have it applied to that tenant's
            // row (e.g. bumping SyncVersion on, or - for Inventory - overwriting the
            // QuantityOnHand of, data it doesn't own). Global rows (Tenant/Permission/
            // SubscriptionPlan - TenantId is null by design, not per-tenant) are exempt since
            // there's no tenant to mismatch against.
            if (currentEntity is { TenantId: not null } && currentEntity.TenantId != User.GetTenantId())
            {
                currentEntity = null;
            }

            if (currentEntity is null)
            {
                // No server-side row yet for this (EntityName, EntityId) - a genuine
                // insert, so no conflict is possible. Nothing to apply to (there's no
                // server row to update from a bare push payload without a typed create
                // path per entity), but there is nothing to reject either.
                queueItem.Status = SyncEntityStatus.Applied;
                queueItem.AppliedAtUtc = DateTime.UtcNow;
            }
            else if (change.EntitySyncVersion <= currentEntity.SyncVersion)
            {
                // The device's view is stale: it last saw a version at or before what the
                // server already has, so applying it would silently clobber newer data.
                // Report it back as a conflict for the client to reconcile instead
                // (Section 8: "merge missing cloud transactions rather than replacing
                // newer local data").
                queueItem.Status = SyncEntityStatus.Conflict;
                queueItem.ConflictReason =
                    $"server has SyncVersion {currentEntity.SyncVersion}, device pushed {change.EntitySyncVersion}";
            }
            else
            {
                // The device's version is strictly newer than the server's - accept it.
                await ApplyChangeAsync(change, currentEntity);
                queueItem.Status = SyncEntityStatus.Applied;
                queueItem.AppliedAtUtc = DateTime.UtcNow;
            }

            await _store.SyncQueue.AddAsync(queueItem);
            results.Add(new SyncPushResult(change.EntityId, queueItem.Status.ToString(), queueItem.ConflictReason));
        }

        var device = await _store.Devices.GetByIdAsync(request.DeviceId);
        if (device is not null && User.BelongsToCurrentTenant(device))
        {
            device.LastSyncedAtUtc = DateTime.UtcNow;
            await _store.Devices.UpdateAsync(device);
        }

        return Ok(results);
    }

    [HttpPost("pull")]
    public async Task<IActionResult> Pull(SyncPullRequest request)
    {
        var tenantId = User.GetTenantId();
        var since = request.SinceUtc ?? DateTime.MinValue;

        // Demo pull: return recently-changed orders/products/inventory since the given watermark.
        var orders = (await _store.Orders.GetAllAsync(tenantId)).Where(o => o.UpdatedAtUtc > since);
        var products = (await _store.Products.GetAllAsync(tenantId)).Where(p => p.UpdatedAtUtc > since);
        var inventory = (await _store.Inventory.GetAllAsync(tenantId)).Where(i => i.UpdatedAtUtc > since);

        return Ok(new { serverTimeUtc = DateTime.UtcNow, orders, products, inventory });
    }

    /// <summary>
    /// Dispatches by the string EntityName (as the device's local sync_queue table stores
    /// it) to the matching typed repository on <see cref="PosSaasStore"/> and returns the
    /// CURRENT server-side row, or null if none exists yet. A switch over the entity name is
    /// used rather than reflection or a pre-built delegate dictionary - simplest and most
    /// readable for a fixed, known set of entity names.
    /// </summary>
    private async Task<BaseEntity?> LookupCurrentEntityAsync(string entityName, Guid entityId)
    {
        switch (entityName)
        {
            case "Tenant": return await _store.Tenants.GetByIdAsync(entityId);
            case "Branch": return await _store.Branches.GetByIdAsync(entityId);
            case "User": return await _store.Users.GetByIdAsync(entityId);
            case "Role": return await _store.Roles.GetByIdAsync(entityId);
            case "Permission": return await _store.Permissions.GetByIdAsync(entityId);
            case "Device": return await _store.Devices.GetByIdAsync(entityId);
            case "Category": return await _store.Categories.GetByIdAsync(entityId);
            case "Unit": return await _store.Units.GetByIdAsync(entityId);
            case "Product": return await _store.Products.GetByIdAsync(entityId);
            case "ProductVariant": return await _store.ProductVariants.GetByIdAsync(entityId);
            case "Barcode": return await _store.Barcodes.GetByIdAsync(entityId);
            case "Modifier": return await _store.Modifiers.GetByIdAsync(entityId);
            case "Customer": return await _store.Customers.GetByIdAsync(entityId);
            case "Table":
            case "RestaurantTable": return await _store.Tables.GetByIdAsync(entityId);
            case "Order": return await _store.Orders.GetByIdAsync(entityId);
            case "OrderItem": return await _store.OrderItems.GetByIdAsync(entityId);
            case "Payment": return await _store.Payments.GetByIdAsync(entityId);
            case "Inventory": return await _store.Inventory.GetByIdAsync(entityId);
            case "StockLedger": return await _store.StockLedger.GetByIdAsync(entityId);
            case "Purchase": return await _store.Purchases.GetByIdAsync(entityId);
            case "PurchaseItem": return await _store.PurchaseItems.GetByIdAsync(entityId);
            case "StockAdjustment": return await _store.StockAdjustments.GetByIdAsync(entityId);
            case "Printer": return await _store.Printers.GetByIdAsync(entityId);
            case "SubscriptionPlan": return await _store.SubscriptionPlans.GetByIdAsync(entityId);
            case "Subscription": return await _store.Subscriptions.GetByIdAsync(entityId);
            case "PaymentTransaction": return await _store.PaymentTransactions.GetByIdAsync(entityId);
            case "Backup": return await _store.Backups.GetByIdAsync(entityId);
            case "AuditLog": return await _store.AuditLogs.GetByIdAsync(entityId);
            default: return null; // unrecognized entity name - treated as "no current row", i.e. an insert
        }
    }

    /// <summary>
    /// Applies an accepted (non-conflicting, strictly-newer) push to the server-side entity.
    ///
    /// TODO: this only deep-merges fields for "Inventory" (<c>QuantityOnHand</c> from the
    /// payload) - the entity type most likely to actually conflict across two devices
    /// selling the same product concurrently. For every other entity type this pass only
    /// acknowledges the push as the newer version (bumps SyncVersion/UpdatedAtUtc via
    /// <see cref="BumpVersionAsync"/>) without merging the payload's other fields in. Full
    /// per-field payload merge for every entity type is the next increment; this pass fixes
    /// the conflict-*detection* gap (silently overwriting newer data with no version check
    /// at all), which was the actually dangerous bug - not full field-level merge for all
    /// 29 tables.
    /// </summary>
    private async Task ApplyChangeAsync(SyncPushItem change, BaseEntity currentEntity)
    {
        if (change.EntityName == "Inventory" && currentEntity is Inventory inventory)
        {
            TryApplyInventoryQuantity(change.PayloadJson, inventory);
            await _store.Inventory.UpdateAsync(inventory);
            return;
        }

        await BumpVersionAsync(change.EntityName, currentEntity);
    }

    private static void TryApplyInventoryQuantity(string payloadJson, Inventory inventory)
    {
        try
        {
            using var payload = JsonDocument.Parse(payloadJson);
            if (payload.RootElement.TryGetProperty("quantityOnHand", out var qty) ||
                payload.RootElement.TryGetProperty("QuantityOnHand", out qty))
            {
                if (qty.ValueKind == JsonValueKind.Number)
                {
                    inventory.QuantityOnHand = qty.GetDecimal();
                }
            }
        }
        catch (JsonException)
        {
            // Malformed/empty payload - fall back to just bumping the version below rather
            // than failing the whole push for one bad item.
        }
    }

    /// <summary>
    /// Bumps SyncVersion/UpdatedAtUtc on the current server row via the matching typed
    /// repository's UpdateAsync, without touching any other field - the "acknowledge as
    /// newer, don't merge yet" fallback used for every entity type except Inventory. See
    /// the TODO on <see cref="ApplyChangeAsync"/>.
    /// </summary>
    private Task BumpVersionAsync(string entityName, BaseEntity currentEntity) => entityName switch
    {
        "Tenant" => _store.Tenants.UpdateAsync((Tenant)currentEntity),
        "Branch" => _store.Branches.UpdateAsync((Branch)currentEntity),
        "User" => _store.Users.UpdateAsync((User)currentEntity),
        "Role" => _store.Roles.UpdateAsync((Role)currentEntity),
        "Permission" => _store.Permissions.UpdateAsync((Permission)currentEntity),
        "Device" => _store.Devices.UpdateAsync((Device)currentEntity),
        "Category" => _store.Categories.UpdateAsync((Category)currentEntity),
        "Unit" => _store.Units.UpdateAsync((Unit)currentEntity),
        "Product" => _store.Products.UpdateAsync((Product)currentEntity),
        "ProductVariant" => _store.ProductVariants.UpdateAsync((ProductVariant)currentEntity),
        "Barcode" => _store.Barcodes.UpdateAsync((Barcode)currentEntity),
        "Modifier" => _store.Modifiers.UpdateAsync((Modifier)currentEntity),
        "Customer" => _store.Customers.UpdateAsync((Customer)currentEntity),
        "Table" or "RestaurantTable" => _store.Tables.UpdateAsync((RestaurantTable)currentEntity),
        "Order" => _store.Orders.UpdateAsync((Order)currentEntity),
        "OrderItem" => _store.OrderItems.UpdateAsync((OrderItem)currentEntity),
        "Payment" => _store.Payments.UpdateAsync((Payment)currentEntity),
        "StockLedger" => _store.StockLedger.UpdateAsync((StockLedger)currentEntity),
        "Purchase" => _store.Purchases.UpdateAsync((Purchase)currentEntity),
        "PurchaseItem" => _store.PurchaseItems.UpdateAsync((PurchaseItem)currentEntity),
        "StockAdjustment" => _store.StockAdjustments.UpdateAsync((StockAdjustment)currentEntity),
        "Printer" => _store.Printers.UpdateAsync((Printer)currentEntity),
        "SubscriptionPlan" => _store.SubscriptionPlans.UpdateAsync((SubscriptionPlan)currentEntity),
        "Subscription" => _store.Subscriptions.UpdateAsync((Subscription)currentEntity),
        "PaymentTransaction" => _store.PaymentTransactions.UpdateAsync((PaymentTransaction)currentEntity),
        "Backup" => _store.Backups.UpdateAsync((Backup)currentEntity),
        "AuditLog" => _store.AuditLogs.UpdateAsync((AuditLog)currentEntity),
        _ => Task.CompletedTask
    };
}
