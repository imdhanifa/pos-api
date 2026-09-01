using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PosSaas.Api.Auth;

namespace PosSaas.Api.Notifications;

/// <summary>
/// Real-time push channel to connected devices - mobile/src/notifications/NotificationsHub.ts
/// connects here right after login and listens for "SubscriptionExpiring" (pushed by
/// <see cref="SubscriptionExpiryNotifier"/>). Each connection joins a group named after its own
/// tenant (see OnConnectedAsync) so a push only reaches devices belonging to that tenant, never
/// every connected client.
/// </summary>
[Authorize]
public class NotificationsHub : Hub
{
    public const string SubscriptionExpiringEvent = "SubscriptionExpiring";

    public override async Task OnConnectedAsync()
    {
        var tenantId = Context.User?.GetTenantId();
        if (tenantId is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenantId.Value));
        }
        await base.OnConnectedAsync();
    }

    public static string TenantGroup(Guid tenantId) => $"tenant:{tenantId}";
}
