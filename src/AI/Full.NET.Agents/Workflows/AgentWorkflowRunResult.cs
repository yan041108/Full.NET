namespace Full.NET.Agents.Workflows;

/// <summary>工作流执行结果。</summary>
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
