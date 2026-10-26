namespace Full.NET.Modules.Workflow.Domain;

/// <summary>审批与投票可构成真实人工决策；普通填报或领取不计入人审，审批席位类型另行裁定。</summary>
internal static class WorkflowHumanActivityKinds
{
    public const string Approval = "human.approval";
    public const string GroupApproval = "human.group_approval";
    public const string Task = "human.task";
    public const string Vote = "human.vote";
    public const string Claim = "human.claim";
    public static bool IsFormActivity(string? kind) => IsApproval(kind) || kind is Task or Vote or Claim;
    public static bool IsApproval(string? kind) => kind is Approval or GroupApproval;
    public static bool IsHumanDecision(string? kind) => IsApproval(kind) || kind == Vote;
}
