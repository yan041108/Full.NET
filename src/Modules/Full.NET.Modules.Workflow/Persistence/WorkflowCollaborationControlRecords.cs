namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>目标发布及认证本地收件的最小事实；源接纳只能修改自己的状态及共享协议状态。</summary>
internal sealed record WorkflowCollaborationControlRecord(Guid Id, Guid CallId, string SourceTenantScopeKey, string TargetTenantScopeKey,
    string KindKey, string BodyJson, string BodyHash, Guid? EnvelopeMessageId, string StatusKey, long Revision,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? ReceivedAtUtc);

/// <summary>同一目标来源树的预算汇总；损坏或未收敛时不能退回源预留。</summary>
internal sealed record WorkflowCollaborationBudgetFacts(int UnusedActivities, int InstanceCount, int InvalidBudgets)
{
    // MySQL的SUM/COUNT以Decimal/Int64返回；默认构造后的显式属性映射允许受控数值转换。
    public WorkflowCollaborationBudgetFacts() : this(0, 0, 0) { }
}
