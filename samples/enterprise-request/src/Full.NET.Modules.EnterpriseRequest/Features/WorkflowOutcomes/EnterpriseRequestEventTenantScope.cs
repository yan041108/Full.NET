using Full.NET.Abstractions.Tenancy;

namespace Full.NET.Modules.EnterpriseRequest.Features.WorkflowOutcomes;

/// <summary>只允许后台可信事件元数据建立作用域，结束后恢复 Worker 原上下文。</summary>
internal sealed class EnterpriseRequestEventTenantScope : IDisposable
{
    private readonly ICurrentTenantContextWriter writer;
    private readonly bool wasHost;
    private readonly TenantContext? previous;

    internal EnterpriseRequestEventTenantScope(ICurrentTenantContextWriter writer, TenantContext tenant)
    {
        this.writer = writer;
        wasHost = writer.IsHost;
        previous = !wasHost && writer.IsAvailable && writer.Id is { } id
            ? new TenantContext(id, writer.Identifier ?? id.ToString("D"), writer.Name ?? id.ToString("D")) : null;
        writer.SetTenant(tenant);
    }

    public void Dispose()
    {
        if (wasHost) writer.SetHost();
        else if (previous is not null) writer.SetTenant(previous);
        else writer.Clear();
    }
}
