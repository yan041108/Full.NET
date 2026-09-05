using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>工作流待办超时扫描与条件推进 SQL。</summary>
internal static class WorkflowTodoTimeoutSql
{
    private const string Projection = """
        instance.TenantId, instance.ScopeKey, instance.TenantScopeKey,
        instance.Id AS InstanceId, todo.Id AS TodoId, todo.StepId,
        todo.AssigneeUserId, instance.BusinessType, instance.BusinessId,
        todo.Revision, todo.NextReminderAtUtc, todo.EscalateAtUtc,
        todo.ReminderIntervalMinutes, todo.MaxReminderCount, todo.ReminderCount,
        todo.EscalationRecipientUserId, todo.EscalatedAtUtc,
        todo.NextTimeoutSignalAtUtc
        """;

    /// <summary>SQL Server 有界扫描；暂停和终态实例不会进入候选。</summary>
    public static readonly SqlStatement ScanDueSqlServer = new(
        "workflow.todo_timeout.scan_due.sqlserver",
        $"""
        SELECT TOP (@Take) {Projection}
        FROM fn_workflow_todo AS todo
        INNER JOIN fn_workflow_instance AS instance ON instance.Id = todo.InstanceId
        WHERE todo.StatusKey = 'active'
          AND instance.StatusKey = 'active'
          AND todo.NextTimeoutSignalAtUtc IS NOT NULL
          AND todo.NextTimeoutSignalAtUtc <= @Now
          AND (@HasAfter = 0
               OR todo.NextTimeoutSignalAtUtc > @AfterSignalAtUtc
               OR (todo.NextTimeoutSignalAtUtc = @AfterSignalAtUtc AND todo.Id > @AfterTodoId))
        ORDER BY todo.NextTimeoutSignalAtUtc, todo.Id
        """,
        SqlDataScope.Global);

    /// <summary>MySQL 有界扫描；排序与 SQL Server 保持等价。</summary>
    public static readonly SqlStatement ScanDueMySql = new(
        "workflow.todo_timeout.scan_due.mysql",
        $"""
        SELECT {Projection}
        FROM fn_workflow_todo AS todo
        INNER JOIN fn_workflow_instance AS instance ON instance.Id = todo.InstanceId
        WHERE todo.StatusKey = 'active'
          AND instance.StatusKey = 'active'
          AND todo.NextTimeoutSignalAtUtc IS NOT NULL
          AND todo.NextTimeoutSignalAtUtc <= @Now
          AND (@HasAfter = 0
               OR todo.NextTimeoutSignalAtUtc > @AfterSignalAtUtc
               OR (todo.NextTimeoutSignalAtUtc = @AfterSignalAtUtc AND todo.Id > @AfterTodoId))
        ORDER BY todo.NextTimeoutSignalAtUtc, todo.Id
        LIMIT @Take
        """,
        SqlDataScope.Global);

    /// <summary>按修订号和原调度时间原子提交一次催办，重复扫描只能有一个写入者成功。</summary>
    public static readonly SqlStatement CommitReminder = new(
        "workflow.todo_timeout.commit_reminder",
        """
        UPDATE fn_workflow_todo
        SET ReminderCount = @ReminderCount,
            LastReminderAtUtc = @Now,
            NextReminderAtUtc = @NextReminderAtUtc,
            NextTimeoutSignalAtUtc = @NextTimeoutSignalAtUtc,
            Revision = Revision + 1
        WHERE Id = @TodoId
          AND StatusKey = 'active'
          AND Revision = @Revision
          AND NextTimeoutSignalAtUtc = @ExpectedSignalAtUtc
          AND EXISTS (
              SELECT 1 FROM fn_workflow_instance AS instance
              WHERE instance.Id = fn_workflow_todo.InstanceId
                AND instance.StatusKey = 'active'
                AND instance.TenantScopeKey = @TenantScopeKey)
        """,
        SqlDataScope.Global);

    /// <summary>原子提交一次升级并关闭该待办的后续超时调度。</summary>
    public static readonly SqlStatement CommitEscalation = new(
        "workflow.todo_timeout.commit_escalation",
        """
        UPDATE fn_workflow_todo
        SET EscalatedAtUtc = @Now,
            NextReminderAtUtc = NULL,
            NextTimeoutSignalAtUtc = NULL,
            Revision = Revision + 1
        WHERE Id = @TodoId
          AND StatusKey = 'active'
          AND Revision = @Revision
          AND EscalatedAtUtc IS NULL
          AND NextTimeoutSignalAtUtc = @ExpectedSignalAtUtc
          AND EXISTS (
              SELECT 1 FROM fn_workflow_instance AS instance
              WHERE instance.Id = fn_workflow_todo.InstanceId
                AND instance.StatusKey = 'active'
                AND instance.TenantScopeKey = @TenantScopeKey)
        """,
        SqlDataScope.Global);
}
