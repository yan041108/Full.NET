using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.ImportExport;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Persistence;
using Full.NET.Modules.Reporting.Domain;
using Full.NET.Modules.Reporting.Security;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Reporting;

/// <summary>双主库验收发布目录、版本授权、真实外部只读查询、导出与下载，不替换业务或授权 Port。</summary>
[TestClass]
public sealed class ReportingTenantPublishedApiTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.MySql)]
    [DataRow(DatabaseProvider.SqlServer)]
    public async Task Granted_version_executes_exports_and_revokes(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.MySql ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync()
            : await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        // 两种主库均连接同一正式 SQL Server 外部驱动；MySQL VerifyFull 外部 TLS 不以替身或降级证书验收。
        var external = new SqlConnectionStringBuilder(await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
        var target = external.DataSource.Split(','); Assert.AreEqual(2, target.Length);
        var host = target[0]; var port = int.Parse(target[1], System.Globalization.CultureInfo.InvariantCulture);
        using var api = new FullNetApiFactory(provider, cs, new Dictionary<string, string?>
        {
            ["FullNet:ExternalDatabaseAccess:AllowedDestinations:0:Provider"] = "SqlServer",
            ["FullNet:ExternalDatabaseAccess:AllowedDestinations:0:Host"] = host,
            ["FullNet:ExternalDatabaseAccess:AllowedDestinations:0:Port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["FullNet:ExternalDatabaseAccess:AllowedDestinations:0:AllowUntrustedCertificate"] = "true",
        });
        await api.InitializeAsync(); using var client = api.CreateClientForHost("localhost");
        var tenantToken = await ImportExportTaskAssertions.LoginAndEnterAcmeTenantAsync(client, default);
        var hostToken = await api.CreateHostAccessTokenAsync([ReportingDefinitionPermissions.GrantTenants]);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tenantToken);
        var tenantId = Guid.Parse(jwt.Claims.Single(c => c.Type == FullNetIdentityClaimTypes.TenantId).Value);
        var definition = await SeedPublishedAsync(api, external, host, port, Guid.Parse(jwt.Subject));
        var grants = $"/api/v1/reporting/definitions/{definition:D}/versions/1/tenant-grants";
        using (var initial = await SendAsync(client, HttpMethod.Get, grants, hostToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, initial.StatusCode);
            Assert.AreEqual(0L, (await initial.Content.ReadFromJsonAsync<PagedResult<Guid>>())!.Total);
        }
        using (var denied = await SendAsync(client, HttpMethod.Get, grants, tenantToken))
            Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        var readOnlyHost = await api.CreateHostAccessTokenAsync([ReportingDefinitionPermissions.Read]);
        using (var denied = await SendAsync(client, HttpMethod.Get, grants, readOnlyHost))
            Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var anonymous = await client.GetAsync(grants)) Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var catalog = "/api/v1/reporting/published-definitions";
        using (var empty = await SendAsync(client, HttpMethod.Get, catalog, tenantToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, empty.StatusCode);
            Assert.AreEqual(0, (await empty.Content.ReadFromJsonAsync<ReportingPublishedDefinitionResponse[]>())!.Length);
        }
        var execute = $"/api/v1/reporting/definitions/{definition:D}/execute";
        var parameters = new[] { new ReportingExecutionParameterValue("topN", "20") };
        using (var denied = await SendAsync(client, HttpMethod.Post, execute, tenantToken,
            new ExecuteReportingDefinitionRequest(null, parameters))) Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        var grant = $"/api/v1/reporting/definitions/{definition:D}/versions/1/tenant-grants/{tenantId:D}";
        using (var granted = await SendAsync(client, HttpMethod.Put, grant, hostToken)) Assert.AreEqual(HttpStatusCode.OK, granted.StatusCode);
        using (var duplicate = await SendAsync(client, HttpMethod.Put, grant, hostToken)) Assert.AreEqual(HttpStatusCode.OK, duplicate.StatusCode);
        using (var firstPage = await SendAsync(client, HttpMethod.Get, grants + "?page=1&pageSize=1", hostToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, firstPage.StatusCode);
            var page = (await firstPage.Content.ReadFromJsonAsync<PagedResult<Guid>>())!;
            Assert.AreEqual(1L, page.Total); Assert.AreEqual(1, page.Page); Assert.AreEqual(1, page.PageSize);
            CollectionAssert.AreEqual(new[] { tenantId }, page.Items.ToArray());
        }
        using (var secondPage = await SendAsync(client, HttpMethod.Get, grants + "?page=2&pageSize=1", hostToken))
        {
            var page = (await secondPage.Content.ReadFromJsonAsync<PagedResult<Guid>>())!;
            Assert.AreEqual(1L, page.Total); Assert.AreEqual(0, page.Items.Count);
        }
        using (var anotherVersion = await SendAsync(client, HttpMethod.Get, grants.Replace("/versions/1/", "/versions/2/", StringComparison.Ordinal), hostToken))
            Assert.AreEqual(0L, (await anotherVersion.Content.ReadFromJsonAsync<PagedResult<Guid>>())!.Total);
        foreach (var query in new[] { "?page=0", "?pageSize=201" })
        {
            using var invalid = await SendAsync(client, HttpMethod.Get, grants + query, hostToken);
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using (var list = await SendAsync(client, HttpMethod.Get, catalog, tenantToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, list.StatusCode);
            var published = (await list.Content.ReadFromJsonAsync<ReportingPublishedDefinitionResponse[]>())!;
            Assert.AreEqual(1, published.Length); Assert.AreEqual(1, published[0].VersionNumber);
            Assert.AreEqual(definition, published[0].DefinitionId);
            Assert.AreEqual("topN", published[0].ParameterSchema.Single().ParameterKey);
            Assert.AreEqual("20", published[0].ParameterSchema.Single().DefaultValue);
            Assert.IsFalse((await list.Content.ReadAsStringAsync()).Contains("passwordProtected", StringComparison.Ordinal));
            Assert.IsFalse((await list.Content.ReadAsStringAsync()).Contains("serverHost", StringComparison.Ordinal));
        }
        using (var configuration = await SendAsync(client, HttpMethod.Get, "/api/v1/reporting/definitions", tenantToken))
            Assert.AreEqual(HttpStatusCode.Forbidden, configuration.StatusCode);
        using (var result = await SendAsync(client, HttpMethod.Post, execute, tenantToken, new ExecuteReportingDefinitionRequest(null, parameters)))
        {
            Assert.AreEqual(HttpStatusCode.OK, result.StatusCode, await result.Content.ReadAsStringAsync());
            var page = (await result.Content.ReadFromJsonAsync<ReportingExecutionPageResponse>())!;
            Assert.AreEqual(1, page.VersionNumber); Assert.IsGreaterThan(0, page.Rows.Count);
            Assert.IsTrue(page.Columns.Any(column => column.ColumnKey == "SchemaName"));
            // 正式 HTTP 字典键必须与静态列键一致，否则客户端按列读取会显示空值。
            Assert.IsTrue(page.Rows[0].Values.TryGetValue("SchemaName", out var schemaName));
            Assert.IsFalse(string.IsNullOrWhiteSpace(schemaName));
        }
        using (var futureVersion = await SendAsync(client, HttpMethod.Post, execute, tenantToken, new ExecuteReportingDefinitionRequest(2, parameters)))
            Assert.AreEqual(HttpStatusCode.Forbidden, futureVersion.StatusCode);
        Guid taskId;
        using (var exported = await SendAsync(client, HttpMethod.Post, "/api/v1/reporting/export-tasks", tenantToken,
            new CreateReportingExportTaskRequest(definition, "excel", null, parameters)))
        {
            Assert.AreEqual(HttpStatusCode.Created, exported.StatusCode, await exported.Content.ReadAsStringAsync());
            var task = (await exported.Content.ReadFromJsonAsync<ReportingExportTaskDetailResponse>())!;
            Assert.AreEqual(1, task.VersionNumber); Assert.AreEqual("succeeded", task.StatusKey);
            Assert.IsGreaterThan(0, task.RowCount); taskId = task.Id;
            Assert.IsFalse((await exported.Content.ReadAsStringAsync()).Contains("securityStamp", StringComparison.Ordinal));
        }
        var download = $"/api/v1/reporting/export-tasks/{taskId:D}/download";
        using (var file = await SendAsync(client, HttpMethod.Get, download, tenantToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, file.StatusCode);
            var bytes = await file.Content.ReadAsByteArrayAsync(); Assert.IsGreaterThan(100, bytes.Length);
            Assert.AreEqual((byte)'P', bytes[0]); Assert.AreEqual((byte)'K', bytes[1]);
        }
        using (var revoked = await SendAsync(client, HttpMethod.Delete, grant, hostToken)) Assert.AreEqual(HttpStatusCode.OK, revoked.StatusCode);
        using (var empty = await SendAsync(client, HttpMethod.Get, grants, hostToken))
            Assert.AreEqual(0L, (await empty.Content.ReadFromJsonAsync<PagedResult<Guid>>())!.Total);
        using (var denied = await SendAsync(client, HttpMethod.Get, download, tenantToken)) Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await SendAsync(client, HttpMethod.Post, execute, tenantToken, new ExecuteReportingDefinitionRequest(null, parameters)))
            Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    private static async Task<Guid> SeedPublishedAsync(FullNetApiFactory api, SqlConnectionStringBuilder external,
        string host, int port, Guid actor)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>(); tenant.SetHost();
        try
        {
            var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
            var source = Guid.CreateVersion7(); var group = Guid.CreateVersion7(); var definition = Guid.CreateVersion7();
            var now = DateTimeOffset.UtcNow;
            await command.ExecuteAsync(ReportingGroupSql.Insert, new ReportingGroupRecord
                { Id = group, Name = "Tenant runtime fixture", IsEnabled = true, CreatedAtUtc = now, Version = 1 });
            await command.ExecuteAsync(ReportingDataSourceSql.Insert, new ReportingDataSourceRecord
            {
                Id = source, Name = "Real SQL Server fixture", ProviderKey = ReportingDataSourceProviderKeys.SqlServer,
                ServerHost = host, Port = port, DatabaseName = external.InitialCatalog, Username = external.UserID,
                PasswordProtected = scope.ServiceProvider.GetRequiredService<ReportingDataSourceSecretProtector>().Protect(external.Password),
                TrustServerCertificate = true, IsEnabled = true, CreatedAtUtc = now, Version = 1,
            });
            const string layout = "{\"columns\":[{\"key\":\"SchemaName\",\"requiredPermission\":\"reporting.executions.columns.schema_name\"}]}";
            await command.ExecuteAsync(ReportingDefinitionSql.InsertDefinition, new ReportingDefinitionRecord
            {
                Id = definition, GroupId = group, DataSourceId = source, DefinitionKey = $"tenant-runtime-{definition:N}",
                Name = "Tenant runtime fixture", QueryPortKey = "reporting.schema_inventory", IsEnabled = true,
                LatestPublishedVersionNumber = 2, LayoutConfigJson = layout, CreatedAtUtc = now, Version = 1,
            });
            var parameterSchema = ReportingDefinitionJson.SerializeParameterSchema([
                new ReportingParameterSchemaEntry("topN", "返回条数", "integer", true, "20")]);
            foreach (var number in new[] { 1, 2 }) await command.ExecuteAsync(ReportingDefinitionSql.InsertVersion,
                new ReportingDefinitionVersionRecord
                {
                    Id = Guid.CreateVersion7(), DefinitionId = definition, VersionNumber = number, DataSourceId = source,
                    QueryPortKey = "reporting.schema_inventory", LayoutConfigJson = layout, ParameterSchemaJson = parameterSchema,
                    PublishedByUserId = actor, PublishedAtUtc = now,
                });
            return definition;
        }
        finally { tenant.Clear(); }
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path,
        string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return SendAndDisposeAsync();
        async Task<HttpResponseMessage> SendAndDisposeAsync()
        {
            using (request) return await client.SendAsync(request);
        }
    }
}
