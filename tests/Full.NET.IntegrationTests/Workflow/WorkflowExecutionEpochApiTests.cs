using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using static Full.NET.IntegrationTests.Workflow.WorkflowDurableDelayApiTests;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>真实执行验证代次基础；测试内部注入新区域代次，不代表设计器已开放循环。</summary>
[TestClass]
public sealed class WorkflowExecutionEpochApiTests
{
    [TestMethod]
    public async Task Execution_epochs_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Execution_epochs_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        var identity = await factory.CreateHostIdentityAsync("epoch.owner." + Guid.NewGuid().ToString("N"), [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish,
            WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.DefinitionsRead, WorkflowPermissions.InstancesStart,
            WorkflowPermissions.InstancesRead, WorkflowPermissions.InstancesCancel, WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, "tenancy.tenants.switch"]);
        using var host = factory.CreateClientForHost("localhost"); host.DefaultRequestHeaders.Authorization = new("Bearer", identity.AccessToken);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        await VerifyScopeAsync(factory, db, host, null);
        using var probe = factory.CreateClientForHost("acme.localhost"); var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(probe);
        var role = Guid.CreateVersion7();
        var hostRole = await db.ExecuteScalarAsync<Guid>("SELECT RoleId FROM fn_identity_user_role WHERE UserId=@user", new { user = identity.UserId });
        await db.ExecuteAsync("INSERT INTO fn_identity_tenant_member (Id,TenantId,UserId,MemberRole,Status,CreatedAtUtc,UpdatedAtUtc,Version) VALUES (@id,@tenant,@user,'Member','Active',@now,@now,1)",
            new { id = Guid.CreateVersion7(), tenant = tenant.Id, user = identity.UserId, now = DateTimeOffset.UtcNow });
        await db.ExecuteAsync("""
            INSERT INTO fn_identity_role (Id,TenantId,ScopeKey,Code,Name,IsSystem,IsActive,IsSuperAdministrator,DataScopeKind,CreatedAtUtc,Version)
            SELECT @role,@tenant,@scope,@code,Name,IsSystem,IsActive,IsSuperAdministrator,DataScopeKind,CreatedAtUtc,Version FROM fn_identity_role WHERE Id=@hostRole
            """, new { role, tenant = tenant.Id, scope = $"tenant:{tenant.Id:N}", code = "epoch-" + role.ToString("N"), hostRole });
        await db.ExecuteAsync("INSERT INTO fn_identity_role_permission (RoleId,PermissionCode) SELECT @role,PermissionCode FROM fn_identity_role_permission WHERE RoleId=@hostRole", new { role, hostRole });
        await db.ExecuteAsync("INSERT INTO fn_identity_user_role (UserId,RoleId) VALUES (@user,@role)", new { user = identity.UserId, role });
        using var tenantClient = factory.CreateClientForHost("localhost"); tenantClient.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, identity.AccessToken, tenant.Id));
        await VerifyScopeAsync(factory, db, tenantClient, tenant.Id);

        var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_execution_epoch");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "ALTER TABLE fn_workflow_execution_epoch DROP CONSTRAINT ck_fn_workflow_execution_epoch_state" : "ALTER TABLE fn_workflow_execution_epoch DROP CHECK ck_fn_workflow_execution_epoch_state");
        await db.ExecuteAsync("ALTER TABLE fn_workflow_subprocess_call DROP CONSTRAINT fk_fn_workflow_subprocess_call_epoch".Replace("DROP CONSTRAINT", provider == DatabaseProvider.SqlServer ? "DROP CONSTRAINT" : "DROP FOREIGN KEY", StringComparison.Ordinal));
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%323_WorkflowExecutionEpochs.sql' OR ScriptName LIKE '%319_WorkflowSubprocesses.sql'" : "DELETE FROM schemaversions WHERE ScriptName LIKE '%323_WorkflowExecutionEpochs.sql' OR ScriptName LIKE '%319_WorkflowSubprocesses.sql'");
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection); await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection);
        Assert.AreEqual(count, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_execution_epoch"));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>(provider == DatabaseProvider.SqlServer ? "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('fn_workflow_subprocess_call') AND name='ux_fn_workflow_subprocess_call_node'" : "SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_call' AND INDEX_NAME='ux_fn_workflow_subprocess_call_node'"));
        var forks = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_parallel_join");
        await db.ExecuteAsync("ALTER TABLE fn_workflow_parallel_join DROP CONSTRAINT fk_fn_workflow_parallel_join_epoch".Replace("DROP CONSTRAINT", provider == DatabaseProvider.SqlServer ? "DROP CONSTRAINT" : "DROP FOREIGN KEY", StringComparison.Ordinal));
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DROP INDEX ix_fn_workflow_parallel_join_epoch ON fn_workflow_parallel_join" : "ALTER TABLE fn_workflow_parallel_join DROP INDEX ix_fn_workflow_parallel_join_epoch");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%324_WorkflowForkEpochs.sql'" : "DELETE FROM schemaversions WHERE ScriptName LIKE '%324_WorkflowForkEpochs.sql'");
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection); await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection);
        Assert.AreEqual(forks, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_parallel_join"));
        Assert.IsTrue(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_parallel_join WHERE ExecutionEpochId<>InstanceId") > 0);
        await Assert.ThrowsAsync<DbException>(async () => await db.ExecuteAsync("UPDATE fn_workflow_execution_epoch SET Depth=-1"));
        if (provider == DatabaseProvider.MySql) Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA=DATABASE() AND ROUTINE_NAME IN ('fn_workflow_execution_epoch_ensure','fn_workflow_execution_epoch_bindings_ensure','fn_workflow_subprocess_epoch_ensure','fn_workflow_fork_epoch_ensure')"));
    }

    private static async Task VerifyScopeAsync(FullNetApiFactory factory, DbConnection db, HttpClient client, Guid? tenantId)
    {
        object Node(string key, string type, object config) => new { nodeKey = key, nodeTypeKey = type, nodeSchemaVersion = 1, config };
        var created = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "epoch." + Guid.NewGuid().ToString("N"), draft = new {
            schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields = new[] { new { fieldKey = "note", fieldTypeKey = "text", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
        var form = (await PostAsync(client, $"/api/v1/workflow/forms/{created.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        object Approval() => new { nextNodeKeys = new[] { "end" }, fieldPolicies = new { note = "editable" }, assigneePolicy = new { sources = new[] { new { resolverKindKey = "initiator" } } } };
        var child = await PublishNodesAsync(client, form, [Node("start", "start", new { nextNodeKeys = new[] { "review" } }), Node("review", "human.approval", Approval()), Node("end", "end", new { })]);
        var version = await PublishNodesAsync(client, form, [Node("start", "start", new { nextNodeKeys = new[] { "child" } }), Node("child", "process.subprocess", new {
            nextNodeKeys = new[] { "after" }, fieldPolicies = new { note = "editable" }, subprocessPolicy = new { definitionVersionId = child, inputFields = new { note = "note" }, outputFields = new { note = "note" } } }),
            Node("after", "human.approval", Approval()), Node("end", "end", new { })]);
        var root = (await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "epoch.test", businessId = Guid.NewGuid().ToString("N"), initialValues = new { note = "original" }, idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        async Task Tick() { await using var scope = factory.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<WorkflowSubprocessProcessor>().ProcessPendingAsync(default); await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default); }
        async Task Until(Func<Task<bool>> condition, string reason) { for (var i = 0; i < 64; i++) { if (await condition()) return; await Tick(); } Assert.Fail(reason); }
        async Task<Guid> Todo(Guid instance) => await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@instance AND StatusKey='active'", new { instance });
        async Task Approve(Guid instance, string note)
        {
            var todo = await Todo(instance); var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}");
            await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/approve", new { expectedRevision = detail.GetProperty("revision").GetInt64(), expectedFormRevision = detail.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { note }, idempotencyKey = Guid.NewGuid().ToString("N") });
        }
        await Until(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_item WHERE InstanceId=@root AND ChildInstanceId IS NOT NULL", new { root }) == 1, "初始代次没有创建真实子根");
        var firstChild = await db.ExecuteScalarAsync<Guid>("SELECT ChildInstanceId FROM fn_workflow_subprocess_item WHERE InstanceId=@root", new { root }); var oldTodo = await Todo(firstChild);
        await Approve(firstChild, "round-one");
        await Until(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@root AND NodeKey='after' AND StatusKey='active'", new { root }) == 1, "初始子结果未续办");
        var cause = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_subprocess_call WHERE InstanceId=@root", new { root });
        var after = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_step WHERE InstanceId=@root AND NodeKey='after' AND StatusKey='active'", new { root });
        var epoch = Guid.CreateVersion7(); var scopeKey = tenantId is { } tenant ? $"tenant:{tenant:N}" : "host";
        var foreignStep = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_step WHERE InstanceId=@firstChild AND NodeKey='review'", new { firstChild });
        foreach (var wrongParent in new[] { true, false })
        {
            var invalid = await Assert.ThrowsAsync<DbException>(async () => await db.ExecuteAsync("""
                INSERT INTO fn_workflow_execution_epoch (Id,InstanceId,TenantScopeKey,ParentEpochId,CauseStepId,RegionKey,IterationNumber,Depth,StatusKey,Revision,CreatedAtUtc,ClosedAtUtc)
                VALUES (@id,@root,@scopeKey,@parent,@causeStep,'probe',1,1,'active',1,@now,NULL)
                """, new { id = Guid.CreateVersion7(), root, scopeKey, parent = wrongParent ? firstChild : root, causeStep = wrongParent ? cause : foreignStep, now = DateTimeOffset.UtcNow }));
            Assert.IsTrue(invalid is SqlException { Number: 547 } or MySqlException { Number: 1452 });
        }
        // 内部故障注入模拟已验证的区域进入，不暴露客户端选代次或任意回边接口。
        await db.ExecuteAsync("UPDATE fn_workflow_todo SET StatusKey='cancelled',CompletedAtUtc=@now,Revision=Revision+1 WHERE StepId=@after AND InstanceId=@root AND StatusKey='active'", new { after, root, now = DateTimeOffset.UtcNow });
        await db.ExecuteAsync("UPDATE fn_workflow_step SET StatusKey='cancelled',CompletedAtUtc=@now,Revision=Revision+1 WHERE Id=@after AND InstanceId=@root", new { after, root, now = DateTimeOffset.UtcNow });
        await db.ExecuteAsync("""
            INSERT INTO fn_workflow_execution_epoch (Id,InstanceId,TenantScopeKey,ParentEpochId,CauseStepId,RegionKey,IterationNumber,Depth,StatusKey,Revision,CreatedAtUtc,ClosedAtUtc)
            VALUES (@epoch,@root,@scopeKey,@root,@cause,'child',1,1,'active',1,@now,NULL)
            """, new { epoch, root, scopeKey, cause, now = DateTimeOffset.UtcNow });
        await db.ExecuteAsync("INSERT INTO fn_workflow_region_frame (ExecutionEpochId,InstanceId,TenantScopeKey,DefinitionVersionId,EntryNodeKey,ExitNodeKey,ParentForkActivationId,ParentBranchKey,CreatedAtUtc) VALUES (@epoch,@root,@scopeKey,@version,'child','after',NULL,NULL,@now)", new { epoch,root,scopeKey,version,now=DateTimeOffset.UtcNow });
        var closedEpoch = Guid.CreateVersion7();
        await db.ExecuteAsync("""
            INSERT INTO fn_workflow_execution_epoch (Id,InstanceId,TenantScopeKey,ParentEpochId,CauseStepId,RegionKey,IterationNumber,Depth,StatusKey,Revision,CreatedAtUtc,ClosedAtUtc)
            VALUES (@closedEpoch,@root,@scopeKey,@root,@after,'closed-probe',2,1,'closed',1,@now,@now)
            """, new { closedEpoch, root, scopeKey, after, now = DateTimeOffset.UtcNow });
        async Task Schedule(Guid selectedEpoch)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var writer = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            if (tenantId is { } id) writer.SetTenant((await scope.ServiceProvider.GetRequiredService<IActiveTenantContextResolver>().ResolveActiveByIdAsync(id, default))!); else writer.SetHost();
            try {
                var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>(); var commands = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
                await scope.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async token => {
                    await commands.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction, WorkflowSqlParameters.Create(("Id", root), ("TenantScopeKey", scopeKey)), token);
                    var instance = (await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, WorkflowSqlParameters.Create(("Id", root), ("TenantScopeKey", scopeKey)), token))!;
                    var submission = (await query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance, WorkflowSqlParameters.Create(("InstanceId", root), ("TenantScopeKey", scopeKey)), token))!;
                    var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(submission.SubmissionJson)!;
                    var sequence = await query.QuerySingleOrDefaultAsync<long>(WorkflowSql.FindNextStepExecutionSequence, WorkflowSqlParameters.Create(("InstanceId", root)), token);
                    await scope.ServiceProvider.GetRequiredService<WorkflowActivityScheduler>().EnqueueAsync(instance, selectedEpoch, "child", values, submission.Revision, sequence, null, null, DateTimeOffset.UtcNow, token);
                    return true;
                }, default);
            } finally { writer.Clear(); }
        }
        var closed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Schedule(closedEpoch));
        Assert.AreEqual("workflow.execution_epoch.missing", closed.Message);
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_work WHERE InstanceId=@root AND ExecutionEpochId=@closedEpoch", new { root, closedEpoch }));
        await Schedule(epoch);
        await Until(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_item WHERE InstanceId=@root AND ChildInstanceId IS NOT NULL", new { root }) == 2, "新代次错误复用了旧同名调用");
        var call = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_subprocess_call WHERE InstanceId=@root AND ExecutionEpochId=@epoch", new { root, epoch });
        var secondChild = await db.ExecuteScalarAsync<Guid>("SELECT ChildInstanceId FROM fn_workflow_subprocess_item WHERE CallId=@call", new { call }); Assert.AreNotEqual(firstChild, secondChild);
        await PostAsync(client, $"/api/v1/workflow/todos/{oldTodo:D}/approve", new { expectedRevision = 1, expectedFormRevision = 1, fieldPatch = new { note = "late-old-result" }, idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Conflict);
        await Tick(); Assert.AreEqual("active", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@secondChild", new { secondChild }));
        var wrongCall = await Assert.ThrowsAsync<DbException>(async () => await db.ExecuteAsync("UPDATE fn_workflow_subprocess_call SET ExecutionEpochId=@root,NodeKey='epoch-probe' WHERE Id=@call", new { root, call }));
        Assert.IsTrue(wrongCall is SqlException { Number: 547 } or MySqlException { Number: 1452 });
        var wrongWork = await Assert.ThrowsAsync<DbException>(async () => await db.ExecuteAsync("UPDATE fn_workflow_activity_work SET ExecutionEpochId=@secondChild WHERE InstanceId=@root", new { secondChild, root }));
        Assert.IsTrue(wrongWork is SqlException { Number: 547 } or MySqlException { Number: 1452 });
        await Approve(secondChild, "round-two");
        await Until(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@root AND NodeKey='after' AND StatusKey='active' AND ExecutionEpochId=@epoch", new { root, epoch }) == 1, "新代次没有创建独立人工等待");
        await Approve(root, "final");
        Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@root", new { root }));
        Assert.AreEqual(3, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_execution_epoch WHERE InstanceId=@root AND StatusKey='closed'", new { root }));
        await VerifyForkScopeAsync(factory, db, client, tenantId, form);
    }

    private static async Task VerifyForkScopeAsync(FullNetApiFactory factory, DbConnection db, HttpClient client, Guid? tenantId, Guid form)
    {
        object Node(string key, string type, object config) => new { nodeKey=key, nodeTypeKey=type, nodeSchemaVersion=1, config };
        object Approval(string next, string access) => new { nextNodeKeys=new[] { next }, fieldPolicies=new { note=access }, assigneePolicy=new { sources=new[] { new { resolverKindKey="initiator" } } } };
        var version = await PublishNodesAsync(client, form, [Node("start","start",new { nextNodeKeys=new[] { "fork" } }),
            Node("fork","gateway.parallel",new { gatewayRoleKey="fork",joinNodeKey="join",branches=new[] { new { branchKey="left",nextNodeKey="left" },new { branchKey="right",nextNodeKey="right" } } }),
            Node("left","human.approval",Approval("join","readOnly")),Node("right","human.approval",Approval("join","readOnly")),
            Node("join","gateway.parallel",new { gatewayRoleKey="join",forkNodeKey="fork",nextNodeKeys=new[] { "after" } }),
            Node("after","human.approval",Approval("end","editable")),Node("end","end",new { })]);
        var root=(await PostAsync(client,"/api/v1/workflow/instances",new { definitionVersionId=version,businessType="epoch.fork",businessId=Guid.NewGuid().ToString("N"),initialValues=new { note="fork" },idempotencyKey=Guid.NewGuid().ToString("N") },HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var scopeKey=tenantId is { } tenant?$"tenant:{tenant:N}":"host";
        async Task Approve(Guid todo)
        {
            var detail=await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}");
            await PostAsync(client,$"/api/v1/workflow/todos/{todo:D}/approve",new { expectedRevision=detail.GetProperty("revision").GetInt64(),expectedFormRevision=detail.GetProperty("submissionRevision").GetInt64(),fieldPatch=new { },idempotencyKey=Guid.NewGuid().ToString("N") });
        }
        async Task Tick() { await using var scope=factory.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default); }
        async Task<Guid[]> Todos() => (await db.QueryAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@root AND StatusKey='active'",new { root })).ToArray();
        var first=(await Todos()); Assert.AreEqual(2,first.Length);
        var oldJoin=await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_parallel_join WHERE InstanceId=@root",new { root });
        foreach(var todo in first) await Approve(todo);
        for(var i=0;i<32 && (await Todos()).Length!=1;i++) await Tick();
        Assert.AreEqual(1,(await Todos()).Length);
        Assert.AreEqual(2,await db.ExecuteScalarAsync<int>("SELECT ArrivedBranchCount FROM fn_workflow_parallel_join WHERE Id=@oldJoin",new { oldJoin }));
        var cause=await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_step WHERE InstanceId=@root AND NodeKey='fork'",new { root });
        var epoch=Guid.CreateVersion7();
        // 只注入可信内部区域进入，公开循环仍保持关闭；旧分叉及其到达事实完整保留。
        await db.ExecuteAsync("UPDATE fn_workflow_todo SET StatusKey='cancelled',CompletedAtUtc=@now,Revision=Revision+1 WHERE InstanceId=@root AND StatusKey='active'",new { root,now=DateTimeOffset.UtcNow });
        await db.ExecuteAsync("UPDATE fn_workflow_step SET StatusKey='cancelled',CompletedAtUtc=@now,Revision=Revision+1 WHERE InstanceId=@root AND StatusKey='active'",new { root,now=DateTimeOffset.UtcNow });
        await db.ExecuteAsync("INSERT INTO fn_workflow_execution_epoch (Id,InstanceId,TenantScopeKey,ParentEpochId,CauseStepId,RegionKey,IterationNumber,Depth,StatusKey,Revision,CreatedAtUtc,ClosedAtUtc) VALUES (@epoch,@root,@scopeKey,@root,@cause,'fork',1,1,'active',1,@now,NULL)",new { epoch,root,scopeKey,cause,now=DateTimeOffset.UtcNow });
        await db.ExecuteAsync("INSERT INTO fn_workflow_region_frame (ExecutionEpochId,InstanceId,TenantScopeKey,DefinitionVersionId,EntryNodeKey,ExitNodeKey,ParentForkActivationId,ParentBranchKey,CreatedAtUtc) VALUES (@epoch,@root,@scopeKey,@version,'fork','after',NULL,NULL,@now)",new { epoch,root,scopeKey,version,now=DateTimeOffset.UtcNow });
        await using(var scope=factory.Services.CreateAsyncScope())
        {
            var writer=scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            if(tenantId is { } id) writer.SetTenant((await scope.ServiceProvider.GetRequiredService<IActiveTenantContextResolver>().ResolveActiveByIdAsync(id,default))!); else writer.SetHost();
            try {
                var query=scope.ServiceProvider.GetRequiredService<IQueryExecutor>(); var command=scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
                await scope.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async token => {
                    await command.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction,WorkflowSqlParameters.Create(("Id",root),("TenantScopeKey",scopeKey)),token);
                    var instance=(await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,WorkflowSqlParameters.Create(("Id",root),("TenantScopeKey",scopeKey)),token))!;
                    var submission=(await query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance,WorkflowSqlParameters.Create(("InstanceId",root),("TenantScopeKey",scopeKey)),token))!;
                    var sequence=await query.QuerySingleOrDefaultAsync<long>(WorkflowSql.FindNextStepExecutionSequence,WorkflowSqlParameters.Create(("InstanceId",root)),token);
                    await scope.ServiceProvider.GetRequiredService<WorkflowActivityScheduler>().EnqueueAsync(instance,epoch,"fork",JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(submission.SubmissionJson)!,submission.Revision,sequence,null,null,DateTimeOffset.UtcNow,token);
                    return true;
                },default);
            } finally { writer.Clear(); }
        }
        for(var i=0;i<32 && (await Todos()).Length!=2;i++) await Tick();
        Assert.AreEqual(2,(await Todos()).Length);
        var nextJoin=await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_parallel_join WHERE InstanceId=@root AND ExecutionEpochId=@epoch",new { root,epoch }); Assert.AreNotEqual(oldJoin,nextJoin);
        var foreignEpoch=await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_execution_epoch WHERE InstanceId<>@root AND TenantScopeKey=@scopeKey",new { root,scopeKey });
        var wrongFrame=await Assert.ThrowsAsync<DbException>(async()=>await db.ExecuteAsync("UPDATE fn_workflow_parallel_join SET ExecutionEpochId=@foreignEpoch WHERE Id=@nextJoin",new { foreignEpoch,nextJoin }));
        Assert.IsTrue(wrongFrame is SqlException { Number:547 } or MySqlException { Number:1452 });
        Assert.AreEqual(0,await db.ExecuteScalarAsync<int>("SELECT ArrivedBranchCount FROM fn_workflow_parallel_join WHERE Id=@nextJoin",new { nextJoin }));
        await using(var scope=factory.Services.CreateAsyncScope())
        {
            var writer=scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            if(tenantId is { } id) writer.SetTenant((await scope.ServiceProvider.GetRequiredService<IActiveTenantContextResolver>().ResolveActiveByIdAsync(id,default))!); else writer.SetHost();
            try {
                await scope.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async token => {
                    await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(WorkflowSql.LockInstanceForTodoAction,WorkflowSqlParameters.Create(("Id",root),("TenantScopeKey",scopeKey)),token);
                    var coordinator=scope.ServiceProvider.GetRequiredService<WorkflowParallelJoinCoordinator>();
                    foreach(var pair in new[] { (Epoch:epoch,Join:oldJoin),(Epoch:root,Join:nextJoin) }) {
                        var result=await coordinator.TryRecordArrivalAsync(root,pair.Epoch,pair.Join,"fork","join","left",DateTimeOffset.UtcNow,token);
                        Assert.IsFalse(result.IsValid); Assert.IsFalse(result.IsNewArrival);
                    }
                    return true;
                },default);
            } finally { writer.Clear(); }
        }
        await PostAsync(client,$"/api/v1/workflow/todos/{first[0]:D}/approve",new { expectedRevision=1,expectedFormRevision=1,fieldPatch=new { },idempotencyKey=Guid.NewGuid().ToString("N") },HttpStatusCode.Conflict);
        Assert.AreEqual(0,await db.ExecuteScalarAsync<int>("SELECT ArrivedBranchCount FROM fn_workflow_parallel_join WHERE Id=@nextJoin",new { nextJoin }));
        var second=await Todos(); await Approve(second[0]);
        Assert.AreEqual(1,await db.ExecuteScalarAsync<int>("SELECT ArrivedBranchCount FROM fn_workflow_parallel_join WHERE Id=@nextJoin",new { nextJoin }));
        Assert.AreEqual(0,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@root AND NodeKey='after' AND ExecutionEpochId=@epoch",new { root,epoch }));
        await Approve(second[1]); Assert.AreEqual(2,await db.ExecuteScalarAsync<int>("SELECT ArrivedBranchCount FROM fn_workflow_parallel_join WHERE Id=@nextJoin",new { nextJoin }));
        for(var i=0;i<32 && (await Todos()).Length!=1;i++) await Tick();
        await Approve((await Todos()).Single());
        Assert.AreEqual("completed",await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@root",new { root }));
        Assert.AreEqual(4,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_parallel_branch_arrival WHERE ParallelJoinId IN (@oldJoin,@nextJoin)",new { oldJoin,nextJoin }));
    }
}
