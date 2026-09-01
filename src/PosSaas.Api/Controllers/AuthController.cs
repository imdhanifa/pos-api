using Microsoft.AspNetCore.Mvc;
using PosSaas.Api.Dtos;
using PosSaas.Domain.Common;
using PosSaas.Domain.Entities;
using PosSaas.Infrastructure.Persistence;
using PosSaas.Infrastructure.Security;

namespace PosSaas.Api.Controllers;

/// <summary>Login and merchant sign-up (Section 3) - issues a hand-rolled JWT via <see cref="SimpleJwtService"/>. Demo login: owner@demo.pos / Demo@123 (see SeedData).</summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly PosSaasStore _store;
    private readonly SimpleJwtService _jwtService;

    public AuthController(PosSaasStore store, SimpleJwtService jwtService)
    {
        _store = store;
        _jwtService = jwtService;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var users = await _store.Users.GetAllAsync(null);
        var user = users.FirstOrDefault(u => string.Equals(u.Email, request.Email, StringComparison.OrdinalIgnoreCase));
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { message = "invalid email or password" });
        }

        var role = await _store.Roles.GetByIdAsync(user.RoleId);
        var roleName = role?.Name ?? "Cashier";

        if (user.TenantId is not null)
        {
            await UpsertDevice(user.TenantId.Value, user.BranchId, request.DeviceId, request.DeviceName);
        }

        var token = _jwtService.IssueToken(user.Id, user.TenantId ?? Guid.Empty, roleName);
        return Ok(new LoginResponse(token, user.Id, user.TenantId ?? Guid.Empty, roleName, user.FullName));
    }

    /// <summary>
    /// Onboards a brand-new merchant: Tenant + default Branch + Owner role/user, starts a trial on
    /// the cheapest SubscriptionPlan, and logs them straight in (same response shape as Login) so
    /// there's no separate "now go log in" step after registering.
    /// </summary>
    [HttpPost("register")]
    public async Task<ActionResult<LoginResponse>> Register(RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BusinessName) || string.IsNullOrWhiteSpace(request.OwnerFullName)
            || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Business name, your name, email and password are all required." });
        }
        if (request.Password.Length < 8)
        {
            return BadRequest(new { message = "Password must be at least 8 characters." });
        }

        var existingUsers = await _store.Users.GetAllAsync(null);
        if (existingUsers.Any(u => string.Equals(u.Email, request.Email, StringComparison.OrdinalIgnoreCase)))
        {
            return Conflict(new { message = "An account with this email already exists." });
        }

        var tenant = new Tenant { Name = request.BusinessName.Trim(), DefaultCurrency = request.DefaultCurrency ?? "INR" };
        await _store.Tenants.AddAsync(tenant);

        var branch = new Branch { TenantId = tenant.Id, Name = "Main Branch" };
        await _store.Branches.AddAsync(branch);

        var ownerRole = new Role { TenantId = tenant.Id, Name = "Owner", IsSystemRole = true };
        await _store.Roles.AddAsync(ownerRole);

        var owner = new User
        {
            TenantId = tenant.Id,
            BranchId = branch.Id,
            FullName = request.OwnerFullName.Trim(),
            Email = request.Email.Trim(),
            PasswordHash = PasswordHasher.Hash(request.Password),
            RoleId = ownerRole.Id,
        };
        await _store.Users.AddAsync(owner);

        // Cheapest plan (Basic, price 0) so a new merchant is never blocked from signing up just
        // because no plan was picked yet - they can upgrade later via SubscriptionController.
        var plan = (await _store.SubscriptionPlans.GetAllAsync(null)).OrderBy(p => p.MonthlyPriceInr).FirstOrDefault();
        if (plan is not null)
        {
            await _store.Subscriptions.AddAsync(new Subscription
            {
                TenantId = tenant.Id,
                PlanId = plan.Id,
                Status = SubscriptionStatus.Trialing,
                TrialEndsAtUtc = DateTime.UtcNow.AddDays(plan.TrialDays),
            });
        }

        await UpsertDevice(tenant.Id, branch.Id, request.DeviceId, request.DeviceName);

        var token = _jwtService.IssueToken(owner.Id, tenant.Id, ownerRole.Name);
        return Ok(new LoginResponse(token, owner.Id, tenant.Id, ownerRole.Name, owner.FullName));
    }

    /// <summary>Records which physical device is signing in - see mobile/src/device/deviceInfo.ts,
    /// which generates and persists one DeviceId per install. Looked up by that client-generated
    /// Id (same idempotent-create pattern as PosController's clientOrderId) so repeat logins from
    /// the same device update LastSyncedAtUtc instead of piling up duplicate rows.</summary>
    private async Task UpsertDevice(Guid tenantId, Guid? branchId, Guid? deviceId, string? deviceName)
    {
        if (deviceId is null) return;

        // Compared directly against the tenantId parameter rather than User.BelongsToCurrentTenant
        // - there's no ClaimsPrincipal tenant claim to check against yet at Login (that's what
        // this call is part of establishing), and at Register `tenantId` IS the brand-new tenant.
        // Without this, a client-supplied deviceId belonging to another tenant would get its Name
        // renamed and LastSyncedAtUtc bumped by this login - a low-stakes but still real
        // cross-tenant write.
        var existing = await _store.Devices.GetByIdAsync(deviceId.Value);
        if (existing is not null && existing.TenantId == tenantId)
        {
            existing.Name = deviceName ?? existing.Name;
            existing.LastSyncedAtUtc = DateTime.UtcNow;
            await _store.Devices.UpdateAsync(existing);
        }
        else if (existing is null)
        {
            await _store.Devices.AddAsync(new Device
            {
                Id = deviceId.Value,
                TenantId = tenantId,
                BranchId = branchId,
                Name = string.IsNullOrWhiteSpace(deviceName) ? "Unnamed device" : deviceName,
                Kind = DeviceKind.MobilePos,
                LastSyncedAtUtc = DateTime.UtcNow,
            });
        }
    }
}
