using System.Text.Json;

namespace Full.NET.AI.Abstractions.Tools;

/// <summary>逐次查询权威会话、账号、租户和权限，失败返回空。</summary>
public interface IToolAuthorizationPort
{
    /// <summary>不信任工具参数或历史令牌中的权限快照。</summary>
    ValueTask<ToolActor?> AuthorizeAsync(string permissionCode, CancellationToken cancellationToken);
}

/// <summary>执行意图和结果由业务所有者持久化；写入失败不能伪装成功。</summary>
public interface IToolAuditPort
{
    /// <summary>以 OperationId 唯一插入意图或拒绝记录；重复调用必须失败关闭。</summary>
    ValueTask BeginAsync(ToolInvocation invocation, string permissionCode, string statusKey, string? errorCode, CancellationToken cancellationToken);
    /// <summary>只更新仍处于 started 的同一操作，摘要不包含原始输入输出。</summary>
    ValueTask CompleteAsync(Guid operationId, string statusKey, string? errorCode, int outputBytes, int durationMs, CancellationToken cancellationToken);
}

/// <summary>写工具消费前校验审批绑定；只读工具返回 NotRequired。</summary>
public interface IAgentApprovalPort
{
    /// <summary>写工具在派发 Handler 前校验已批准且未消费的绑定，不在此处消费。</summary>
    ValueTask<AgentApprovalExecutionStatus> ValidateForExecutionAsync(
        ToolInvocation invocation,
        string sideEffectKey,
        ToolActor actor,
        CancellationToken cancellationToken);
}

/// <summary>审批门禁结果；Required 表示需先走人工审批 API。</summary>
public enum AgentApprovalExecutionStatus
{
    NotRequired,
    Approved,
    Required,
    Denied,
}

/// <summary>只能由服务器显式注册的静态 Handler 实现。</summary>
public interface IAgentToolHandler
{
    /// <summary>校验固定 Schema，拒绝未知字段与越界数据。</summary>
    bool ValidateArguments(JsonElement arguments);
    /// <summary>在授权后的可信主体范围执行；写工具须在同事务内消费审批后再改状态。</summary>
    ValueTask<JsonElement> ExecuteAsync(ToolInvocation invocation, ToolActor actor, CancellationToken cancellationToken);
}
