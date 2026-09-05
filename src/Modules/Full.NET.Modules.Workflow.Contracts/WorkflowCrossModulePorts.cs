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
    Task<WorkflowPublishedDefinitionVersion?> FindLatestPublishedAsync(
        string definitionKey,
        CancellationToken cancellationToken = default);
}

/// <summary>跨模块启动工作流实例的命令。</summary>
/// <param name="DefinitionVersionId">已发布定义版本标识。</param>
/// <param name="BusinessType">稳定业务类型机器码。</param>
/// <param name="BusinessId">稳定业务标识。</param>
/// <param name="InitialValuesJson">表单初始值 JSON 文本。</param>
/// <param name="IdempotencyKey">调用方幂等键。</param>
public sealed record StartWorkflowInstanceCommand(
    Guid DefinitionVersionId,
    string BusinessType,
    string BusinessId,
    string InitialValuesJson,
    string IdempotencyKey);

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
    Task<Result<WorkflowInstanceLifecycleResult>> CancelAsync(
        Guid actorUserId,
        CancelWorkflowInstanceCommand command,
        CancellationToken cancellationToken = default);
}
