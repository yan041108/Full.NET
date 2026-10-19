using Full.NET.Caching.Fusion;

namespace Full.NET.UnitTests.Caching;

/// <summary>缓存键构建：租户与策略段稳定拼接。</summary>
[TestClass]
public sealed class CacheKeyBuilderTests
{
    [TestMethod]
    public void Key_rejects_empty_tenant_or_unbounded_segments_without_breaking_grid_compound_id()
    {
        Assert.Throws<ArgumentException>(() => CacheKeyBuilder.TenantResolutionById("Testing", Guid.Empty));
        Assert.Throws<ArgumentException>(() => CacheKeyBuilder.ForGlobal("prod:other", "settings", "grid", "id", "v1"));
        Assert.Throws<ArgumentException>(() => CacheKeyBuilder.ForGlobal("prod", "settings", "grid", new string('中', 1025), "v1"));
        Assert.AreEqual("fullnet:prod:host:settings:grid-preference:abc:identity.users:v2",
            CacheKeyBuilder.ForGlobal("prod", "settings", "grid-preference", "abc:identity.users", "v2"));
    }

    [TestMethod]
    public void ForTenant_BuildsStableTenantIsolatedKey()
    {
        var tenantId = Guid.Parse("0198f4d3-735d-7d2a-9046-76263fd84101");
        var userId = Guid.Parse("0198f4d3-735d-7d2a-9046-76263fd84102");

        var key = CacheKeyBuilder.ForTenant(
            "prod",
            tenantId,
            "identity",
            "permissions",
            userId,
            "v1");

        Assert.AreEqual(
            "fullnet:prod:0198f4d3-735d-7d2a-9046-76263fd84101:identity:permissions:0198f4d3-735d-7d2a-9046-76263fd84102:v1",
            key);
    }

    [TestMethod]
    public void ForTenant_RejectsHostTenantIdentifier()
    {
        Assert.Throws<ArgumentException>(() => CacheKeyBuilder.ForTenant(
            "prod",
            Guid.Empty,
            "identity",
            "permissions",
            Guid.NewGuid(),
            "v1"));
    }

    [TestMethod]
    public void GlobalKeysAndTags_AreNormalized()
    {
        var tenantId = Guid.Parse("0198f4d3-735d-7d2a-9046-76263fd84101");

        Assert.AreEqual(
            "fullnet:production:host:tenancy:domains:example.com:v1",
            CacheKeyBuilder.ForGlobal("Production", "tenancy", "domains", "example.com", "v1"));
        Assert.AreEqual(
            "tenant:0198f4d3-735d-7d2a-9046-76263fd84101",
            CacheKeyBuilder.TenantTag(tenantId));
        Assert.AreEqual(
            "tenancy:domain:example.com",
            CacheKeyBuilder.DomainTag("Example.COM"));
    }
}
