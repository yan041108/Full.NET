namespace Full.NET.Modules.Workflow.Contracts;

/// <summary>业务恢复要求匹配原启动幂等键及表单快照；实例标识不能单独作为绑定证据。</summary>
/// <param name="InstanceId">待核对的既有流程实例。</param>
/// <param name="BusinessType">所有者约定的稳定业务类型。</param>
/// <param name="BusinessId">原启动绑定的业务标识。</param>
/// <param name="InitialValuesJson">原启动表单快照。</param>
/// <param name="IdempotencyKey">原启动幂等键。</param>
public sealed record WorkflowBusinessStartProofRequest(Guid InstanceId, string BusinessType, string BusinessId,
    string InitialValuesJson, string IdempotencyKey);

/// <summary>由 Workflow 所有者核对启动回执后的实例摘要，不包含私有表或运行节点。</summary>
/// <param name="InstanceId">已核对的流程实例。</param>
/// <param name="TenantId">可信租户标识。</param>
/// <param name="DefinitionVersionId">原启动固定的定义版本。</param>
/// <param name="BusinessType">稳定业务类型。</param>
/// <param name="BusinessId">原业务标识。</param>
/// <param name="BusinessTitle">原启动标题。</param>
/// <param name="StatusKey">读取时的权威流程状态。</param>
/// <param name="StartedById">原启动操作者，不是恢复操作者。</param>
/// <param name="StartedAtUtc">权威启动时间。</param>
/// <param name="CompletedAtUtc">权威终态时间；非终态为空。</param>
public sealed record WorkflowBusinessInstanceSnapshot(Guid InstanceId, Guid TenantId, Guid DefinitionVersionId,
    string BusinessType, string BusinessId, string? BusinessTitle, string StatusKey, Guid StartedById,
    DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc);

/// <summary>仅在当前可信租户内、业务本地事务之外查询原启动证据，不创建或恢复流程。</summary>
public interface IWorkflowBusinessStartProofDirectory
{
    /// <summary>实例、业务键、原启动参数及原发起人回执均匹配时返回摘要，否则为空。</summary>
    /// <param name="request">原启动证据要求。</param>
    /// <param name="cancellationToken">调用取消信号。</param>
    /// <returns>已核对摘要或空值；不改变流程。</returns>
    Task<WorkflowBusinessInstanceSnapshot?> FindAsync(WorkflowBusinessStartProofRequest request,
        CancellationToken cancellationToken = default);
}
