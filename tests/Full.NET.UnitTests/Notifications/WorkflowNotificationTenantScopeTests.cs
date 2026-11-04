using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Notifications.Features.ProjectWorkflowNotifications;

namespace Full.NET.UnitTests.Notifications;

/// <summary>通知投影使用 Envelope 租户，退出后不泄露到同消息的其他消费者。</summary>
[TestClass]
public sealed class WorkflowNotificationTenantScopeTests
{
    [TestMethod]
    [DataRow("host", false)] [DataRow("host", true)]
    [DataRow("tenant", false)] [DataRow("tenant", true)]
    [DataRow("unset", false)] [DataRow("unset", true)]
    public void Trusted_scope_restores_original_context_on_success_or_failure(string initial, bool fail)
    {
        ICurrentTenantContextWriter writer = new CurrentTenantAccessor();
        var original = new TenantContext(Guid.CreateVersion7(), "original", "Original");
        if (initial == "host") writer.SetHost();
        if (initial == "tenant") writer.SetTenant(original);
        var target = Guid.CreateVersion7();
        try
        {
            using var scope = new WorkflowNotificationTenantScope(writer, target);
            Assert.AreEqual(target, writer.Id); Assert.IsFalse(writer.IsHost);
            if (fail) throw new InvalidOperationException("test.delivery_failed");
        }
        catch (InvalidOperationException error) when (error.Message == "test.delivery_failed") { }
        Assert.AreEqual(initial == "host", writer.IsHost);
        Assert.AreEqual(initial == "tenant" ? original.Id : (Guid?)null, writer.Id);
        if (initial == "tenant") { Assert.AreEqual(original.Identifier, writer.Identifier); Assert.AreEqual(original.Name, writer.Name); }
        if (initial == "unset") Assert.IsFalse(writer.IsAvailable);
    }

    [TestMethod]
    public void Host_event_clears_tenant_temporarily_and_invalid_tenant_does_not_modify_context()
    {
        ICurrentTenantContextWriter writer = new CurrentTenantAccessor();
        var original = new TenantContext(Guid.CreateVersion7(), "original", "Original"); writer.SetTenant(original);
        using (new WorkflowNotificationTenantScope(writer, null)) { Assert.IsTrue(writer.IsHost); Assert.IsNull(writer.Id); }
        Assert.AreEqual(original.Id, writer.Id);
        Assert.Throws<InvalidOperationException>(() => new WorkflowNotificationTenantScope(writer, Guid.Empty));
        Assert.AreEqual(original.Id, writer.Id);
    }
}
