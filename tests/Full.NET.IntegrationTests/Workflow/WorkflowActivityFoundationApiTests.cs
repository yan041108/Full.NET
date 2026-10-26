using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Abstractions.Tenancy;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>聚焦活动基础的真实双库验收：续办、租约、生命周期、原始回执及分支合并。</summary>
[TestClass]
public sealed class WorkflowActivityFoundationApiTests
{
    [TestMethod]
    public async Task Activity_foundation_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer,
        await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

    [TestMethod]
    public async Task Activity_foundation_with_mysql() => await VerifyAsync(DatabaseProvider.MySql,
        await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider, string connectionString)
    {
        using var factory = new FullNetApiFactory(provider, connectionString);
        // 完整迁移保留双库外键；MySQL 的 CREATE TABLE LIKE 模板不具备外键等价性。
        await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection connection = provider == DatabaseProvider.SqlServer
            ? new SqlConnection(connectionString)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connectionString));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(provider == DatabaseProvider.SqlServer
            ? "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('dbo.fn_workflow_countersign_item') AND referenced_object_id=OBJECT_ID('dbo.fn_workflow_todo')"
            : "SELECT COUNT(*) FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_countersign_item' AND REFERENCED_TABLE_NAME='fn_workflow_todo'"));
        // 模拟部分 DDL 已成功但 DbUp 尚未记账，重跑必须补齐缺失的最后一张表。
        await connection.ExecuteAsync("DROP TABLE fn_workflow_branch_merge");
        await connection.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%288_WorkflowActivityFoundation.sql'"
            : "DELETE FROM schemaversions WHERE ScriptName LIKE '%288_WorkflowActivityFoundation.sql'");
        var recovery = provider == DatabaseProvider.SqlServer
            ? Migration114To126RecoveryTestSupport.MigrateSqlServer(connectionString, 288)
            : Migration114To126RecoveryTestSupport.MigrateMySql(connectionString, 288);
        Assert.IsTrue(recovery.Successful, recovery.Error?.Message);
        await connection.ExecuteAsync("DROP TABLE fn_workflow_todo_view");
        await connection.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%290_WorkflowHumanViews.sql'"
            : "DELETE FROM schemaversions WHERE ScriptName LIKE '%290_WorkflowHumanViews.sql'");
        var viewRecovery = provider == DatabaseProvider.SqlServer
            ? Migration114To126RecoveryTestSupport.MigrateSqlServer(connectionString, 290)
            : Migration114To126RecoveryTestSupport.MigrateMySql(connectionString, 290);
        Assert.IsTrue(viewRecovery.Successful, viewRecovery.Error?.Message);
        await VerifyActivationIndexRecoveryAsync(provider, connectionString, connection);
        await VerifyAssigneeSnapshotRecoveryAsync(provider, connectionString, connection);
        using var client = factory.CreateClientForHost("localhost");
        var identity = await factory.CreateHostIdentityAsync("activity-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate,
                WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesRead,
                WorkflowPermissions.InstancesPause, WorkflowPermissions.InstancesResume, WorkflowPermissions.InstancesCancel,
                WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, WorkflowPermissions.TodosReject,
                WorkflowPermissions.DefinitionsRead, WorkflowPermissions.TodosCountersign, WorkflowPermissions.TodosReturn, WorkflowPermissions.TodosSign,
                "tenancy.tenants.read", "tenancy.tenants.switch"]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken);
        var catalog = await client.GetFromJsonAsync<JsonElement>("/api/v1/workflow/definitions/node-type-catalog");
        Assert.AreEqual(16, catalog.GetProperty("catalogVersion").GetInt32());
        var inclusiveCapability = catalog.GetProperty("nodeTypes").EnumerateArray().Single(node => node.GetProperty("nodeTypeKey").GetString() == "gateway.inclusive");
        Assert.AreEqual(8, inclusiveCapability.GetProperty("maximumBranches").GetInt32());
        var form = await PostAsync(client, "/api/v1/workflow/forms", new
        {
            formKey = "activity." + Guid.NewGuid().ToString("N"),
            draft = new { schemaVersion = 1, adapterVersion = 1, sections = new[] { new {
                sectionKey = "main", fields = new[] {
                    new { fieldKey = "first", fieldTypeKey = "text", required = false, constraints = new { } },
                    new { fieldKey = "second", fieldTypeKey = "text", required = false, constraints = new { } },
                } } } },
        }, HttpStatusCode.Created);
        var formVersion = await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish",
            new { expectedRevision = 1 });
        var formVersionId = formVersion.GetProperty("id").GetGuid();
        var nodes = new List<object> { Node("start", "start", new { nextNodeKeys = new[] { "copy0" } }) };
        for (var index = 0; index < 35; index++)
            nodes.Add(Node("copy" + index, "notify.cc", new { nextNodeKeys = new[] { index == 34 ? "end" : "copy" + (index + 1) },
                recipientUserIds = new[] { identity.UserId } }));
        nodes.Add(Node("end", "end", new { }));
        var version = await PublishAsync(client, formVersionId, nodes, "automation");
        object StartRequest(string key) => new { definitionVersionId = version, businessType = "activity.foundation",
            businessId = key, initialValues = new { first = "initial", second = "initial" }, idempotencyKey = "start-" + key };
        var request = StartRequest("continue");
        var started = await PostAsync(client, "/api/v1/workflow/instances", request, HttpStatusCode.Created);
        Assert.AreEqual("active", started.GetProperty("statusKey").GetString());
        Assert.AreEqual(JsonValueKind.Null, started.GetProperty("activeTodoId").ValueKind);
        var instanceId = started.GetProperty("id").GetGuid();
        Assert.AreEqual(32, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@instanceId", new { instanceId }));
        Assert.AreEqual(1, await ProcessAsync(factory));
        Assert.AreEqual("completed", await connection.ExecuteScalarAsync<string>(
            "SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instanceId", new { instanceId }));
        Assert.AreEqual(35, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@instanceId", new { instanceId }));
        var replay = await PostAsync(client, "/api/v1/workflow/instances", request, HttpStatusCode.Created);
        Assert.AreEqual(started.GetRawText(), replay.GetRawText());
        Assert.AreEqual(0, await ProcessAsync(factory));

        var reclaimed = await PostAsync(client, "/api/v1/workflow/instances", StartRequest("reclaimed"), HttpStatusCode.Created);
        var reclaimedId = reclaimed.GetProperty("id").GetGuid();
        await connection.ExecuteAsync("UPDATE fn_workflow_activity_work SET StatusKey='leased', LeaseGeneration=1, LeaseOwnerKey='expired-owner', LeaseExpiresAtUtc=@past WHERE InstanceId=@reclaimedId",
            new { reclaimedId, past = DateTime.UtcNow.AddMinutes(-1) });
        Assert.AreEqual(1, await ProcessAsync(factory));
        var workId = await connection.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_activity_work WHERE InstanceId=@reclaimedId", new { reclaimedId });
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var staleResult = await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(
                WorkflowActivitySql.FinishWork, WorkflowSqlParameters.Create(("Id", workId), ("LeaseOwnerKey", "expired-owner"),
                    ("LeaseGeneration", 1L), ("StatusKey", "completed"), ("ResultStatusKey", "invalid"),
                    ("ResultTodoId", null), ("Now", DateTimeOffset.UtcNow)));
            Assert.AreEqual(0, staleResult);
        }
        Assert.AreEqual(2, await connection.ExecuteScalarAsync<long>("SELECT LeaseGeneration FROM fn_workflow_activity_work WHERE Id=@workId", new { workId }));

        var paused = await PostAsync(client, "/api/v1/workflow/instances", StartRequest("paused"), HttpStatusCode.Created);
        var pausedId = paused.GetProperty("id").GetGuid();
        await PostAsync(client, $"/api/v1/workflow/instances/{pausedId:D}/pause", new { expectedRevision = 1, idempotencyKey = "pause" });
        Assert.AreEqual(0, await ProcessAsync(factory));
        await PostAsync(client, $"/api/v1/workflow/instances/{pausedId:D}/resume", new { expectedRevision = 2, idempotencyKey = "resume" });
        Assert.AreEqual(1, await ProcessAsync(factory));

        var cancelled = await PostAsync(client, "/api/v1/workflow/instances", StartRequest("cancelled"), HttpStatusCode.Created);
        var cancelledId = cancelled.GetProperty("id").GetGuid();
        await PostAsync(client, $"/api/v1/workflow/instances/{cancelledId:D}/cancel",
            new { expectedRevision = 1, reason = "取消续办", idempotencyKey = "cancel" });
        Assert.AreEqual(0, await ProcessAsync(factory));
        Assert.AreEqual("cancelled", await connection.ExecuteScalarAsync<string>(
            "SELECT StatusKey FROM fn_workflow_activity_work WHERE InstanceId=@cancelledId", new { cancelledId }));
        await VerifyBranchMergeAsync(client, formVersionId, connection, factory, false);
        await VerifyBranchMergeAsync(client, formVersionId, connection, factory, true);
        await VerifyInclusiveTraceAsync(client, formVersionId, identity.UserId, connection);
        await VerifyRejectWithQueuedWorkAsync(client, formVersionId, identity.UserId, factory, connection);
        await VerifyEmptyConditionalAsync(client, formVersionId, connection);
        var hostSignatureId = await VerifySignaturePolicyAsync(client, formVersionId, connection, factory, provider, connectionString);
        await VerifyAttachmentViewsAsync(factory, connection);
        await VerifyTodoReceiptBindingAsync(client, formVersionId, connection);
        await VerifyCountersignsAsync(factory, client, formVersionId, identity.UserId, connection);
        await VerifyCountersignCandidateOwnershipAsync(factory, client, formVersionId);
        await VerifyInitiatorRevalidationAsync(factory, client, formVersionId, connection);
        await VerifyOrganizationAssigneesAsync(factory, connection);
        await VerifyTenantSignatureAsync(client, connection, identity.UserId, hostSignatureId);
    }

    private static async Task VerifyInitiatorRevalidationAsync(FullNetApiFactory factory, HttpClient designer,
        Guid formVersionId, DbConnection connection)
    {
        var initiator = await factory.CreateHostIdentityAsync("queued-initiator-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.InstancesStart]);
        using var client = factory.CreateClientForHost("localhost");
        client.DefaultRequestHeaders.Authorization = new("Bearer", initiator.AccessToken);
        var nodes = new List<object> { Node("start", "start", new { nextNodeKeys = new[] { "empty0" } }) };
        for (var index = 0; index < 35; index++)
            nodes.Add(Node("empty" + index, "control.empty", new { nextNodeKeys = new[] { index == 34 ? "review" : "empty" + (index + 1) } }));
        nodes.Add(Node("review", "human.approval", new { nextNodeKeys = new[] { "end" } }));
        nodes.Add(Node("end", "end", new { }));
        var version = await PublishAsync(designer, formVersionId, nodes, "approval");
        var started = await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version,
            businessType = "activity.initiator", businessId = "revalidate", initialValues = new { }, idempotencyKey = "queued-initiator" }, HttpStatusCode.Created);
        var instanceId = started.GetProperty("id").GetGuid();
        Assert.AreEqual(JsonValueKind.Null, started.GetProperty("activeTodoId").ValueKind);
        await connection.ExecuteAsync("UPDATE fn_identity_user SET IsActive=0 WHERE Id=@Id", new { Id = initiator.UserId });
        Assert.AreEqual(1, await ProcessAsync(factory));
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@instanceId", new { instanceId }));
        Assert.AreEqual(32, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@instanceId", new { instanceId }));
        // 失败推进回滚，身份重新有效后以同一工作项/输入重领，不能把 Worker 当前身份当作发起人。
        await connection.ExecuteAsync("UPDATE fn_identity_user SET IsActive=1 WHERE Id=@Id", new { Id = initiator.UserId });
        await connection.ExecuteAsync("UPDATE fn_workflow_activity_work SET LeaseExpiresAtUtc=@past WHERE InstanceId=@instanceId",
            new { instanceId, past = DateTime.UtcNow.AddMinutes(-1) });
        Assert.AreEqual(1, await ProcessAsync(factory));
        Assert.AreEqual(initiator.UserId, await connection.ExecuteScalarAsync<Guid>(
            "SELECT AssigneeUserId FROM fn_workflow_todo WHERE InstanceId=@instanceId AND StatusKey='active'", new { instanceId }));
    }

    private static async Task VerifyCountersignCandidateOwnershipAsync(FullNetApiFactory factory, HttpClient designer, Guid formVersionId)
    {
        var actor = await factory.CreateHostIdentityAsync("candidate-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, WorkflowPermissions.TodosCountersign]);
        using var client = factory.CreateClientForHost("localhost");
        client.DefaultRequestHeaders.Authorization = new("Bearer", actor.AccessToken);
        var version = await PublishAsync(designer, formVersionId, [
            Node("start", "start", new { nextNodeKeys = new[] { "review" } }),
            Node("review", "human.approval", new { nextNodeKeys = new[] { "end" },
                assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { actor.UserId } } } } }),
            Node("end", "end", new { }) ], "approval");
        var started = await PostAsync(designer, "/api/v1/workflow/instances", new { definitionVersionId = version,
            businessType = "activity.candidates", businessId = "owned", initialValues = new { }, idempotencyKey = "start-candidate" }, HttpStatusCode.Created);
        var todo = started.GetProperty("activeTodoId").GetGuid();
        using var definitions = await client.GetAsync("/api/v1/workflow/definitions/recipient-candidates");
        Assert.AreEqual(HttpStatusCode.Forbidden, definitions.StatusCode);
        using var candidates = await client.GetAsync($"/api/v1/workflow/todos/{todo:D}/countersign-candidates");
        Assert.AreEqual(HttpStatusCode.OK, candidates.StatusCode, await candidates.Content.ReadAsStringAsync());
        using var foreign = await designer.GetAsync($"/api/v1/workflow/todos/{todo:D}/countersign-candidates");
        Assert.AreEqual(HttpStatusCode.Forbidden, foreign.StatusCode);
        await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/approve", new {
            expectedRevision = 1, expectedFormRevision = 1, fieldPatch = new { }, idempotencyKey = "candidate-approve" });
        using var completed = await client.GetAsync($"/api/v1/workflow/todos/{todo:D}/countersign-candidates");
        Assert.AreEqual(HttpStatusCode.Conflict, completed.StatusCode);
    }

    private static async Task VerifyOrganizationAssigneesAsync(FullNetApiFactory factory, DbConnection connection)
    {
        using var client = factory.CreateClientForHost("localhost");
        client.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var login = await PostAsync(client, "/api/v1/auth/login", new LoginRequest("admin", FullNetApiFactory.TestPassword));
        client.DefaultRequestHeaders.Authorization = new("Bearer", login.GetProperty("accessToken").GetString());
        var available = await client.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available");
        var tenant = available!.Single(x => x.Identifier == "acme");
        using var context = await client.PutAsJsonAsync("/api/v1/tenancy/context", new ChangeTenantContextRequest(tenant.Id));
        Assert.AreEqual(HttpStatusCode.OK, context.StatusCode);
        var token = await context.Content.ReadFromJsonAsync<TenantContextTokenResponse>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token!.AccessToken);
        var candidates = await client.GetFromJsonAsync<JsonElement>("/api/v1/organization/user-positions/assignable-users?page=1&pageSize=100");
        var actor = candidates.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("username").GetString() == "admin").GetProperty("id").GetGuid();
        var parent = await PostAsync(client, "/api/v1/organization/units", new { parentId = (Guid?)null,
            code = "wf-root-" + Guid.NewGuid().ToString("N")[..12], name = "流程上级", displayOrder = 1 }, HttpStatusCode.Created);
        var rootId = parent.GetProperty("id").GetGuid();
        var unit = await PostAsync(client, "/api/v1/organization/units", new { parentId = rootId,
            code = "wf-unit-" + Guid.NewGuid().ToString("N")[..12], name = "流程部门", displayOrder = 1 }, HttpStatusCode.Created);
        var unitId = unit.GetProperty("id").GetGuid();
        foreach (var configured in new[] { parent, unit })
        {
            using var leadership = await client.PutAsJsonAsync($"/api/v1/organization/units/{configured.GetProperty("id").GetGuid():D}/leadership",
                new { leaderUserId = actor, expectedUnitVersion = configured.GetProperty("version").GetInt32(), expectedRevision = 0 });
            Assert.AreEqual(HttpStatusCode.OK, leadership.StatusCode, await leadership.Content.ReadAsStringAsync());
        }
        await PostAsync(client, "/api/v1/organization/user-units", new { userId = actor, unitId = rootId, isPrimary = false }, HttpStatusCode.Created);
        await PostAsync(client, "/api/v1/organization/user-units", new { userId = actor, unitId, isPrimary = true }, HttpStatusCode.Created);
        var position = await PostAsync(client, "/api/v1/organization/positions", new {
            code = "wf-pos-" + Guid.NewGuid().ToString("N")[..12], name = "流程岗位", displayOrder = 1 }, HttpStatusCode.Created);
        var positionId = position.GetProperty("id").GetGuid();
        await PostAsync(client, "/api/v1/organization/user-positions", new { userId = actor, positionId, isPrimary = true }, HttpStatusCode.Created);
        var positions = await client.GetFromJsonAsync<JsonElement>("/api/v1/workflow/definitions/position-candidates?page=1&pageSize=100");
        Assert.IsTrue(positions.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == positionId));
        var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "organization." + Guid.NewGuid().ToString("N"),
            draft = new { schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields = new[] {
                new { fieldKey = "first", fieldTypeKey = "text", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
        var formVersion = await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 });
        var version = await PublishAsync(client, formVersion.GetProperty("id").GetGuid(), [
            Node("start", "start", new { nextNodeKeys = new[] { "review" } }),
            Node("review", "human.approval", new { nextNodeKeys = new[] { "end" },
                approvalPolicy = new { modeKey = "all", approverUserIds = Array.Empty<Guid>() },
                assigneePolicy = new { sources = new object[] {
                    new { resolverKindKey = "position_members", positionIds = new[] { positionId } },
                    new { resolverKindKey = "organization_unit_members", unitId },
                    new { resolverKindKey = "initiator_leader_chain", maximumLevels = 2 } } } }),
            Node("end", "end", new { }) ], "approval");
        var started = await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version,
            businessType = "activity.organization", businessId = "frozen", initialValues = new { }, idempotencyKey = "org-start" }, HttpStatusCode.Created);
        var instanceId = started.GetProperty("id").GetGuid();
        await VerifyAssigneeSnapshotAsync(connection, instanceId, actor);
        var frozen = await connection.ExecuteScalarAsync<string>("SELECT AssigneeSnapshotJson FROM fn_workflow_step WHERE InstanceId=@instanceId", new { instanceId });
        using var snapshot = JsonDocument.Parse(frozen!);
        Assert.AreEqual(3, snapshot.RootElement.GetProperty("sources").GetArrayLength());
        Assert.AreEqual(2, snapshot.RootElement.GetProperty("sources")[2].GetProperty("memberships").GetArrayLength());
        await PostAsync(client, $"/api/v1/organization/positions/{positionId:D}/disable", new { });
        var todo = started.GetProperty("activeTodoId").GetGuid();
        using var detail = await client.GetAsync($"/api/v1/workflow/todos/{todo:D}/runtime");
        Assert.AreEqual(HttpStatusCode.OK, detail.StatusCode);
        Assert.AreEqual(frozen, await connection.ExecuteScalarAsync<string>("SELECT AssigneeSnapshotJson FROM fn_workflow_step WHERE InstanceId=@instanceId", new { instanceId }));
        using var later = await client.PostAsJsonAsync("/api/v1/workflow/instances", new { definitionVersionId = version,
            businessType = "activity.organization", businessId = "inactive", initialValues = new { }, idempotencyKey = "org-inactive" });
        Assert.AreEqual(HttpStatusCode.BadRequest, later.StatusCode);
        await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/approve", new {
            expectedRevision = 1, expectedFormRevision = 1, fieldPatch = new { }, idempotencyKey = "org-approve" });
    }

    private static async Task VerifyActivationIndexRecoveryAsync(DatabaseProvider provider, string connectionString, DbConnection connection)
    {
        // 保留 MySQL 已生成的占用列，但移除最后一个索引步骤并恢复旧约束，模拟未记账的半完成 DDL。
        await connection.ExecuteAsync("CREATE UNIQUE INDEX UX_fn_workflow_todo_Step_Assignee ON fn_workflow_todo(StepId, AssigneeUserId)");
        await connection.ExecuteAsync("DROP INDEX UX_fn_workflow_todo_Active_Assignee ON fn_workflow_todo");
        await connection.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%294_WorkflowTodoActivationUniqueness.sql'"
            : "DELETE FROM schemaversions WHERE ScriptName LIKE '%294_WorkflowTodoActivationUniqueness.sql'");
        var recovery = provider == DatabaseProvider.SqlServer
            ? Migration114To126RecoveryTestSupport.MigrateSqlServer(connectionString, 294)
            : Migration114To126RecoveryTestSupport.MigrateMySql(connectionString, 294);
        Assert.IsTrue(recovery.Successful, recovery.Error?.Message);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(provider == DatabaseProvider.SqlServer
            ? "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.fn_workflow_todo') AND name='UX_fn_workflow_todo_Active_Assignee' AND is_unique=1 AND has_filter=1"
            : "SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo' AND INDEX_NAME='UX_fn_workflow_todo_Active_Assignee' AND COLUMN_NAME='ActiveAssigneeUserId' AND NON_UNIQUE=0"));
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(provider == DatabaseProvider.SqlServer
            ? "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.fn_workflow_todo') AND name='UX_fn_workflow_todo_Step_Assignee'"
            : "SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo' AND INDEX_NAME='UX_fn_workflow_todo_Step_Assignee'"));
    }

    private static async Task VerifyAssigneeSnapshotRecoveryAsync(DatabaseProvider provider, string connectionString, DbConnection connection)
    {
        // 保留已创建的 JSON 列，模拟摘要列与约束尚未完成；重跑不能要求丢弃已有事实。
        await connection.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "ALTER TABLE fn_workflow_step DROP CONSTRAINT CK_fn_workflow_step_AssigneeSnapshot"
            : "ALTER TABLE fn_workflow_step DROP CHECK CK_fn_workflow_step_AssigneeSnapshot");
        await connection.ExecuteAsync("ALTER TABLE fn_workflow_step DROP COLUMN AssigneeSnapshotHash");
        await connection.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%295_WorkflowAssigneeResolution.sql'"
            : "DELETE FROM schemaversions WHERE ScriptName LIKE '%295_WorkflowAssigneeResolution.sql'");
        var recovery = provider == DatabaseProvider.SqlServer
            ? Migration114To126RecoveryTestSupport.MigrateSqlServer(connectionString, 295)
            : Migration114To126RecoveryTestSupport.MigrateMySql(connectionString, 295);
        Assert.IsTrue(recovery.Successful, recovery.Error?.Message);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(provider == DatabaseProvider.SqlServer
            ? "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('dbo.fn_workflow_step') AND name='CK_fn_workflow_step_AssigneeSnapshot'"
            : "SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_step' AND CONSTRAINT_NAME='CK_fn_workflow_step_AssigneeSnapshot'"));
    }

    private static async Task VerifyAssigneeSnapshotAsync(DbConnection connection, Guid instanceId, Guid expectedUser)
    {
        var json = await connection.ExecuteScalarAsync<string>("SELECT AssigneeSnapshotJson FROM fn_workflow_step WHERE InstanceId=@instanceId AND NodeKey='review'", new { instanceId });
        var hash = await connection.ExecuteScalarAsync<string>("SELECT AssigneeSnapshotHash FROM fn_workflow_step WHERE InstanceId=@instanceId AND NodeKey='review'", new { instanceId });
        Assert.IsNotNull(json);
        Assert.AreEqual(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))), hash);
        using var document = JsonDocument.Parse(json);
        var members = document.RootElement.GetProperty("memberUserIds").EnumerateArray().Select(x => x.GetGuid()).ToArray();
        CollectionAssert.AreEqual(new[] { expectedUser }, members);
        var slots = (await connection.QueryAsync<Guid>("SELECT AssigneeUserId FROM fn_workflow_approval_slot WHERE InstanceId=@instanceId", new { instanceId })).ToArray();
        CollectionAssert.AreEquivalent(members, slots);
    }

    private static async Task VerifyCountersignsAsync(FullNetApiFactory factory, HttpClient originClient,
        Guid formVersionId, Guid originActorId, DbConnection connection)
    {
        var signer = await factory.CreateHostIdentityAsync("chain-signer-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, WorkflowPermissions.TodosReject]);
        var nextActor = await factory.CreateHostIdentityAsync("chain-next-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove]);
        using var signerClient = factory.CreateClientForHost("localhost");
        using var nextClient = factory.CreateClientForHost("localhost");
        signerClient.DefaultRequestHeaders.Authorization = new("Bearer", signer.AccessToken);
        nextClient.DefaultRequestHeaders.Authorization = new("Bearer", nextActor.AccessToken);
        var version = await PublishAsync(originClient, formVersionId, [
            Node("start", "start", new { nextNodeKeys = new[] { "review" } }),
            Node("review", "human.approval", new { nextNodeKeys = new[] { "next" },
                assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { originActorId } } } } }),
            Node("next", "human.approval", new { nextNodeKeys = new[] { "end" },
                assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { nextActor.UserId } } } } }),
            Node("end", "end", new { }),
        ], "approval");
        foreach (var scenario in new[] { "before", "after", "cancel" })
        {
            var started = await PostAsync(originClient, "/api/v1/workflow/instances", new { definitionVersionId = version,
                businessType = "activity.countersign", businessId = scenario, initialValues = new { }, idempotencyKey = "chain-start-" + scenario }, HttpStatusCode.Created);
            var instanceId = started.GetProperty("id").GetGuid();
            var originTodo = started.GetProperty("activeTodoId").GetGuid();
            await VerifyAssigneeSnapshotAsync(connection, instanceId, originActorId);
            var chainRequest = new { directionKey = scenario == "after" ? "after" : "before", assigneeUserIds = new[] { signer.UserId },
                expectedRevision = 1, comment = "请协助核对", idempotencyKey = "chain-config-" + scenario };
            var configured = await PostAsync(originClient, $"/api/v1/workflow/todos/{originTodo:D}/countersign", chainRequest);
            object Act(long revision, long formRevision, string key) => new { expectedRevision = revision,
                expectedFormRevision = formRevision, fieldPatch = new { }, comment = "已核对", idempotencyKey = key };
            JsonElement signerResult = default;
            JsonElement originResult;
            Guid signerTodo = Guid.Empty;
            if (scenario == "cancel")
            {
                signerTodo = configured.GetProperty("activeTodoId").GetGuid();
                await PostAsync(originClient, $"/api/v1/workflow/todos/{originTodo:D}/countersign/cancel",
                    new { expectedRevision = 1, comment = "取消", idempotencyKey = "stale-cancel" }, HttpStatusCode.Conflict);
                var cancelRequest = new { expectedRevision = 2, comment = "取消", idempotencyKey = "chain-cancel" };
                var cancelled = await PostAsync(originClient, $"/api/v1/workflow/todos/{originTodo:D}/countersign/cancel", cancelRequest);
                var replay = await PostAsync(originClient, $"/api/v1/workflow/todos/{originTodo:D}/countersign/cancel", cancelRequest);
                Assert.AreEqual(cancelled.GetRawText(), replay.GetRawText());
                var resumedTodo = cancelled.GetProperty("activeTodoId").GetGuid();
                Assert.AreNotEqual(originTodo, resumedTodo);
                originResult = await PostAsync(originClient, $"/api/v1/workflow/todos/{resumedTodo:D}/approve", Act(1, 1, "chain-origin-cancel"));
                Assert.AreEqual("cancelled", await connection.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_todo WHERE Id=@signerTodo", new { signerTodo }));
            }
            else if (scenario == "before")
            {
                signerTodo = configured.GetProperty("activeTodoId").GetGuid();
                signerResult = await PostAsync(signerClient, $"/api/v1/workflow/todos/{signerTodo:D}/approve", Act(1, 1, "chain-sign-before"));
                var resumedTodo = signerResult.GetProperty("activeTodoId").GetGuid();
                Assert.AreNotEqual(originTodo, resumedTodo);
                originResult = await PostAsync(originClient, $"/api/v1/workflow/todos/{resumedTodo:D}/approve", Act(1, 2, "chain-origin-before"));
            }
            else
            {
                originResult = await PostAsync(originClient, $"/api/v1/workflow/todos/{originTodo:D}/approve", Act(1, 1, "chain-origin-after"));
                signerTodo = originResult.GetProperty("activeTodoId").GetGuid();
                signerResult = await PostAsync(signerClient, $"/api/v1/workflow/todos/{signerTodo:D}/approve", Act(1, 2, "chain-sign-after"));
            }
            var nextTodo = (scenario == "after" ? signerResult : originResult).GetProperty("activeTodoId").GetGuid();
            var detail = await nextClient.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{nextTodo:D}");
            Assert.AreEqual(nextActor.UserId, detail.GetProperty("assigneeUserId").GetGuid());
            var completed = await PostAsync(nextClient, $"/api/v1/workflow/todos/{nextTodo:D}/approve",
                Act(1, scenario == "cancel" ? 2 : 3, "chain-next-" + scenario));
            Assert.AreEqual("completed", completed.GetProperty("statusKey").GetString());
            Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId=@instanceId AND DecisionKey IS NULL", new { instanceId }));
            var configReplay = await PostAsync(originClient, $"/api/v1/workflow/todos/{originTodo:D}/countersign", chainRequest);
            Assert.AreEqual(configured.GetRawText(), configReplay.GetRawText());
            if (scenario != "cancel")
            {
                var signerReplay = await PostAsync(signerClient, $"/api/v1/workflow/todos/{signerTodo:D}/approve",
                    Act(1, scenario == "after" ? 2 : 1, "chain-sign-" + scenario));
                Assert.AreEqual(signerResult.GetRawText(), signerReplay.GetRawText());
            }
        }
        await VerifyCountersignSequenceAsync(originClient, signerClient, nextClient, formVersionId,
            signer.UserId, nextActor.UserId, connection);
        await VerifyPercentageCohortAsync(originClient, signerClient, nextClient, formVersionId,
            originActorId, signer.UserId, nextActor.UserId, connection);
        await VerifyCountersignTerminalClosureAsync(originClient, signerClient, version, signer.UserId, connection);
    }

    private static async Task VerifyCountersignTerminalClosureAsync(HttpClient origin, HttpClient signer,
        Guid version, Guid signerId, DbConnection connection)
    {
        foreach (var direction in new[] { "before", "after" })
        foreach (var terminal in new[] { "cancel", "reject" })
        {
            var key = direction + "-" + terminal;
            var started = await PostAsync(origin, "/api/v1/workflow/instances", new { definitionVersionId = version,
                businessType = "activity.chain.terminal", businessId = key, initialValues = new { },
                idempotencyKey = "terminal-start-" + key }, HttpStatusCode.Created);
            var instanceId = started.GetProperty("id").GetGuid();
            var originTodo = started.GetProperty("activeTodoId").GetGuid();
            var configured = await PostAsync(origin, $"/api/v1/workflow/todos/{originTodo:D}/countersign", new {
                directionKey = direction, assigneeUserIds = new[] { signerId }, expectedRevision = 1, idempotencyKey = "terminal-chain-" + key });
            var waiting = configured;
            if (direction == "after") waiting = await PostAsync(origin, $"/api/v1/workflow/todos/{originTodo:D}/approve", new {
                expectedRevision = 1, expectedFormRevision = 1, fieldPatch = new { }, idempotencyKey = "terminal-origin-" + key });
            var supplemental = waiting.GetProperty("activeTodoId").GetGuid();
            var path = terminal == "cancel" ? $"/api/v1/workflow/instances/{instanceId:D}/cancel"
                : $"/api/v1/workflow/todos/{supplemental:D}/reject";
            object request = terminal == "cancel"
                ? new { expectedRevision = waiting.GetProperty("revision").GetInt64(), reason = "终态取消", idempotencyKey = "terminal-action-" + key }
                : new { expectedRevision = 1, expectedFormRevision = direction == "before" ? 1 : 2,
                    fieldPatch = new { }, comment = "核对未通过", idempotencyKey = "terminal-action-" + key };
            var actor = terminal == "cancel" ? origin : signer;
            var result = await PostAsync(actor, path, request);
            Assert.AreEqual(terminal == "cancel" ? "cancelled" : "rejected", result.GetProperty("statusKey").GetString());
            Assert.AreEqual(result.GetRawText(), (await PostAsync(actor, path, request)).GetRawText());
            Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@instanceId AND StatusKey IN ('active','before_countersign')", new { instanceId }));
            Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM fn_workflow_countersign_chain WHERE InstanceId=@instanceId AND StatusKey='active'", new { instanceId }));
            Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM fn_workflow_countersign_item AS item INNER JOIN fn_workflow_countersign_chain AS chain ON chain.Id=item.ChainId WHERE chain.InstanceId=@instanceId AND item.StatusKey IN ('active','pending')", new { instanceId }));
            await PostAsync(signer, $"/api/v1/workflow/todos/{supplemental:D}/approve", new { expectedRevision = 1,
                expectedFormRevision = 1, fieldPatch = new { }, idempotencyKey = "terminal-late-" + key }, HttpStatusCode.Conflict);
        }
    }

    private static async Task VerifyCountersignSequenceAsync(HttpClient origin, HttpClient first, HttpClient second,
        Guid formVersionId, Guid firstActorId, Guid secondActorId, DbConnection connection)
    {
        var version = await PublishAsync(origin, formVersionId, [
            Node("start", "start", new { nextNodeKeys = new[] { "review" } }),
            Node("review", "human.approval", new { nextNodeKeys = new[] { "end" } }),
            Node("end", "end", new { }),
        ], "approval");
        foreach (var direction in new[] { "before", "after" })
        {
            var started = await PostAsync(origin, "/api/v1/workflow/instances", new { definitionVersionId = version,
                businessType = "activity.countersign.sequence", businessId = direction, initialValues = new { },
                idempotencyKey = "sequence-start-" + direction }, HttpStatusCode.Created);
            var instanceId = started.GetProperty("id").GetGuid();
            var originTodo = started.GetProperty("activeTodoId").GetGuid();
            var config = new { directionKey = direction, assigneeUserIds = new[] { firstActorId, secondActorId },
                expectedRevision = 1, comment = "依次协助", idempotencyKey = "sequence-config-" + direction };
            var configured = await Task.WhenAll(
                PostAsync(origin, $"/api/v1/workflow/todos/{originTodo:D}/countersign", config),
                PostAsync(origin, $"/api/v1/workflow/todos/{originTodo:D}/countersign", config));
            Assert.AreEqual(configured[0].GetRawText(), configured[1].GetRawText());
            Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM fn_workflow_countersign_chain WHERE InstanceId=@instanceId", new { instanceId }));
            object Act(long revision, long formRevision, string actor) => new { expectedRevision = revision,
                expectedFormRevision = formRevision, fieldPatch = new { }, comment = "已核对",
                idempotencyKey = $"sequence-{direction}-{actor}" };
            var formRevision = 1L;
            var waiting = configured[0];
            if (direction == "after")
            {
                waiting = await PostAsync(origin, $"/api/v1/workflow/todos/{originTodo:D}/approve", Act(1, formRevision, "origin"));
                formRevision++;
            }
            var firstTodo = waiting.GetProperty("activeTodoId").GetGuid();
            var firstRequest = Act(1, formRevision++, "first");
            var firstResult = await PostAsync(first, $"/api/v1/workflow/todos/{firstTodo:D}/approve", firstRequest);
            var secondTodo = firstResult.GetProperty("activeTodoId").GetGuid();
            var secondRequest = Act(1, formRevision++, "second");
            var secondResult = await PostAsync(second, $"/api/v1/workflow/todos/{secondTodo:D}/approve", secondRequest);
            var completed = secondResult;
            if (direction == "before")
            {
                var resumedTodo = secondResult.GetProperty("activeTodoId").GetGuid();
                Assert.AreNotEqual(originTodo, resumedTodo);
                completed = await PostAsync(origin, $"/api/v1/workflow/todos/{resumedTodo:D}/approve", Act(1, formRevision, "origin"));
            }
            Assert.AreEqual("completed", completed.GetProperty("statusKey").GetString());
            Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM fn_workflow_countersign_item AS item INNER JOIN fn_workflow_countersign_chain AS chain ON chain.Id=item.ChainId WHERE chain.InstanceId=@instanceId AND item.StatusKey<>'completed'",
                new { instanceId }));
            var firstReplay = await PostAsync(first, $"/api/v1/workflow/todos/{firstTodo:D}/approve", firstRequest);
            var secondReplay = await PostAsync(second, $"/api/v1/workflow/todos/{secondTodo:D}/approve", secondRequest);
            Assert.AreEqual(firstResult.GetRawText(), firstReplay.GetRawText());
            Assert.AreEqual(secondResult.GetRawText(), secondReplay.GetRawText());
        }
    }

    private static async Task VerifyPercentageCohortAsync(HttpClient origin, HttpClient first, HttpClient second,
        Guid formVersionId, Guid originActorId, Guid firstActorId, Guid secondActorId, DbConnection connection)
    {
        var participants = new[] { originActorId, firstActorId, secondActorId };
        var version = await PublishAsync(origin, formVersionId, [
            Node("start", "start", new { nextNodeKeys = new[] { "review" } }),
            Node("review", "human.approval", new { nextNodeKeys = new[] { "end" },
                assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = participants } } },
                approvalPolicy = new { modeKey = "percent", approverUserIds = participants.Take(2).ToArray(), requiredPercentage = 67 } }),
            Node("end", "end", new { }),
        ], "approval");
        var started = await PostAsync(origin, "/api/v1/workflow/instances", new { definitionVersionId = version,
            businessType = "activity.approval.percent", businessId = "cohort", initialValues = new { },
            idempotencyKey = "percent-cohort" }, HttpStatusCode.Created);
        var instanceId = started.GetProperty("id").GetGuid();
        Assert.AreEqual(3, await connection.ExecuteScalarAsync<int>(
            "SELECT RequiredApprovalCount FROM fn_workflow_step WHERE InstanceId=@instanceId AND NodeKey='review'", new { instanceId }));
        Assert.AreEqual(3, await connection.ExecuteScalarAsync<int>(
            "SELECT ApprovalSlotCount FROM fn_workflow_step WHERE InstanceId=@instanceId AND NodeKey='review'", new { instanceId }));
        var clients = new[] { origin, first, second };
        for (var index = 0; index < participants.Length; index++)
        {
            var actorId = participants[index];
            var todoId = await connection.ExecuteScalarAsync<Guid>(
                "SELECT Id FROM fn_workflow_todo WHERE InstanceId=@instanceId AND AssigneeUserId=@actorId AND StatusKey='active'",
                new { instanceId, actorId });
            var result = await PostAsync(clients[index], $"/api/v1/workflow/todos/{todoId:D}/approve", new {
                expectedRevision = 1, expectedFormRevision = index + 1, fieldPatch = new { },
                comment = "按固定席位核对", idempotencyKey = "percent-" + index,
            });
            Assert.AreEqual(index == 2 ? "completed" : "active", result.GetProperty("statusKey").GetString());
        }
        var explicitVersion = await PublishAsync(origin, formVersionId, [
            Node("start", "start", new { nextNodeKeys = new[] { "review" } }),
            Node("review", "human.approval", new { nextNodeKeys = new[] { "end" },
                approvalPolicy = new { modeKey = "all", approverUserIds = new[] { firstActorId, secondActorId } } }),
            Node("end", "end", new { }),
        ], "approval");
        var explicitStart = await PostAsync(origin, "/api/v1/workflow/instances", new { definitionVersionId = explicitVersion,
            businessType = "activity.approval.explicit", businessId = "no-implicit-initiator", initialValues = new { },
            idempotencyKey = "explicit-cohort" }, HttpStatusCode.Created);
        var explicitInstance = explicitStart.GetProperty("id").GetGuid();
        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>(
            "SELECT ApprovalSlotCount FROM fn_workflow_step WHERE InstanceId=@explicitInstance AND NodeKey='review'", new { explicitInstance }));
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fn_workflow_approval_slot WHERE InstanceId=@explicitInstance AND AssigneeUserId=@originActorId", new { explicitInstance, originActorId }));
        for (var index = 0; index < 2; index++)
        {
            var actorId = index == 0 ? firstActorId : secondActorId;
            var todoId = await connection.ExecuteScalarAsync<Guid>(
                "SELECT Id FROM fn_workflow_todo WHERE InstanceId=@explicitInstance AND AssigneeUserId=@actorId AND StatusKey='active'",
                new { explicitInstance, actorId });
            var result = await PostAsync(index == 0 ? first : second, $"/api/v1/workflow/todos/{todoId:D}/approve", new {
                expectedRevision = 1, expectedFormRevision = index + 1, fieldPatch = new { }, idempotencyKey = "explicit-" + index });
            Assert.AreEqual(index == 1 ? "completed" : "active", result.GetProperty("statusKey").GetString());
        }
    }

    private static async Task VerifyTodoReceiptBindingAsync(HttpClient client, Guid formVersionId, DbConnection connection)
    {
        var version = await PublishAsync(client, formVersionId, [
            Node("start", "start", new { nextNodeKeys = new[] { "fork" } }),
            Node("fork", "gateway.parallel", new { gatewayRoleKey = "fork", joinNodeKey = "join",
                branches = new[] { new { branchKey = "left", nextNodeKey = "left" }, new { branchKey = "right", nextNodeKey = "right" } } }),
            Node("left", "human.approval", new { nextNodeKeys = new[] { "join" } }),
            Node("right", "human.approval", new { nextNodeKeys = new[] { "join" } }),
            Node("join", "gateway.parallel", new { gatewayRoleKey = "join", forkNodeKey = "fork", nextNodeKeys = new[] { "end" } }),
            Node("end", "end", new { }),
        ], "approval");
        var started = await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version,
            businessType = "activity.receipt.binding", businessId = "same-actor-branches", initialValues = new { },
            idempotencyKey = "receipt-binding" }, HttpStatusCode.Created);
        var instanceId = started.GetProperty("id").GetGuid();
        var left = await connection.ExecuteScalarAsync<Guid>(
            "SELECT todo.Id FROM fn_workflow_todo AS todo INNER JOIN fn_workflow_step AS step ON step.Id=todo.StepId WHERE todo.InstanceId=@instanceId AND step.NodeKey='left'",
            new { instanceId });
        var right = await connection.ExecuteScalarAsync<Guid>(
            "SELECT todo.Id FROM fn_workflow_todo AS todo INNER JOIN fn_workflow_step AS step ON step.Id=todo.StepId WHERE todo.InstanceId=@instanceId AND step.NodeKey='right'",
            new { instanceId });
        var identical = new { expectedRevision = 1, expectedFormRevision = 1, fieldPatch = new { }, idempotencyKey = "shared-body-key" };
        var accepted = await PostAsync(client, $"/api/v1/workflow/todos/{left:D}/approve", identical);
        await PostAsync(client, $"/api/v1/workflow/todos/{right:D}/approve", identical, HttpStatusCode.Conflict);
        Assert.AreEqual("active", await connection.ExecuteScalarAsync<string>(
            "SELECT StatusKey FROM fn_workflow_todo WHERE Id=@right", new { right }));
        var completed = await PostAsync(client, $"/api/v1/workflow/todos/{right:D}/approve",
            new { expectedRevision = 1, expectedFormRevision = 1, fieldPatch = new { }, idempotencyKey = "right-body-key" });
        Assert.AreEqual("completed", completed.GetProperty("statusKey").GetString());
        var replay = await PostAsync(client, $"/api/v1/workflow/todos/{left:D}/approve", identical);
        Assert.AreEqual(accepted.GetRawText(), replay.GetRawText());
    }

    private static async Task<Guid> VerifySignaturePolicyAsync(HttpClient client, Guid formVersionId, DbConnection connection,
        FullNetApiFactory factory, DatabaseProvider provider, string connectionString)
    {
        var version = await PublishAsync(client, formVersionId,
        [
            Node("start", "start", new { nextNodeKeys = new[] { "approval" } }),
            Node("approval", "human.approval", new { nextNodeKeys = new[] { "end" }, signRequired = true,
                commentRequired = true, operationPermissions = new { approve = true, reject = false, @return = false } }),
            Node("end", "end", new { }),
        ], "approval");
        var started = await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version,
            businessType = "activity.signature", businessId = "signed", initialValues = new { first = "initial" },
            idempotencyKey = "start-signed" }, HttpStatusCode.Created);
        var todoId = started.GetProperty("activeTodoId").GetGuid();
        var instanceId = started.GetProperty("id").GetGuid();
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todoId:D}/runtime");
        Assert.IsTrue(detail.GetProperty("signRequired").GetBoolean());
        Assert.IsTrue(detail.GetProperty("commentRequired").GetBoolean());
        CollectionAssert.AreEqual(new[] { "approve" }, detail.GetProperty("allowedActions").EnumerateArray().Select(item => item.GetString()!).ToArray());
        using (var targets = await client.GetAsync($"/api/v1/workflow/todos/{todoId:D}/return-targets"))
            Assert.AreEqual(HttpStatusCode.Forbidden, targets.StatusCode);
        using (var countersign = await client.PostAsJsonAsync($"/api/v1/workflow/todos/{todoId:D}/countersign", new {
            directionKey = "before", assigneeUserIds = new[] { Guid.CreateVersion7() }, expectedRevision = 1, idempotencyKey = "disabled-countersign" }))
            Assert.AreEqual(HttpStatusCode.Forbidden, countersign.StatusCode);
        foreach (var (action, comment, code, status) in new[] {
            ("reject", "拒绝", WorkflowErrorCodes.TodoActionForbidden, HttpStatusCode.Forbidden),
            ("approve", " ", WorkflowErrorCodes.TodoCommentRequired, HttpStatusCode.BadRequest),
            ("approve", "同意", WorkflowErrorCodes.TodoSignatureRequired, HttpStatusCode.BadRequest) })
        {
            using var rejected = await client.PostAsJsonAsync($"/api/v1/workflow/todos/{todoId:D}/{action}", new {
                expectedRevision = 1, expectedFormRevision = 1, fieldPatch = new { }, comment, idempotencyKey = "invalid-" + code });
            Assert.AreEqual(status, rejected.StatusCode, await rejected.Content.ReadAsStringAsync());
            var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
            Assert.AreEqual(code, problem.GetProperty("code").GetString());
        }
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j2e8AAAAASUVORK5CYII=");
        using var upload = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(png); bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        upload.Add(bytes, "file", "signature.png");
        using var uploaded = await client.PostAsync($"/api/v1/workflow/todos/{todoId:D}/signature-uploads?expectedRevision=1", upload);
        Assert.AreEqual(HttpStatusCode.OK, uploaded.StatusCode, await uploaded.Content.ReadAsStringAsync());
        var file = await uploaded.Content.ReadFromJsonAsync<JsonElement>(); var fileId = file.GetProperty("fileId").GetGuid();
        var request = new { expectedRevision = 1, expectedFormRevision = 1, fieldPatch = new { first = "signed" },
            comment = "确认同意", signatureFileId = fileId, idempotencyKey = "signed-approval" };
        var completed = await PostAsync(client, $"/api/v1/workflow/todos/{todoId:D}/approve", request);
        Assert.AreEqual("completed", completed.GetProperty("statusKey").GetString());
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_action_evidence WHERE InstanceId=@instanceId AND FormRevision=2 AND FileId=@fileId", new { instanceId, fileId }));
        var replay = await PostAsync(client, $"/api/v1/workflow/todos/{todoId:D}/approve", request);
        Assert.AreEqual(completed.GetRawText(), replay.GetRawText());
        var evidenceId = await connection.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_action_evidence WHERE InstanceId=@instanceId", new { instanceId });
        var evidence = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todoId:D}/signature-evidence");
        Assert.AreEqual(evidenceId, evidence.EnumerateArray().Single().GetProperty("id").GetGuid());
        using (var content = await client.GetAsync($"/api/v1/workflow/todos/{todoId:D}/signature-evidence/{evidenceId:D}/content"))
        {
            Assert.AreEqual(HttpStatusCode.OK, content.StatusCode);
            CollectionAssert.AreEqual(png, await content.Content.ReadAsByteArrayAsync());
        }
        var stranger = await factory.CreateHostIdentityAsync("signature-stranger-" + Guid.NewGuid().ToString("N"), [WorkflowPermissions.TodosRead]);
        using (var outsider = factory.CreateClientForHost("localhost"))
        {
            outsider.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", stranger.AccessToken);
            using var forbidden = await outsider.GetAsync($"/api/v1/workflow/todos/{todoId:D}/signature-evidence/{evidenceId:D}/content");
            Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var claim = await scope.ServiceProvider.GetRequiredService<IHostFileReferenceClaimService>().ClaimAsync(new(
                HostFileReferenceClaimIdempotencyKeys.WorkflowActionSignature(evidenceId), HostFileReferenceClaimConsumerModules.Workflow, evidenceId, fileId));
            Assert.IsTrue(claim.IsSuccess);
            Assert.AreEqual(HostFileReferenceClaimStates.Active, claim.Value!.State);
        }
        // 模拟建表已提交但 DbUp 未记账；重跑保留原证据、唯一约束及文件绑定。
        await connection.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%289_WorkflowActionEvidence.sql'"
            : "DELETE FROM schemaversions WHERE ScriptName LIKE '%289_WorkflowActionEvidence.sql'");
        var recovery = provider == DatabaseProvider.SqlServer
            ? Migration114To126RecoveryTestSupport.MigrateSqlServer(connectionString, 289)
            : Migration114To126RecoveryTestSupport.MigrateMySql(connectionString, 289);
        Assert.IsTrue(recovery.Successful, recovery.Error?.Message);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_action_evidence WHERE Id=@evidenceId", new { evidenceId }));

        var racing = await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version,
            businessType = "activity.signature", businessId = "signed-race", initialValues = new { first = "initial" },
            idempotencyKey = "start-signed-race" }, HttpStatusCode.Created);
        var racingTodo = racing.GetProperty("activeTodoId").GetGuid();
        var racingId = racing.GetProperty("id").GetGuid();
        var concurrent = await Task.WhenAll(PostAsync(client, $"/api/v1/workflow/todos/{racingTodo:D}/approve", request),
            PostAsync(client, $"/api/v1/workflow/todos/{racingTodo:D}/approve", request));
        Assert.AreEqual(concurrent[0].GetRawText(), concurrent[1].GetRawText());
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_action_evidence WHERE InstanceId=@racingId", new { racingId }));
        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_files_file_reference_claim WHERE FileId=@fileId AND ConsumerModule='workflow' AND State='active'", new { fileId }));
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_files_file_reference_claim WHERE FileId=@fileId AND ConsumerModule='workflow' AND State='pending'", new { fileId }));
        return fileId;
    }

    private static async Task VerifyTenantSignatureAsync(HttpClient client, DbConnection connection, Guid actorId, Guid hostSignatureId)
    {
        using var availableResponse = await client.GetAsync("/api/v1/tenancy/available");
        Assert.AreEqual(HttpStatusCode.OK, availableResponse.StatusCode);
        var available = await availableResponse.Content.ReadFromJsonAsync<TenantContextSummary[]>();
        var acme = available!.Single(tenant => tenant.Identifier == "acme");
        using var enteredResponse = await client.PutAsJsonAsync("/api/v1/tenancy/context", new ChangeTenantContextRequest(acme.Id));
        Assert.AreEqual(HttpStatusCode.OK, enteredResponse.StatusCode, await enteredResponse.Content.ReadAsStringAsync());
        var entered = await enteredResponse.Content.ReadFromJsonAsync<TenantContextTokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", entered!.AccessToken);
        var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "tenant.signature." + Guid.NewGuid().ToString("N"),
            draft = new { schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main",
                fields = new[] { new { fieldKey = "first", fieldTypeKey = "text", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
        var published = await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 });
        var version = await PublishAsync(client, published.GetProperty("id").GetGuid(), [
            Node("start", "start", new { nextNodeKeys = new[] { "approval" } }),
            Node("approval", "human.approval", new { nextNodeKeys = new[] { "end" }, signRequired = true }),
            Node("end", "end", new { }) ], "approval");
        var started = await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version,
            businessType = "activity.signature", businessId = "tenant", initialValues = new { first = "initial" },
            idempotencyKey = "start-tenant-signed" }, HttpStatusCode.Created);
        var todoId = started.GetProperty("activeTodoId").GetGuid();
        using (var foreignFile = await client.PostAsJsonAsync($"/api/v1/workflow/todos/{todoId:D}/approve", new {
            expectedRevision = 1, expectedFormRevision = 1, fieldPatch = new { }, comment = "不能借用 Host 文件",
            signatureFileId = hostSignatureId, idempotencyKey = "tenant-forbidden-host-signature" }))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, foreignFile.StatusCode, await foreignFile.Content.ReadAsStringAsync());
            Assert.AreEqual(WorkflowErrorCodes.TodoSignatureInvalid, (await foreignFile.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j2e8AAAAASUVORK5CYII=");
        using var upload = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(png); bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        upload.Add(bytes, "file", "signature.png");
        using var uploaded = await client.PostAsync($"/api/v1/workflow/todos/{todoId:D}/signature-uploads?expectedRevision=1", upload);
        Assert.AreEqual(HttpStatusCode.OK, uploaded.StatusCode, await uploaded.Content.ReadAsStringAsync());
        var fileId = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fileId").GetGuid();
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_files_tenant_resource_file WHERE Id=@fileId AND TenantId=@TenantId AND ResourceId=@actorId AND OwnerModuleKey='workflow' AND CreatedByUserId=@actorId", new { fileId, TenantId = acme.Id, actorId }));
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_files_file WHERE Id=@fileId", new { fileId }));
        var completed = await PostAsync(client, $"/api/v1/workflow/todos/{todoId:D}/approve", new { expectedRevision = 1,
            expectedFormRevision = 1, fieldPatch = new { first = "tenant signed" }, comment = "确认", signatureFileId = fileId, idempotencyKey = "tenant-signed" });
        Assert.AreEqual("completed", completed.GetProperty("statusKey").GetString());
        var evidenceId = await connection.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_action_evidence WHERE TodoId=@todoId", new { todoId });
        using var content = await client.GetAsync($"/api/v1/workflow/todos/{todoId:D}/signature-evidence/{evidenceId:D}/content");
        Assert.AreEqual(HttpStatusCode.OK, content.StatusCode);
        CollectionAssert.AreEqual(png, await content.Content.ReadAsByteArrayAsync());
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_files_file_reference_claim WHERE FileId=@fileId", new { fileId }));
    }

    private static async Task VerifyEmptyConditionalAsync(HttpClient client, Guid formVersionId, DbConnection connection)
    {
        var version = await PublishAsync(client, formVersionId,
        [
            Node("start", "start", new { nextNodeKeys = new[] { "gate" } }),
            Node("gate", "gateway.exclusive", new { nextNodeKeys = new[] { "empty", "end" }, defaultNextNodeKey = "end",
                branches = new[] { new { branchKey = "selected", nextNodeKey = "empty", condition = new { @operator = "and",
                    conditions = new object[] {
                        new { fieldKey = "first", @operator = "in", value = new[] { "initial", "other" } },
                        new { fieldKey = "second", @operator = "isNotEmpty" } } } } } }),
            Node("empty", "control.empty", new { nextNodeKeys = new[] { "end" } }),
            Node("end", "end", new { }),
        ], "automation");
        var request = new { definitionVersionId = version, businessType = "activity.empty", businessId = "conditional",
            initialValues = new { first = "initial", second = "initial" }, idempotencyKey = "empty-conditional" };
        var result = await PostAsync(client, "/api/v1/workflow/instances", request, HttpStatusCode.Created);
        Assert.AreEqual("completed", result.GetProperty("statusKey").GetString());
        var instanceId = result.GetProperty("id").GetGuid();
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@instanceId AND NodeTypeKey='control.empty' AND StatusKey='completed'", new { instanceId }));
        var replay = await PostAsync(client, "/api/v1/workflow/instances", request, HttpStatusCode.Created);
        Assert.AreEqual(result.GetRawText(), replay.GetRawText());
    }

    private static async Task VerifyInclusiveTraceAsync(HttpClient client, Guid formVersionId, Guid recipient, DbConnection connection)
    {
        var version = await PublishAsync(client, formVersionId,
        [
            Node("start", "start", new { nextNodeKeys = new[] { "fork" } }),
            Node("fork", "gateway.inclusive", new { gatewayRoleKey = "fork", joinNodeKey = "join",
                branches = new[] { new { branchKey = "a", nextNodeKey = "copyA", condition = new { fieldKey = "first", @operator = "equals", value = "initial" } } },
                defaultNextNodeKey = "copyB" }),
            Node("copyA", "notify.cc", new { nextNodeKeys = new[] { "join" }, recipientUserIds = new[] { recipient } }),
            Node("copyB", "notify.cc", new { nextNodeKeys = new[] { "join" }, recipientUserIds = new[] { recipient } }),
            Node("join", "gateway.inclusive", new { gatewayRoleKey = "join", forkNodeKey = "fork", nextNodeKeys = new[] { "end" } }),
            Node("end", "end", new { }),
        ], "automation");
        var started = await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version,
            businessType = "activity.inclusive", businessId = "trace", initialValues = new { first = "initial" },
            idempotencyKey = "inclusive-trace" }, HttpStatusCode.Created);
        Assert.AreEqual("completed", started.GetProperty("statusKey").GetString());
        var instanceId = started.GetProperty("id").GetGuid();
        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@instanceId AND NodeTypeKey='gateway.inclusive'", new { instanceId }));
    }

    private static async Task VerifyRejectWithQueuedWorkAsync(HttpClient client, Guid formVersionId, Guid recipient,
        FullNetApiFactory factory, DbConnection connection)
    {
        var nodes = new List<object>
        {
            Node("start", "start", new { nextNodeKeys = new[] { "fork" } }),
            Node("fork", "gateway.parallel", new { gatewayRoleKey = "fork", joinNodeKey = "join", branches = new[] {
                new { branchKey = "human", nextNodeKey = "approval" }, new { branchKey = "automatic", nextNodeKey = "copy0" } } }),
            Node("approval", "human.approval", new { nextNodeKeys = new[] { "join" } }),
            Node("join", "gateway.parallel", new { gatewayRoleKey = "join", forkNodeKey = "fork", nextNodeKeys = new[] { "end" } }),
            Node("end", "end", new { }),
        };
        for (var index = 0; index < 35; index++) nodes.Add(Node("copy" + index, "notify.cc", new {
            nextNodeKeys = new[] { index == 34 ? "join" : "copy" + (index + 1) }, recipientUserIds = new[] { recipient } }));
        var version = await PublishAsync(client, formVersionId, nodes, "automation");
        var started = await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version,
            businessType = "activity.reject", businessId = "queued", initialValues = new { }, idempotencyKey = "reject-queued" }, HttpStatusCode.Created);
        var instanceId = started.GetProperty("id").GetGuid();
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_work WHERE InstanceId=@instanceId AND StatusKey='pending'", new { instanceId }));
        var rejected = await PostAsync(client, $"/api/v1/workflow/todos/{started.GetProperty("activeTodoId").GetGuid():D}/reject", new {
            expectedRevision = 1, expectedFormRevision = 1, fieldPatch = new { }, comment = "终止", idempotencyKey = "reject" });
        Assert.AreEqual("rejected", rejected.GetProperty("statusKey").GetString());
        Assert.AreEqual(0, await ProcessAsync(factory));
        Assert.AreEqual("cancelled", await connection.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_activity_work WHERE InstanceId=@instanceId", new { instanceId }));
    }

    private static async Task VerifyBranchMergeAsync(HttpClient client, Guid formVersionId, DbConnection connection, FullNetApiFactory factory, bool nested)
    {
        var nodes = new List<object>
        {
            Node("start", "start", new { nextNodeKeys = new[] { "fork" } }),
            Node("fork", "gateway.parallel", new { gatewayRoleKey = "fork", joinNodeKey = "join", branches = new[] {
                new { branchKey = "a", nextNodeKey = nested ? "innerFork" : "approvalA" }, new { branchKey = "b", nextNodeKey = "approvalB" } } }),
            Node("approvalA", "human.approval", new { nextNodeKeys = new[] { nested ? "innerJoin" : "join" }, fieldPolicies = new { first = "editable", second = "readOnly" } }),
            Node("approvalB", "human.approval", new { nextNodeKeys = new[] { "join" }, fieldPolicies = new { first = "readOnly", second = "editable" } }),
            Node("join", "gateway.parallel", new { gatewayRoleKey = "join", forkNodeKey = "fork", nextNodeKeys = new[] { nested ? "end" : "afterJoin" } }),
            Node("end", "end", new { }),
        };
        if (!nested) nodes.Add(Node("afterJoin", "human.approval", new { nextNodeKeys = new[] { "end" } }));
        if (nested) nodes.AddRange([
            Node("innerFork", "gateway.parallel", new { gatewayRoleKey = "fork", joinNodeKey = "innerJoin", branches = new[] {
                new { branchKey = "inner-a", nextNodeKey = "approvalA" }, new { branchKey = "inner-c", nextNodeKey = "approvalC" } } }),
            Node("approvalC", "human.approval", new { nextNodeKeys = new[] { "innerJoin" }, fieldPolicies = new { first = "readOnly", second = "readOnly" } }),
            Node("innerJoin", "gateway.parallel", new { gatewayRoleKey = "join", forkNodeKey = "innerFork", nextNodeKeys = new[] { "join" } }),
        ]);
        var version = await PublishAsync(client, formVersionId, nodes, "approval");
        var started = await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version,
            businessType = "activity.branches", businessId = nested ? "merge-nested" : "merge", initialValues = new { first = "initial", second = "initial" },
            idempotencyKey = nested ? "start-merge-nested" : "start-merge" }, HttpStatusCode.Created);
        var instanceId = started.GetProperty("id").GetGuid();
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/workflow/todos/mine");
        var todos = list.GetProperty("items").EnumerateArray().Where(item => item.GetProperty("instanceId").GetGuid() == instanceId)
            .ToDictionary(item => item.GetProperty("nodeKey").GetString()!, item => item.GetProperty("id").GetGuid());
        var a = todos["approvalA"]; var b = todos["approvalB"];
        var first = await PostAsync(client, $"/api/v1/workflow/todos/{a:D}/approve", new {
            expectedRevision = 1, expectedFormRevision = 1, fieldPatch = new { first = "A" }, idempotencyKey = "branch-a" });
        Assert.AreEqual("active", first.GetProperty("statusKey").GetString());
        var sibling = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{b:D}");
        Assert.AreEqual("initial", sibling.GetProperty("submission").GetProperty("first").GetString());
        Assert.AreEqual(1, sibling.GetProperty("submissionRevision").GetInt64());
        using var stale = await client.PostAsJsonAsync($"/api/v1/workflow/todos/{b:D}/approve", new {
            expectedRevision = 1, expectedFormRevision = 2, fieldPatch = new { second = "B" }, idempotencyKey = "branch-stale" });
        Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode, await stale.Content.ReadAsStringAsync());
        if (nested)
        {
            var inner = await PostAsync(client, $"/api/v1/workflow/todos/{todos["approvalC"]:D}/approve", new {
                expectedRevision = 1, expectedFormRevision = 1, fieldPatch = new { }, idempotencyKey = "branch-c" });
            Assert.AreEqual("active", inner.GetProperty("statusKey").GetString());
        }
        var completed = await PostAsync(client, $"/api/v1/workflow/todos/{b:D}/approve", new {
            expectedRevision = 1, expectedFormRevision = 1, fieldPatch = new { second = "B" }, idempotencyKey = "branch-b" });
        Assert.AreEqual(nested ? "completed" : "active", completed.GetProperty("statusKey").GetString());
        if (!nested)
        {
            Assert.AreEqual(JsonValueKind.Null, completed.GetProperty("activeTodoId").ValueKind);
            Assert.AreEqual(1, await ProcessAsync(factory));
            var resumedTodos = await client.GetFromJsonAsync<JsonElement>("/api/v1/workflow/todos/mine");
            var next = resumedTodos.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("instanceId").GetGuid() == instanceId);
            var final = await PostAsync(client, $"/api/v1/workflow/todos/{next.GetProperty("id").GetGuid():D}/approve", new {
                expectedRevision = 1, expectedFormRevision = 2, fieldPatch = new { }, idempotencyKey = "after-join" });
            Assert.AreEqual("completed", final.GetProperty("statusKey").GetString());
        }
        var json = await connection.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@instanceId", new { instanceId });
        using var merged = JsonDocument.Parse(json!);
        Assert.AreEqual("A", merged.RootElement.GetProperty("first").GetString());
        Assert.AreEqual("B", merged.RootElement.GetProperty("second").GetString());
        Assert.AreEqual(nested ? 2 : 1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_branch_merge AS receipt INNER JOIN fn_workflow_parallel_join AS gateway ON gateway.Id=receipt.ParallelJoinId WHERE gateway.InstanceId=@instanceId", new { instanceId }));
    }

    private static async Task VerifyAttachmentViewsAsync(FullNetApiFactory factory, DbConnection connection)
    {
        var starter = await factory.CreateHostIdentityAsync("attachment-starter-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate,
                WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesRead,
                WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, HostFilePermissions.Upload]);
        var reviewer = await factory.CreateHostIdentityAsync("attachment-reviewer-" + Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, WorkflowPermissions.TodosCountersign, WorkflowPermissions.InstancesRead]);
        using var client = factory.CreateClientForHost("localhost");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", starter.AccessToken);
        using var reviewerClient = factory.CreateClientForHost("localhost");
        reviewerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", reviewer.AccessToken);
        var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "attachment.views." + Guid.NewGuid().ToString("N"),
            draft = new { schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields = new[] {
                new { fieldKey = "evidence", fieldTypeKey = "attachment", required = false,
                    constraints = new { maxCount = 1, maxSizeBytes = 1024, allowedExtensions = new[] { "png" } } } } } } } }, HttpStatusCode.Created);
        var formVersion = await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 });
        var formVersionId = formVersion.GetProperty("id").GetGuid();
        var version = await PublishAsync(client, formVersionId, [
            Node("start", "start", new { nextNodeKeys = new[] { "review" } }),
            Node("review", "human.approval", new { nextNodeKeys = new[] { "final" }, assigneePolicy = new {
                sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { reviewer.UserId } } } } }),
            Node("final", "human.approval", new { nextNodeKeys = new[] { "end" } }), Node("end", "end", new { }) ], "approval");
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j2e8AAAAASUVORK5CYII=");
        async Task<Guid> UploadAsync()
        {
            using var body = new MultipartFormDataContent(); var bytes = new ByteArrayContent(png);
            bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png"); body.Add(bytes, "file", "evidence.png");
            using var response = await client.PostAsync("/api/v1/files/host-files/", body);
            Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }
        var firstFile = await UploadAsync(); var futureFile = await UploadAsync();
        var started = await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version,
            businessType = "activity.attachment", businessId = "history", initialValues = new { evidence = new[] { firstFile } },
            idempotencyKey = "attachment-history-start" }, HttpStatusCode.Created);
        var instanceId = started.GetProperty("id").GetGuid(); var firstTodo = started.GetProperty("activeTodoId").GetGuid();
        string FilePath(Guid file) => $"/api/v1/workflow/instances/{instanceId:D}/form-attachments/{file:D}/content";
        using (var visible = await reviewerClient.GetAsync(FilePath(firstFile))) Assert.AreEqual(HttpStatusCode.OK, visible.StatusCode);
        var advanced = await PostAsync(reviewerClient, $"/api/v1/workflow/todos/{firstTodo:D}/approve", new {
            expectedRevision = 1, expectedFormRevision = 1, fieldPatch = new { }, idempotencyKey = "attachment-review" });
        var finalTodo = advanced.GetProperty("activeTodoId").GetGuid();
        await PostAsync(client, $"/api/v1/workflow/todos/{finalTodo:D}/approve", new {
            expectedRevision = 1, expectedFormRevision = 2, fieldPatch = new { evidence = new[] { futureFile } }, idempotencyKey = "attachment-final" });
        using (var hiddenFuture = await reviewerClient.GetAsync(FilePath(futureFile))) Assert.AreEqual(HttpStatusCode.Forbidden, hiddenFuture.StatusCode);
        using (var ownHistory = await reviewerClient.GetAsync(FilePath(firstFile))) Assert.AreEqual(HttpStatusCode.OK, ownHistory.StatusCode);
        var historical = await reviewerClient.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{firstTodo:D}/runtime");
        Assert.AreEqual(firstFile, historical.GetProperty("submission").GetProperty("evidence").EnumerateArray().Single().GetGuid());
        Assert.AreEqual(2, historical.GetProperty("submissionRevision").GetInt64());
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_files_file_reference_claim WHERE FileId=@firstFile AND ConsumerModule='workflow' AND State='active'", new { firstFile }));

        var hiddenVersion = await PublishAsync(client, formVersionId, [Node("start", "start", new { nextNodeKeys = new[] { "hidden" } }),
            Node("hidden", "human.approval", new { nextNodeKeys = new[] { "end" }, fieldPolicies = new { evidence = "hidden" },
                assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { reviewer.UserId } } } } }),
            Node("end", "end", new { }) ], "approval");
        var hiddenStart = await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = hiddenVersion,
            businessType = "activity.attachment", businessId = "hidden", initialValues = new { evidence = new[] { firstFile } },
            idempotencyKey = "attachment-hidden-start" }, HttpStatusCode.Created);
        using var denied = await reviewerClient.GetAsync($"/api/v1/workflow/instances/{hiddenStart.GetProperty("id").GetGuid():D}/form-attachments/{firstFile:D}/content");
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);

        // 恢复后的原办理人曾看到加签输出，随后清空字段也必须保留该次输入的历史授权。
        var chainStart = await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version,
            businessType = "activity.attachment", businessId = "before-countersign", initialValues = new { evidence = new[] { firstFile } },
            idempotencyKey = "attachment-chain-start" }, HttpStatusCode.Created);
        var chainInstance = chainStart.GetProperty("id").GetGuid();
        var oldTodo = chainStart.GetProperty("activeTodoId").GetGuid();
        var configured = await PostAsync(reviewerClient, $"/api/v1/workflow/todos/{oldTodo:D}/countersign", new {
            directionKey = "before", assigneeUserIds = new[] { starter.UserId }, expectedRevision = 1, idempotencyKey = "attachment-chain-config" });
        var supplemental = configured.GetProperty("activeTodoId").GetGuid();
        var resumed = await PostAsync(client, $"/api/v1/workflow/todos/{supplemental:D}/approve", new {
            expectedRevision = 1, expectedFormRevision = 1, fieldPatch = new { evidence = new[] { futureFile } }, idempotencyKey = "attachment-chain-sign" });
        var resumedTodo = resumed.GetProperty("activeTodoId").GetGuid();
        Assert.AreNotEqual(oldTodo, resumedTodo);
        DbException? duplicateActive = null;
        try
        {
            await connection.ExecuteAsync("""
                INSERT INTO fn_workflow_todo (Id, InstanceId, StepId, AssigneeUserId, StatusKey, ArrivedAtUtc, Revision)
                SELECT @Id, InstanceId, StepId, AssigneeUserId, 'active', ArrivedAtUtc, 1
                FROM fn_workflow_todo WHERE Id=@resumedTodo
                """, new { Id = Guid.CreateVersion7(), resumedTodo });
        }
        catch (DbException exception) { duplicateActive = exception; }
        Assert.IsNotNull(duplicateActive, "历史激活可以重复，活动办理身份仍必须由数据库保证唯一。");
        Assert.IsTrue(duplicateActive is SqlException { Number: 2601 or 2627 } or MySqlException { Number: 1062 });
        var oldView = await reviewerClient.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{oldTodo:D}/runtime");
        Assert.AreEqual(firstFile, oldView.GetProperty("submission").GetProperty("evidence").EnumerateArray().Single().GetGuid());
        var resumedView = await reviewerClient.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{resumedTodo:D}/runtime");
        Assert.AreEqual(futureFile, resumedView.GetProperty("submission").GetProperty("evidence").EnumerateArray().Single().GetGuid());
        var cleared = await PostAsync(reviewerClient, $"/api/v1/workflow/todos/{resumedTodo:D}/approve", new {
            expectedRevision = 1, expectedFormRevision = 2, fieldPatch = new { evidence = Array.Empty<Guid>() }, idempotencyKey = "attachment-chain-clear" });
        await PostAsync(client, $"/api/v1/workflow/todos/{cleared.GetProperty("activeTodoId").GetGuid():D}/approve", new {
            expectedRevision = 1, expectedFormRevision = 3, fieldPatch = new { }, idempotencyKey = "attachment-chain-final" });
        using var priorInput = await reviewerClient.GetAsync($"/api/v1/workflow/instances/{chainInstance:D}/form-attachments/{futureFile:D}/content");
        Assert.AreEqual(HttpStatusCode.OK, priorInput.StatusCode);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fn_workflow_form_submission_attachment WHERE InstanceId=@chainInstance AND FileId=@futureFile", new { chainInstance, futureFile }));
    }

    private static object Node(string nodeKey, string nodeTypeKey, object config) => new { nodeKey, nodeTypeKey, nodeSchemaVersion = 1, config };

    private static async Task<Guid> PublishAsync(HttpClient client, Guid formVersionId, IReadOnlyList<object> nodes, string workflowKindKey)
    {
        var definition = await PostAsync(client, "/api/v1/workflow/definitions", new {
            definitionKey = "activity." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, workflowKindKey, nodes } }, HttpStatusCode.Created);
        var version = await PostAsync(client, $"/api/v1/workflow/definitions/{definition.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId });
        return version.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body, HttpStatusCode status = HttpStatusCode.OK)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        var json = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(status, response.StatusCode, path + " " + json);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static async Task<int> ProcessAsync(FullNetApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default);
    }
}
