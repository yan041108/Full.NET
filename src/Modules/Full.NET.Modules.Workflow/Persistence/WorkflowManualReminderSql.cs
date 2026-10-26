using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>催办仅访问 Workflow 自有事实；读取与写入均由可信实例及真实发起人锚定。</summary>
internal static class WorkflowManualReminderSql
{
    public static readonly SqlStatement ListTargets = new("workflow.manual_reminder.list_targets", """
        SELECT step.Id AS StepId, step.NodeKey, step.NodeTypeKey, step.Revision AS StepRevision,
               todo.Id AS TodoId, todo.AssigneeUserId, todo.Revision AS TodoRevision
        FROM fn_workflow_instance AS instance
        INNER JOIN fn_workflow_step AS step ON step.InstanceId=instance.Id
        INNER JOIN fn_workflow_todo AS todo ON todo.InstanceId=instance.Id AND todo.StepId=step.Id
        WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StartedById=@ActorUserId
          AND instance.StatusKey='active' AND step.StatusKey='active' AND todo.StatusKey='active'
        ORDER BY step.Id, todo.Id
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindReceipt = new("workflow.manual_reminder.find_receipt", """
        SELECT reminder.Id, reminder.StepId, reminder.RequestedByUserId, reminder.InstanceRevision,
               reminder.StepRevision, reminder.ManualSequence, reminder.RecipientCount, reminder.RequestHash, reminder.CreatedAtUtc
        FROM fn_workflow_manual_reminder AS reminder
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=reminder.InstanceId
        WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StartedById=@ActorUserId
          AND reminder.IdempotencyKey=@IdempotencyKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement ReadBudget = new("workflow.manual_reminder.read_budget", """
        SELECT reminder.ManualSequence AS ManualCount, reminder.CreatedAtUtc AS LastRequestedAtUtc
        FROM fn_workflow_manual_reminder AS reminder
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=reminder.InstanceId
        WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StartedById=@ActorUserId
          AND reminder.StepId=@StepId
          AND reminder.ManualSequence=(SELECT MAX(previous.ManualSequence) FROM fn_workflow_manual_reminder AS previous
              WHERE previous.InstanceId=@InstanceId AND previous.StepId=@StepId)
        """, SqlDataScope.Global);
    public static readonly SqlStatement Insert = new("workflow.manual_reminder.insert", """
        INSERT INTO fn_workflow_manual_reminder
          (Id,InstanceId,StepId,RequestedByUserId,InstanceRevision,StepRevision,ManualSequence,RecipientCount,IdempotencyKey,RequestHash,CreatedAtUtc)
        VALUES (@Id,@InstanceId,@StepId,@ActorUserId,@InstanceRevision,@StepRevision,@ManualSequence,@RecipientCount,@IdempotencyKey,@RequestHash,@Now)
        """, SqlDataScope.Global);
}

internal sealed record WorkflowManualReminderTargetRecord(Guid StepId, string NodeKey, string NodeTypeKey, long StepRevision,
    Guid TodoId, Guid AssigneeUserId, long TodoRevision);
internal sealed record WorkflowManualReminderReceiptRecord(Guid Id, Guid StepId, Guid RequestedByUserId, long InstanceRevision,
    long StepRevision, int ManualSequence, int RecipientCount, string RequestHash, DateTimeOffset CreatedAtUtc);
internal sealed record WorkflowManualReminderBudgetRecord(int ManualCount, DateTimeOffset? LastRequestedAtUtc);
