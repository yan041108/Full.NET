using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.ImportExport;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Features.ManageImportTasks;
using Full.NET.Modules.ImportExport.Persistence;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;
using Full.NET.Modules.Reporting.Features.ManageExportTasks;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Reporting;

/// <summary>在双库、真实身份/文件端点下验证已完成输出的下载授权，不模拟权限 Port 或文件读取。</summary>
[TestClass]
public sealed class OutputDownloadAuthorizationTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.MySql)]
    [DataRow(DatabaseProvider.SqlServer)]
    public async Task Completed_outputs_require_owner_and_current_session(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.MySql
            ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync()
            : await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        using var api = new FullNetApiFactory(provider, connection);
        await api.InitializeAsync();
        using var client = api.CreateClientForHost("localhost");
        var token = await ImportExportTaskAssertions.LoginAndEnterAcmeTenantAsync(client, default);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var tenantId = Guid.Parse(jwt.Claims.Single(c => c.Type == FullNetIdentityClaimTypes.TenantId).Value);
        var actorId = Guid.Parse(jwt.Subject);
        var sessionId = Guid.Parse(jwt.Claims.Single(c => c.Type == FullNetIdentityClaimTypes.SessionId).Value);
        var reportId = Guid.CreateVersion7(); var importId = Guid.CreateVersion7();
        var bytes = ReportingExcelExportRenderer.Render([new("SchemaName", "Schema")],
            [new(new Dictionary<string, string?> { ["SchemaName"] = "protected fixture" })]);

        var definitionId = await SeedOutputsAsync(api, tenantId, actorId, reportId, importId, bytes);
        var reportPath = $"/api/v1/reporting/export-tasks/{reportId:D}/download";
        var paths = new[] { $"/api/v1/import-export/tasks/{importId:D}/error-receipt", reportPath };
        await AssertDownloadAsync(client, reportPath, token, HttpStatusCode.Forbidden);
        await AssertReportServiceDownloadAsync(api, tenantId, reportId, token, false);
        // 同管理员再次登录会撤销旧会话；版本授权使用独立 Host 主体，不改写下载创建人的会话。
        var hostGrantToken = await api.CreateHostAccessTokenAsync([ReportingDefinitionPermissions.GrantTenants]);
        var grantPath = $"/api/v1/reporting/definitions/{definitionId:D}/versions/1/tenant-grants/{tenantId:D}";
        using (var deniedGrant = Request(HttpMethod.Put, grantPath, token))
        using (var denied = await client.SendAsync(deniedGrant)) Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var grantRequest = Request(HttpMethod.Put, grantPath, hostGrantToken))
        using (var granted = await client.SendAsync(grantRequest)) Assert.AreEqual(HttpStatusCode.OK, granted.StatusCode);
        foreach (var path in paths) await AssertDownloadAsync(client, path, token, HttpStatusCode.OK, bytes);
        await AssertReportServiceDownloadAsync(api, tenantId, reportId, token, true, bytes);

        // 超级管理员也不得以自己的当前会话读取其他创建人的原文件。
        await ChangeOwnersAsync(api, tenantId, reportId, importId, Guid.CreateVersion7());
        foreach (var path in paths) await AssertDownloadAsync(client, path, token, HttpStatusCode.Forbidden);
        await AssertReportServiceDownloadAsync(api, tenantId, reportId, token, false);
        await ChangeOwnersAsync(api, tenantId, reportId, importId, actorId);

        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        { Content = JsonContent.Create(new LoginRequest("admin", FullNetApiFactory.TestPassword)) };
        login.Headers.Add("Origin", "http://localhost");
        using var loggedIn = await client.SendAsync(login); Assert.AreEqual(HttpStatusCode.OK, loggedIn.StatusCode);
        var hostToken = (await loggedIn.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;
        using var revoke = Request(HttpMethod.Post, $"/api/v1/identity/online-sessions/{sessionId:D}/revoke", hostToken);
        using var revoked = await client.SendAsync(revoke); Assert.AreEqual(HttpStatusCode.OK, revoked.StatusCode);
        foreach (var path in paths) await AssertDownloadAsync(client, path, token, HttpStatusCode.Unauthorized);
        await AssertDownloadAsync(client, reportPath, token, HttpStatusCode.Unauthorized);
        await AssertReportServiceDownloadAsync(api, tenantId, reportId, token, false);
        var freshToken = await ImportExportTaskAssertions.LoginAndEnterAcmeTenantAsync(client, default);
        foreach (var path in paths) await AssertDownloadAsync(client, path, freshToken, HttpStatusCode.OK, bytes);
        using (var revokeGrant = Request(HttpMethod.Delete, grantPath, hostGrantToken))
        using (var revokedGrant = await client.SendAsync(revokeGrant)) Assert.AreEqual(HttpStatusCode.OK, revokedGrant.StatusCode);
        await AssertDownloadAsync(client, reportPath, freshToken, HttpStatusCode.Forbidden);
        await AssertReportServiceDownloadAsync(api, tenantId, reportId, freshToken, false);
        using (var restoreGrant = Request(HttpMethod.Put, grantPath, hostGrantToken))
        using (var restored = await client.SendAsync(restoreGrant)) Assert.AreEqual(HttpStatusCode.OK, restored.StatusCode);
        await AssertDownloadAsync(client, reportPath, freshToken, HttpStatusCode.OK, bytes);
        await AssertReportServiceDownloadAsync(api, tenantId, reportId, freshToken, true, bytes);
    }

    /// <summary>真实数据库、Identity Port 和文件流验证服务边界；不把直接服务调用报告为 HTTP 下载成功。</summary>
    private static async Task AssertReportServiceDownloadAsync(FullNetApiFactory api, Guid tenantId,
        Guid taskId, string token, bool allowed, byte[]? bytes = null)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        string Claim(string type) => jwt.Claims.Single(claim => claim.Type == type).Value;
        var binding = new SessionBindingSnapshot(Guid.Parse(jwt.Subject), tenantId,
            Guid.Parse(Claim(FullNetIdentityClaimTypes.SessionId)), Claim(FullNetIdentityClaimTypes.SecurityStamp),
            Claim(FullNetIdentityClaimTypes.ActorScope), Claim(FullNetIdentityClaimTypes.Scope));
        await using var scope = api.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        tenant.SetTenant(new TenantContext(tenantId, "acme", "Acme"));
        try
        {
            var service = scope.ServiceProvider.GetRequiredService<ReportingExportTaskManagementService>();
            var result = await service.OpenDownloadAsync(taskId, binding);
            Assert.AreEqual(allowed, result.IsSuccess, result.Error?.Message);
            Assert.AreEqual(tenantId, tenant.Id); Assert.IsFalse(tenant.IsHost);
            if (!allowed) { Assert.AreEqual("authorization.permission_denied", result.Error!.Code); return; }
            await using var content = result.Value!.Content;
            using var actual = new MemoryStream(); await content.CopyToAsync(actual);
            CollectionAssert.AreEqual(bytes!, actual.ToArray());
        }
        finally { tenant.Clear(); }
    }

    /// <summary>仅播种已完成输出夹具；生成查询/导入业务链在其他测试验收，此处不把夹具当作业务执行。</summary>
    private static async Task<Guid> SeedOutputsAsync(FullNetApiFactory api, Guid tenantId, Guid actorId,
        Guid reportId, Guid importId, byte[] bytes)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        var commands = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var now = DateTimeOffset.UtcNow; var group = Guid.CreateVersion7(); var source = Guid.CreateVersion7();
        var definition = Guid.CreateVersion7();
        tenant.SetHost();
        try
        {
            await commands.ExecuteAsync(ReportingGroupSql.Insert,
                new ReportingGroupRecord { Id = group, Name = "Download fixture", IsEnabled = true, CreatedAtUtc = now, Version = 1 });
            await commands.ExecuteAsync(ReportingDataSourceSql.Insert, new ReportingDataSourceRecord
            {
                Id = source, Name = "Unused download fixture", ProviderKey = ProviderKey(api.Provider), ServerHost = "localhost",
                Port = 1, DatabaseName = "unused", Username = "unused", IsEnabled = true, CreatedAtUtc = now, Version = 1,
            });
            const string layout = "{\"columns\":[{\"key\":\"SchemaName\",\"requiredPermission\":\"reporting.executions.columns.schema_name\"}]}";
            await commands.ExecuteAsync(ReportingDefinitionSql.InsertDefinition, new ReportingDefinitionRecord
            {
                Id = definition, GroupId = group, DataSourceId = source, DefinitionKey = $"download-{definition:N}",
                Name = "Download fixture", QueryPortKey = "fixture", LayoutConfigJson = layout,
                LatestPublishedVersionNumber = 1, IsEnabled = true, CreatedAtUtc = now, Version = 1,
            });
            await commands.ExecuteAsync(ReportingDefinitionSql.InsertVersion, new ReportingDefinitionVersionRecord
            {
                Id = Guid.CreateVersion7(), DefinitionId = definition, VersionNumber = 1, DataSourceId = source,
                QueryPortKey = "fixture", LayoutConfigJson = layout, PublishedByUserId = actorId, PublishedAtUtc = now,
            });
            tenant.SetTenant(new TenantContext(tenantId, "acme", "Acme"));
            var files = scope.ServiceProvider.GetRequiredService<ITenantResourceFileStore>();
            using var reportStream = new MemoryStream(bytes); using var receiptStream = new MemoryStream(bytes);
            var reportFile = await files.UploadAsync("reporting", reportId, actorId, "report.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", reportStream, bytes.Length);
            Assert.IsTrue(reportFile.IsSuccess, reportFile.Error?.Message);
            var receipt = await files.UploadAsync("import_export", importId, actorId, "errors.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", receiptStream, bytes.Length);
            Assert.IsTrue(receipt.IsSuccess, receipt.Error?.Message);
            await commands.ExecuteAsync(ReportingExportTaskSql.InsertFor(api.Provider), new ReportingExportTaskRecord
            {
                Id = reportId, TenantId = tenantId, DefinitionId = definition, VersionNumber = 1,
                DefinitionKey = "download-fixture", DefinitionName = "Download fixture", FormatKey = ReportingExportFormatKeys.Excel,
                StatusKey = ReportingExportTaskStatusKeys.Succeeded, OutputFileId = reportFile.Value!.FileId,
                OutputFileName = "report.xlsx", RowCount = 1, RequestedByUserId = actorId, CreatedAtUtc = now, CompletedAtUtc = now,
                ActorPermissionCodesJson = ReportingExportTaskMapper.SerializePermissionCodes([ReportingExecutionPermissions.ColumnSchemaName]),
                Version = 1,
            });
            await commands.ExecuteAsync(ImportExportTaskSql.Insert, new ImportExportTaskRecord
            {
                Id = importId, TenantId = tenantId, SchemaKey = StaticImportSchemaKeys.OrganizationTenantPositions,
                SchemaDisplayName = "Positions", WorksheetKey = "positions", SourceFileId = Guid.CreateVersion7(),
                SourceFileName = "source.xlsx", StatusKey = ImportExportTaskStatusKeys.ExecutionPartial,
                TotalRows = 1, ValidRowCount = 1, ExecutionFailedRowCount = 1, ErrorReceiptFileId = receipt.Value!.FileId,
                RequestedByUserId = actorId, CreatedAtUtc = now, Version = 1,
                ExecutionRowsJson = ImportExportTaskMapper.SerializeExecutionState(new(new Dictionary<string, bool>(), [])),
            });
            return definition;
        }
        finally { tenant.Clear(); }
    }

    private static string ProviderKey(DatabaseProvider provider) => provider == DatabaseProvider.MySql ? ReportingDataSourceProviderKeys.MySql : ReportingDataSourceProviderKeys.SqlServer;

    private static async Task ChangeOwnersAsync(FullNetApiFactory api, Guid tenantId,
        Guid reportId, Guid importId, Guid owner)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        tenant.SetTenant(new TenantContext(tenantId, "acme", "Acme"));
        try
        {
            var commands = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
            await commands.ExecuteAsync(new SqlStatement("testing.output_report_owner",
                "UPDATE fn_reporting_export_task SET RequestedByUserId = @Owner WHERE TenantId = @TenantId AND Id = @Id",
                SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId),
                new Dictionary<string, object?> { ["Owner"] = owner, ["Id"] = reportId });
            await commands.ExecuteAsync(new SqlStatement("testing.output_import_owner",
                "UPDATE fn_import_export_task SET RequestedByUserId = @Owner WHERE TenantId = @TenantId AND Id = @Id",
                SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId),
                new Dictionary<string, object?> { ["Owner"] = owner, ["Id"] = importId });
        }
        finally { tenant.Clear(); }
    }

    private static async Task AssertDownloadAsync(HttpClient client, string path, string token,
        HttpStatusCode expected, byte[]? bytes = null)
    {
        using var request = Request(HttpMethod.Get, path, token); using var response = await client.SendAsync(request);
        Assert.AreEqual(expected, response.StatusCode, expected == response.StatusCode ? null : $"{path}: {await response.Content.ReadAsStringAsync()}");
        if (bytes is not null) CollectionAssert.AreEqual(bytes, await response.Content.ReadAsByteArrayAsync());
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); return request;
    }
}
