using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSaas.Api.Auth;
using PosSaas.Api.Dtos;
using PosSaas.Domain.Common;
using PosSaas.Domain.Entities;
using PosSaas.Infrastructure.Persistence;

namespace PosSaas.Api.Controllers;

/// <summary>Dine-in table layout/status - Section 3 Phase 3.</summary>
[ApiController]
[Authorize]
[Route("api/tables")]
public class TablesController : ControllerBase
{
    private readonly PosSaasStore _store;
    public TablesController(PosSaasStore store) => _store = store;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RestaurantTable>>> GetTables([FromQuery] Guid? branchId)
    {
        var tables = (await _store.Tables.GetAllAsync(User.GetTenantId())).AsEnumerable();
        if (branchId is not null)
        {
            tables = tables.Where(t => t.BranchId == branchId);
        }
        return Ok(tables.ToList());
    }

    [HttpPost]
    [Authorize(Roles = "Owner,Manager")]
    public async Task<ActionResult<RestaurantTable>> CreateTable(CreateTableRequest request)
    {
        var table = new RestaurantTable
        {
            TenantId = User.GetTenantId(),
            BranchId = request.BranchId,
            Name = request.Name,
            Capacity = request.Capacity
        };
        await _store.Tables.AddAsync(table);
        return Ok(table);
    }

    [HttpPatch("{id}/status")]
    public async Task<ActionResult<RestaurantTable>> UpdateStatus(Guid id, UpdateTableStatusRequest request)
    {
        if (!Enum.TryParse<TableStatus>(request.Status, true, out var status))
        {
            return BadRequest(new { message = "status must be Available, Occupied, Reserved or Cleaning" });
        }

        var table = await _store.Tables.GetByIdAsync(id);
        if (table is null || !User.BelongsToCurrentTenant(table)) return NotFound();

        table.Status = status;
        await _store.Tables.UpdateAsync(table);
        return Ok(table);
    }
}
