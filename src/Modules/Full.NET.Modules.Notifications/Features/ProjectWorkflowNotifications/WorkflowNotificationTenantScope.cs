using Full.NET.Abstractions.Tenancy;

namespace Full.NET.Modules.Notifications.Features.ProjectWorkflowNotifications;

/// <summary>仅按可信 Outbox 元数据建立通知目录作用域；成功、失败和取消后均恢复调用方上下文。</summary>
internal sealed class WorkflowNotificationTenantScope : IDisposable
{
    private readonly ICurrentTenantContextWriter writer;
    private readonly bool wasHost;
    private readonly TenantContext? previous;

    internal WorkflowNotificationTenantScope(ICurrentTenantContextWriter writer, Guid? tenantId)
    {
        if (tenantId == Guid.Empty) throw new InvalidOperationException("notifications.workflow_tenant_context_invalid");
        this.writer = writer;
        wasHost = writer.IsHost;
        previous = !wasHost && writer.IsAvailable && writer.Id is { } id
            ? new TenantContext(id, writer.Identifier ?? id.ToString("D"), writer.Name ?? id.ToString("D")) : null;
        if (tenantId is { } tenant)
            writer.SetTenant(new TenantContext(tenant, tenant.ToString("D"), tenant.ToString("D")));
        else writer.SetHost();
    }

    public void Dispose()
    {
        if (wasHost) writer.SetHost();
        else if (previous is not null) writer.SetTenant(previous);
        else writer.Clear();
    }
}
