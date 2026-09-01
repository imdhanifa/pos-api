using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSaas.Api.Auth;
using PosSaas.Api.Dtos;
using PosSaas.Domain.Entities;
using PosSaas.Infrastructure.Persistence;
using PosSaas.Infrastructure.Security;

namespace PosSaas.Api.Controllers;

/// <summary>Staff/users and roles - Section 3 Phase 1.</summary>
[ApiController]
[Authorize]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly PosSaasStore _store;
    public UsersController(PosSaasStore store) => _store = store;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<User>>> GetUsers()
        => Ok(await _store.Users.GetAllAsync(User.GetTenantId()));

    [HttpPost]
    [Authorize(Roles = "Owner,Manager")]
    public async Task<ActionResult<User>> CreateUser(CreateUserRequest request)
    {
        var user = new User
        {
            TenantId = User.GetTenantId(),
            BranchId = request.BranchId,
            FullName = request.FullName,
            Email = request.Email,
            PasswordHash = PasswordHasher.Hash(request.Password),
            RoleId = request.RoleId
        };
        await _store.Users.AddAsync(user);
        return Ok(user);
    }

    [HttpGet("roles")]
    public async Task<ActionResult<IReadOnlyList<Role>>> GetRoles()
        => Ok(await _store.Roles.GetAllAsync(User.GetTenantId()));

    [HttpPost("roles")]
    [Authorize(Roles = "Owner")]
    public async Task<ActionResult<Role>> CreateRole(CreateRoleRequest request)
    {
        var role = new Role { TenantId = User.GetTenantId(), Name = request.Name };
        await _store.Roles.AddAsync(role);
        return Ok(role);
    }
}
