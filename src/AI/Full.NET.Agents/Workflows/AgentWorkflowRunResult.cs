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
/// <remarks>枚举成员数值发布后不可调整；新增成员只能追加到末尾，以保持线格式兼容。</remarks>
public enum AgentWorkflowRunStatus
{
    /// <summary>工作流已执行完成并产出最终结果；终态，不再触发后续审批或对账。</summary>
    Completed = 1,
    /// <summary>工作流在写工具处等待人工审批；非终态，审批通过后继续执行，拒绝则转为 Failed。</summary>
    AwaitingApproval = 2,
    /// <summary>执行过程中检测到状态不一致或依赖缺失，需人工介入对账；非终态，禁止自动恢复。</summary>
    ReconciliationRequired = 3,
    /// <summary>工作流执行失败；终态，伴随稳定错误码，调用方应据此做补偿或重试决策。</summary>
    Failed = 4,
}
