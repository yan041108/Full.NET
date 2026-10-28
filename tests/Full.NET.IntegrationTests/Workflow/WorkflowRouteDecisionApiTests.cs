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
using Full.NET.Modules.Workflow.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using static Full.NET.IntegrationTests.Workflow.WorkflowDurableDelayApiTests;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>真实公开流程验证路由选择、区域换代、旧工作拒绝及人工恢复；两个提供程序均覆盖Host和Tenant。</summary>
[TestClass]
public sealed class WorkflowRouteDecisionApiTests
{
    [TestMethod]
    public async Task Route_decisions_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Route_decisions_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    [TestMethod]
    public async Task Nested_routes_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(), nested: true);
    [TestMethod]
    public async Task Nested_routes_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), nested: true);

    private static async Task VerifyAsync(DatabaseProvider provider, string connection, bool nested = false)
    {
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        var identity = await factory.CreateHostIdentityAsync("route.owner." + Guid.NewGuid().ToString("N"), [WorkflowPermissions.FormsCreate,
            WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish,
            WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesRead, WorkflowPermissions.InstancesResume, WorkflowPermissions.InstancesRecover,
            WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, WorkflowPermissions.RoutesRead, WorkflowPermissions.RoutesReconcile, "tenancy.tenants.switch"]);
        using var host = factory.CreateClientForHost("localhost"); host.DefaultRequestHeaders.Authorization = new("Bearer", identity.AccessToken);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        if (nested) await VerifyNestedScopeAsync(factory, db, host);
        else await VerifyScopeAsync(factory, db, host, identity.UserId, null);
        using var probe = factory.CreateClientForHost("acme.localhost"); var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(probe);
        var role = Guid.CreateVersion7(); var hostRole = await db.ExecuteScalarAsync<Guid>("SELECT RoleId FROM fn_identity_user_role WHERE UserId=@user", new { user = identity.UserId });
        await db.ExecuteAsync("INSERT INTO fn_identity_tenant_member (Id,TenantId,UserId,MemberRole,Status,CreatedAtUtc,UpdatedAtUtc,Version) VALUES (@id,@tenant,@user,'Member','Active',@now,@now,1)", new { id = Guid.CreateVersion7(), tenant = tenant.Id, user = identity.UserId, now = DateTimeOffset.UtcNow });
        await db.ExecuteAsync("INSERT INTO fn_identity_role (Id,TenantId,ScopeKey,Code,Name,IsSystem,IsActive,IsSuperAdministrator,DataScopeKind,CreatedAtUtc,Version) SELECT @role,@tenant,@scope,@code,Name,IsSystem,IsActive,IsSuperAdministrator,DataScopeKind,CreatedAtUtc,Version FROM fn_identity_role WHERE Id=@hostRole", new { role, tenant = tenant.Id, scope = $"tenant:{tenant.Id:N}", code = "route-" + role.ToString("N"), hostRole });
        await db.ExecuteAsync("INSERT INTO fn_identity_role_permission (RoleId,PermissionCode) SELECT @role,PermissionCode FROM fn_identity_role_permission WHERE RoleId=@hostRole", new { role, hostRole });
        await db.ExecuteAsync("INSERT INTO fn_identity_user_role (UserId,RoleId) VALUES (@user,@role)", new { user = identity.UserId, role });
        using var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, identity.AccessToken, tenant.Id));
        if (nested) { await VerifyNestedScopeAsync(factory, db, client); return; }
        await VerifyScopeAsync(factory, db, client, identity.UserId, tenant.Id);
        var tenantRoute = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_route_decision WHERE TenantScopeKey=@scope ORDER BY CreatedAtUtc LIMIT 1".Replace("SELECT Id", provider == DatabaseProvider.SqlServer ? "SELECT TOP (1) Id" : "SELECT Id", StringComparison.Ordinal).Replace(" LIMIT 1", provider == DatabaseProvider.SqlServer ? "" : " LIMIT 1", StringComparison.Ordinal), new { scope = $"tenant:{tenant.Id:N}" });
        var tenantRoot = await db.ExecuteScalarAsync<Guid>("SELECT InstanceId FROM fn_workflow_route_decision WHERE Id=@tenantRoute", new { tenantRoute });
        var hostRoute = await db.ExecuteScalarAsync<Guid>(provider == DatabaseProvider.SqlServer
            ? "SELECT TOP (1) Id FROM fn_workflow_route_decision WHERE TenantScopeKey='host' ORDER BY CreatedAtUtc"
            : "SELECT Id FROM fn_workflow_route_decision WHERE TenantScopeKey='host' ORDER BY CreatedAtUtc LIMIT 1");
        var hostRoot = await db.ExecuteScalarAsync<Guid>("SELECT InstanceId FROM fn_workflow_route_decision WHERE Id=@hostRoute", new { hostRoute });
        await Assert.ThrowsAsync<DbException>(async () => { _ = await db.ExecuteAsync("UPDATE fn_workflow_route_decision SET ExecutionEpochId=@hostRoot WHERE Id=@tenantRoute", new { hostRoot, tenantRoute }); });
        var tenantWork = await db.ExecuteScalarAsync<Guid>("SELECT NextWorkId FROM fn_workflow_route_decision WHERE Id=@tenantRoute", new { tenantRoute });
        await Assert.ThrowsAsync<DbException>(async () => { _ = await db.ExecuteAsync("UPDATE fn_workflow_activity_work SET RouteDecisionId=@hostRoute WHERE Id=@tenantWork", new { hostRoute, tenantWork }); });
        using var reader = factory.CreateClientForHost("localhost");
        var readIdentity = await factory.CreateHostIdentityAsync("route.read." + Guid.NewGuid().ToString("N"), [WorkflowPermissions.RoutesRead]);
        reader.DefaultRequestHeaders.Authorization = new("Bearer", readIdentity.AccessToken);
        Assert.AreEqual(HttpStatusCode.NotFound, (await reader.GetAsync($"/api/v1/workflow/instances/{tenantRoot:D}/routes/{tenantRoute:D}")).StatusCode);
        await PostAsync(reader, $"/api/v1/workflow/instances/{tenantRoot:D}/routes/{tenantRoute:D}/reconcile", new { expectedInstanceRevision = 1, expectedRevision = 1, selectedTargetKey = (string?)null, reason = "权限隔离", idempotencyKey = "denied" }, HttpStatusCode.Forbidden);
        await VerifyRecoveryAsync(provider, connection, db);
    }

    private static async Task VerifyScopeAsync(FullNetApiFactory factory, DbConnection db, HttpClient client, Guid actor, Guid? tenant)
    {
        var created = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "route." + Guid.NewGuid().ToString("N"), draft = new {
            schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields = new object[] {
                new { fieldKey = "amount", fieldTypeKey = "integer", required = false, constraints = new { } },
                new { fieldKey = "choice", fieldTypeKey = "select", required = false, constraints = new { options = new[] { "retry", "other" } } } } } } } }, HttpStatusCode.Created);
        var form = (await PostAsync(client, $"/api/v1/workflow/forms/{created.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        var scopeKey = tenant is { } tenantId ? $"tenant:{tenantId:N}" : "host";
        var version = await PublishLoopAsync(client, form, parallel: true);
        var root = await StartAsync(client, version);
        await ApproveAsync(client, await TodoAsync(db, root, "review"), 1, "first");
        var before = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@root", new { root });
        // 中段异常回滚实际Step、Binding、因果、区域帧和唯一后继，原待处理工作仍可由Worker恢复。
        var rolledBack = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            await using var services = factory.Services.CreateAsyncScope();
            var writer = services.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            if (tenant is { } id) writer.SetTenant((await services.ServiceProvider.GetRequiredService<IActiveTenantContextResolver>().ResolveActiveByIdAsync(id, default))!); else writer.SetHost();
            try
            {
                var query = services.ServiceProvider.GetRequiredService<IQueryExecutor>(); var command = services.ServiceProvider.GetRequiredService<ICommandExecutor>();
                await services.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync<bool>(async token =>
                {
                    Assert.AreEqual(1, await command.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction, WorkflowSqlParameters.Create(("Id", root), ("TenantScopeKey", scopeKey)), token));
                    var instance = (await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, WorkflowSqlParameters.Create(("Id", root), ("TenantScopeKey", scopeKey)), token))!;
                    var asset = (await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset, WorkflowSqlParameters.Create(("DefinitionVersionId", version), ("TenantScopeKey", scopeKey)), token))!;
                    var draft = JsonSerializer.Deserialize(asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft)!;
                    var schema = JsonSerializer.Deserialize(asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema)!;
                    Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, schema, out var plan));
                    var workId = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_activity_work WHERE InstanceId=@root AND StatusKey='pending'", new { root });
                    // 使用真实固定工作投影，不能以测试猜测的帧或输入代替实际分支。
                    var sql = services.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value.Provider == DatabaseProvider.SqlServer ? WorkflowActivitySql.ListDueSqlServer : WorkflowActivitySql.ListDueMySql;
                    var work = (await query.QueryAsync<WorkflowActivityWorkRecord>(sql, WorkflowSqlParameters.Create(("Now", DateTimeOffset.UtcNow.AddMinutes(1)), ("Take", 64)), token)).Single(item => item.Id == workId);
                    var values = JsonSerializer.Deserialize(work.InputJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement)!;
                    var sequence = await query.QuerySingleOrDefaultAsync<long>(WorkflowSql.FindNextStepExecutionSequence, WorkflowSqlParameters.Create(("InstanceId", root)), token);
                    var result = await services.ServiceProvider.GetRequiredService<WorkflowRouteDecisionExecutor>().ActivateAsync(instance, work, plan!.DynamicRoutes,
                        draft.Nodes.Single(node => node.NodeKey == "route").Config, values, new(work.SourceStepId!.Value, work.ParallelJoinId, work.ParallelBranchKey, work.CauseKindKey!), sequence, DateTimeOffset.UtcNow, token);
                    Assert.IsTrue(result.Applied, result.FailureKey); throw new InvalidOperationException("route.rollback.probe");
                }, default);
            }
            finally { writer.Clear(); }
        });
        Assert.AreEqual("route.rollback.probe", rolledBack.Message);
        Assert.AreEqual(before, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@root", new { root }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_route_decision WHERE InstanceId=@root", new { root }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_execution_epoch WHERE InstanceId=@root", new { root }));
        Assert.AreEqual(1, await WorkAsync(factory));
        var route = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_route_decision WHERE InstanceId=@root", new { root });
        var workNext = await db.ExecuteScalarAsync<Guid>("SELECT NextWorkId FROM fn_workflow_route_decision WHERE Id=@route", new { route });
        var epoch = await db.ExecuteScalarAsync<Guid>("SELECT TargetEpochId FROM fn_workflow_route_decision WHERE Id=@route", new { route });
        Assert.AreNotEqual(root, epoch);
        Assert.AreEqual(route, await db.ExecuteScalarAsync<Guid>("SELECT RouteDecisionId FROM fn_workflow_activity_work WHERE Id=@workNext", new { workNext }));
        // 同根Start也不能冒充跨代次来源；恢复只重排原Work，不能产生另一条后继或新一轮。
        await db.ExecuteAsync("UPDATE fn_workflow_activity_work SET SourceStepId=@root WHERE Id=@workNext", new { root, workNext });
        await WorkAsync(factory);
        Assert.AreEqual("suspended", await StatusAsync(db, root));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@root AND NodeKey='review'", new { root }));
        await db.ExecuteAsync("UPDATE fn_workflow_activity_work SET SourceStepId=@route WHERE Id=@workNext", new { route, workNext });
        var state = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/instances/{root:D}");
        await PostAsync(client, $"/api/v1/workflow/instances/{root:D}/recover", new { expectedRevision = state.GetProperty("revision").GetInt64(), idempotencyKey = "recover-original-work", reason = "来源已修复" });
        await DrainAsync(factory);
        var todo = await TodoAsync(db, root, "review");
        Assert.AreNotEqual(Guid.Empty, todo);
        Assert.AreEqual(epoch, await db.ExecuteScalarAsync<Guid>("SELECT step.ExecutionEpochId FROM fn_workflow_todo AS todo INNER JOIN fn_workflow_step AS step ON step.Id=todo.StepId WHERE todo.Id=@todo", new { todo }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo AS todo INNER JOIN fn_workflow_step AS step ON step.Id=todo.StepId WHERE todo.InstanceId=@root AND step.NodeKey='right' AND todo.StatusKey='active'", new { root }));
        await ApproveAsync(client, todo, 0, "second"); await DrainAsync(factory);
        Assert.AreEqual("closed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_execution_epoch WHERE Id=@epoch", new { epoch }));
        await ApproveAsync(client, await TodoAsync(db, root, "right"), null, "right"); await DrainAsync(factory);
        Assert.AreEqual("completed", await StatusAsync(db, root));
        await WorkflowCausalityAssertions.CompleteRootAsync(db, root);
        await VerifyUnknownAsync(factory, db, client, form, actor, decisionField: true);
        await VerifyUnknownAsync(factory, db, client, form, actor, decisionField: false);
        await VerifyForwardAsync(factory, db, client, form);
    }

    private static async Task VerifyUnknownAsync(FullNetApiFactory factory, DbConnection db, HttpClient client, Guid form, Guid actor, bool decisionField)
    {
        var version = await PublishLoopAsync(client, form, decisionField: decisionField, maximum: 1);
        var root = await StartAsync(client, version);
        await ApproveAsync(client, await TodoAsync(db, root, "review"), 1, "unknown-first"); await DrainAsync(factory);
        if (!decisionField) { await ApproveAsync(client, await TodoAsync(db, root, "review"), 1, "exhausted"); await DrainAsync(factory); }
        Assert.AreEqual("suspended", await StatusAsync(db, root));
        var route = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_route_decision WHERE InstanceId=@root AND StatusKey='decision_unknown'", new { root });
        var state = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/instances/{root:D}/routes/{route:D}");
        Assert.IsFalse(state.TryGetProperty("inputJson", out _)); Assert.AreEqual("decision_unknown", state.GetProperty("statusKey").GetString());
        var revision = state.GetProperty("instanceRevision").GetInt64(); var routeRevision = state.GetProperty("revision").GetInt64();
        await PostAsync(client, $"/api/v1/workflow/instances/{root:D}/resume", new { expectedRevision = revision, idempotencyKey = "cannot-bypass-route" }, HttpStatusCode.Conflict);
        await PostAsync(client, $"/api/v1/workflow/instances/{root:D}/recover", new { expectedRevision = revision, reason = "强制恢复也不能跳过未知选择", idempotencyKey = "cannot-force-route" }, HttpStatusCode.Conflict);
        await PostAsync(client, $"/api/v1/workflow/instances/{root:D}/routes/{route:D}/reconcile", new { expectedInstanceRevision = revision, expectedRevision = routeRevision, selectedTargetKey = "unpublished", reason = "拒绝任意目标", idempotencyKey = "unknown-target" }, HttpStatusCode.Conflict);
        Assert.AreEqual("suspended", await StatusAsync(db, root));
        var request = new { expectedInstanceRevision = revision, expectedRevision = routeRevision, selectedTargetKey = (string?)null, reason = "核对后选择正常出口", idempotencyKey = "resolve-normal" };
        var path = $"/api/v1/workflow/instances/{root:D}/routes/{route:D}/reconcile";
        var receipts = await Task.WhenAll(PostAsync(client, path, request), PostAsync(client, path, request));
        Assert.AreEqual(receipts[0].GetRawText(), receipts[1].GetRawText());
        var next = receipts[0].GetProperty("continuationWorkId").GetGuid();
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_route_resolution WHERE RouteId=@route AND ActorUserId=@actor", new { route, actor }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_work WHERE RouteDecisionId=@route AND Id=@next", new { route, next }));
        await PostAsync(client, path, new { request.expectedInstanceRevision, request.expectedRevision, selectedTargetKey = "retry", request.reason, request.idempotencyKey }, HttpStatusCode.Conflict);
        await DrainAsync(factory); Assert.AreEqual("completed", await StatusAsync(db, root));
        Assert.AreEqual(receipts[0].GetRawText(), (await PostAsync(client, path, request)).GetRawText());
    }

    private static async Task VerifyForwardAsync(FullNetApiFactory factory, DbConnection db, HttpClient client, Guid form)
    {
        var version = await PublishNodesAsync(client, form, [Node("start", "start", new { nextNodeKeys = new[] { "route" } }),
            Node("route", "control.route", new { nextNodeKeys = new[] { "skip" }, routePolicy = new { modeKey = "rules", targets = new[] { new { targetKey = "forward", nodeKey = "target", regionKey = "forward", maximumIterations = 1, condition = new { fieldKey = "amount", @operator = "greaterThan", value = 0 } } } } }),
            Node("skip", "control.empty", new { nextNodeKeys = new[] { "target" } }), Node("target", "control.empty", new { nextNodeKeys = new[] { "end" } }), Node("end", "end", new { })]);
        var root = await StartAsync(client, version); await DrainAsync(factory);
        Assert.AreEqual("completed", await StatusAsync(db, root));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@root AND NodeKey='skip'", new { root }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_execution_epoch WHERE InstanceId=@root", new { root }));
    }

    private static async Task VerifyNestedScopeAsync(FullNetApiFactory factory, DbConnection db, HttpClient client)
    {
        var created = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "nested-route." + Guid.NewGuid().ToString("N"), draft = new {
            schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields = new[] {
                new { fieldKey = "outer", fieldTypeKey = "integer", required = false, constraints = new { } },
                new { fieldKey = "inner", fieldTypeKey = "integer", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
        var form = (await PostAsync(client, $"/api/v1/workflow/forms/{created.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        object Route(string key, string next, string entry, string field, string region) => Node(key, "control.route", new { nextNodeKeys = new[] { next },
            routePolicy = new { modeKey = "rules", targets = new[] { new { targetKey = "retry", nodeKey = entry, regionKey = region, maximumIterations = 2,
                condition = new { fieldKey = field, @operator = "greaterThan", value = 0 } } } } });
        var version = await PublishNodesAsync(client, form, [Node("start", "start", new { nextNodeKeys = new[] { "outerReview" } }),
            Node("outerReview", "human.approval", new { nextNodeKeys = new[] { "innerReview" }, fieldPolicies = new { outer = "editable", inner = "editable" } }),
            Node("innerReview", "human.approval", new { nextNodeKeys = new[] { "innerRoute" }, fieldPolicies = new { outer = "editable", inner = "editable" } }),
            Route("innerRoute", "outerRoute", "innerReview", "inner", "inner-round"), Route("outerRoute", "end", "outerReview", "outer", "outer-round"), Node("end", "end", new { })]);
        var root = (await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "route.nested", businessId = Guid.NewGuid().ToString("N"),
            initialValues = new { outer = 1, inner = 1 }, idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        async Task Approve(string node, object patch, string key)
        {
            var todo = await TodoAsync(db, root, node); Assert.AreNotEqual(Guid.Empty, todo);
            var runtime = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime");
            await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/approve", new { expectedRevision = runtime.GetProperty("revision").GetInt64(),
                expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(), fieldPatch = patch, idempotencyKey = key });
            await DrainAsync(factory);
        }
        await Approve("outerReview", new { }, "outer-first");
        await Approve("innerReview", new { inner = 1 }, "inner-first");
        await Approve("innerReview", new { inner = 0 }, "inner-exit");
        await Approve("outerReview", new { outer = 0, inner = 1 }, "outer-second");
        await Approve("innerReview", new { inner = 1 }, "nested-inner-first");
        await Approve("innerReview", new { inner = 0 }, "nested-inner-exit");
        Assert.AreEqual("completed", await StatusAsync(db, root));
        var epochs = (await db.QueryAsync<NestedEpoch>("SELECT Id,ParentEpochId,RegionKey,Depth FROM fn_workflow_execution_epoch WHERE InstanceId=@root AND Id<>InstanceId", new { root })).ToArray();
        Assert.AreEqual(3, epochs.Length);
        var firstInner = epochs.Single(epoch => epoch.RegionKey == "inner-round" && epoch.Depth == 1);
        var outer = epochs.Single(epoch => epoch.RegionKey == "outer-round"); var nested = epochs.Single(epoch => epoch.RegionKey == "inner-round" && epoch.Depth == 2);
        Assert.AreEqual(root, firstInner.ParentEpochId); Assert.AreEqual(root, outer.ParentEpochId); Assert.AreEqual(outer.Id, nested.ParentEpochId);
        Assert.AreEqual(4, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_execution_epoch WHERE InstanceId=@root AND StatusKey='closed'", new { root }));
        Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@root AND NodeKey='outerReview'", new { root }));
        Assert.AreEqual(4, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@root AND NodeKey='innerReview'", new { root }));
        await WorkflowCausalityAssertions.CompleteRootAsync(db, root);
    }

    private sealed record NestedEpoch(Guid Id, Guid ParentEpochId, string RegionKey, int Depth);

    private static Task<Guid> PublishLoopAsync(HttpClient client, Guid form, bool parallel = false, bool decisionField = false, int maximum = 3)
    {
        object target = decisionField ? new { targetKey = "retry", nodeKey = "review", regionKey = "review-round", maximumIterations = maximum } :
            new { targetKey = "retry", nodeKey = "review", regionKey = "review-round", maximumIterations = maximum, condition = new { fieldKey = "amount", @operator = "greaterThan", value = 0 } };
        object policy = decisionField ? new { modeKey = "decision_field", decisionFieldKey = "choice", targets = new[] { target } } : new { modeKey = "rules", targets = new[] { target } };
        var nodes = new List<object> { Node("start", "start", new { nextNodeKeys = new[] { parallel ? "fork" : "review" } }),
            Node("review", "human.approval", new { nextNodeKeys = new[] { "route" }, fieldPolicies = new { amount = "editable", choice = "editable" } }),
            Node("route", "control.route", new { nextNodeKeys = new[] { parallel ? "join" : "end" }, routePolicy = policy }), Node("end", "end", new { }) };
        if (parallel) nodes.AddRange([Node("fork", "gateway.parallel", new { gatewayRoleKey = "fork", joinNodeKey = "join", branches = new[] { new { branchKey = "left", nextNodeKey = "review" }, new { branchKey = "right", nextNodeKey = "right" } } }),
            Node("right", "human.approval", new { nextNodeKeys = new[] { "join" }, fieldPolicies = new { amount = "readOnly", choice = "readOnly" } }),
            Node("join", "gateway.parallel", new { gatewayRoleKey = "join", forkNodeKey = "fork", nextNodeKeys = new[] { "end" } })]);
        return PublishNodesAsync(client, form, nodes.ToArray());
    }
    private static async Task<Guid> StartAsync(HttpClient client, Guid version) => (await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version,
        businessType = "route.test", businessId = Guid.NewGuid().ToString("N"), initialValues = new { amount = 1 }, idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
    private static async Task ApproveAsync(HttpClient client, Guid todo, int? amount, string key)
    {
        var runtime = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime");
        await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/approve", new { expectedRevision = runtime.GetProperty("revision").GetInt64(),
            expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(), fieldPatch = amount is null ? new Dictionary<string, int>() : new Dictionary<string, int> { ["amount"] = amount.Value }, idempotencyKey = key });
    }
    private static object Node(string key, string type, object config) => new { nodeKey = key, nodeTypeKey = type, nodeSchemaVersion = 1, config };
    private static Task<Guid> TodoAsync(DbConnection db, Guid root, string node) => db.ExecuteScalarAsync<Guid>("SELECT todo.Id FROM fn_workflow_todo AS todo INNER JOIN fn_workflow_step AS step ON step.Id=todo.StepId WHERE todo.InstanceId=@root AND step.NodeKey=@node AND todo.StatusKey='active'", new { root, node });
    private static Task<string?> StatusAsync(DbConnection db, Guid root) => db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@root", new { root });
    private static async Task<int> WorkAsync(FullNetApiFactory factory) { await using var scope = factory.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default); }
    private static async Task DrainAsync(FullNetApiFactory factory) { for (var count = 0; count < 16; count++) if (await WorkAsync(factory) == 0) return; Assert.Fail("路由工作未在有界批次内收敛"); }
    private static async Task VerifyRecoveryAsync(DatabaseProvider provider, string connection, DbConnection db)
    {
        var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_route_decision");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "ALTER TABLE fn_workflow_activity_work DROP CONSTRAINT fk_fn_workflow_activity_work_route" : "ALTER TABLE fn_workflow_activity_work DROP FOREIGN KEY fk_fn_workflow_activity_work_route");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DROP INDEX ux_fn_workflow_route_decision_work ON fn_workflow_route_decision" : "ALTER TABLE fn_workflow_route_decision DROP INDEX ux_fn_workflow_route_decision_work");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%328_WorkflowRouteDecisions.sql'" : "DELETE FROM schemaversions WHERE ScriptName LIKE '%328_WorkflowRouteDecisions.sql'");
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection); await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection);
        Assert.AreEqual(count, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_route_decision"));
    }
}
