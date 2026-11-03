using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Api;

/// <summary>方案以真实会话和双库元数据隔离；业务执行仍由样例原接口授权。</summary>
[TestClass]
public sealed class QuerySchemesApiTests
{
    private const string Grid = "enterprise_request.enterprise_requests";
    private const string Base = "/api/v1/me/query-schemes/" + Grid;
    private static readonly string[] Permissions = [QuerySchemePermissions.Read, QuerySchemePermissions.Create, QuerySchemePermissions.Update,
        QuerySchemePermissions.Delete, QuerySchemePermissions.Share, QuerySchemePermissions.SetDefault, "identity.navigation.read",
        "tenancy.tenants.read", "tenancy.tenants.switch", "enterprise_request.enterprise_requests.read"];

    [TestMethod]
    public Task Saved_queries_enforce_owner_scope_sharing_defaults_and_concurrency_with_sql_server() => VerifyAsync(DatabaseProvider.SqlServer);
    [TestMethod]
    public Task Saved_queries_enforce_owner_scope_sharing_defaults_and_concurrency_with_mysql() => VerifyAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection, configureTestServices: services => services.AddSingleton<IQuerySchemeContributor, HostTestCatalog>(), includeEnterpriseRequest: true);
        await factory.InitializeAsync(); using var administrator = factory.CreateClientForHost("localhost");
        var administratorToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(administrator, "admin"); administrator.DefaultRequestHeaders.Authorization = new("Bearer", administratorToken);
        var tenant = (await administrator.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(item => item.Identifier == "acme");
        var identifier = "queries-" + Guid.CreateVersion7().ToString("N")[..12];
        var otherTenant = await SendAsync(administrator, HttpMethod.Post, "/api/v1/tenancy/tenants", new ProvisionTenantRequest(identifier, identifier, identifier + ".localhost"), HttpStatusCode.Created);
        await SendAsync(administrator, HttpMethod.Get, Base + "/schema", null, HttpStatusCode.Forbidden);
        var hostScheme = await SendAsync(administrator, HttpMethod.Post, "/api/v1/me/query-schemes/test.host_grid", Save("Host", HostConfiguration()), HttpStatusCode.Created);
        var hostId = hostScheme.GetProperty("summary").GetProperty("id").GetGuid();
        await SendAsync(administrator, HttpMethod.Put, $"/api/v1/me/query-schemes/test.host_grid/{hostId:D}/sharing", new ShareQuerySchemeRequest(true, 1), HttpStatusCode.Forbidden);
        administratorToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(administrator, administratorToken, tenant.Id);
        administrator.DefaultRequestHeaders.Authorization = new("Bearer", administratorToken);
        var first = await MemberAsync("owner", Permissions); using var owner = first.Client;
        var second = await MemberAsync("reader", Permissions); using var reader = second.Client;
        var denied = await MemberAsync("denied", Permissions.Where(code => code != "enterprise_request.enterprise_requests.read").ToArray()); using var deniedClient = denied.Client;
        await SendAsync(deniedClient, HttpMethod.Get, Base + "/schema", null, HttpStatusCode.Forbidden);
        var schema = await SendAsync(owner, HttpMethod.Get, Base + "/schema", null, HttpStatusCode.OK);
        Assert.AreEqual(1, schema.GetProperty("schemaVersion").GetInt32()); Assert.IsFalse(schema.GetProperty("supportsHost").GetBoolean());
        // 共享的是筛选条件，普通成员仍须经过真实业务读取与数据范围解析。
        await SendAsync(reader, HttpMethod.Get, "/api/v1/enterprise_request/enterprise-requests?filterTitle=Literal_%25%5B~&sortBy=title", null, HttpStatusCode.OK);
        await SendAsync(owner, HttpMethod.Post, Base, Save("Old", Configuration(), schemaVersion: 99), HttpStatusCode.BadRequest);
        var created = await SendAsync(owner, HttpMethod.Post, Base, Save("Personal", Configuration()), HttpStatusCode.Created);
        var id = created.GetProperty("summary").GetProperty("id").GetGuid(); Assert.AreEqual(first.UserId, created.GetProperty("summary").GetProperty("ownerUserId").GetGuid());
        await SendAsync(reader, HttpMethod.Get, $"{Base}/{id:D}", null, HttpStatusCode.NotFound);
        await SendAsync(administrator, HttpMethod.Get, $"{Base}/{id:D}", null, HttpStatusCode.NotFound);
        var initial = await SendAsync(owner, HttpMethod.Get, Base + "/default", null, HttpStatusCode.OK); Assert.AreEqual(0L, initial.GetProperty("version").GetInt64());
        await SendAsync(owner, HttpMethod.Put, Base + "/default", new SetDefaultQuerySchemeRequest(id, 0), HttpStatusCode.OK);
        await SendAsync(owner, HttpMethod.Put, $"{Base}/{id:D}", Save("Changed", Configuration() with { Descending = true }, 1), HttpStatusCode.OK);
        await SendAsync(owner, HttpMethod.Put, $"{Base}/{id:D}", Save("Stale overwrite", Configuration(), 1), HttpStatusCode.Conflict);
        var current = await SendAsync(owner, HttpMethod.Get, $"{Base}/{id:D}", null, HttpStatusCode.OK); Assert.AreEqual("Changed", current.GetProperty("summary").GetProperty("name").GetString());
        await SendAsync(owner, HttpMethod.Put, $"{Base}/{id:D}/sharing", new ShareQuerySchemeRequest(true, 2), HttpStatusCode.OK);
        await SendAsync(reader, HttpMethod.Get, $"{Base}/{id:D}", null, HttpStatusCode.OK);
        await SendAsync(reader, HttpMethod.Put, $"{Base}/{id:D}", Save("Cannot edit creator", Configuration(), 3), HttpStatusCode.NotFound);
        await SendAsync(reader, HttpMethod.Delete, $"{Base}/{id:D}", new DeleteQuerySchemeRequest(3), HttpStatusCode.NotFound);
        await SendAsync(reader, HttpMethod.Put, $"{Base}/{id:D}/sharing", new ShareQuerySchemeRequest(false, 3), HttpStatusCode.NotFound);
        await SendAsync(reader, HttpMethod.Put, Base + "/default", new SetDefaultQuerySchemeRequest(id, 0), HttpStatusCode.OK);
        var shared = await SendAsync(reader, HttpMethod.Get, Base + "?shared=true", null, HttpStatusCode.OK); Assert.AreEqual(1, shared.GetProperty("items").GetArrayLength());
        Assert.IsFalse(shared.GetProperty("items")[0].TryGetProperty("configuration", out _));
        await SendAsync(owner, HttpMethod.Put, $"{Base}/{id:D}/sharing", new ShareQuerySchemeRequest(false, 3), HttpStatusCode.OK);
        await SendAsync(reader, HttpMethod.Get, $"{Base}/{id:D}", null, HttpStatusCode.NotFound);
        var revoked = await SendAsync(reader, HttpMethod.Get, Base + "/default", null, HttpStatusCode.OK);
        Assert.AreEqual(JsonValueKind.Null, revoked.GetProperty("scheme").ValueKind); Assert.IsTrue(revoked.GetProperty("isUnavailable").GetBoolean()); Assert.AreEqual(1L, revoked.GetProperty("version").GetInt64());
        await SendAsync(reader, HttpMethod.Put, Base + "/default", new SetDefaultQuerySchemeRequest(null, 0), HttpStatusCode.Conflict);
        await SendAsync(reader, HttpMethod.Put, Base + "/default", new SetDefaultQuerySchemeRequest(null, 1), HttpStatusCode.OK);
        await using var database = MyTasksApiTests.Open(provider, connection); await database.OpenAsync();
        await database.ExecuteAsync("UPDATE fn_settings_query_scheme SET SchemaVersion=99 WHERE Id=@Id", new { Id = id });
        var stale = await SendAsync(owner, HttpMethod.Get, $"{Base}/{id:D}", null, HttpStatusCode.OK);
        Assert.IsFalse(stale.GetProperty("summary").GetProperty("isCompatible").GetBoolean()); Assert.AreEqual(JsonValueKind.Null, stale.GetProperty("configuration").ValueKind);
        await SendAsync(owner, HttpMethod.Put, $"{Base}/{id:D}/sharing", new ShareQuerySchemeRequest(true, 4), HttpStatusCode.BadRequest);
        var badDefault = await SendAsync(owner, HttpMethod.Get, Base + "/default", null, HttpStatusCode.OK); Assert.IsTrue(badDefault.GetProperty("isUnavailable").GetBoolean());
        await SendAsync(owner, HttpMethod.Put, $"{Base}/{id:D}", Save("Repaired", Configuration(), 4), HttpStatusCode.OK);
        await SendAsync(owner, HttpMethod.Delete, $"{Base}/{id:D}", new DeleteQuerySchemeRequest(4), HttpStatusCode.Conflict);
        await SendAsync(owner, HttpMethod.Delete, $"{Base}/{id:D}", new DeleteQuerySchemeRequest(5), HttpStatusCode.OK);
        var cleared = await SendAsync(owner, HttpMethod.Get, Base + "/default", null, HttpStatusCode.OK); Assert.IsFalse(cleared.GetProperty("isUnavailable").GetBoolean()); Assert.AreEqual(2L, cleared.GetProperty("version").GetInt64());

        // 快照数据只用于把合法本人偏好推到上限边界，最后两个并发请求仍经过真实 API 及作用域行锁。
        var json = JsonSerializer.Serialize(Configuration(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var time = new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc).AddTicks(1234560);
        await database.ExecuteAsync("""
            INSERT INTO fn_settings_query_scheme (Id,TenantId,ScopeKey,GridKey,OwnerUserId,Name,SchemaVersion,ConfigurationJson,IsShared,CreatedAtUtc,UpdatedAtUtc,Version)
            VALUES (@Id,@TenantId,@ScopeKey,@GridKey,@UserId,@Name,1,@Json,0,@Now,NULL,1)
            """, Enumerable.Range(0, 31).Select(index => new { Id = Guid.CreateVersion7(), TenantId = tenant.Id, GridKey = Grid, ScopeKey = $"tenant:{tenant.Id:N}", UserId = first.UserId, Name = "Bound " + index, Json = json, Now = time }));
        var attempts = await Task.WhenAll(CreateAsync("Last slot"), CreateAsync("Rejected overflow"));
        CollectionAssert.AreEquivalent(new[] { HttpStatusCode.Created, HttpStatusCode.BadRequest }, attempts);
        Assert.AreEqual(32, await database.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM fn_settings_query_scheme WHERE TenantId=@TenantId AND OwnerUserId=@UserId", new { TenantId = tenant.Id, UserId = first.UserId }));
        var seen = new List<Guid>(); string? cursor = null; string? firstCursor = null;
        do
        {
            var result = await SendAsync(owner, HttpMethod.Get, Base + "?pageSize=7" + (cursor is null ? "" : "&cursor=" + cursor), null, HttpStatusCode.OK);
            var pageIds = result.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray();
            Assert.IsFalse(pageIds.Any(seen.Contains), "游标必须前进，不能因时间参数舍入重复返回已读取的行。");
            seen.AddRange(pageIds); cursor = result.GetProperty("nextCursor").GetString(); firstCursor ??= cursor;
        } while (cursor is not null);
        Assert.AreEqual(32, seen.Distinct().Count()); Assert.AreEqual(32, seen.Count);
        await SendAsync(reader, HttpMethod.Get, Base + "?cursor=" + firstCursor, null, HttpStatusCode.BadRequest);
        await SendAsync(owner, HttpMethod.Get, Base + "?shared=true&cursor=" + firstCursor, null, HttpStatusCode.BadRequest);
        await SendAsync(owner, HttpMethod.Get, Base + "?cursor=bad", null, HttpStatusCode.BadRequest);
        var isolated = await SendAsync(owner, HttpMethod.Get, "/api/v1/me/query-schemes/test.other_grid", null, HttpStatusCode.OK); Assert.AreEqual(0, isolated.GetProperty("items").GetArrayLength());
        await SendAsync(owner, HttpMethod.Get, "/api/v1/me/query-schemes/test.other_grid?cursor=" + firstCursor, null, HttpStatusCode.BadRequest);
        administrator.DefaultRequestHeaders.Authorization = null; administratorToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(administrator, "admin");
        administrator.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(administrator, administratorToken, otherTenant.GetProperty("id").GetGuid()));
        var other = await SendAsync(administrator, HttpMethod.Get, Base, null, HttpStatusCode.OK); Assert.AreEqual(0, other.GetProperty("items").GetArrayLength());
        await SendAsync(administrator, HttpMethod.Get, Base + "?cursor=" + firstCursor, null, HttpStatusCode.BadRequest);
        await SendAsync(administrator, HttpMethod.Get, $"{Base}/{seen[0]:D}", null, HttpStatusCode.NotFound);
        await VerifyMigrationRecoveryAsync(database, provider);
        await QuerySchemeQueryEvidence.CaptureAsync(database, provider);

        async Task<(HttpClient Client, Guid UserId)> MemberAsync(string suffix, string[] permissions)
        {
            var username = "queries." + suffix + "." + Guid.CreateVersion7().ToString("N"); var identity = await factory.CreateHostIdentityAsync(username, permissions, password: FullNetApiFactory.TestPassword);
            var email = username + "@example.com"; await factory.EnsureHostUserProfileEmailAsync(identity.UserId, email, username);
            var invitation = await SendAsync(administrator, HttpMethod.Post, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member), HttpStatusCode.OK);
            var client = factory.CreateClientForHost("localhost"); var hostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, username); client.DefaultRequestHeaders.Authorization = new("Bearer", hostToken);
            await SendAsync(client, HttpMethod.Post, $"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { }, HttpStatusCode.OK);
            client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, hostToken, tenant.Id)); return (client, identity.UserId);
        }
        async Task<HttpStatusCode> CreateAsync(string name)
        {
            using var response = await owner.PostAsJsonAsync(Base, Save(name, Configuration()));
            if (response.StatusCode == HttpStatusCode.BadRequest)
                Assert.AreEqual(SettingsErrorCodes.QuerySchemeLimitReached, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
            return response.StatusCode;
        }
    }

    private static async Task VerifyMigrationRecoveryAsync(DbConnection database, DatabaseProvider provider)
    {
        var count = await database.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM fn_settings_query_scheme");
        await database.ExecuteAsync("DROP TABLE fn_settings_query_scheme_default");
        var file = Path.Combine(FullNetApiFactory.FindRepositoryRoot(), "src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations",
            provider == DatabaseProvider.SqlServer ? "SqlServer" : "MySql", "355_SettingsQuerySchemes.sql");
        var script = await File.ReadAllTextAsync(file);
        for (var repeat = 0; repeat < 2; repeat++)
            foreach (var batch in provider == DatabaseProvider.SqlServer ? Regex.Split(script, @"(?im)^GO\s*$") : [script])
                if (!string.IsNullOrWhiteSpace(batch)) await database.ExecuteAsync(batch);
        Assert.AreEqual(count, await database.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM fn_settings_query_scheme"));
        Assert.AreEqual(0, await database.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM fn_settings_query_scheme_default"));
    }

    private static QuerySchemeConfiguration Configuration() => new([new("title", "contains", "Literal_%[~")], "title", false,
        [new("requestNumber", 0, null, true, null), new("title", 1, 240, true, null), new("status", 2, null, false, null)]);
    private static QuerySchemeConfiguration HostConfiguration() => new([], "id", false, [new("name", 0, null, true, null)]);
    private static SaveQuerySchemeRequest Save(string name, QuerySchemeConfiguration configuration, long version = 0, int schemaVersion = 1) => new(name, schemaVersion, configuration, version);
    private static async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string path, object? body, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, path); if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request); var json = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, path + " " + json);
        return JsonDocument.Parse(json).RootElement.Clone();
    }
    private sealed class HostTestCatalog : IQuerySchemeContributor
    {
        public IReadOnlyCollection<QuerySchemeDefinition> Definitions { get; } =
        [new("test.host_grid",1,"identity.users.read",true,false,[],["id"],["name"]),
            new("test.other_grid",1,"enterprise_request.enterprise_requests.read",false,true,[],["id"],["title"])];
    }
}
