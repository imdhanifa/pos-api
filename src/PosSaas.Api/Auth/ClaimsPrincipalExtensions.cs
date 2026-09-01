using System.Security.Claims;
using PosSaas.Domain.Common;

namespace PosSaas.Api.Auth;

/// <summary>Reads the tenant/user/role claims that <see cref="BearerAuthHandler"/> puts on the authenticated principal.</summary>
public static class ClaimsPrincipalExtensions
{
    public static Guid? GetTenantId(this ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue("tenantId"), out var id) ? id : null;

    public static Guid? GetUserId(this ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static string? GetRole(this ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Role);

    /// <summary>
    /// True only when `entity` exists AND belongs to the caller's own tenant. Every controller
    /// that takes a client-supplied id and resolves it via IRepository&lt;T&gt;.GetByIdAsync must
    /// gate on this before reading or mutating the result - GetByIdAsync itself has no tenant
    /// awareness by design (see EfRepository's doc comment: only GetAllAsync filters by tenant),
    /// so without this check any authenticated user of ANY tenant could read or modify another
    /// tenant's order/customer/product/payment/etc. by id alone (an IDOR - insecure direct object
    /// reference). Callers should return NotFound() (never Forbid()) on a false result, so a
    /// cross-tenant guess can't even confirm the id exists.
    /// </summary>
    public static bool BelongsToCurrentTenant(this ClaimsPrincipal user, BaseEntity? entity)
        => entity is not null && entity.TenantId == user.GetTenantId();
}
