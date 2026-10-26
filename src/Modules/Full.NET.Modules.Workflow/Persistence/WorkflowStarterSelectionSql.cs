using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>启动时原子保存有界选择快照；后续只能通过可信实例租户读取，模块不提供覆盖或删除入口。</summary>
internal static class WorkflowStarterSelectionSql
{
    public static readonly SqlStatement Find = new("workflow.starter_selection.find", """
        SELECT snapshot.SelectionsJson, snapshot.SelectionsHash
        FROM fn_workflow_starter_selection AS snapshot
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=snapshot.Id
        WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement Insert = new("workflow.starter_selection.insert", """
        INSERT INTO fn_workflow_starter_selection (Id, SelectionsJson, SelectionsHash, CreatedAtUtc)
        SELECT instance.Id, @SelectionsJson, @SelectionsHash, @Now
        FROM fn_workflow_instance AS instance
        WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StartedById=@ActorUserId AND instance.StatusKey='active'
        """, SqlDataScope.Global);
}
internal sealed record WorkflowStarterSelectionRecord(string SelectionsJson, string SelectionsHash);
