using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;

namespace Full.NET.UnitTests.Tenancy;

[TestClass]
public sealed class CurrentTenantAccessorTests
{
    [TestMethod]
    public void Accessor_transitions_between_unavailable_tenant_host_and_clear()
    {
        var accessor = new CurrentTenantAccessor();
        Assert.IsFalse(accessor.IsAvailable);

        var tenantId = Guid.CreateVersion7();
        accessor.SetTenant(new TenantContext(tenantId, "acme", "Acme"));
        Assert.IsTrue(accessor.IsAvailable);
        Assert.AreEqual(tenantId, accessor.Id);
        Assert.AreEqual("acme", accessor.Identifier);
        Assert.AreEqual("Acme", accessor.Name);
        Assert.IsFalse(accessor.IsHost);

        accessor.SetHost();
        Assert.IsTrue(accessor.IsAvailable);
        Assert.IsTrue(accessor.IsHost);
        Assert.IsNull(accessor.Id);

        accessor.Clear();
        Assert.IsFalse(accessor.IsAvailable);
        Assert.IsFalse(accessor.IsHost);
    }

    [TestMethod]
    public void Guid_generator_creates_version_7_ids()
    {
        var id = new GuidV7IdGenerator().NewId();

        Assert.AreEqual(7, id.Version);
    }

    [TestMethod]
    public void System_clock_returns_utc_time()
    {
        var before = DateTimeOffset.UtcNow;
        var value = new SystemClock().UtcNow;
        var after = DateTimeOffset.UtcNow;

        Assert.AreEqual(TimeSpan.Zero, value.Offset);
        var sampleMidpoint = before + ((after - before) / 2);
        Assert.IsTrue(
            (value - sampleMidpoint).Duration() <= TimeSpan.FromSeconds(1));
    }
}
