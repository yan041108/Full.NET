using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Api;

/// <summary>本人 AI 摘要在正式双库中先隔离再分页，不调用模型或公开运行快照。</summary>
[TestClass]
public sealed class MyAgentTasksApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public async Task Mine_runs_bind_actor_tenant_filter_and_cursor_before_paging(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync();
        using var host = factory.CreateClientForHost("localhost");
        var hostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin");
        host.DefaultRequestHeaders.Authorization = new("Bearer", hostToken);
        var tenant = (await host.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(item => item.Identifier == "acme");
        var otherName = "agent-tasks-" + Guid.CreateVersion7().ToString("N")[..12];
        var other = await SendAsync(host, HttpMethod.Post, "/api/v1/tenancy/tenants",
            new ProvisionTenantRequest(otherName, otherName, otherName + ".localhost"), HttpStatusCode.Created);
        var otherTenantId = other.GetProperty("id").GetGuid();
        var name = "agent-tasks." + Guid.CreateVersion7().ToString("N");
        var identity = await factory.CreateHostIdentityAsync(name, ["tenancy.tenants.read", "tenancy.tenants.switch", "ai.agent_runs.read"],
            password: FullNetApiFactory.TestPassword);
        var email = name + "@example.com"; await factory.EnsureHostUserProfileEmailAsync(identity.UserId, email, name);
        await SendAsync(host, HttpMethod.Get, Mine, null, HttpStatusCode.Forbidden);
        host.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, hostToken, tenant.Id));
        var invitation = await SendAsync(host, HttpMethod.Post, "/api/v1/identity/tenant-members/invitations",
            new CreateTenantInvitationRequest(email, TenantMemberRoles.Member), HttpStatusCode.OK);
        using var member = factory.CreateClientForHost("localhost");
        var memberToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(member, name);
        member.DefaultRequestHeaders.Authorization = new("Bearer", memberToken);
        // 测试账号邮箱尚未验证，使用本次签发的一次性凭据证明邀请归属。
        await SendAsync(member, HttpMethod.Post, "/api/v1/identity/tenant-invitations/accept",
            new AcceptTenantInvitationRequest(invitation.GetProperty("invitationToken").GetString()!), HttpStatusCode.OK);
        member.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(member, memberToken, tenant.Id));
        await using var database = MyTasksApiTests.Open(provider, connection); await database.OpenAsync();
        var now = DateTimeOffset.UtcNow;
        var states = new[] { "queued", "running", "completed", "failed", "cancelled", "expired", "awaiting_approval",
            "authorization_required", "retry_scheduled", "reconciliation_required" };
        var owned = Enumerable.Range(0, 103).Select(i => Draft(tenant.Id, tenant.Id.ToString("N"), identity.UserId, now, states[i % states.Length])).ToArray();
        var foreign = Enumerable.Range(0, 20).SelectMany(_ => new[] {
            Draft(tenant.Id, tenant.Id.ToString("N"), Guid.CreateVersion7(), now, "completed"),
            Draft(otherTenantId, otherTenantId.ToString("N"), identity.UserId, now, "completed"),
            Draft(null, "host", identity.UserId, now, "completed"),
            Draft(tenant.Id, "host", identity.UserId, now, "completed") }).ToArray();
        await database.ExecuteAsync(AiAgentRunSql.InsertRun.Text, owned.Concat(foreign));
        await database.ExecuteAsync("UPDATE fn_ai_agent_run SET StatusKey=@StatusKey WHERE Id=@Id", owned.Concat(foreign));
        var expected = (await database.QueryAsync<Guid>("SELECT Id FROM fn_ai_agent_run WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND ActorUserId=@ActorUserId ORDER BY CreatedAtUtc DESC, Id DESC",
            new { TenantId = tenant.Id, ScopeKey = tenant.Id.ToString("N"), ActorUserId = identity.UserId })).ToArray();
        var seen = new List<Guid>(); string? cursor = null; string? firstCursor = null;
        do
        {
            var page = await SendAsync(member, HttpMethod.Get, Mine + "?pageSize=17" + (cursor is null ? "" : "&cursor=" + cursor), null, HttpStatusCode.OK);
            foreach (var row in page.GetProperty("items").EnumerateArray())
            {
                CollectionAssert.AreEquivalent(new[] { "id", "statusKey", "definitionKey", "definitionVersion", "deadlineAtUtc", "createdAtUtc", "updatedAtUtc" },
                    row.EnumerateObject().Select(property => property.Name).ToArray());
                seen.Add(row.GetProperty("id").GetGuid());
            }
            cursor = page.GetProperty("nextCursor").GetString(); firstCursor ??= cursor;
            Assert.AreEqual(cursor is not null, page.GetProperty("hasMore").GetBoolean());
        } while (cursor is not null);
        CollectionAssert.AreEqual(expected, seen.ToArray()); Assert.AreEqual(103, seen.Count);
        var clamped = await SendAsync(member, HttpMethod.Get, Mine + "?pageSize=1000&actorUserId=" + foreign[0].ActorUserId + "&tenantId=" + otherTenantId,
            null, HttpStatusCode.OK);
        Assert.AreEqual(100, clamped.GetProperty("items").GetArrayLength());
        var filtered = await SendAsync(member, HttpMethod.Get, Mine + "?statusKey=awaiting_approval&pageSize=100", null, HttpStatusCode.OK);
        Assert.AreEqual(10, filtered.GetProperty("items").GetArrayLength());
        Assert.IsTrue(filtered.GetProperty("items").EnumerateArray().All(row => row.GetProperty("statusKey").GetString() == "awaiting_approval"));
        foreach (var bad in new[] { "cursor=bad", "statusKey=bogus", "statusKey=completed&cursor=" + firstCursor })
            await SendAsync(member, HttpMethod.Get, Mine + "?" + bad, null, HttpStatusCode.BadRequest);
        var ownDetail = await SendAsync(member, HttpMethod.Get, Base + "/" + owned[0].Id, null, HttpStatusCode.OK);
        Assert.AreEqual(owned[0].Id, ownDetail.GetProperty("id").GetGuid());
        foreach (var id in new[] { foreign[0].Id, foreign[1].Id, Guid.Empty })
            await SendAsync(member, HttpMethod.Get, Base + "/" + id, null, HttpStatusCode.NotFound);
        await SendAsync(host, HttpMethod.Get, Mine + "?cursor=" + firstCursor, null, HttpStatusCode.BadRequest);
        host.DefaultRequestHeaders.Authorization = null;
        hostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin");
        host.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, hostToken, otherTenantId));
        await SendAsync(host, HttpMethod.Get, Mine + "?cursor=" + firstCursor, null, HttpStatusCode.BadRequest);
        var empty = await SendAsync(host, HttpMethod.Get, Mine, null, HttpStatusCode.OK);
        Assert.AreEqual(0, empty.GetProperty("items").GetArrayLength());
        using var denied = factory.CreateClientForHost("localhost");
        var deniedName = "agent-denied." + Guid.CreateVersion7().ToString("N");
        await factory.CreateHostIdentityAsync(deniedName, ["platform.my_tasks.read"], password: FullNetApiFactory.TestPassword);
        denied.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(denied, deniedName));
        await SendAsync(denied, HttpMethod.Get, Mine, null, HttpStatusCode.Forbidden);
        denied.DefaultRequestHeaders.Authorization = null;
        await SendAsync(denied, HttpMethod.Get, Mine, null, HttpStatusCode.Unauthorized);
    }

    private const string Base = "/api/v1/ai/agent/runs";
    private const string Mine = Base + "/mine";

    private static RunSeed Draft(Guid? tenantId, string scopeKey, Guid actor, DateTimeOffset now, string status) => new(tenantId, scopeKey, actor, now, status);

    private sealed record RunSeed(Guid? TenantId, string ScopeKey, Guid ActorUserId, DateTimeOffset Now, string StatusKey)
    {
        public Guid Id { get; } = Guid.CreateVersion7();
        public Guid SessionId { get; } = Guid.CreateVersion7();
        public string SessionKind => "refresh";
        public Guid ClientRequestId { get; } = Guid.CreateVersion7();
        public Guid AuthorizationBindingId { get; } = Guid.CreateVersion7();
        public string RequestHash => new('A', 64);
        public string DefinitionKey => "task-list-test";
        public int DefinitionVersion => 1;
        public string SecurityStamp => "PRIVATE_STAMP";
        public string ActorScope => "PRIVATE_ACTOR";
        public string EffectiveScope => "PRIVATE_EFFECTIVE";
        public string BudgetJson => "PRIVATE_BUDGET";
        public string? TraceParent => null;
        public DateTimeOffset DeadlineAtUtc => Now.AddHours(1);
    }

    private static async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string path, object? body, HttpStatusCode expected)
    {
        using var message = new HttpRequestMessage(method, path); if (body is not null) message.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(message); var json = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(expected, response.StatusCode, path + " " + json);
        if (expected == HttpStatusCode.OK && method == HttpMethod.Get && path.StartsWith(Base, StringComparison.Ordinal))
            Assert.AreEqual("no-store", response.Headers.CacheControl?.ToString());
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}
