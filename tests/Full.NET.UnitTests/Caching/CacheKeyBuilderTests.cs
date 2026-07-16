using Full.NET.Caching.Fusion;

namespace Full.NET.UnitTests.Caching;

[TestClass]
public sealed class CacheKeyBuilderTests
{
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
