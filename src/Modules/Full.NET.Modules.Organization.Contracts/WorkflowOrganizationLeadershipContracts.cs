namespace Full.NET.Modules.Organization.Contracts;

/// <summary>只读 Port 的闭合组织领导权来源；不接受租户、执行人或任意查询条件。</summary>
public enum WorkflowOrganizationLeaderSourceKind { Unit, InitiatorPrimaryUnit, InitiatorAncestorUnit, InitiatorLeaderChain }
/// <summary>固定机构使用 UnitId；上级及连续主管使用一至二十的层级。</summary>
public sealed record WorkflowOrganizationLeaderSource(WorkflowOrganizationLeaderSourceKind Kind, Guid? UnitId = null, int Depth = 0);
/// <summary>无效来源必须拒绝，活动机构未配置领导权是有效空结果；账号资格仍由 Identity 复核。</summary>
public sealed record WorkflowOrganizationLeaderResolution(bool SourceIsValid, IReadOnlyList<WorkflowOrganizationLeaderDirectoryEntry> Leaders);
