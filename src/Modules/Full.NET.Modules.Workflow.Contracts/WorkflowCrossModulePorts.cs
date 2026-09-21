using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Workflow.Contracts;

/// <summary>已发布工作流定义版本的跨模块只读摘要。</summary>
/// <param name="DefinitionVersionId">不可变定义版本标识。</param>
/// <param name="FormVersionId">绑定的表单版本标识。</param>
/// <param name="DefinitionKey">稳定定义键。</param>
public sealed record WorkflowPublishedDefinitionVersion(
    Guid DefinitionVersionId,
    Guid FormVersionId,
    string DefinitionKey);

/// <summary>按定义键查询当前作用域内最新已发布版本。</summary>
public interface IWorkflowPublishedDefinitionDirectory
{
    /// <summary>查找指定定义键在当前可信作用域下的最新已发布版本。</summary>
    /// <param name="definitionKey">稳定工作流定义键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前作用域内最新的已发布版本；不存在或不可见时为 <see langword="null"/>。</returns>
    Task<WorkflowPublishedDefinitionVersion?> FindLatestPublishedAsync(
        string definitionKey,
        CancellationToken cancellationToken = default);

    /// <summary>按版本标识查找当前可信作用域内已发布的工作流定义版本。</summary>
    /// <param name="definitionVersionId">已发布定义版本标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>匹配的已发布版本；不存在或不属于当前可信作用域时为 <see langword="null"/>。</returns>
    Task<WorkflowPublishedDefinitionVersion?> FindPublishedByVersionIdAsync(
        Guid definitionVersionId,
        CancellationToken cancellationToken = default);
}

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>跨模块启动工作流实例的命令。</summary>
/// <param name="DefinitionVersionId">已发布定义版本标识。</param>
/// <param name="BusinessType">稳定业务类型机器码。</param>
/// <param name="BusinessId">稳定业务标识。</param>
/// <param name="InitialValuesJson">表单初始值 JSON 文本。</param>
/// <param name="IdempotencyKey">调用方幂等键。</param>
/// <param name="BusinessTitle">可选业务标题；用于实例列表展示，为空时由 Workflow 模块生成默认标题。</param>
public sealed record StartWorkflowInstanceCommand(
    Guid DefinitionVersionId,
    string BusinessType,
    string BusinessId,
    string InitialValuesJson,
    string IdempotencyKey,
    string? BusinessTitle = null);

/// <summary>跨模块取消工作流实例的命令。</summary>
/// <param name="InstanceId">工作流实例标识。</param>
/// <param name="ExpectedRevision">客户端读取到的实例修订号。</param>
/// <param name="Reason">可选取消原因。</param>
/// <param name="IdempotencyKey">调用方幂等键。</param>
public sealed record CancelWorkflowInstanceCommand(
    Guid InstanceId,
    long ExpectedRevision,
    string? Reason,
    string IdempotencyKey);

/// <summary>跨模块可见的工作流实例启动或取消结果摘要。</summary>
/// <param name="InstanceId">实例标识。</param>
/// <param name="StatusKey">实例状态机器键。</param>
/// <param name="Revision">实例乐观并发修订号。</param>
public sealed record WorkflowInstanceLifecycleResult(
    Guid InstanceId,
    string StatusKey,
    long Revision);

/// <summary>供其他模块在本地事务外启动工作流实例的最小端口。</summary>
public interface IWorkflowInstanceStarter
{
    /// <summary>在可信作用域内按已发布版本启动实例。</summary>
    /// <param name="actorUserId">发起人用户标识。</param>
    /// <param name="command">启动命令。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功时返回启动后的实例摘要；失败时返回 Result 错误，如定义版本未发布、幂等冲突或作用域不匹配。</returns>
    Task<Result<WorkflowInstanceLifecycleResult>> StartAsync(
        Guid actorUserId,
        StartWorkflowInstanceCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>供其他模块取消已关联工作流实例的最小端口。</summary>
public interface IWorkflowInstanceCanceller
{
    /// <summary>取消运行中或已暂停的实例。</summary>
    /// <param name="actorUserId">执行取消的用户标识。</param>
    /// <param name="command">取消命令。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功时返回取消后的实例状态摘要；失败时返回 Result 错误，如实例已终结或 ExpectedRevision 不匹配。</returns>
    Task<Result<WorkflowInstanceLifecycleResult>> CancelAsync(
        Guid actorUserId,
        CancelWorkflowInstanceCommand command,
        CancellationToken cancellationToken = default);
}
