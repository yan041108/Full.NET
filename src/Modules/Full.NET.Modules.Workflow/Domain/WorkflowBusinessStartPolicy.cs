using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>与启动服务使用相同Trim规范化；所有权只来自实际模块注册，不来自请求中的声明。</summary>
internal static class WorkflowBusinessStartPolicy
{
    public static bool RequiresOwner(string? businessType, IEnumerable<WorkflowBusinessTypeOwnership> ownerships) =>
        businessType is not null && ownerships.Any(owner => string.Equals(owner.BusinessType, businessType.Trim(), StringComparison.Ordinal));
}
