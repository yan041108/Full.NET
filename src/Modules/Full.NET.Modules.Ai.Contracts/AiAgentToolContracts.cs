namespace Full.NET.Modules.Ai.Contracts;

/// <summary>Agent Tool 副作用类别键。</summary>
public static class AiAgentToolSideEffectKeys
{
    /// <summary>无副作用，仅用于探测或元数据。</summary>
    public const string None = "none";

    /// <summary>只读查询，不修改业务状态。</summary>
    public const string Read = "read";
}

/// <summary>Agent Tool 调用状态键。</summary>
public static class AiAgentToolCallStatusKeys
{
    /// <summary>调用成功完成。</summary>
    public const string Succeeded = "succeeded";

    /// <summary>调用失败。</summary>
    public const string Failed = "failed";

    /// <summary>因权限或策略被拒绝。</summary>
    public const string Denied = "denied";
}

/// <summary>静态 Agent Tool 目录项。</summary>
/// <param name="ToolName">稳定工具名。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="Description">工具说明。</param>
/// <param name="PermissionCode">执行所需权限码。</param>
/// <param name="SideEffectKey">副作用类别键。</param>
/// <param name="InputSchemaJson">输入 JSON Schema。</param>
/// <param name="OutputSchemaJson">输出 JSON Schema。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="McpExposureKey">MCP 暴露状态键。</param>
public sealed record AiAgentToolCatalogItem(
    string ToolName,
    string DisplayName,
    string Description,
    string PermissionCode,
    string SideEffectKey,
    string InputSchemaJson,
    string OutputSchemaJson,
    bool IsEnabled,
    string McpExposureKey);

/// <summary>Agent Tool 调用审计列表项。</summary>
/// <param name="Id">审计记录标识。</param>
/// <param name="TenantId">租户标识；Host 调用为空。</param>
/// <param name="ActorUserId">调用用户标识。</param>
/// <param name="ToolName">工具名。</param>
/// <param name="PermissionCode">校验使用的权限码。</param>
/// <param name="StatusKey">调用状态键。</param>
/// <param name="DurationMs">耗时毫秒。</param>
/// <param name="InputSummary">脱敏后的输入摘要。</param>
/// <param name="OutputSummary">脱敏后的输出摘要。</param>
/// <param name="ErrorCode">失败时的稳定错误码。</param>
/// <param name="TraceId">关联 Trace 标识。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
public sealed record AiAgentToolCallListItem(
    Guid Id,
    Guid? TenantId,
    Guid ActorUserId,
    string ToolName,
    string PermissionCode,
    string StatusKey,
    int? DurationMs,
    string InputSummary,
    string? OutputSummary,
    string? ErrorCode,
    string? TraceId,
    DateTimeOffset CreatedAtUtc);

/// <summary>Agent Tool 调用审计查询参数。</summary>
/// <param name="Page">页码。</param>
/// <param name="PageSize">页大小。</param>
/// <param name="TenantId">可选租户过滤。</param>
/// <param name="ToolName">可选工具名过滤。</param>
/// <param name="StatusKey">可选状态过滤。</param>
public sealed record AiAgentToolCallListQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? TenantId = null,
    string? ToolName = null,
    string? StatusKey = null);
