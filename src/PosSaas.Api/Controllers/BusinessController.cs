using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSaas.Api.Auth;
using PosSaas.Api.Dtos;
using PosSaas.Domain.Entities;
using PosSaas.Infrastructure.Persistence;

namespace PosSaas.Api.Controllers;

/// <summary>Business/tenant profile and branches - Section 3 Phase 1.</summary>
[ApiController]
[Authorize]
[Route("api/business")]
public class BusinessController : ControllerBase
{
    private readonly PosSaasStore _store;
    public BusinessController(PosSaasStore store) => _store = store;

    [HttpGet]
    public async Task<ActionResult<Tenant>> GetCurrentTenant()
    {
        var tenantId = User.GetTenantId();
        var tenant = tenantId is null ? null : await _store.Tenants.GetByIdAsync(tenantId.Value);
        return tenant is null ? NotFound() : Ok(tenant);
    }

    [HttpPut]
    [Authorize(Roles = "Owner")]
    public async Task<ActionResult<Tenant>> UpdateCurrentTenant(UpdateBusinessRequest request)
    {
        var tenantId = User.GetTenantId();
        var tenant = tenantId is null ? null : await _store.Tenants.GetByIdAsync(tenantId.Value);
        if (tenant is null) return NotFound();

        tenant.Name = request.Name;
        tenant.LegalName = request.LegalName;
        tenant.BusinessType = request.BusinessType;
        tenant.DefaultCurrency = request.DefaultCurrency;

        // Billing template fields are all optional on the request - a null means "leave this
        // field as it is" (e.g. a caller that only wants to rename the business shouldn't
        // accidentally blank out the receipt footer), not "clear it".
        tenant.ReceiptAddressLine = request.ReceiptAddressLine ?? tenant.ReceiptAddressLine;
        tenant.ReceiptPhone = request.ReceiptPhone ?? tenant.ReceiptPhone;
        tenant.ReceiptFooterMessage = request.ReceiptFooterMessage ?? tenant.ReceiptFooterMessage;
        tenant.ReceiptShowTaxBreakdown = request.ReceiptShowTaxBreakdown ?? tenant.ReceiptShowTaxBreakdown;
        tenant.ReceiptShowDiscountLine = request.ReceiptShowDiscountLine ?? tenant.ReceiptShowDiscountLine;
        tenant.ReceiptPaperWidth = request.ReceiptPaperWidth ?? tenant.ReceiptPaperWidth;

        await _store.Tenants.UpdateAsync(tenant);
        return Ok(tenant);
    }

    [HttpGet("branches")]
    public async Task<ActionResult<IReadOnlyList<Branch>>> GetBranches()
        => Ok(await _store.Branches.GetAllAsync(User.GetTenantId()));

    [HttpPost("branches")]
    [Authorize(Roles = "Owner,Manager")]
    public async Task<ActionResult<Branch>> CreateBranch(CreateBranchRequest request)
    {
        var branch = new Branch { TenantId = User.GetTenantId(), Name = request.Name, Address = request.Address };
        await _store.Branches.AddAsync(branch);
        return Ok(branch);
    }
}
