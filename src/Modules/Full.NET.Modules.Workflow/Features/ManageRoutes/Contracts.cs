namespace Full.NET.Modules.Workflow.Features.ManageRoutes;

/// <summary>恢复只允许选择固定版本的目标机器键；空值表示已发布正常出口，不接受节点或代次身份。</summary>
public sealed record ReconcileWorkflowRouteRequest(long ExpectedInstanceRevision, long ExpectedRevision,
    string? SelectedTargetKey, string Reason, string IdempotencyKey);

/// <summary>应用后的唯一后继身份不随后续执行改变，可作为幂等重放回执。</summary>
public sealed record WorkflowRouteResolutionResponse(Guid RouteId, Guid ContinuationWorkId, Guid ExecutionEpochId, string NextNodeKey);

/// <summary>运维只暴露固定版本允许的目标及摘要，不返回原业务输入。</summary>
public sealed record WorkflowRouteDecisionResponse(Guid Id, Guid InstanceId, string NodeKey, string StatusKey, long Revision,
    string InstanceStatusKey, long InstanceRevision, Guid SourceExecutionEpochId, Guid? TargetExecutionEpochId,
    string? SelectedTargetKey, string? NextNodeKey, Guid? ContinuationWorkId, IReadOnlyList<WorkflowRouteTargetResponse> Targets,
    string NormalNextNodeKey, DateTimeOffset CreatedAtUtc, DateTimeOffset? AppliedAtUtc);

/// <summary>目标来自已发布且已验证的区域引用。</summary>
public sealed record WorkflowRouteTargetResponse(string TargetKey, string NodeKey, string RegionKey, bool Reenters, int MaximumIterations);
