namespace Full.NET.Modules.Workflow.Features.ManageRecoveryTasks;

/// <summary>查询工作流恢复任务的分页参数由查询字符串提供，本类型仅作列表项契约。</summary>
/// <param name="Id">恢复任务标识。</param>
/// <param name="InstanceId">关联工作流实例标识。</param>
/// <param name="StepId">关联步骤标识；实例级任务为空。</param>
/// <param name="KindKey">恢复种类键。</param>
/// <param name="StatusKey">恢复任务状态键。</param>
/// <param name="AttemptCount">已尝试次数。</param>
/// <param name="Revision">任务修订号。</param>
/// <param name="LeaseOwnerKey">当前租约持有者；空闲时为空。</param>
/// <param name="LeaseExpiresAtUtc">租约过期时间。</param>
/// <param name="LeaseGeneration">租约世代。</param>
/// <param name="NextAttemptAtUtc">下次允许领取时间。</param>
/// <param name="LastError">最后错误摘要。</param>
/// <param name="CreatedAtUtc">创建时间。</param>
/// <param name="UpdatedAtUtc">最近更新时间。</param>
internal sealed record WorkflowRecoveryTaskResponse(
    Guid Id,
    Guid InstanceId,
    Guid? StepId,
    string KindKey,
    string StatusKey,
    int AttemptCount,
    long Revision,
    string? LeaseOwnerKey,
    DateTimeOffset? LeaseExpiresAtUtc,
    int LeaseGeneration,
    DateTimeOffset? NextAttemptAtUtc,
    string? LastError,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>人工重试恢复任务请求。</summary>
/// <param name="ExpectedRevision">客户端最后读取到的任务修订号。</param>
/// <param name="Reason">重试原因，规范化后不得为空。</param>
/// <param name="IdempotencyKey">调用方生成的幂等键。</param>
internal sealed record RetryWorkflowRecoveryTaskRequest(
    long ExpectedRevision,
    string Reason,
    string IdempotencyKey);

/// <summary>对账并收敛恢复任务请求。</summary>
/// <param name="ExpectedRevision">客户端最后读取到的任务修订号。</param>
/// <param name="Reason">可选对账原因，最多 512 个字符。</param>
/// <param name="IdempotencyKey">调用方生成的幂等键。</param>
internal sealed record ReconcileWorkflowRecoveryTaskRequest(
    long ExpectedRevision,
    string? Reason,
    string IdempotencyKey);
