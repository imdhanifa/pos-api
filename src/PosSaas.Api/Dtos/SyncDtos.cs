namespace PosSaas.Api.Dtos;

public record SyncPushItem(string EntityName, Guid EntityId, string Operation, long EntitySyncVersion, string PayloadJson);

public record SyncPushRequest(Guid DeviceId, List<SyncPushItem> Changes);

public record SyncPushResult(Guid EntityId, string Status, string? ConflictReason);

public record SyncPullRequest(DateTime? SinceUtc);
