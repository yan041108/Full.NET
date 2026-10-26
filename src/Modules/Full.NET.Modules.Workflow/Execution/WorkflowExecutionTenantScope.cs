using Full.NET.Abstractions.Tenancy;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>后台只接纳持久实例的可信租户身份；退出时恢复进入前的作用域。</summary>
internal sealed class WorkflowExecutionTenantScope : IDisposable
{
    private readonly ICurrentTenantContextWriter writer;
    private readonly bool wasHost;
    private readonly TenantContext? previous;
    public WorkflowExecutionTenantScope(ICurrentTenantContextWriter writer, Guid? tenantId)
    {
        this.writer = writer; wasHost = writer.IsHost;
        previous = writer.Id is { } id && writer.Identifier is { } identifier && writer.Name is { } name
            ? new TenantContext(id, identifier, name) : null;
        if (tenantId is { } target) writer.SetTenant(new TenantContext(target, target.ToString("N"), target.ToString("D")));
        else writer.SetHost();
    }
    public void Dispose()
    {
        if (!wasHost && previous is not null) writer.SetTenant(previous);
        else writer.SetHost();
    }
}
