using Microsoft.EntityFrameworkCore;
using PosSaas.Domain.Common;

namespace PosSaas.Infrastructure.Persistence;

/// <summary>
/// Real EF Core-backed implementation of <see cref="IRepository{T}"/>, wrapping a
/// <see cref="PosSaasDbContext"/> `DbSet&lt;T&gt;`. This is the production repository the
/// README's "Swapping in EF Core + SQL Server" section describes - see
/// <see cref="InMemoryRepository{T}"/> for the lightweight test double this stands
/// alongside (not replaces; see <see cref="PosSaasStore"/>'s doc comment for both paths).
///
/// Semantics deliberately mirror <see cref="InMemoryRepository{T}"/> exactly, so swapping
/// between the two never changes controller-visible behavior:
///   - GetByIdAsync excludes soft-deleted rows.
///   - GetAllAsync filters by tenant when a tenantId is supplied, newest-updated first.
///   - AddAsync/UpdateAsync/SoftDeleteAsync all stamp CreatedAtUtc/UpdatedAtUtc/SyncVersion
///     the same way, then persist immediately via SaveChangesAsync.
/// Kept simple and tracked throughout (one SaveChangesAsync per call) rather than a
/// unit-of-work spanning multiple repository calls, matching the in-memory store's
/// directness for this scaffold.
/// </summary>
public class EfRepository<T> : IRepository<T> where T : BaseEntity
{
    private readonly PosSaasDbContext _db;

    public EfRepository(PosSaasDbContext db)
    {
        _db = db;
    }

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Set<T>().FirstOrDefaultAsync(e => e.Id == id, ct);
        return entity is { IsDeleted: false } ? entity : null;
    }

    public async Task<IReadOnlyList<T>> GetAllAsync(Guid? tenantId, CancellationToken ct = default)
    {
        IQueryable<T> query = _db.Set<T>().AsNoTracking().Where(e => !e.IsDeleted);
        if (tenantId is not null)
        {
            query = query.Where(e => e.TenantId == tenantId);
        }
        return await query.OrderByDescending(e => e.UpdatedAtUtc).ToListAsync(ct);
    }

    public async Task<T> AddAsync(T entity, CancellationToken ct = default)
    {
        entity.CreatedAtUtc = DateTime.UtcNow;
        entity.UpdatedAtUtc = entity.CreatedAtUtc;
        entity.SyncVersion = 1;
        // Entry().State = Added marks only this entity, unlike Set<T>().Add(entity), which
        // walks the whole reachable object graph and would also insert anything sitting in
        // a populated navigation collection (e.g. PosController populates order.Items before
        // calling Orders.AddAsync, then separately adds each item via OrderItems.AddAsync -
        // Add()'s graph walk would insert those same rows a second time and violate the
        // OrderItems primary key). Every controller in this codebase already inserts child
        // rows through their own repository explicitly, so AddAsync here should only ever
        // touch the one entity it was called for.
        _db.Entry(entity).State = EntityState.Added;
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<T> UpdateAsync(T entity, CancellationToken ct = default)
    {
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.SyncVersion += 1;
        // Entity may have been read via a tracked GetByIdAsync call already (most common
        // path in the controllers), or handed in fresh - Update() attaches+marks-modified
        // either way without throwing on an already-tracked instance of the same entity.
        _db.Set<T>().Update(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Set<T>().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is not null)
        {
            entity.IsDeleted = true;
            entity.UpdatedAtUtc = DateTime.UtcNow;
            entity.SyncVersion += 1;
            await _db.SaveChangesAsync(ct);
        }
    }
}
