using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>运营只读查询沿用实例当前协作授权证明；COUNT与分页使用同一可信作用域和过滤条件。</summary>
internal static class WorkflowOperationsSql
{
    private const string Steps = """
        FROM fn_workflow_step AS step
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=step.InstanceId
        INNER JOIN fn_workflow_definition_version AS version ON version.Id=instance.DefinitionVersionId
        INNER JOIN fn_workflow_definition AS definition ON definition.Id=version.DefinitionId AND definition.TenantScopeKey=instance.TenantScopeKey
        LEFT JOIN fn_workflow_sla AS sla ON sla.Id=step.Id AND sla.InstanceId=instance.Id AND sla.TenantScopeKey=instance.TenantScopeKey
        WHERE instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey IN ('active','suspended')
          AND step.StatusKey IN ('active','suspended') AND step.StartedAtUtc<=@StartedBeforeUtc
          AND (@DefinitionVersionId IS NULL OR instance.DefinitionVersionId=@DefinitionVersionId)
          AND (@OnlyOverdue=0 OR (instance.StatusKey='active' AND step.StatusKey='active' AND sla.StatusKey='waiting' AND sla.PausedAtUtc IS NULL AND sla.DueAtUtc<=@Now))
        """;
    private const string StepColumns = """
        step.Id,instance.Id AS InstanceId,instance.DefinitionVersionId,definition.DefinitionKey,step.NodeKey,step.NodeTypeKey,
        instance.StatusKey AS InstanceStatusKey,step.StatusKey,step.StartedAtUtc,sla.DueAtUtc,sla.PausedAtUtc,sla.StatusKey AS SlaStatusKey
        """;
    private const string Backlog = """
        FROM fn_workflow_activity_work AS work
        INNER JOIN fn_workflow_execution_epoch AS epoch ON epoch.Id=work.ExecutionEpochId AND epoch.InstanceId=work.InstanceId AND epoch.StatusKey='active'
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=work.InstanceId AND instance.TenantScopeKey=work.TenantScopeKey
        INNER JOIN fn_workflow_definition_version AS version ON version.Id=instance.DefinitionVersionId
        INNER JOIN fn_workflow_definition AS definition ON definition.Id=version.DefinitionId AND definition.TenantScopeKey=instance.TenantScopeKey
        WHERE instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey IN ('active','suspended')
          AND work.StatusKey IN ('pending','leased','faulted')
          AND (@DefinitionVersionId IS NULL OR instance.DefinitionVersionId=@DefinitionVersionId)
        """;
    private const string WorkColumns = """
        work.Id,instance.Id AS InstanceId,instance.DefinitionVersionId,definition.DefinitionKey,work.NodeKey,work.StatusKey,
        instance.StatusKey AS InstanceStatusKey,work.DueAtUtc,work.LeaseExpiresAtUtc,work.AttemptCount
        """;
    public static readonly SqlStatement CountStepsSqlServer = new("workflow.operations.count_steps_sqlserver", "SELECT COUNT_BIG(*) " + Steps + " AND " + WorkflowCollaborationListSql.AllowsSqlServer, SqlDataScope.Global);
    public static readonly SqlStatement CountStepsMySql = new("workflow.operations.count_steps_mysql", "SELECT COUNT(*) " + Steps + " AND " + WorkflowCollaborationListSql.AllowsMySql, SqlDataScope.Global);
    public static readonly SqlStatement PageStepsSqlServer = new("workflow.operations.page_steps_sqlserver", "SELECT " + StepColumns + " " + Steps + " AND " + WorkflowCollaborationListSql.AllowsSqlServer + " ORDER BY step.StartedAtUtc,step.Id OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", SqlDataScope.Global);
    public static readonly SqlStatement PageStepsMySql = new("workflow.operations.page_steps_mysql", "SELECT " + StepColumns + " " + Steps + " AND " + WorkflowCollaborationListSql.AllowsMySql + " ORDER BY step.StartedAtUtc,step.Id LIMIT @PageSize OFFSET @Offset", SqlDataScope.Global);
    public static readonly SqlStatement CountBacklogSqlServer = new("workflow.operations.count_backlog_sqlserver", "SELECT COUNT_BIG(*) " + Backlog + " AND " + WorkflowCollaborationListSql.AllowsSqlServer, SqlDataScope.Global);
    public static readonly SqlStatement CountBacklogMySql = new("workflow.operations.count_backlog_mysql", "SELECT COUNT(*) " + Backlog + " AND " + WorkflowCollaborationListSql.AllowsMySql, SqlDataScope.Global);
    public static readonly SqlStatement PageBacklogSqlServer = new("workflow.operations.page_backlog_sqlserver", "SELECT " + WorkColumns + " " + Backlog + " AND " + WorkflowCollaborationListSql.AllowsSqlServer + " ORDER BY work.DueAtUtc,work.Id OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", SqlDataScope.Global);
    public static readonly SqlStatement PageBacklogMySql = new("workflow.operations.page_backlog_mysql", "SELECT " + WorkColumns + " " + Backlog + " AND " + WorkflowCollaborationListSql.AllowsMySql + " ORDER BY work.DueAtUtc,work.Id LIMIT @PageSize OFFSET @Offset", SqlDataScope.Global);
}

/// <summary>列顺序与运营步骤显式投影一致，不读取业务输入或成员快照。</summary>
internal sealed record WorkflowOperationsStepRecord(Guid Id, Guid InstanceId, Guid DefinitionVersionId, string DefinitionKey,
    string NodeKey, string NodeTypeKey, string InstanceStatusKey, string StatusKey, DateTimeOffset StartedAtUtc,
    DateTimeOffset? DueAtUtc, DateTimeOffset? PausedAtUtc, string? SlaStatusKey);
/// <summary>积压仅表示真实耐久工作，不把未来定时项或暂停实例标成可执行。</summary>
internal sealed record WorkflowOperationsWorkRecord(Guid Id, Guid InstanceId, Guid DefinitionVersionId, string DefinitionKey,
    string NodeKey, string StatusKey, string InstanceStatusKey, DateTimeOffset DueAtUtc, DateTimeOffset? LeaseExpiresAtUtc, int AttemptCount);
