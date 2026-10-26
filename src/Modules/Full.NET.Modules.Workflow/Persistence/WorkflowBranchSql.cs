using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>分支输入、独立输出和原子合并回执；读取必须经过所属实例的租户过滤。</summary>
internal static class WorkflowBranchSql
{
    public static readonly SqlStatement CountSelectedBranch = new("workflow.branch_state.count_selected", """
        SELECT COUNT(*) FROM fn_workflow_branch_state WHERE ParallelJoinId = @ParallelJoinId AND BranchKey = @BranchKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountArrival = new("workflow.branch_arrival.count", """
        SELECT COUNT(*) FROM fn_workflow_parallel_branch_arrival WHERE ParallelJoinId = @ParallelJoinId AND BranchKey = @BranchKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertContext = new("workflow.fork_context.insert", """
        INSERT INTO fn_workflow_fork_context (ParallelJoinId, ParentParallelJoinId, ParentBranchKey)
        VALUES (@ParallelJoinId, @ParentParallelJoinId, @ParentBranchKey)
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindContext = new("workflow.fork_context.find", """
        SELECT context.ParentParallelJoinId, context.ParentBranchKey
        FROM fn_workflow_fork_context AS context
        INNER JOIN fn_workflow_parallel_join AS gateway ON gateway.Id = context.ParallelJoinId
        INNER JOIN fn_workflow_instance AS instance ON instance.Id = gateway.InstanceId
        WHERE context.ParallelJoinId = @ParallelJoinId AND instance.Id = @InstanceId
          AND instance.TenantScopeKey = @TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement Insert = new("workflow.branch_state.insert", """
        INSERT INTO fn_workflow_branch_state (ParallelJoinId, BranchKey, InputJson, InputFormRevision, OutputJson, Revision)
        VALUES (@ParallelJoinId, @BranchKey, @InputJson, @InputFormRevision, '{}', 1)
        """, SqlDataScope.Global);
    private const string Projection = """
        SELECT branch.BranchKey, branch.InputJson, branch.InputFormRevision, branch.OutputJson, branch.Revision
        FROM fn_workflow_branch_state AS branch
        INNER JOIN fn_workflow_parallel_join AS gateway ON gateway.Id = branch.ParallelJoinId
        INNER JOIN fn_workflow_instance AS instance ON instance.Id = gateway.InstanceId
        WHERE branch.ParallelJoinId = @ParallelJoinId AND instance.Id = @InstanceId
          AND instance.TenantScopeKey = @TenantScopeKey
        """;
    public static readonly SqlStatement Find = new("workflow.branch_state.find",
        Projection + " AND branch.BranchKey = @BranchKey", SqlDataScope.Global);
    public static readonly SqlStatement List = new("workflow.branch_state.list", Projection, SqlDataScope.Global);
    public static readonly SqlStatement Update = new("workflow.branch_state.update", """
        UPDATE fn_workflow_branch_state SET OutputJson = @OutputJson, Revision = Revision + 1
        WHERE ParallelJoinId = @ParallelJoinId AND BranchKey = @BranchKey AND Revision = @Revision
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertMerge = new("workflow.branch_merge.insert", """
        INSERT INTO fn_workflow_branch_merge (ParallelJoinId, FormRevision, OutputJson, MergedAtUtc)
        VALUES (@ParallelJoinId, @FormRevision, @OutputJson, @MergedAtUtc)
        """, SqlDataScope.Global);
}

/// <summary>分支局部表单修订与共同输入修订分开保存。</summary>
internal sealed record WorkflowBranchStateRecord(string BranchKey, string InputJson, long InputFormRevision,
    string OutputJson, long Revision);

/// <summary>嵌套分叉恢复其父分支，不凭节点键推断执行范围。</summary>
internal sealed record WorkflowForkContextRecord(Guid? ParentParallelJoinId, string? ParentBranchKey);
