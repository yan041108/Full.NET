using System.Data.Common;
using System.Net;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using static Full.NET.IntegrationTests.Workflow.WorkflowDurableDelayApiTests;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>真实双库验证区域内部持久化协议；路由准入由固定测试IR注入，不代表公共目录已开放。</summary>
[TestClass]
public sealed class WorkflowRegionFrameApiTests
{
    [TestMethod]
    public async Task Region_frames_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Region_frames_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        var identity = await factory.CreateHostIdentityAsync("region.owner." + Guid.NewGuid().ToString("N"), [WorkflowPermissions.FormsCreate,
            WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish,
            WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesRead, WorkflowPermissions.TodosRead, "tenancy.tenants.switch"]);
        using var host = factory.CreateClientForHost("localhost"); host.DefaultRequestHeaders.Authorization = new("Bearer", identity.AccessToken);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        await VerifyScopeAsync(factory, db, host, null);
        using var probe = factory.CreateClientForHost("acme.localhost"); var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(probe);
        var role = Guid.CreateVersion7(); var hostRole = await db.ExecuteScalarAsync<Guid>("SELECT RoleId FROM fn_identity_user_role WHERE UserId=@user", new { user = identity.UserId });
        await db.ExecuteAsync("INSERT INTO fn_identity_tenant_member (Id,TenantId,UserId,MemberRole,Status,CreatedAtUtc,UpdatedAtUtc,Version) VALUES (@id,@tenant,@user,'Member','Active',@now,@now,1)", new { id = Guid.CreateVersion7(), tenant = tenant.Id, user = identity.UserId, now = DateTimeOffset.UtcNow });
        await db.ExecuteAsync("INSERT INTO fn_identity_role (Id,TenantId,ScopeKey,Code,Name,IsSystem,IsActive,IsSuperAdministrator,DataScopeKind,CreatedAtUtc,Version) SELECT @role,@tenant,@scope,@code,Name,IsSystem,IsActive,IsSuperAdministrator,DataScopeKind,CreatedAtUtc,Version FROM fn_identity_role WHERE Id=@hostRole", new { role, tenant = tenant.Id, scope = $"tenant:{tenant.Id:N}", code = "region-" + role.ToString("N"), hostRole });
        await db.ExecuteAsync("INSERT INTO fn_identity_role_permission (RoleId,PermissionCode) SELECT @role,PermissionCode FROM fn_identity_role_permission WHERE RoleId=@hostRole", new { role, hostRole });
        await db.ExecuteAsync("INSERT INTO fn_identity_user_role (UserId,RoleId) VALUES (@user,@role)", new { user = identity.UserId, role });
        using var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, identity.AccessToken, tenant.Id));
        await VerifyScopeAsync(factory, db, client, tenant.Id);
        var frames = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_region_frame");
        var bindings = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activation_binding");
        await db.ExecuteAsync("ALTER TABLE fn_workflow_activation_binding DROP CONSTRAINT fk_fn_workflow_activation_binding_source".Replace("DROP CONSTRAINT",provider==DatabaseProvider.SqlServer?"DROP CONSTRAINT":"DROP FOREIGN KEY",StringComparison.Ordinal));
        await db.ExecuteAsync(provider==DatabaseProvider.SqlServer?"DROP INDEX ux_fn_workflow_activation_binding_node ON fn_workflow_activation_binding":"ALTER TABLE fn_workflow_activation_binding DROP INDEX ux_fn_workflow_activation_binding_node");
        await db.ExecuteAsync(provider==DatabaseProvider.SqlServer?"DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%326_WorkflowActivationBindings.sql'":"DELETE FROM schemaversions WHERE ScriptName LIKE '%326_WorkflowActivationBindings.sql'");
        await db.ExecuteAsync("ALTER TABLE fn_workflow_region_frame DROP CONSTRAINT fk_fn_workflow_region_frame_epoch".Replace("DROP CONSTRAINT", provider == DatabaseProvider.SqlServer ? "DROP CONSTRAINT" : "DROP FOREIGN KEY", StringComparison.Ordinal));
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "ALTER TABLE fn_workflow_region_frame DROP CONSTRAINT ck_fn_workflow_region_frame_state" : "ALTER TABLE fn_workflow_region_frame DROP CHECK ck_fn_workflow_region_frame_state");
        foreach (var name in new[] { "ux_fn_workflow_execution_epoch_iteration", "ux_fn_workflow_execution_epoch_cause" })
            await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? $"DROP INDEX {name} ON fn_workflow_execution_epoch" : $"ALTER TABLE fn_workflow_execution_epoch DROP INDEX {name}");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%325_WorkflowRegionFrames.sql'" : "DELETE FROM schemaversions WHERE ScriptName LIKE '%325_WorkflowRegionFrames.sql'");
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection); await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection);
        Assert.AreEqual(frames, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_region_frame"));
        Assert.AreEqual(bindings, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activation_binding"));
        if (provider==DatabaseProvider.MySql) Assert.AreEqual(0,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA=DATABASE() AND ROUTINE_NAME='fn_workflow_activation_binding_ensure'"));
        if (provider == DatabaseProvider.MySql) Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA=DATABASE() AND ROUTINE_NAME='fn_workflow_region_frame_ensure'"));
    }

    private static async Task VerifyScopeAsync(FullNetApiFactory factory, DbConnection db, HttpClient client, Guid? tenantId)
    {
        var created = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "region." + Guid.NewGuid().ToString("N"), draft = new {
            schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields = new[] { new { fieldKey = "amount", fieldTypeKey = "integer", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
        var form = (await PostAsync(client, $"/api/v1/workflow/forms/{created.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        WorkflowNodeDraft Node(string key, string type, object config) => new(key, type, 1, JsonSerializer.SerializeToElement(config));
        var nodes = new[] { Node("start", "start", new { nextNodeKeys = new[] { "fork" } }),
            Node("fork", "gateway.parallel", new { gatewayRoleKey = "fork", joinNodeKey = "join", branches = new[] { new { branchKey = "left", nextNodeKey = "review" }, new { branchKey = "right", nextNodeKey = "right" } } }),
            Node("review", "human.approval", new { nextNodeKeys = new[] { "route" }, fieldPolicies = new { amount = "readOnly" } }),
            Node("route", "control.empty", new { nextNodeKeys = new[] { "join" } }),
            Node("right", "human.approval", new { nextNodeKeys = new[] { "join" }, fieldPolicies = new { amount = "readOnly" } }),
            Node("join", "gateway.parallel", new { gatewayRoleKey = "join", forkNodeKey = "fork", nextNodeKeys = new[] { "end" } }), Node("end", "end", new { }) };
        var version = await PublishNodesAsync(client, form, nodes.Cast<object>().ToArray());
        var root = (await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "region.test", businessId = Guid.NewGuid().ToString("N"), initialValues = new { amount = 0 }, idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var join = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_parallel_join WHERE InstanceId=@root", new { root });
        var scopeKey = tenantId is { } tenant ? $"tenant:{tenant:N}" : "host";
        // 固定测试IR只替换尚未公开的路由准入，正常拓扑和真实外层分叉来自实际发布版本。
        var internalNodes = nodes.Select(node => node.NodeKey == "route" ? Node("route", WorkflowDynamicRoutePolicy.NodeType,
            new { nextNodeKeys = new[] { "join" }, routePolicy = new { modeKey = "rules", targets = new[] { new { targetKey = "retry", nodeKey = "review", regionKey = "review-round", maximumIterations = 3,
                condition = new { fieldKey = "amount", @operator = "greaterThan", value = 0 } } } } }) : node).ToArray();
        var schema = new WorkflowFormSchema(1, 1, [new("main", [new("amount", "integer", false, new Dictionary<string, JsonElement>())])]);
        var edges = new Dictionary<string, string[]> { ["start"] = ["fork"], ["fork"] = ["review", "right"], ["review"] = ["route"], ["route"] = ["join"], ["right"] = ["join"], ["join"] = ["end"], ["end"] = [] };
        Assert.IsTrue(WorkflowDynamicRoutePlan.TryCompile(new(1, internalNodes), edges, schema, out var plan)); var target = plan!.Routes["route"].Targets.Single();
        await db.ExecuteAsync("UPDATE fn_workflow_todo SET StatusKey='cancelled',CompletedAtUtc=@now,Revision=Revision+1 WHERE InstanceId=@root AND StepId IN (SELECT Id FROM fn_workflow_step WHERE InstanceId=@root AND NodeKey='review')", new { root, now = DateTimeOffset.UtcNow });
        await db.ExecuteAsync("UPDATE fn_workflow_step SET StatusKey='cancelled',CompletedAtUtc=@now,Revision=Revision+1 WHERE InstanceId=@root AND NodeKey='review'", new { root, now = DateTimeOffset.UtcNow });
        async Task<Guid> Source(Guid epoch)
        {
            var id = Guid.CreateVersion7(); var sequence = await db.ExecuteScalarAsync<long>("SELECT COALESCE(MAX(ExecutionSequence),0)+1 FROM fn_workflow_step WHERE InstanceId=@root", new { root });
            await db.ExecuteAsync("INSERT INTO fn_workflow_step (Id,InstanceId,ExecutionEpochId,NodeKey,NodeTypeKey,StatusKey,AssignedUserId,DueAtUtc,AttemptCount,Revision,ExecutionSequence,StartedAtUtc,CompletedAtUtc,ParallelJoinId,ParallelBranchKey) VALUES (@id,@root,@epoch,'route','control.route','completed',NULL,NULL,0,1,@sequence,@now,@now,@join,'left')", new { id, root, epoch, sequence, now = DateTimeOffset.UtcNow, join });
            Guid? review=null;
            if (epoch!=root) {
                review=Guid.CreateVersion7();
                // 内部IR夹具承认本轮已完成的真实步骤；公共路由和人工投票重建仍由后续纵向场景验收。
                await db.ExecuteAsync("INSERT INTO fn_workflow_step (Id,InstanceId,ExecutionEpochId,NodeKey,NodeTypeKey,StatusKey,AssignedUserId,DueAtUtc,AttemptCount,Revision,ExecutionSequence,StartedAtUtc,CompletedAtUtc,ParallelJoinId,ParallelBranchKey) VALUES (@review,@root,@epoch,'review','human.approval','completed',NULL,NULL,0,1,@sequence,@now,@now,@join,'left')",new { review,root,epoch,sequence=sequence+1,now=DateTimeOffset.UtcNow,join });
            }
            await using var scope=factory.Services.CreateAsyncScope(); var writer=scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            if (tenantId is {} sourceTenant) writer.SetTenant((await scope.ServiceProvider.GetRequiredService<IActiveTenantContextResolver>().ResolveActiveByIdAsync(sourceTenant,default))!); else writer.SetHost();
            try {
                var command=scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
                await scope.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async token=> {
                    Assert.AreEqual(1,await command.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction,WorkflowSqlParameters.Create(("Id",root),("TenantScopeKey",scopeKey)),token));
                    await WorkflowActivationBindingWriter.BindAsync(command,root,scopeKey,epoch,id,DateTimeOffset.UtcNow,token);
                    if (review is {} actual) await WorkflowActivationBindingWriter.BindAsync(command,root,scopeKey,epoch,actual,DateTimeOffset.UtcNow,token);
                    return true;
                },default);
            } finally { writer.Clear(); }
            return id;
        }
        async Task<WorkflowRegionEpochChange> Change(Guid epoch, Guid source, bool exit = false, bool failFrame = false)
        {
            await using var scope = factory.Services.CreateAsyncScope(); var writer = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            if (tenantId is { } id) writer.SetTenant((await scope.ServiceProvider.GetRequiredService<IActiveTenantContextResolver>().ResolveActiveByIdAsync(id, default))!); else writer.SetHost();
            try {
                var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>(); var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
                var selectedCommand = command;
                if (failFrame) selectedCommand = new FailRegionFrameCommandExecutor(command);
                return await scope.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async token => {
                    Assert.AreEqual(1, await command.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction, WorkflowSqlParameters.Create(("Id", root), ("TenantScopeKey", scopeKey)), token));
                    var instance = (await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, WorkflowSqlParameters.Create(("Id", root), ("TenantScopeKey", scopeKey)), token))!;
                    var coordinator = new WorkflowRegionEpochCoordinator(query, selectedCommand, scope.ServiceProvider.GetRequiredService<IIdGenerator>());
                    var result = exit ? await coordinator.ExitAsync(instance, epoch, source, plan, "join", null, DateTimeOffset.UtcNow, token)
                        : await coordinator.EnterAsync(instance, epoch, source, plan, target, null, DateTimeOffset.UtcNow, token);
                    if (result.IsSuccess) Assert.AreEqual(1, await command.ExecuteAsync(WorkflowSql.AdvanceInstanceWithRevision, WorkflowSqlParameters.Create(("Id", root), ("TenantScopeKey", scopeKey), ("Revision", instance.Revision)), token));
                    return result;
                }, default);
            } finally { writer.Clear(); }
        }
        var firstSource = await Source(root); var first = await Change(root, firstSource); Assert.IsTrue(first.IsSuccess); var firstEpoch = first.ExecutionEpochId!.Value;
        var forkStep=await db.ExecuteScalarAsync<Guid>("SELECT StepId FROM fn_workflow_activation_binding WHERE ExecutionEpochId=@root AND NodeKey='fork'",new { root });
        Assert.AreEqual(forkStep,await db.ExecuteScalarAsync<Guid>("SELECT StepId FROM fn_workflow_activation_binding WHERE ExecutionEpochId=@firstEpoch AND NodeKey='fork'",new { firstEpoch }));
        Assert.AreEqual(0,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activation_binding WHERE ExecutionEpochId=@firstEpoch AND NodeKey IN ('review','route')",new { firstEpoch }));
        Assert.AreEqual(join, await db.ExecuteScalarAsync<Guid>("SELECT ParentForkActivationId FROM fn_workflow_region_frame WHERE ExecutionEpochId=@firstEpoch", new { firstEpoch }));
        await using (var scope = factory.Services.CreateAsyncScope()) {
            var writer = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            if (tenantId is { } id) writer.SetTenant((await scope.ServiceProvider.GetRequiredService<IActiveTenantContextResolver>().ResolveActiveByIdAsync(id, default))!); else writer.SetHost();
            try {
                var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>(); var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
                await scope.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async token => {
                    Assert.AreEqual(1, await command.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction, WorkflowSqlParameters.Create(("Id", root), ("TenantScopeKey", scopeKey)), token));
                    var instance = (await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, WorkflowSqlParameters.Create(("Id", root), ("TenantScopeKey", scopeKey)), token))!;
                    Assert.IsTrue(await WorkflowExecutionFrameScope.IsValidAsync(query, instance, firstEpoch, join, "left", token));
                    Assert.IsFalse(await WorkflowExecutionFrameScope.IsValidAsync(query, instance, firstEpoch, join, "right", token)); return true;
                }, default);
            } finally { writer.Clear(); }
        }
        var secondSource = await Source(firstEpoch); var before = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_execution_epoch WHERE InstanceId=@root", new { root });
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => { _ = await Change(firstEpoch, secondSource, failFrame: true); });
        Assert.AreEqual(before, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_execution_epoch WHERE InstanceId=@root", new { root }));
        Assert.AreEqual("active", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_execution_epoch WHERE Id=@firstEpoch", new { firstEpoch }));
        var second = await Change(firstEpoch, secondSource); Assert.IsTrue(second.IsSuccess); var secondEpoch = second.ExecutionEpochId!.Value;
        Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT IterationNumber FROM fn_workflow_execution_epoch WHERE Id=@secondEpoch", new { secondEpoch }));
        Assert.AreEqual(root, await db.ExecuteScalarAsync<Guid>("SELECT ParentEpochId FROM fn_workflow_execution_epoch WHERE Id=@secondEpoch", new { secondEpoch }));
        Assert.AreEqual(0,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activation_binding WHERE ExecutionEpochId=@secondEpoch AND NodeKey IN ('review','route')",new { secondEpoch }));
        var exit = await Change(secondEpoch, await Source(secondEpoch), exit: true); Assert.IsTrue(exit.IsSuccess); Assert.AreEqual(root, exit.ExecutionEpochId);
        Assert.AreEqual(secondEpoch,await db.ExecuteScalarAsync<Guid>("SELECT SourceEpochId FROM fn_workflow_activation_binding WHERE ExecutionEpochId=@root AND NodeKey='review'",new { root }));
        var replay = await Change(root, firstSource); Assert.IsFalse(replay.IsSuccess); Assert.AreEqual("workflow.activity.region_source_already_applied", replay.FailureKey);
        var invalid = await Assert.ThrowsAsync<DbException>(async () => await db.ExecuteAsync("UPDATE fn_workflow_region_frame SET ParentForkActivationId=NULL,ParentBranchKey='left' WHERE ExecutionEpochId=@secondEpoch", new { secondEpoch }));
        Assert.IsTrue(invalid is SqlException { Number: 547 } or MySqlException { Number: 3819 });
        var unique = await Assert.ThrowsAsync<DbException>(async () => await db.ExecuteAsync("INSERT INTO fn_workflow_execution_epoch (Id,InstanceId,TenantScopeKey,ParentEpochId,CauseStepId,RegionKey,IterationNumber,Depth,StatusKey,Revision,CreatedAtUtc,ClosedAtUtc) VALUES (@id,@root,@scopeKey,@root,@firstSource,'source-probe',1,1,'active',1,@now,NULL)", new { id = Guid.CreateVersion7(), root, scopeKey, firstSource, now = DateTimeOffset.UtcNow }));
        Assert.IsTrue(unique is SqlException { Number: 2601 or 2627 } or MySqlException { Number: 1062 });
        var freshCause = await Source(root);
        var ordinal = await Assert.ThrowsAsync<DbException>(async () => await db.ExecuteAsync("INSERT INTO fn_workflow_execution_epoch (Id,InstanceId,TenantScopeKey,ParentEpochId,CauseStepId,RegionKey,IterationNumber,Depth,StatusKey,Revision,CreatedAtUtc,ClosedAtUtc) VALUES (@id,@root,@scopeKey,@root,@freshCause,'review-round',1,1,'active',1,@now,NULL)", new { id = Guid.CreateVersion7(), root, scopeKey, freshCause, now = DateTimeOffset.UtcNow }));
        Assert.IsTrue(ordinal is SqlException { Number: 2601 or 2627 } or MySqlException { Number: 1062 });
        var foreignVersion = await PublishNodesAsync(client, form, nodes.Cast<object>().ToArray());
        var foreignRoot = (await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "region.foreign", businessId = Guid.NewGuid().ToString("N"), initialValues = new { amount = 0 }, idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var foreignFork = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_parallel_join WHERE InstanceId=@foreignRoot", new { foreignRoot });
        var foreignStep=await db.ExecuteScalarAsync<Guid>("SELECT StepId FROM fn_workflow_activation_binding WHERE ExecutionEpochId=@foreignRoot AND NodeKey='fork'",new { foreignRoot });
        var foreignBinding=await Assert.ThrowsAsync<DbException>(async()=>await db.ExecuteAsync("UPDATE fn_workflow_activation_binding SET StepId=@foreignStep,SourceEpochId=@foreignRoot WHERE ExecutionEpochId=@root AND NodeKey='fork'",new { foreignStep,foreignRoot,root }));
        Assert.IsTrue(foreignBinding is SqlException { Number:547 } or MySqlException { Number:1452 });
        foreach (var field in new[] { "version", "root", "fork" })
        {
            var foreign = await Assert.ThrowsAsync<DbException>(async () => await db.ExecuteAsync(field switch {
                "version" => "UPDATE fn_workflow_region_frame SET DefinitionVersionId=@foreignVersion WHERE ExecutionEpochId=@secondEpoch",
                "root" => "UPDATE fn_workflow_region_frame SET InstanceId=@foreignRoot WHERE ExecutionEpochId=@secondEpoch",
                _ => "UPDATE fn_workflow_region_frame SET ParentForkActivationId=@foreignFork WHERE ExecutionEpochId=@secondEpoch" }, new { foreignVersion, foreignRoot, foreignFork, secondEpoch }));
            Assert.IsTrue(foreign is SqlException { Number: 547 } or MySqlException { Number: 1452 });
        }
    }

    /// <summary>只在帧写入处注入失败；其余命令继续使用真实执行器及同一Workflow事务。</summary>
    private sealed class FailRegionFrameCommandExecutor(ICommandExecutor inner) : ICommandExecutor
    {
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            statement.Equals(WorkflowRegionSql.InsertFrame) ? Task.FromResult(0) : inner.ExecuteAsync(statement, parameters, cancellationToken);
    }
}
