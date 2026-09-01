namespace PosSaas.Domain.Common;

/// <summary>
/// The single persistence seam every controller depends on, via <c>PosSaasStore</c> - never
/// on a concrete repository type or on <c>PosSaasDbContext</c> directly. Two implementations
/// exist: <c>EfRepository&lt;T&gt;</c> (real EF Core + SQL Server) and
/// <c>InMemoryRepository&lt;T&gt;</c> (no database needed), both in PosSaas.Infrastructure -
/// see <c>PosSaasStore</c>'s doc comment for when each is wired up.
/// </summary>
public interface IRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetAllAsync(Guid? tenantId, CancellationToken ct = default);
    Task<T> AddAsync(T entity, CancellationToken ct = default);
    Task<T> UpdateAsync(T entity, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);
}
