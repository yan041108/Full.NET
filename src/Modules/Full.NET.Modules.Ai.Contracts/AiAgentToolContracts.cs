namespace Full.NET.Modules.Ai.Contracts;

/// <summary>Agent Tool 副作用类别键。</summary>
public static class AiAgentToolSideEffectKeys
{
    /// <summary>无副作用，仅用于探测或元数据。</summary>
    public const string None = "none";

    /// <summary>只读查询，不修改业务状态。</summary>
    public const string Read = "read";

    /// <summary>写入业务状态，默认需要人工审批。</summary>
    public const string Write = "write";
}

/// <summary>Agent Tool 调用状态键。</summary>
public static class AiAgentToolCallStatusKeys
{
    /// <summary>已持久化执行意图，尚无终态回执。</summary>
    public const string Started = "started";

    /// <summary>执行已取消。</summary>
    public const string Cancelled = "cancelled";

    /// <summary>调用成功完成。</summary>
    public const string Succeeded = "succeeded";

    /// <summary>调用失败。</summary>
    public const string Failed = "failed";

    /// <summary>因权限或策略被拒绝。</summary>
    public const string Denied = "denied";
}

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
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

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
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
/// <param name="RunId">关联 Agent Run 标识；非 Run 触发的调用为 <see langword="null"/>。</param>
/// <param name="ArgumentsHash">输入参数哈希；用于审计比对与重放去重。</param>
/// <param name="ApprovalId">关联审批请求标识；未触发审批的调用为 <see langword="null"/>。</param>
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
    Guid? RunId,
    string? ArgumentsHash,
    Guid? ApprovalId,
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
