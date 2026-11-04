using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>八种本人命令分别在实际实例锁之后撤权，局部事实必须一起回滚且可由新会话重办。</summary>
[TestClass]
public sealed class WorkflowTodoRemainingCommitAuthorityTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Remaining_commands_revalidate_actor_after_lock_and_rollback_all_local_facts(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var window = new RevocationWindow();
        using var factory = new FullNetApiFactory(provider, connection, configureTestServices: services =>
        {
            var original = services.Last(item => item.ServiceType == typeof(ICommandExecutor)); services.Remove(original);
            services.AddScoped<ICommandExecutor>(sp => new AfterLockCommand((ICommandExecutor)(original.ImplementationInstance
                ?? original.ImplementationFactory?.Invoke(sp) ?? ActivatorUtilities.CreateInstance(sp, original.ImplementationType!)), window));
        });
        await factory.InitializeAsync();
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        await db.OpenAsync();
        using var owner = factory.CreateClientForHost("localhost");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(owner, "admin"));
        var name = "remaining-authority-" + Guid.CreateVersion7().ToString("N");
        var actor = await factory.CreateHostIdentityAsync(name, [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosTransfer,
            WorkflowPermissions.TodosDelegate, WorkflowPermissions.TodosRecallDelegation, WorkflowPermissions.TodosCountersign,
            WorkflowPermissions.ClaimPoolsRead, WorkflowPermissions.ClaimPoolsClaim, WorkflowPermissions.ClaimPoolsAssign, WorkflowPermissions.TodosRelease],
            password: FullNetApiFactory.TestPassword);
        var other = await factory.CreateHostIdentityAsync("remaining-target-" + Guid.CreateVersion7().ToString("N"), [WorkflowPermissions.TodosRead]);
        using var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new("Bearer", actor.AccessToken);
        var form = await PostAsync(owner, "/api/v1/workflow/forms", new { formKey = "authority." + Guid.CreateVersion7().ToString("N"), draft = new
        { schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields = new[] { new { fieldKey = "title", fieldTypeKey = "text", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
        var formVersion = (await PostAsync(owner, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        var approval = await PublishAsync(owner, formVersion, actor.UserId, other.UserId, false);
        var claim = await PublishAsync(owner, formVersion, actor.UserId, other.UserId, true);
        window.Revoke = async () => Assert.AreEqual(1, await db.ExecuteAsync("UPDATE fn_identity_user SET IsActive=@Active WHERE Id=@Id", new { Active = false, Id = actor.UserId }));
        var expectedHits = 0;
        foreach (var action in new[] { "transfer", "delegate", "recall", "countersign", "cancel_countersign", "claim", "assign", "release" })
        {
            var isPool = action is "claim" or "assign" or "release";
            var started = await PostAsync(owner, "/api/v1/workflow/instances", new { definitionVersionId = isPool ? claim : approval,
                businessType = "authority", businessId = action, initialValues = new { title = "original" }, idempotencyKey = Guid.CreateVersion7().ToString("N") }, HttpStatusCode.Created);
            var instance = started.GetProperty("id").GetGuid();
            Guid? pool = isPool ? await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_claim WHERE InstanceId=@instance", new { instance }) : null;
            Guid? todo = isPool ? null : await ActiveTodoAsync(db, instance, actor.UserId);
            var origin = todo;
            if (action == "recall")
            {
                await PostAsync(client, $"/api/v1/workflow/todos/{origin:D}/delegate", new { assigneeUserId = other.UserId, expectedRevision = 1, expectedFormRevision = 1, idempotencyKey = "prepare-recall" });
                todo = await ActiveTodoAsync(db, instance, other.UserId);
            }
            if (action == "cancel_countersign")
                await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/countersign", new { directionKey = "before", assigneeUserIds = new[] { other.UserId }, expectedRevision = 1, idempotencyKey = "prepare-cancel" });
            if (action == "release")
            {
                await PostAsync(client, $"/api/v1/workflow/claim-pools/{pool:D}/claim", new { expectedRevision = 1, idempotencyKey = "prepare-release" });
                todo = await ActiveTodoAsync(db, instance, actor.UserId);
            }
            var before = await ReadStateAsync(db, instance, todo, pool);
            var key = Guid.CreateVersion7().ToString("N");
            object command = action switch
            {
                "transfer" or "delegate" => new { assigneeUserId = other.UserId, expectedRevision = before.TodoRevision, expectedFormRevision = before.FormRevision, idempotencyKey = key },
                "recall" => new { expectedRevision = before.TodoRevision, expectedFormRevision = before.FormRevision, idempotencyKey = key },
                "countersign" => new { directionKey = "before", assigneeUserIds = new[] { other.UserId }, expectedRevision = before.TodoRevision, idempotencyKey = key },
                "cancel_countersign" => new { expectedRevision = before.TodoRevision, idempotencyKey = key },
                "claim" => new { expectedRevision = before.PoolRevision, idempotencyKey = key },
                "assign" => new { expectedRevision = before.PoolRevision, assigneeUserId = other.UserId, idempotencyKey = key },
                "release" => new { expectedRevision = before.TodoRevision, expectedPoolRevision = before.PoolRevision, expectedFormRevision = before.FormRevision, idempotencyKey = key },
                _ => throw new InvalidOperationException(action),
            };
            var path = action switch
            {
                "claim" or "assign" => $"/api/v1/workflow/claim-pools/{pool:D}/{action}",
                "recall" => $"/api/v1/workflow/todos/{origin:D}/delegation/recall",
                "cancel_countersign" => $"/api/v1/workflow/todos/{todo:D}/countersign/cancel",
                _ => $"/api/v1/workflow/todos/{todo:D}/{action}",
            };
            window.Armed = 1;
            using (var denied = await client.PostAsJsonAsync(path, command))
                Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode, action + ": " + await denied.Content.ReadAsStringAsync());
            Assert.AreEqual(++expectedHits, window.Hits, action + " 必须在事务实际持锁后撤权。");
            Assert.AreEqual(before, await ReadStateAsync(db, instance, todo, pool), action + " 不能留下局部提交。");
            await db.ExecuteAsync("UPDATE fn_identity_user SET IsActive=@Active WHERE Id=@Id", new { Active = true, Id = actor.UserId });
            client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, name, FullNetApiFactory.TestPassword));
            await PostAsync(client, path, command);
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_action_record WHERE InstanceId=@instance AND IdempotencyKey=@key", new { instance, key }), action);
        }
    }

    private static async Task<Guid> PublishAsync(HttpClient owner, Guid formVersion, Guid actor, Guid other, bool claim)
    {
        object config = claim ? new { nextNodeKeys = new[] { "end" }, claimPolicy = new { allowRelease = true },
                assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { actor, other } } } } }
            : new { nextNodeKeys = new[] { "end" }, assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { actor } } } } };
        var definition = await PostAsync(owner, "/api/v1/workflow/definitions", new { definitionKey = "authority." + Guid.CreateVersion7().ToString("N"),
            draft = new { schemaVersion = 1, workflowKindKey = claim ? "automation" : "approval", nodes = new[] { Node("start", "start", new { nextNodeKeys = new[] { "review" } }),
                Node("review", claim ? "human.claim" : "human.approval", config), Node("end", "end", new { }) } } }, HttpStatusCode.Created);
        return (await PostAsync(owner, $"/api/v1/workflow/definitions/{definition.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1, formVersionId = formVersion })).GetProperty("id").GetGuid();
    }

    private static object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
    private static Task<Guid> ActiveTodoAsync(DbConnection db, Guid instance, Guid actor) => db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@instance AND AssigneeUserId=@actor AND StatusKey='active'", new { instance, actor });
    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body, HttpStatusCode expected = HttpStatusCode.OK)
    { using var response = await client.PostAsJsonAsync(path, body); var json = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, path + ": " + json); return JsonSerializer.Deserialize<JsonElement>(json); }
    private static Task<State> ReadStateAsync(DbConnection db, Guid instance, Guid? todo, Guid? pool) => db.QuerySingleAsync<State>("""
        SELECT item.Revision AS InstanceRevision,
          (SELECT Revision FROM fn_workflow_form_submission WHERE InstanceId=@instance) AS FormRevision,
          (SELECT StatusKey FROM fn_workflow_todo WHERE Id=@todo) AS TodoStatus,
          (SELECT Revision FROM fn_workflow_todo WHERE Id=@todo) AS TodoRevision,
          (SELECT StatusKey FROM fn_workflow_claim WHERE Id=@pool) AS PoolStatus,
          (SELECT Revision FROM fn_workflow_claim WHERE Id=@pool) AS PoolRevision,
          (SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@instance) AS Todos,
          (SELECT COUNT(*) FROM fn_workflow_action_record WHERE InstanceId=@instance) AS Actions,
          (SELECT COUNT(*) FROM fn_workflow_countersign_chain WHERE InstanceId=@instance) AS Chains,
          (SELECT COUNT(*) FROM fn_workflow_todo_transfer WHERE InstanceId=@instance) AS Handoffs,
          (SELECT COUNT(*) FROM fn_workflow_claim_attempt WHERE InstanceId=@instance) AS Attempts,
          (SELECT COUNT(*) FROM fn_outbox_message) AS Outbox
        FROM fn_workflow_instance AS item WHERE item.Id=@instance
        """, new { instance, todo, pool });
    private sealed record State
    {
        public long InstanceRevision { get; set; } public long FormRevision { get; set; }
        public string? TodoStatus { get; set; } public long? TodoRevision { get; set; }
        public string? PoolStatus { get; set; } public long? PoolRevision { get; set; }
        public long Todos { get; set; } public long Actions { get; set; } public long Chains { get; set; }
        public long Handoffs { get; set; } public long Attempts { get; set; } public long Outbox { get; set; }
    }
    private sealed class RevocationWindow { public int Armed; public int Hits; public Func<Task> Revoke = () => Task.CompletedTask; }
    private sealed class AfterLockCommand(ICommandExecutor inner, RevocationWindow window) : ICommandExecutor
    {
        public async Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken token = default)
        {
            var count = await inner.ExecuteAsync(statement, parameters, token);
            if (count == 1 && ReferenceEquals(statement, WorkflowSql.LockInstanceForTodoAction) && Interlocked.Exchange(ref window.Armed, 0) == 1)
            { window.Hits++; await window.Revoke(); }
            return count;
        }
    }
}
