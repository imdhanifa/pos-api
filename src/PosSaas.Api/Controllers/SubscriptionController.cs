using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSaas.Api.Auth;
using PosSaas.Api.Dtos;
using PosSaas.Domain.Common;
using PosSaas.Domain.Entities;
using PosSaas.Infrastructure.Persistence;

namespace PosSaas.Api.Controllers;

/// <summary>Subscription plans and trial/billing state - Section 3 Phase 9.</summary>
[ApiController]
[Authorize]
[Route("api/subscription")]
public class SubscriptionController : ControllerBase
{
    private readonly PosSaasStore _store;
    public SubscriptionController(PosSaasStore store) => _store = store;

    [HttpGet("plans")]
    public async Task<ActionResult<IReadOnlyList<SubscriptionPlan>>> GetPlans()
        => Ok(await _store.SubscriptionPlans.GetAllAsync(null));

    [HttpGet]
    public async Task<ActionResult<Subscription>> GetCurrent()
    {
        var tenantId = User.GetTenantId();
        var subscriptions = await _store.Subscriptions.GetAllAsync(tenantId);
        var current = subscriptions.OrderByDescending(s => s.CreatedAtUtc).FirstOrDefault();
        return current is null ? NotFound(new { message = "no subscription started yet" }) : Ok(current);
    }

    /// <summary>
    /// Expiry summary for the subscription banner (Section 3 Phase 9's own trial/billing state,
    /// reduced to what the client needs to decide whether to nag the owner) - "expiring soon"
    /// means the active end date (trial or paid period, whichever governs right now) is 5 days
    /// out or less.
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<SubscriptionStatusDto>> GetStatus()
    {
        var tenantId = User.GetTenantId();
        var subscriptions = await _store.Subscriptions.GetAllAsync(tenantId);
        var current = subscriptions.OrderByDescending(s => s.CreatedAtUtc).FirstOrDefault();
        if (current is null)
        {
            return Ok(new SubscriptionStatusDto("None", null, null, false));
        }

        var endDate = current.Status == SubscriptionStatus.Trialing ? current.TrialEndsAtUtc : current.CurrentPeriodEndUtc;
        int? daysRemaining = endDate is null ? null : Math.Max(0, (int)Math.Ceiling((endDate.Value - DateTime.UtcNow).TotalDays));
        var expiringSoon = daysRemaining is not null && daysRemaining <= 5;

        return Ok(new SubscriptionStatusDto(current.Status.ToString(), endDate, daysRemaining, expiringSoon));
    }

    [HttpPost("start-trial")]
    [Authorize(Roles = "Owner")]
    public async Task<ActionResult<Subscription>> StartTrial(StartTrialRequest request)
    {
        var tenantId = User.GetTenantId();
        var plan = await _store.SubscriptionPlans.GetByIdAsync(request.PlanId);
        if (plan is null) return NotFound(new { message = "plan not found" });

        var subscription = new Subscription
        {
            TenantId = tenantId,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Trialing,
            TrialEndsAtUtc = DateTime.UtcNow.AddDays(plan.TrialDays)
        };
        await _store.Subscriptions.AddAsync(subscription);
        return Ok(subscription);
    }
}
