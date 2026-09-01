using PosSaas.Domain.Common;
using PosSaas.Infrastructure.Persistence;
using Xunit;

namespace PosSaas.Tests;

/// <summary>Trivial concrete entity, defined here purely so InMemoryRepository&lt;T&gt; has a T to test against.</summary>
public class TestEntity : TenantScopedEntity
{
    public string Name { get; set; } = string.Empty;
}

public class InMemoryRepositoryTests
{
    [Fact]
    public async Task AddThenGetById_ReturnsIt()
    {
        var repo = new InMemoryRepository<TestEntity>();
        var entity = new TestEntity { Name = "Widget" };

        await repo.AddAsync(entity);
        var fetched = await repo.GetByIdAsync(entity.Id);

        Assert.NotNull(fetched);
        Assert.Equal(entity.Id, fetched!.Id);
        Assert.Equal("Widget", fetched.Name);
    }

    [Fact]
    public async Task Update_BumpsSyncVersionAndUpdatedAtUtc()
    {
        var repo = new InMemoryRepository<TestEntity>();
        var entity = new TestEntity { Name = "Widget" };
        await repo.AddAsync(entity);

        var originalVersion = entity.SyncVersion;
        var originalUpdatedAt = entity.UpdatedAtUtc;
        await Task.Delay(10);

        entity.Name = "Widget v2";
        await repo.UpdateAsync(entity);

        Assert.Equal(originalVersion + 1, entity.SyncVersion);
        Assert.True(entity.UpdatedAtUtc > originalUpdatedAt);
    }

    [Fact]
    public async Task SoftDelete_MakesGetByIdReturnNull_AndExcludesFromGetAll()
    {
        var repo = new InMemoryRepository<TestEntity>();
        var entity = new TestEntity { Name = "Widget" };
        await repo.AddAsync(entity);

        await repo.SoftDeleteAsync(entity.Id);

        var fetched = await repo.GetByIdAsync(entity.Id);
        Assert.Null(fetched);

        var all = await repo.GetAllAsync(null);
        Assert.DoesNotContain(all, e => e.Id == entity.Id);
    }

    [Fact]
    public async Task GetAllAsync_FiltersByTenantId_WhenTwoTenantsHaveRows()
    {
        var repo = new InMemoryRepository<TestEntity>();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await repo.AddAsync(new TestEntity { TenantId = tenantA, Name = "A1" });
        await repo.AddAsync(new TestEntity { TenantId = tenantA, Name = "A2" });
        await repo.AddAsync(new TestEntity { TenantId = tenantB, Name = "B1" });

        var tenantAResults = await repo.GetAllAsync(tenantA);
        var tenantBResults = await repo.GetAllAsync(tenantB);
        var allResults = await repo.GetAllAsync(null);

        Assert.Equal(2, tenantAResults.Count);
        Assert.All(tenantAResults, e => Assert.Equal(tenantA, e.TenantId));
        Assert.Single(tenantBResults);
        Assert.Equal(3, allResults.Count);
    }
}
