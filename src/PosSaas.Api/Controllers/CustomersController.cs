using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSaas.Api.Auth;
using PosSaas.Api.Dtos;
using PosSaas.Domain.Entities;
using PosSaas.Infrastructure.Persistence;

namespace PosSaas.Api.Controllers;

/// <summary>
/// Customer directory - Section 3 Phase 5. The Customer entity/repository (PosSaasStore.Customers)
/// already existed for PosController's optional CustomerId on an order and SyncController's
/// generic push/pull, but had no dedicated REST surface for the Customers screen to browse/add
/// against directly - this fills that gap the same way TablesController does for tables.
/// </summary>
[ApiController]
[Authorize]
[Route("api/customers")]
public class CustomersController : ControllerBase
{
    private readonly PosSaasStore _store;
    public CustomersController(PosSaasStore store) => _store = store;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Customer>>> GetCustomers()
        => Ok(await _store.Customers.GetAllAsync(User.GetTenantId()));

    [HttpGet("{id}")]
    public async Task<ActionResult<Customer>> GetCustomer(Guid id)
    {
        var customer = await _store.Customers.GetByIdAsync(id);
        return customer is null || !User.BelongsToCurrentTenant(customer) ? NotFound() : Ok(customer);
    }

    [HttpPost]
    public async Task<ActionResult<Customer>> CreateCustomer(CreateCustomerRequest request)
    {
        var customer = new Customer
        {
            TenantId = User.GetTenantId(),
            Name = request.Name,
            Phone = request.Phone,
            Email = request.Email
        };
        await _store.Customers.AddAsync(customer);
        return Ok(customer);
    }
}
