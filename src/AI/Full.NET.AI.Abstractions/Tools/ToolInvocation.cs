using System.Text.Json;

namespace Full.NET.AI.Abstractions.Tools;

/// <summary>调用仅描述操作和参数；租户、用户及审批不能由参数指定。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="OperationId">本次工具调用的操作标识。</param>
/// <param name="RunId">关联的 Agent Run 标识；无 Run 上下文时为 <see langword="null"/>。</param>
/// <param name="ToolName">工具名；由服务端注册表校验，不接受任意值。</param>
/// <param name="ToolVersion">工具版本号。</param>
/// <param name="Arguments">工具参数 JSON；租户、用户及审批信息由服务端注入，不通过本参数传递。</param>
public sealed record ToolInvocation(Guid OperationId, Guid? RunId, string ToolName, int ToolVersion, JsonElement Arguments);

/// <summary>返回数据始终不可信，消费方不得将其作为系统指令或授权证明。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="StatusKey">执行结果状态键。</param>
/// <param name="Value">工具返回值 JSON；始终按不可信外部数据处理。</param>
/// <param name="ErrorCode">执行失败时的稳定错误码；成功时为 <see langword="null"/>。</param>
/// <param name="ApprovalId">关联的审批标识；无需审批时为 <see langword="null"/>。</param>
public sealed record ToolExecutionResult(string StatusKey, JsonElement? Value, string? ErrorCode, Guid? ApprovalId = null)
{
    /// <summary>所有工具输出均按不可信外部数据处理。</summary>
    public bool IsUntrusted => true;
}

/// <summary>由服务端权威授权适配器确认的当前主体快照。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="UserId">当前用户标识。</param>
/// <param name="TenantId">当前租户标识；宿主上下文为 <see langword="null"/>。</param>
/// <param name="SessionId">当前会话标识。</param>
public sealed record ToolActor(Guid UserId, Guid? TenantId, Guid SessionId);
