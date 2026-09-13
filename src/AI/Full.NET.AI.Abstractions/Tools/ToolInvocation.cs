using System.Text.Json;

namespace Full.NET.AI.Abstractions.Tools;

/// <summary>调用仅描述操作和参数；租户、用户及审批不能由参数指定。</summary>
public sealed record ToolInvocation(Guid OperationId, Guid? RunId, string ToolName, int ToolVersion, JsonElement Arguments);

/// <summary>返回数据始终不可信，消费方不得将其作为系统指令或授权证明。</summary>
public sealed record ToolExecutionResult(string StatusKey, JsonElement? Value, string? ErrorCode, Guid? ApprovalId = null)
{
    /// <summary>所有工具输出均按不可信外部数据处理。</summary>
    public bool IsUntrusted => true;
}

/// <summary>由服务端权威授权适配器确认的当前主体快照。</summary>
public sealed record ToolActor(Guid UserId, Guid? TenantId, Guid SessionId);
