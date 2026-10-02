using System.Text.Json;

namespace Full.NET.AI.Abstractions.Tools;

/// <summary>逐次查询权威会话、账号、租户和权限，失败返回空。</summary>
public interface IToolAuthorizationPort
{
    /// <summary>不信任工具参数或历史令牌中的权限快照。</summary>
    /// <param name="permissionCode">需校验的权限码；由调用方按业务约定提供，不得为空。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>可信主体快照；权限不足、主体不存在或校验失败时为 null。</returns>
    ValueTask<ToolActor?> AuthorizeAsync(string permissionCode, CancellationToken cancellationToken);
}

/// <summary>执行意图和结果由业务所有者持久化；写入失败不能伪装成功。</summary>
public interface IToolAuditPort
{
    /// <summary>以 OperationId 唯一插入意图或拒绝记录；重复调用必须失败关闭。</summary>
    /// <param name="invocation">工具调用上下文，含 OperationId、租户与调用方信息。</param>
    /// <param name="permissionCode">本次调用关联的权限码。</param>
    /// <param name="statusKey">调用状态稳定机器码。</param>
    /// <param name="errorCode">失败原因码；成功时为 null。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask BeginAsync(ToolInvocation invocation, string permissionCode, string statusKey, string? errorCode, CancellationToken cancellationToken);
    /// <summary>只更新仍处于 started 的同一操作，摘要不包含原始输入输出。</summary>
    /// <param name="operationId">BeginAsync 写入的操作标识。</param>
    /// <param name="statusKey">完成状态稳定机器码。</param>
    /// <param name="errorCode">失败原因码；成功时为 null。</param>
    /// <param name="outputBytes">输出摘要字节数；用于审计，不记录原始内容。</param>
    /// <param name="durationMs">工具执行耗时（毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask CompleteAsync(Guid operationId, string statusKey, string? errorCode, int outputBytes, int durationMs, CancellationToken cancellationToken);
}

/// <summary>写工具消费前校验审批绑定；只读工具返回 NotRequired。</summary>
public interface IAgentApprovalPort
{
    /// <summary>写工具在派发 Handler 前校验已批准且未消费的绑定，不在此处消费。</summary>
    /// <param name="invocation">工具调用上下文。</param>
    /// <param name="sideEffectKey">写工具副作用稳定键；用于定位审批绑定。</param>
    /// <param name="actor">已授权可信主体快照。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>审批门禁结果；NotRequired 表示只读工具无需审批，Approved 表示可执行，Required 表示需先走人工审批，Denied 表示已拒绝。</returns>
    ValueTask<AgentApprovalExecutionStatus> ValidateForExecutionAsync(
        ToolInvocation invocation,
        string sideEffectKey,
        ToolActor actor,
        CancellationToken cancellationToken);
}

/// <summary>审批门禁结果；Required 表示需先走人工审批 API。</summary>
/// <remarks>枚举成员数值发布后不可调整；新增成员只能追加到末尾，以保持线格式兼容。</remarks>
public enum AgentApprovalExecutionStatus
{
    /// <summary>只读工具无需审批，可直接执行；与 Approved 的边界在于不产生副作用。</summary>
    NotRequired,
    /// <summary>写工具已存在未消费的有效审批绑定，可执行并在同事务内消费审批。</summary>
    Approved,
    /// <summary>写工具需要人工审批但尚未获得有效绑定；调用方须先走人工审批 API，禁止直接执行。</summary>
    Required,
    /// <summary>审批已被拒绝或绑定已失效；调用方不得重试执行，应返回拒绝结果。</summary>
    Denied,
}

/// <summary>只能由服务器显式注册的静态 Handler 实现。</summary>
public interface IAgentToolHandler
{
    /// <summary>校验固定 Schema，拒绝未知字段与越界数据。</summary>
    /// <param name="arguments">待校验的工具参数 JSON 元素。</param>
    /// <returns>true 表示参数符合固定 Schema；false 表示存在未知字段或越界数据。</returns>
    bool ValidateArguments(JsonElement arguments);
    /// <summary>在授权后的可信主体范围执行；写工具须在同事务内消费审批后再改状态。</summary>
    /// <param name="invocation">工具调用上下文，含已校验参数。</param>
    /// <param name="actor">已授权可信主体快照。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工具执行结果的 JSON 元素；具体结构由 Handler 契约定义。</returns>
    ValueTask<JsonElement> ExecuteAsync(ToolInvocation invocation, ToolActor actor, CancellationToken cancellationToken);
}
