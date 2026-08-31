using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Features;
using NSubstitute;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class NotificationInboxScopeTests
{
    [TestMethod]
    public void Host_and_tenant_scopes_use_trusted_context_keys()
    {
        var host = Substitute.For<ICurrentTenant>();
        host.IsHost.Returns(true);
        var hostScope = NotificationInboxScope.Resolve(host);
        Assert.AreEqual("host", hostScope.ScopeKey);
        Assert.AreEqual("host", hostScope.TenantScopeKey);
        Assert.IsNull(hostScope.TenantId);

        var tenantId = Guid.CreateVersion7();
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.IsHost.Returns(false);
        tenant.IsAvailable.Returns(true);
        tenant.Id.Returns(tenantId);
        var tenantScope = NotificationInboxScope.Resolve(tenant);
        Assert.AreEqual("tenant", tenantScope.ScopeKey);
        Assert.AreEqual($"tenant:{tenantId:N}", tenantScope.TenantScopeKey);
        Assert.AreEqual(tenantId, tenantScope.TenantId);
    }

    [TestMethod]
    public void Missing_tenant_context_fails_closed()
    {
        var currentTenant = Substitute.For<ICurrentTenant>();
        currentTenant.IsHost.Returns(false);
        currentTenant.IsAvailable.Returns(false);
        Assert.ThrowsExactly<TenantContextMissingException>(() =>
            NotificationInboxScope.Resolve(currentTenant));
    }
}
