using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Migrations;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>从真实公开流程产生的事实校验整条因果，不以内部IR注入或模拟写入代替执行证据。</summary>
internal static class WorkflowCausalityAssertions
{
    public static async Task CompleteRootAsync(DbConnection db,Guid instance)
    {
        Assert.AreEqual(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE Id=@instance AND InstanceId=@instance AND NodeTypeKey='start' AND StatusKey='completed' AND ExecutionSequence=0",new { instance }));
        Assert.AreEqual(0,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step AS step WHERE step.InstanceId=@instance AND step.NodeTypeKey<>'start' AND NOT EXISTS (SELECT 1 FROM fn_workflow_step_cause AS cause WHERE cause.StepId=step.Id AND cause.InstanceId=step.InstanceId)",new { instance }));
        Assert.AreEqual(0,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step_cause AS cause INNER JOIN fn_workflow_step AS child ON child.Id=cause.StepId INNER JOIN fn_workflow_step AS parent ON parent.Id=cause.ParentStepId WHERE cause.InstanceId=@instance AND (child.InstanceId<>parent.InstanceId OR child.ExecutionSequence<=parent.ExecutionSequence)",new { instance }));
        Assert.AreEqual(0,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_work WHERE InstanceId=@instance AND SourceStepId IS NULL",new { instance }));
        Assert.AreEqual(0,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_work WHERE InstanceId=@instance AND CauseKindKey IS NULL",new { instance }));
        var joins=await db.QueryAsync<CausalJoin>("SELECT Id,JoinNodeKey,ExecutionEpochId,ForkStepId,RequiredBranchCount,StatusKey FROM fn_workflow_parallel_join WHERE InstanceId=@instance",new { instance });
        foreach(var join in joins)
        {
            Assert.AreNotEqual(Guid.Empty,join.ForkStepId);
            var parents=(await db.QueryAsync<Guid>("SELECT SourceStepId FROM fn_workflow_parallel_branch_arrival WHERE ParallelJoinId=@id AND InstanceId=@instance",new { id=join.Id,instance })).ToArray();
            Assert.IsFalse(parents.Contains(Guid.Empty));
            Assert.IsTrue(parents.Length <= join.RequiredBranchCount);
            if (join.StatusKey == "cancelled") continue;
            Assert.AreEqual("completed", join.StatusKey);
            Assert.AreEqual(join.RequiredBranchCount,parents.Length);
            // 后续Return会导出新一轮绑定；历史汇合须按原到达来源核对，不能借用当前同名节点。
            var candidates = (await db.QueryAsync<Guid>("SELECT DISTINCT step.Id FROM fn_workflow_step AS step INNER JOIN fn_workflow_step_cause AS cause ON cause.StepId=step.Id AND cause.InstanceId=step.InstanceId WHERE step.ExecutionEpochId=@epoch AND step.NodeKey=@node AND step.InstanceId=@instance AND cause.KindKey='join' AND cause.ParentStepId IN @parents",
                new { epoch=join.ExecutionEpochId,node=join.JoinNodeKey,instance,parents })).ToArray();
            var matched = 0;
            foreach (var finalStep in candidates)
            {
                var causes = (await db.QueryAsync<Guid>("SELECT ParentStepId FROM fn_workflow_step_cause WHERE StepId=@finalStep AND InstanceId=@instance AND KindKey='join'", new { finalStep, instance })).ToArray();
                if (causes.ToHashSet().SetEquals(parents) && causes.Length == parents.Distinct().Count()) matched++;
            }
            Assert.AreEqual(1, matched, "每个已完成fork必须有且仅有一个匹配原到达集合的真实汇合。");
        }
    }

    public static async Task RecoverAsync(DatabaseProvider provider,string connection,DbConnection db,Guid instance,Guid foreignRoot)
    {
        var probe=(await db.QueryAsync<Guid>("SELECT Id FROM fn_workflow_parallel_branch_arrival WHERE InstanceId=@instance",new { instance })).First();
        var retained=await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step_cause");
        foreach(var constraint in new[]{"fk_fn_workflow_parallel_branch_arrival_source","fk_fn_workflow_parallel_branch_arrival_root_join"})
            await db.ExecuteAsync(provider==DatabaseProvider.SqlServer?$"ALTER TABLE fn_workflow_parallel_branch_arrival DROP CONSTRAINT {constraint}":$"ALTER TABLE fn_workflow_parallel_branch_arrival DROP FOREIGN KEY {constraint}");
        await db.ExecuteAsync(provider==DatabaseProvider.SqlServer?"ALTER TABLE fn_workflow_parallel_branch_arrival ALTER COLUMN InstanceId uniqueidentifier NULL":"ALTER TABLE fn_workflow_parallel_branch_arrival MODIFY COLUMN InstanceId binary(16) NULL COMMENT '从真实分叉回填的根实例身份'");
        await db.ExecuteAsync("UPDATE fn_workflow_parallel_branch_arrival SET InstanceId=NULL,SourceStepId=NULL WHERE Id=@probe",new { probe });
        await db.ExecuteAsync(provider==DatabaseProvider.SqlServer?"ALTER TABLE fn_workflow_step_cause DROP CONSTRAINT fk_fn_workflow_step_cause_parent":"ALTER TABLE fn_workflow_step_cause DROP FOREIGN KEY fk_fn_workflow_step_cause_parent");
        await db.ExecuteAsync("DROP INDEX ux_fn_workflow_step_cause_edge ON fn_workflow_step_cause");
        await db.ExecuteAsync(provider==DatabaseProvider.SqlServer?"DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%327_WorkflowCausality.sql'":"DELETE FROM schemaversions WHERE ScriptName LIKE '%327_WorkflowCausality.sql'");
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider,connection);await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider,connection);
        Assert.AreEqual(retained,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step_cause"));
        Assert.AreEqual(instance,await db.ExecuteScalarAsync<Guid>("SELECT InstanceId FROM fn_workflow_parallel_branch_arrival WHERE Id=@probe",new { probe }));
        Assert.AreEqual(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_parallel_branch_arrival WHERE Id=@probe AND SourceStepId IS NULL",new { probe }));
        var child=(await db.QueryAsync<Guid>("SELECT Id FROM fn_workflow_step WHERE InstanceId=@instance AND NodeTypeKey<>'start'",new { instance })).First();
        var scope=await db.ExecuteScalarAsync<string>("SELECT TenantScopeKey FROM fn_workflow_instance WHERE Id=@instance",new { instance });
        var foreign=await Assert.ThrowsAsync<DbException>(async()=>await db.ExecuteAsync("INSERT INTO fn_workflow_step_cause (Id,InstanceId,TenantScopeKey,StepId,ParentStepId,KindKey,CreatedAtUtc) VALUES (@id,@instance,@scope,@child,@foreignRoot,'sequence',@now)",new { id=Guid.CreateVersion7(),instance,scope,child,foreignRoot,now=DateTimeOffset.UtcNow }));
        Assert.IsTrue(foreign is SqlException { Number:547 } or MySqlException { Number:1452 });
        if(provider==DatabaseProvider.MySql) Assert.AreEqual(0,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA=DATABASE() AND ROUTINE_NAME='fn_workflow_causality_ensure'"));
    }

    private sealed record CausalJoin(Guid Id,string JoinNodeKey,Guid ExecutionEpochId,Guid ForkStepId,int RequiredBranchCount,string StatusKey);
}
