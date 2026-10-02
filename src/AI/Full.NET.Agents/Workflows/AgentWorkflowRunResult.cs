namespace Full.NET.Agents.Workflows;

/// <summary>工作流执行结果。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Status">工作流执行终态或等待态，取值自 AgentWorkflowRunStatus。</param>
/// <param name="State">执行结束时的工作流状态快照；用于恢复或对账。</param>
/// <param name="FinalText">模型最终输出文本；无输出时为 <see langword="null"/>。</param>
/// <param name="ErrorCode">失败时的稳定错误码；成功或等待审批时为 <see langword="null"/>。</param>
public sealed record AgentWorkflowRunResult(
    AgentWorkflowRunStatus Status,
    AgentWorkflowState State,
    string? FinalText,
    string? ErrorCode);

/// <summary>工作流执行状态。</summary>
public enum AgentWorkflowRunStatus
{
    Completed = 1,
    AwaitingApproval = 2,
    ReconciliationRequired = 3,
    Failed = 4,
}
