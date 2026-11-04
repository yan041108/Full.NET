using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageTenantMembers.Persistence;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>真实双库、身份目录和改派写入；候选不泄漏跨范围用户，移除成员后不能再作为目标。</summary>
[TestClass]
public sealed class WorkflowReassignmentCandidatesApiTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Administrative_candidates_are_active_scoped_paged_and_support_real_reassignment(DatabaseProvider provider)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90)); var token = deadline.Token;
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection, new Dictionary<string, string?> { ["Identity:SessionLoginPolicy"] = "AllowMultiple" });
        await factory.InitializeAsync(token);
        using var host = factory.CreateClientForHost("localhost"); using var tenantClient = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(tenantClient, token);
        var admin = await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin", FullNetApiFactory.TestPassword, token);
        host.DefaultRequestHeaders.Authorization = new("Bearer", admin);
        tenantClient.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, admin, tenant.Id, token));
        var members = new List<(HostTestIdentity User, Guid MemberId)>();
        // 目录来自真实活动 Host 身份与当前 Tenant 成员；只为无关成员关系建立最小测试数据。
        for (var i = 0; i < 5; i++)
        {
            var identity = await factory.CreateHostIdentityAsync("reassign-" + Guid.CreateVersion7().ToString("N"),
                [WorkflowPermissions.InstancesRead], token, FullNetApiFactory.TestPassword);
            var memberId = Guid.CreateVersion7(); members.Add((identity, memberId));
            if (i == 3) continue;
            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetTenant(new TenantContext(tenant.Id, "acme", "Acme"));
            await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(TenantMembershipSql.InsertMember,
                IdentitySqlParameters.Create(("Id", memberId), ("UserId", identity.UserId), ("MemberRole", TenantMemberRoles.Member),
                    ("Status", TenantMemberStatuses.Active), ("CreatedAtUtc", DateTimeOffset.UtcNow), ("UpdatedAtUtc", DateTimeOffset.UtcNow), ("Version", 1)), token);
        }
        var form = await SendAsync(tenantClient, HttpMethod.Post, "/api/v1/workflow/forms", new { formKey = "reassign." + Guid.CreateVersion7().ToString("N"),
            draft = new { schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields = new[] {
                new { fieldKey = "title", fieldTypeKey = "text", required = false, constraints = new { } } } } } } }, HttpStatusCode.Created);
        var formVersion = await SendAsync(tenantClient, HttpMethod.Post, $"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 });
        var definition = await SendAsync(tenantClient, HttpMethod.Post, "/api/v1/workflow/definitions", new { definitionKey = "reassign." + Guid.CreateVersion7().ToString("N"),
            draft = new { schemaVersion = 1, workflowKindKey = "approval", nodes = new[] {
                Node("start", "start", new { nextNodeKeys = new[] { "review" } }),
                Node("review", "human.approval", new { nextNodeKeys = new[] { "end" }, assigneePolicy = new {
                    sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { members[0].User.UserId } } } } }), Node("end", "end", new { }) } } }, HttpStatusCode.Created);
        var published = await SendAsync(tenantClient, HttpMethod.Post, $"/api/v1/workflow/definitions/{definition.GetProperty("id").GetGuid():D}/publish",
            new { expectedRevision = 1, formVersionId = formVersion.GetProperty("id").GetGuid() });
        var started = await SendAsync(tenantClient, HttpMethod.Post, "/api/v1/workflow/instances", new {
            definitionVersionId = published.GetProperty("id").GetGuid(), businessType = "reassign", businessId = "directory",
            initialValues = new { title = "Original" }, idempotencyKey = "directory-start" }, HttpStatusCode.Created);
        var instanceId = started.GetProperty("id").GetGuid(); var todoId = started.GetProperty("activeTodoId").GetGuid();
        string Candidates(Guid instance, Guid todo, int page = 1, int size = 100) => $"/api/v1/workflow/instances/{instance:D}/reassignment-candidates?todoId={todo:D}&page={page}&pageSize={size}";
        var before = await SendAsync(tenantClient, HttpMethod.Get, Candidates(instanceId, todoId));
        Assert.IsTrue(Ids(before).Contains(members[0].User.UserId)); Assert.IsTrue(Ids(before).Contains(members[1].User.UserId));
        Assert.IsFalse(Ids(before).Contains(members[3].User.UserId));
        await SendAsync(tenantClient, HttpMethod.Delete, $"/api/v1/identity/tenant-members/{members[0].MemberId:D}?version=1");
        // 切租户可能轮换原会话，Host 管理操作使用新的正式登录会话。
        host.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin", FullNetApiFactory.TestPassword, token));
        await SendAsync(host, HttpMethod.Post, $"/api/v1/identity/users/{members[2].User.UserId:D}/disable", new { });
        var after = await SendAsync(tenantClient, HttpMethod.Get, Candidates(instanceId, todoId)); var ids = Ids(after);
        Assert.IsTrue(ids.Contains(members[1].User.UserId));
        foreach (var excluded in new[] { 0, 2, 3 }) Assert.IsFalse(ids.Contains(members[excluded].User.UserId));
        var first = await SendAsync(tenantClient, HttpMethod.Get, Candidates(instanceId, todoId, 1, 1));
        var second = await SendAsync(tenantClient, HttpMethod.Get, Candidates(instanceId, todoId, 2, 1));
        Assert.AreEqual(1, first.GetProperty("items").GetArrayLength()); Assert.AreEqual(1, second.GetProperty("items").GetArrayLength());
        Assert.AreEqual(first.GetProperty("total").GetInt64(), second.GetProperty("total").GetInt64());
        Assert.AreNotEqual(Ids(first).Single(), Ids(second).Single());
        await SendAsync(tenantClient, HttpMethod.Get, Candidates(instanceId, todoId, 0), expected: HttpStatusCode.BadRequest);
        await SendAsync(tenantClient, HttpMethod.Get, Candidates(instanceId, todoId, 1, 101), expected: HttpStatusCode.BadRequest);
        await SendAsync(tenantClient, HttpMethod.Get, Candidates(instanceId, Guid.CreateVersion7()), expected: HttpStatusCode.NotFound);
        await SendAsync(host, HttpMethod.Get, Candidates(instanceId, todoId), expected: HttpStatusCode.NotFound);
        using var anonymous = factory.CreateClientForHost("acme.localhost");
        await SendAsync(anonymous, HttpMethod.Get, Candidates(instanceId, todoId), expected: HttpStatusCode.Unauthorized);
        using var reader = factory.CreateClientForHost("acme.localhost");
        var readerToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, members[4].User.Username, FullNetApiFactory.TestPassword, token);
        reader.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, readerToken, tenant.Id, token));
        await SendAsync(reader, HttpMethod.Get, Candidates(instanceId, todoId), expected: HttpStatusCode.Forbidden);
        object Reassign(Guid target, string key) => new { assigneeUserId = target, expectedRevision = started.GetProperty("revision").GetInt64(),
            reason = "Recover removed assignee", idempotencyKey = key, todoId, expectedTodoRevision = 1 };
        await SendAsync(tenantClient, HttpMethod.Post, $"/api/v1/workflow/instances/{instanceId:D}/reassign", Reassign(members[0].User.UserId, "removed-target"), HttpStatusCode.BadRequest);
        var request = Reassign(members[1].User.UserId, "active-target");
        var reassigned = await SendAsync(tenantClient, HttpMethod.Post, $"/api/v1/workflow/instances/{instanceId:D}/reassign", request);
        var nextTodo = reassigned.GetProperty("activeTodoId").GetGuid(); Assert.AreNotEqual(todoId, nextTodo);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        Assert.AreEqual(members[1].User.UserId, await db.ExecuteScalarAsync<Guid>("SELECT AssigneeUserId FROM fn_workflow_todo WHERE Id=@nextTodo", new { nextTodo }));
        Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_todo WHERE Id=@todoId", new { todoId }));
        var replay = await SendAsync(tenantClient, HttpMethod.Post, $"/api/v1/workflow/instances/{instanceId:D}/reassign", request);
        Assert.AreEqual(reassigned.GetRawText(), replay.GetRawText());
        await SendAsync(tenantClient, HttpMethod.Get, Candidates(instanceId, todoId), expected: HttpStatusCode.Conflict);

        async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string path, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
        {
            using var message = new HttpRequestMessage(method, path); if (body is not null) message.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(message, token); var json = await response.Content.ReadAsStringAsync(token);
            Assert.AreEqual(expected, response.StatusCode, path + " " + json);
            if (string.IsNullOrWhiteSpace(json)) return default;
            using var document = JsonDocument.Parse(json); return document.RootElement.Clone();
        }
    }
    private static Guid[] Ids(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray();
    private static object Node(string key, string type, object config) => new { nodeKey = key, nodeTypeKey = type, nodeSchemaVersion = 1, config };
}
