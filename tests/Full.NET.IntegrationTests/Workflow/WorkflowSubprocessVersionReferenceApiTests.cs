using System.Net;
using System.Data.Common;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Dapper;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;
using static Full.NET.IntegrationTests.Workflow.WorkflowDurableDelayApiTests;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>固定发布引用和终态历史也持有版本；删除不能留下悬空子版本或泄漏数据库外键错误。</summary>
[TestClass]
public sealed class WorkflowSubprocessVersionReferenceApiTests
{
    [TestMethod]
    public async Task Published_child_references_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Published_child_references_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        var fault = new ReferenceFault();
        using var factory = new FullNetApiFactory(provider, connection, configureTestServices: services => {
            var descriptor = services.Last(item => item.ServiceType == typeof(ICommandExecutor));
            var create = descriptor.ImplementationFactory ?? throw new InvalidOperationException("Expected the command executor factory.");
            services.Replace(ServiceDescriptor.Scoped<ICommandExecutor>(scope => new ReferenceFaultExecutor((ICommandExecutor)create(scope), fault)));
        });
        await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        var actor = await factory.CreateHostIdentityAsync("version.refs." + Guid.NewGuid().ToString("N"), [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish,
            WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsRead, WorkflowPermissions.DefinitionsPublish,
            WorkflowPermissions.DefinitionsDeleteVersion, WorkflowPermissions.InstancesStart]);
        using var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Authorization = new("Bearer", actor.AccessToken);
        var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "version.refs." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1,
            sections = new[] { new { sectionKey = "main", fields = new[] { new { fieldKey = "note", fieldTypeKey = "text", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
        var formVersion = (await PostAsync(client, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
        async Task<Guid> Publish(bool parent, Guid child = default) => await PublishNodesAsync(client, formVersion, parent ? [
            Node("start", "start", new { nextNodeKeys = new[] { "child" } }),
            Node("child", "process.subprocess", new { nextNodeKeys = new[] { "end" }, fieldPolicies = new { },
                subprocessPolicy = new { definitionVersionId = child, inputFields = new { }, outputFields = new { } } }), Node("end", "end", new { })] : [
            Node("start", "start", new { nextNodeKeys = new[] { "end" } }), Node("end", "end", new { })]);
        async Task Delete(Guid id, HttpStatusCode status)
        {
            using var response = await client.DeleteAsync($"/api/v1/workflow/definition-versions/{id:D}");
            Assert.AreEqual(status, response.StatusCode, await response.Content.ReadAsStringAsync());
            if (status == HttpStatusCode.Conflict) Assert.AreEqual(WorkflowErrorCodes.VersionInUse,
                (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        var childVersion = await Publish(false); var parentVersion = await Publish(true, childVersion);
        await Delete(childVersion, HttpStatusCode.Conflict);
        await Delete(parentVersion, HttpStatusCode.NoContent); await Delete(childVersion, HttpStatusCode.NoContent);

        var historicalVersion = await Publish(false);
        var completed = await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = historicalVersion,
            businessType = "version.refs", businessId = Guid.NewGuid().ToString("N"), initialValues = new { }, idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Created);
        Assert.AreEqual("completed", completed.GetProperty("statusKey").GetString());
        await Delete(historicalVersion, HttpStatusCode.Conflict);

        object ParentDraft(Guid child) => new { schemaVersion = 1, workflowKindKey = "automation", nodes = new[] {
            Node("start", "start", new { nextNodeKeys = new[] { "child" } }), Node("child", "process.subprocess", new { nextNodeKeys = new[] { "end" }, fieldPolicies = new { },
                subprocessPolicy = new { definitionVersionId = child, inputFields = new { }, outputFields = new { } } }), Node("end", "end", new { }) } };
        var retryChild = await Publish(false);
        var pending = await PostAsync(client, "/api/v1/workflow/definitions", new { definitionKey = "retry.refs." + Guid.NewGuid().ToString("N"), draft = ParentDraft(retryChild) }, HttpStatusCode.Created);
        var pendingId = pending.GetProperty("id").GetGuid(); fault.Arm();
        await PostAsync(client, $"/api/v1/workflow/definitions/{pendingId:D}/publish", new { expectedRevision = 1, formVersionId = formVersion }, HttpStatusCode.Conflict);
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_definition_version WHERE DefinitionId=@id", new { id = pendingId }));
        var draftAfterFailure = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/definitions/{pendingId:D}");
        Assert.AreEqual(1L, draftAfterFailure.GetProperty("draftRevision").GetInt64());
        var retried = await PostAsync(client, $"/api/v1/workflow/definitions/{pendingId:D}/publish", new { expectedRevision = 1, formVersionId = formVersion });
        await Delete(retryChild, HttpStatusCode.Conflict);
        await Delete(retried.GetProperty("id").GetGuid(), HttpStatusCode.NoContent); await Delete(retryChild, HttpStatusCode.NoContent);

        // 只允许发布或删除赢得竞态；已成功父版本绝不能固定一个已删除子版本。
        var racingChild = await Publish(false);
        var racingParent = await PostAsync(client, "/api/v1/workflow/definitions", new { definitionKey = "race.refs." + Guid.NewGuid().ToString("N"), draft = ParentDraft(racingChild) }, HttpStatusCode.Created);
        var racingParentId = racingParent.GetProperty("id").GetGuid();
        var publishTask = client.PostAsJsonAsync($"/api/v1/workflow/definitions/{racingParentId:D}/publish", new { expectedRevision = 1, formVersionId = formVersion });
        var deleteTask = client.DeleteAsync($"/api/v1/workflow/definition-versions/{racingChild:D}");
        await Task.WhenAll(publishTask, deleteTask); using var published = await publishTask; using var deleted = await deleteTask;
        if (published.StatusCode == HttpStatusCode.OK)
        {
            Assert.AreEqual(HttpStatusCode.Conflict, deleted.StatusCode, await deleted.Content.ReadAsStringAsync());
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_ref WHERE ChildVersionId=@id", new { id = racingChild }));
        }
        else
        {
            Assert.IsTrue(published.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict, await published.Content.ReadAsStringAsync());
            Assert.AreEqual(HttpStatusCode.NoContent, deleted.StatusCode, await deleted.Content.ReadAsStringAsync());
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_definition_version WHERE DefinitionId=@id", new { id = racingParentId }));
        }

        var keptChild = await Publish(false); var keptParent = await Publish(true, keptChild);
        var missingChild = await Publish(false); var missingParent = await Publish(true, missingChild);
        await db.ExecuteAsync("DELETE FROM fn_workflow_subprocess_ref WHERE ParentVersionId=@id", new { id = missingParent });
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "ALTER TABLE dbo.fn_workflow_subprocess_ref DROP CONSTRAINT fk_fn_workflow_subprocess_ref_child"
            : "ALTER TABLE fn_workflow_subprocess_ref DROP FOREIGN KEY fk_fn_workflow_subprocess_ref_child");
        await db.ExecuteAsync("DROP INDEX ix_fn_workflow_subprocess_ref_child ON fn_workflow_subprocess_ref");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%320_WorkflowSubprocessVersionReferences.sql'"
            : "DELETE FROM schemaversions WHERE ScriptName LIKE '%320_WorkflowSubprocessVersionReferences.sql'");
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection); await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection);
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_ref WHERE ParentVersionId=@parent AND ChildVersionId=@child AND TenantScopeKey='host'", new { parent = keptParent, child = keptChild }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_ref WHERE ParentVersionId=@parent AND ChildVersionId=@child AND TenantScopeKey='host'", new { parent = missingParent, child = missingChild }));
        await Delete(missingChild, HttpStatusCode.Conflict);
        if (provider == DatabaseProvider.MySql) Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA=DATABASE() AND ROUTINE_NAME='fn_workflow_subprocess_ref_ensure'"));
    }
    private sealed class ReferenceFaultExecutor(ICommandExecutor inner, ReferenceFault fault) : ICommandExecutor
    {
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            statement.Name == "workflow.definition_version.insert_reference" && fault.Take() ? Task.FromResult(0) : inner.ExecuteAsync(statement, parameters, cancellationToken);
    }
    private sealed class ReferenceFault
    {
        private int armed;
        public void Arm() => Interlocked.Exchange(ref armed, 1);
        public bool Take() => Interlocked.Exchange(ref armed, 0) == 1;
    }
}
