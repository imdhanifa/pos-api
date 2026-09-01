using System.Collections.Concurrent;
using PosSaas.Domain.Common;

namespace PosSaas.Infrastructure.Persistence;

/// <summary>
/// Lightweight in-process stand-in for <see cref="EfRepository{T}"/> - no database needed at
/// all. Kept alongside the real EF Core path (see <see cref="PosSaasStore"/>'s doc comment)
/// for a quick no-database demo and for PosSaas.Tests, which exercises this directly without
/// spinning up SQL Server. Semantics deliberately mirror <see cref="EfRepository{T}"/>
/// exactly, so swapping between the two never changes controller-visible behavior.
/// </summary>
public class InMemoryRepository<T> : IRepository<T> where T : BaseEntity
{
    private readonly ConcurrentDictionary<Guid, T> _rows = new();

    public Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var found = _rows.TryGetValue(id, out var entity) && entity is { IsDeleted: false } ? entity : null;
        return Task.FromResult(found);
    }

    public Task<IReadOnlyList<T>> GetAllAsync(Guid? tenantId, CancellationToken ct = default)
    {
        IEnumerable<T> query = _rows.Values.Where(e => !e.IsDeleted);
        if (tenantId is not null)
        {
            query = query.Where(e => e.TenantId == tenantId);
        }
        IReadOnlyList<T> result = query.OrderByDescending(e => e.UpdatedAtUtc).ToList();
        return Task.FromResult(result);
    }

    public Task<T> AddAsync(T entity, CancellationToken ct = default)
    {
        entity.CreatedAtUtc = DateTime.UtcNow;
        entity.UpdatedAtUtc = entity.CreatedAtUtc;
        entity.SyncVersion = 1;
        _rows[entity.Id] = entity;
        return Task.FromResult(entity);
    }

    public Task<T> UpdateAsync(T entity, CancellationToken ct = default)
    {
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.SyncVersion += 1;
        _rows[entity.Id] = entity;
        return Task.FromResult(entity);
    }

    public Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (_rows.TryGetValue(id, out var entity))
        {
            entity.IsDeleted = true;
            entity.UpdatedAtUtc = DateTime.UtcNow;
            entity.SyncVersion += 1;
        }
        return Task.CompletedTask;
    }
}
