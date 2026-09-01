using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using PosSaas.Domain.Common;
using PosSaas.Domain.Entities;
using PosSaas.Infrastructure.Persistence;

namespace PosSaas.Api.Notifications;

/// <summary>
/// "If end date is within 5 days, notify every day" - scans every tenant's latest subscription
/// on a timer and pushes NotificationsHub.SubscriptionExpiringEvent to that tenant's group
/// (mobile/src/notifications/NotificationsHub.ts) once per calendar UTC day while its end date
/// (trial or paid period, whichever is active) is 5 days out or less. Runs the scan itself every
/// few minutes rather than once every 24h purely so a freshly-started server (or a demo) doesn't
/// have to wait up to a day to see it fire - _lastNotifiedUtcDate is what actually enforces the
/// once-a-day cap, not the scan interval.
/// </summary>
public class SubscriptionExpiryNotifier : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(15);
    private const int ExpiringSoonThresholdDays = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<NotificationsHub> _hub;
    private readonly ILogger<SubscriptionExpiryNotifier> _logger;

    // Process-lifetime only, matching RestoreController's precedent for small per-tenant state
    // that doesn't need its own persisted table - restarting the API just re-sends today's
    // notification once more, which is harmless.
    private readonly ConcurrentDictionary<Guid, DateOnly> _lastNotifiedUtcDate = new();

    public SubscriptionExpiryNotifier(IServiceScopeFactory scopeFactory, IHubContext<NotificationsHub> hub, ILogger<SubscriptionExpiryNotifier> logger)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanAndNotifyAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // A bad scan shouldn't kill the hosted service - just log and try again next tick.
                _logger.LogError(ex, "SubscriptionExpiryNotifier scan failed");
            }

            try
            {
                await Task.Delay(ScanInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    private async Task ScanAndNotifyAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<PosSaasStore>();

        var allSubscriptions = await store.Subscriptions.GetAllAsync(null, ct);
        var latestPerTenant = allSubscriptions
            .Where(s => s.TenantId is not null)
            .GroupBy(s => s.TenantId!.Value)
            .Select(g => g.OrderByDescending(s => s.CreatedAtUtc).First());

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var subscription in latestPerTenant)
        {
            var tenantId = subscription.TenantId!.Value;
            var endDate = subscription.Status == SubscriptionStatus.Trialing
                ? subscription.TrialEndsAtUtc
                : subscription.CurrentPeriodEndUtc;
            if (endDate is null) continue;

            var daysRemaining = Math.Max(0, (int)Math.Ceiling((endDate.Value - DateTime.UtcNow).TotalDays));
            if (daysRemaining > ExpiringSoonThresholdDays) continue;

            if (_lastNotifiedUtcDate.TryGetValue(tenantId, out var lastSent) && lastSent == today) continue;

            await _hub.Clients.Group(NotificationsHub.TenantGroup(tenantId)).SendAsync(
                NotificationsHub.SubscriptionExpiringEvent,
                new { status = subscription.Status.ToString(), endDateUtc = endDate, daysRemaining },
                ct);

            _lastNotifiedUtcDate[tenantId] = today;
            _logger.LogInformation("Pushed SubscriptionExpiring to tenant {TenantId} ({DaysRemaining} days remaining)", tenantId, daysRemaining);
        }
    }
}
