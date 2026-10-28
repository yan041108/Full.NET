using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>有界读取本根实际事实；投影不包含历史表单或成员原文，数量越界时调用方失败关闭。</summary>
internal static class WorkflowReturnScopeSql
{
    private const string Steps = """
        step.Id,step.InstanceId,step.ExecutionEpochId,step.NodeKey,step.NodeTypeKey,step.StatusKey,step.ParallelJoinId,step.ParallelBranchKey
        FROM fn_workflow_step AS step INNER JOIN fn_workflow_instance AS instance ON instance.Id=step.InstanceId
        WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active'
        ORDER BY step.Id
        """;
    public static readonly SqlStatement ListStepsSqlServer = new("workflow.return_scope.steps.sqlserver", "SELECT TOP (@Take) " + Steps, SqlDataScope.Global);
    public static readonly SqlStatement ListStepsMySql = new("workflow.return_scope.steps.mysql", "SELECT " + Steps + " LIMIT @Take", SqlDataScope.Global);
    private const string Forks = """
        fork.Id,fork.InstanceId,fork.ExecutionEpochId,fork.ForkNodeKey,fork.JoinNodeKey,fork.StatusKey,context.ParentParallelJoinId,context.ParentBranchKey
        FROM fn_workflow_parallel_join AS fork
        INNER JOIN fn_workflow_fork_context AS context ON context.ParallelJoinId=fork.Id
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=fork.InstanceId
        WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active'
        ORDER BY fork.Id
        """;
    public static readonly SqlStatement ListForksSqlServer = new("workflow.return_scope.forks.sqlserver", "SELECT TOP (@Take) " + Forks, SqlDataScope.Global);
    public static readonly SqlStatement ListForksMySql = new("workflow.return_scope.forks.mysql", "SELECT " + Forks + " LIMIT @Take", SqlDataScope.Global);
    private const string Work = """
        work.Id,work.InstanceId,work.ExecutionEpochId,work.ParallelJoinId,work.ParallelBranchKey,work.NodeKey,work.StatusKey
        FROM fn_workflow_activity_work AS work INNER JOIN fn_workflow_instance AS instance ON instance.Id=work.InstanceId AND instance.TenantScopeKey=work.TenantScopeKey
        WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active'
          AND work.StatusKey IN ('pending','leased','faulted')
        ORDER BY work.Id
        """;
    public static readonly SqlStatement ListWorkSqlServer = new("workflow.return_scope.work.sqlserver", "SELECT TOP (@Take) " + Work, SqlDataScope.Global);
    public static readonly SqlStatement ListWorkMySql = new("workflow.return_scope.work.mysql", "SELECT " + Work + " LIMIT @Take", SqlDataScope.Global);
}
