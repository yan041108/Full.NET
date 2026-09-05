namespace Full.NET.Modules.Workflow.Domain;

/// <summary>描述并行分叉后需要同时激活的分支计划。</summary>
/// <param name="ForkNodeKey">分叉节点键。</param>
/// <param name="JoinNodeKey">对应汇合节点键。</param>
/// <param name="Branches">按发布顺序排列的分支计划。</param>
internal sealed record WorkflowParallelForkPlan(
    string ForkNodeKey,
    string JoinNodeKey,
    IReadOnlyList<WorkflowParallelBranchPlan> Branches,
    string GatewayTypeKey = "parallel");

/// <summary>描述单个并行分支从入口到首个等待点或汇合点的计划。</summary>
/// <param name="BranchKey">稳定分支键。</param>
/// <param name="NextApprovalNodeKey">分支上的下一人工审批节点；直接到达汇合时为空。</param>
/// <param name="CompletesInstance">分支在汇合前是否已结束实例。</param>
/// <param name="AutomaticNodes">到达下一等待点前按顺序执行的自动节点。</param>
/// <param name="TimeoutPolicy">下一审批节点固化的超时策略。</param>
/// <param name="ApprovalPolicy">下一审批节点固化的多人审批策略。</param>
/// <param name="AssigneePolicy">下一审批节点固化的办理人解析策略。</param>
/// <param name="JoinArrival">分支已到达汇合点且需要等待其他分支时的到达计划。</param>
internal sealed record WorkflowParallelBranchPlan(
    string BranchKey,
    string? NextApprovalNodeKey,
    bool CompletesInstance,
    IReadOnlyList<WorkflowAutomaticRuntimeNode> AutomaticNodes,
    WorkflowTodoTimeoutPolicy? TimeoutPolicy,
    WorkflowApprovalPolicy? ApprovalPolicy,
    WorkflowAssigneePolicy AssigneePolicy,
    WorkflowJoinArrivalPlan? JoinArrival = null);

/// <summary>描述单个并行分支到达汇合点时的同步计划。</summary>
/// <param name="JoinNodeKey">汇合节点键。</param>
/// <param name="ForkNodeKey">对应分叉节点键。</param>
/// <param name="BranchKey">当前到达的分支键。</param>
/// <param name="TrailingAutomaticNodes">到达汇合点前尚未落库的自动节点。</param>
internal sealed record WorkflowJoinArrivalPlan(
    string JoinNodeKey,
    string ForkNodeKey,
    string BranchKey,
    IReadOnlyList<WorkflowAutomaticRuntimeNode> TrailingAutomaticNodes);
