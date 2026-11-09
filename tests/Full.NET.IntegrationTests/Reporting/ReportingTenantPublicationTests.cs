using System.IO.Compression;
using System.Data.Common;
using Dapper;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.Abstractions.Tenancy;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Features.ManageExportTasks;
using Full.NET.Modules.Reporting.Persistence;
using Full.NET.Modules.Reporting.Serialization;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Reporting;

/// <summary>真实成员、授权 SQL、后台循环和文件落库均使用生产实现；只替代外部数据库连接以隔离网络。</summary>
[TestClass]
public sealed class ReportingTenantPublicationTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Tenant_publication_worker_and_download_recheck_grant_and_original_session(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var external = new ControlledExternalDatabase();
        using var factory = new FullNetApiFactory(provider, connection, configureTestServices: services => services.AddSingleton<IExternalDatabaseConnectionFactory>(external));
        await factory.InitializeAsync();
        using var host = factory.CreateClientForHost("localhost");
        var hostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin");
        host.DefaultRequestHeaders.Authorization = new("Bearer", hostToken);
        var tenant = (await host.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(item => item.Identifier == "acme");
        var source = await SendAsync(host, HttpMethod.Post, "/api/v1/reporting/data-sources",
            new CreateReportingDataSourceRequest(null, "Published source", provider == DatabaseProvider.SqlServer ? "sql_server" : "mysql",
                "reports.example.com", provider == DatabaseProvider.SqlServer ? 1433 : 3306, "reporting", "readonly", "Test!Password2026", false, true), HttpStatusCode.Created);
        var group = await SendAsync(host, HttpMethod.Post, "/api/v1/reporting/groups", new CreateReportingGroupRequest(null, "Tenant publications", 1, true), HttpStatusCode.Created);
        var otherIdentifier = "report-" + Guid.CreateVersion7().ToString("N")[..16];
        var otherTenant = await SendAsync(host, HttpMethod.Post, "/api/v1/tenancy/tenants", new ProvisionTenantRequest(otherIdentifier, "Other reporting tenant", otherIdentifier + ".localhost"), HttpStatusCode.Created);
        var otherSource = await SendAsync(host, HttpMethod.Post, "/api/v1/reporting/data-sources",
            new CreateReportingDataSourceRequest(otherTenant.GetProperty("id").GetGuid(), "Other tenant source", provider == DatabaseProvider.SqlServer ? "sql_server" : "mysql",
                "reports.example.com", provider == DatabaseProvider.SqlServer ? 1433 : 3306, "reporting", "readonly", "Test!Password2026", false, true), HttpStatusCode.Created);
        var publishedId = await CreateDefinitionAsync("visible");
        _ = await CreateDefinitionAsync("hidden");
        var restrictedId = await CreateDefinitionAsync("other_tenant", otherSource.GetProperty("id").GetGuid());
        using var owner = factory.CreateClientForHost("localhost");
        var ownerToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(owner, "admin");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, ownerToken, tenant.Id));
        var username = "report.member." + Guid.CreateVersion7().ToString("N");
        var identity = await factory.CreateHostIdentityAsync(username, [ReportingExportTaskPermissions.Create, ReportingExportTaskPermissions.Read,
            ReportingExportTaskPermissions.Download, ReportingDefinitionGrantPermissions.PublishedRead, ReportingExecutionPermissions.ColumnSchemaName,
            "identity.navigation.read", "tenancy.tenants.read", "tenancy.tenants.switch"], password: FullNetApiFactory.TestPassword);
        var email = username + "@example.com"; await factory.EnsureHostUserProfileEmailAsync(identity.UserId, email, username);
        var invitation = await SendAsync(owner, HttpMethod.Post, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member), HttpStatusCode.OK);
        using var member = factory.CreateClientForHost("localhost");
        var memberHostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(member, username);
        member.DefaultRequestHeaders.Authorization = new("Bearer", memberHostToken);
        // 测试账号邮箱未验证，使用正式的一次性凭据接受本次邀请。
        await SendAsync(member, HttpMethod.Post, "/api/v1/identity/tenant-invitations/accept",
            new AcceptTenantInvitationRequest(invitation.GetProperty("invitationToken").GetString()!), HttpStatusCode.OK);
        member.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(member, memberHostToken, tenant.Id));
        Assert.IsFalse((await member.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("isSuperAdministrator").GetBoolean());
        // 默认没有租户发布；没有授权时分页总数也必须为零。
        var empty = await SendAsync(member, HttpMethod.Get, "/api/v1/reporting/published-definitions?page=1&pageSize=1", null, HttpStatusCode.OK);
        Assert.AreEqual(0, empty.GetProperty("total").GetInt32());
        await SendAsync(member, HttpMethod.Post, "/api/v1/reporting/export-tasks", CreateTask(), HttpStatusCode.NotFound);
        host.DefaultRequestHeaders.Authorization = null;
        host.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin"));
        await using (DbConnection legacy = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection)))
        {
            await legacy.OpenAsync();
            const string selector = "ScopeKey='host' AND TenantId IS NULL AND IsSystem=1 AND MenuType='directory' AND RouteName='domain-data-intelligence' AND Path='/domains/data-intelligence'";
            Assert.AreEqual(1, await legacy.ExecuteAsync("UPDATE fn_identity_navigation SET RequiredPermission=@PreviousPermission WHERE " + selector,
                new { PreviousPermission = global::Full.NET.Modules.Identity.IdentityAuthorizationContributor.DashboardRead }));
            var before = await legacy.ExecuteScalarAsync<long>("SELECT Version FROM fn_identity_navigation WHERE " + selector);
            await SendAsync(host, HttpMethod.Post, "/api/v1/identity/menus/sync-catalog", null, HttpStatusCode.OK);
            Assert.AreEqual(before + 1, await legacy.ExecuteScalarAsync<long>("SELECT Version FROM fn_identity_navigation WHERE " + selector));
            await SendAsync(host, HttpMethod.Post, "/api/v1/identity/menus/sync-catalog", null, HttpStatusCode.OK);
            Assert.AreEqual(before + 1, await legacy.ExecuteScalarAsync<long>("SELECT Version FROM fn_identity_navigation WHERE " + selector));
        }
        var grantPath = $"/api/v1/reporting/definitions/{publishedId:D}/tenant-grants/{tenant.Id:D}";
        var grant = await SendAsync(host, HttpMethod.Put, grantPath, new SetReportingDefinitionTenantGrantRequest(true, 0), HttpStatusCode.OK);
        var incompatible = await SendAsync(host, HttpMethod.Put, $"/api/v1/reporting/definitions/{restrictedId:D}/tenant-grants/{tenant.Id:D}",
            new SetReportingDefinitionTenantGrantRequest(true, 0), HttpStatusCode.BadRequest);
        Assert.AreEqual(ReportingErrorCodes.GrantSourceUnavailable, incompatible.GetProperty("code").GetString());
        await SendAsync(host, HttpMethod.Put, grantPath, new SetReportingDefinitionTenantGrantRequest(false, 0), HttpStatusCode.Conflict);
        var catalog = await SendAsync(member, HttpMethod.Get, "/api/v1/reporting/published-definitions?page=1&pageSize=1", null, HttpStatusCode.OK);
        Assert.AreEqual(1, catalog.GetProperty("total").GetInt32()); Assert.AreEqual(publishedId, catalog.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.IsFalse(catalog.GetProperty("items")[0].TryGetProperty("dataSourceId", out _));
        var navigation = (await member.GetFromJsonAsync<NavigationNodeResponse[]>("/api/v1/navigation"))!;
        var routes = Flatten(navigation).Select(item => item.RouteName).ToArray();
        CollectionAssert.Contains(routes, "reporting-export-tasks"); CollectionAssert.DoesNotContain(routes, "reporting-data-sources");
        await SendAsync(member, HttpMethod.Get, $"/api/v1/reporting/definitions/{publishedId:D}", null, HttpStatusCode.Forbidden);
        await SendAsync(member, HttpMethod.Get, $"/api/v1/reporting/data-sources/{source.GetProperty("id").GetGuid():D}", null, HttpStatusCode.Forbidden);
        var created = await SendAsync(member, HttpMethod.Post, "/api/v1/reporting/export-tasks", CreateTask(), HttpStatusCode.Created);
        Assert.AreEqual("queued", created.GetProperty("statusKey").GetString()); var taskId = created.GetProperty("id").GetGuid();
        using (var worker = await BuildWorkerAsync())
        {
            await worker.StartAsync(); await WaitAsync(taskId, "succeeded"); await worker.StopAsync();
        }
        Assert.AreEqual(1, external.QueryCount);
        var detailPath = $"/api/v1/reporting/export-tasks/{taskId:D}";
        var ready = await member.GetFromJsonAsync<ReportingExportTaskDetailResponse>(detailPath);
        Assert.IsNotNull(ready);
        Assert.AreEqual(FileAvailabilityKeys.Registered, ready.OutputAvailabilityKey);
        Assert.IsNotNull(ready.OutputFileId);
        using (var download = await member.GetAsync($"/api/v1/reporting/export-tasks/{taskId:D}/download"))
        {
            Assert.AreEqual(HttpStatusCode.OK, download.StatusCode, await download.Content.ReadAsStringAsync());
            using var zip = new ZipArchive(new MemoryStream(await download.Content.ReadAsByteArrayAsync()));
            Assert.IsNotNull(zip.GetEntry("xl/worksheets/sheet1.xml"));
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetTenant(new(tenant.Id, tenant.Identifier, tenant.Name));
            await scope.ServiceProvider.GetRequiredService<ITenantResourceFileStore>()
                .ReleaseAsync("reporting", taskId, ready.OutputFileId.Value);
        }
        var released = await member.GetFromJsonAsync<ReportingExportTaskDetailResponse>(detailPath);
        Assert.AreEqual(FileAvailabilityKeys.Released, released!.OutputAvailabilityKey);
        Assert.AreEqual(ready.StatusKey, released.StatusKey);
        Assert.AreEqual(ready.OutputFileId, released.OutputFileId);
        owner.DefaultRequestHeaders.Authorization = null;
        var alternateHost = await IntegrationTestAuthHelper.LoginAsHostUserAsync(owner, "admin");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, alternateHost, tenant.Id));
        Assert.IsNull((await owner.GetFromJsonAsync<ReportingExportTaskDetailResponse>(detailPath))!.OutputAvailabilityKey);
        await SendAsync(owner, HttpMethod.Get, $"/api/v1/reporting/export-tasks/{taskId:D}/download", null, HttpStatusCode.Forbidden);
        host.DefaultRequestHeaders.Authorization = null;
        host.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin"));
        // 停止循环后排队，再撤销授权；下一 Worker 必须先拒绝，不能读取外部源或绑定既有对象。
        var queued = await SendAsync(member, HttpMethod.Post, "/api/v1/reporting/export-tasks", CreateTask(), HttpStatusCode.Created);
        await SendAsync(host, HttpMethod.Put, grantPath, new SetReportingDefinitionTenantGrantRequest(false, grant.GetProperty("version").GetInt32()), HttpStatusCode.OK);
        Assert.IsNull((await member.GetFromJsonAsync<ReportingExportTaskDetailResponse>(detailPath))!.OutputAvailabilityKey);
        await SendAsync(member, HttpMethod.Get, $"/api/v1/reporting/export-tasks/{taskId:D}/download", null, HttpStatusCode.Forbidden);
        using (var worker = await BuildWorkerAsync())
        {
            await worker.StartAsync(); var failed = await WaitAsync(queued.GetProperty("id").GetGuid(), "failed");
            Assert.AreEqual(ReportingErrorCodes.ExportAuthorityDenied, failed.GetProperty("errorCode").GetString()); await worker.StopAsync();
        }
        Assert.AreEqual(1, external.QueryCount);
        await SendAsync(host, HttpMethod.Put, grantPath, new SetReportingDefinitionTenantGrantRequest(true, 2), HttpStatusCode.OK);
        var sessionTask = await SendAsync(member, HttpMethod.Post, "/api/v1/reporting/export-tasks", CreateTask(), HttpStatusCode.Created);
        // 单客户端重新登录撤销旧会话；新会话即使仍有权限，也不能继承旧任务的下载身份。
        member.DefaultRequestHeaders.Authorization = null;
        var replacement = await IntegrationTestAuthHelper.LoginAsHostUserAsync(member, username);
        member.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(member, replacement, tenant.Id));
        await SendAsync(member, HttpMethod.Get, $"/api/v1/reporting/export-tasks/{taskId:D}/download", null, HttpStatusCode.Forbidden);
        using (var worker = await BuildWorkerAsync())
        {
            await worker.StartAsync(); await WaitAsync(sessionTask.GetProperty("id").GetGuid(), "failed"); await worker.StopAsync();
        }
        Assert.AreEqual(1, external.QueryCount);

        var fieldTask = await SendAsync(member, HttpMethod.Post, "/api/v1/reporting/export-tasks", CreateTask(), HttpStatusCode.Created);
        var roles = (await host.GetFromJsonAsync<HostUserRolesResponse>($"/api/v1/identity/users/{identity.UserId:D}/roles"))!;
        var role = (await host.GetFromJsonAsync<HostRoleResponse>($"/api/v1/identity/roles/{roles.RoleIds.Single():D}"))!;
        await SendAsync(host, HttpMethod.Put, $"/api/v1/identity/roles/{role.Id:D}/permissions", new ReplaceHostRolePermissionsRequest(
            role.PermissionCodes.Where(code => code != ReportingExecutionPermissions.ColumnSchemaName).ToArray(), role.Version), HttpStatusCode.OK);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetTenant(new TenantContext(tenant.Id, tenant.Identifier, tenant.Name));
            var stored = await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<ReportingExportTaskRecord>(
                ReportingExportTaskSql.FindByIdFor(provider), new Dictionary<string, object?> { ["Id"] = fieldTask.GetProperty("id").GetGuid() });
            var binding = JsonSerializer.Deserialize(stored!.ActorSessionBindingJson!, ReportingJsonSerializerContext.Default.SessionBindingSnapshot)!;
            Assert.IsTrue(ReportingExportTaskMapper.DeserializePermissionCodes(stored.ActorPermissionCodesJson).Contains(ReportingExecutionPermissions.ColumnSchemaName));
            // 真实角色更新会轮换安全戳并撤销会话，不伪造仍有效的绑定；独立字段校验另有真实授权器单测。
            Assert.IsFalse(await scope.ServiceProvider.GetRequiredService<IBackgroundSessionBindingValidator>().IsValidAsync(binding));
        }
        member.DefaultRequestHeaders.Authorization = null;
        replacement = await IntegrationTestAuthHelper.LoginAsHostUserAsync(member, username);
        member.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(member, replacement, tenant.Id));
        var reduced = await member.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.IsFalse(reduced.GetProperty("permissions").EnumerateArray().Any(code => code.GetString() == ReportingExecutionPermissions.ColumnSchemaName));
        using (var worker = await BuildWorkerAsync())
        {
            await worker.StartAsync(); await WaitAsync(fieldTask.GetProperty("id").GetGuid(), "failed"); await worker.StopAsync();
        }
        Assert.AreEqual(1, external.QueryCount);
        await SendAsync(host, HttpMethod.Put, $"/api/v1/reporting/data-sources/{source.GetProperty("id").GetGuid():D}", new UpdateReportingDataSourceRequest(
            "Published source", provider == DatabaseProvider.SqlServer ? "sql_server" : "mysql", "reports.example.com", provider == DatabaseProvider.SqlServer ? 1433 : 3306,
            "reporting", "readonly", null, false, false, source.GetProperty("version").GetInt32()), HttpStatusCode.OK);
        var unavailable = await SendAsync(member, HttpMethod.Get, "/api/v1/reporting/published-definitions", null, HttpStatusCode.OK);
        Assert.AreEqual(0, unavailable.GetProperty("total").GetInt32());
        await SendAsync(member, HttpMethod.Post, "/api/v1/reporting/export-tasks", CreateTask(), HttpStatusCode.NotFound);
        owner.DefaultRequestHeaders.Authorization = null;
        alternateHost = await IntegrationTestAuthHelper.LoginAsHostUserAsync(owner, "admin");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, alternateHost, otherTenant.GetProperty("id").GetGuid()));
        await SendAsync(owner, HttpMethod.Get, $"/api/v1/reporting/export-tasks/{taskId:D}", null, HttpStatusCode.NotFound);
        var isolated = await SendAsync(owner, HttpMethod.Get, "/api/v1/reporting/published-definitions", null, HttpStatusCode.OK);
        Assert.AreEqual(0, isolated.GetProperty("total").GetInt32());

        CreateReportingExportTaskRequest CreateTask() => new(publishedId, "excel", 1, [new("topN", "20")]);
        async Task<Guid> CreateDefinitionAsync(string key, Guid? sourceId = null)
        {
            var value = await SendAsync(host, HttpMethod.Post, "/api/v1/reporting/definitions", new CreateReportingDefinitionRequest(
                group.GetProperty("id").GetGuid(), sourceId ?? source.GetProperty("id").GetGuid(), key, key, null,
                "reporting.schema_inventory", [new("topN", "Limit", ReportingParameterDataTypeKeys.Integer, true, "20")],
                JsonSerializer.Serialize(new { columns = new[] { new { key = "SchemaName", displayName = "Schema", requiredPermission = ReportingExecutionPermissions.ColumnSchemaName } } }), true), HttpStatusCode.Created);
            var id = value.GetProperty("id").GetGuid();
            await SendAsync(host, HttpMethod.Post, $"/api/v1/reporting/definitions/{id:D}/publish",
                new PublishReportingDefinitionRequest("Tenant publication", value.GetProperty("version").GetInt32()), HttpStatusCode.OK);
            return id;
        }
        Task<IHost> BuildWorkerAsync() => IntegrationWorkerHostFactory.BuildAsync(provider, connection,
            new Dictionary<string, string?> { ["FullNet:Reporting:Export:PollSeconds"] = "5" }, "Reporting.Publication.Worker", nameof(ReportingExportTaskHostedProcessor),
            fileRootPath: factory.Services.GetRequiredService<IConfiguration>()["Files:Local:RootPath"], configureServices: services =>
            {
                services.AddSingleton(factory.Services.GetRequiredService<IDataProtectionProvider>());
                services.AddSingleton<IExternalDatabaseConnectionFactory>(external);
            });
        async Task<JsonElement> WaitAsync(Guid id, string status)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
            while (true)
            {
                var value = await SendAsync(member, HttpMethod.Get, $"/api/v1/reporting/export-tasks/{id:D}", null, HttpStatusCode.OK);
                if (value.GetProperty("statusKey").GetString() == status) return value;
                if (DateTimeOffset.UtcNow >= deadline) Assert.Fail(value.ToString());
                await Task.Delay(200);
            }
        }
    }

    private static IEnumerable<NavigationNodeResponse> Flatten(IEnumerable<NavigationNodeResponse> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Flatten(node.Children)));

    private static async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string path, object? body, HttpStatusCode status)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var response = await client.SendAsync(request); var text = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(status, response.StatusCode, $"{method} {path}: {text}");
        return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>仅替换外部连接端口；执行仍经过固定版本、静态 SQL、权限裁列和实际 XLSX 渲染。</summary>
    internal sealed class ControlledExternalDatabase : IExternalDatabaseConnectionFactory, IExternalDatabaseSession
    {
        private int queryCount;
        internal int QueryCount => Volatile.Read(ref queryCount);
        public Task<ExternalDatabaseSessionResult> OpenAsync(ExternalDatabaseConnectionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ExternalDatabaseSessionResult(true, null, this));
        public Task<ExternalDatabaseScalarResult> ExecuteScalarAsync(string sql, int commandTimeoutSeconds, CancellationToken cancellationToken = default)
            => Task.FromResult(new ExternalDatabaseScalarResult(true, null, "1"));
        public Task<ExternalDatabaseQueryResult> ExecuteQueryAsync(string sql, IReadOnlyDictionary<string, object?> parameters,
            int commandTimeoutSeconds, int maxCellValueLength, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref queryCount);
            var column = sql.Contains("SchemaName", StringComparison.Ordinal) ? "SchemaName" : "EngineVersion";
            Assert.IsTrue(column == "SchemaName" || sql.Contains("SERVERPROPERTY", StringComparison.Ordinal) || sql.Contains("VERSION()", StringComparison.Ordinal));
            return Task.FromResult(new ExternalDatabaseQueryResult(true, null, [column], [new Dictionary<string, string?> { [column] = "2026.1" }]));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
