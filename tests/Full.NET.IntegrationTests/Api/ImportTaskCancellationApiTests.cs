using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.ImportTasks;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Api;

/// <summary>取消与真实 Worker 领取竞争，同一检查点的原输入和已提交业务行必须保留。</summary>
[TestClass]
public sealed class ImportTaskCancellationApiTests
{
    private const string Tasks = "/api/v1/import-export/tasks";
    private static readonly string[] Headers = ["code", "name", "displayOrder", "unitCode", "positionLevelCode"];
    private static readonly string[] Permissions = [ImportExportPermissions.ImportTasksCreate, ImportExportPermissions.ImportTasksRead,
        ImportExportPermissions.ImportTasksExecute, ImportExportPermissions.ImportTasksCancel, ImportExportPermissions.StaticSchemasRead,
        OrganizationPositionManagementPermissions.Import, "identity.navigation.read", "tenancy.tenants.switch"];

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Cancel_preserves_input_and_progress_and_serializes_with_worker_claim(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection, new Dictionary<string, string?> {
            ["FullNet:ImportExport:ExecutionEnabled"] = "true", ["FullNet:ImportExport:RunSynchronously"] = "false", ["FullNet:ImportExport:BatchSize"] = "1" });
        // MySQL CREATE TABLE LIKE 会重命名 CHECK；约束恢复验收必须使用真实迁移建库而非 API 克隆模板。
        await factory.InitializeAsync(useSchemaTemplate: false); using var admin = factory.CreateClientForHost("localhost");
        var hostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(admin, "admin"); admin.DefaultRequestHeaders.Authorization = new("Bearer", hostToken);
        var tenant = (await admin.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(item => item.Identifier == "acme");
        using var host = factory.CreateClientForHost("localhost"); host.DefaultRequestHeaders.Authorization = new("Bearer", hostToken);
        admin.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(admin, hostToken, tenant.Id));
        var owner = await MemberAsync("owner", Permissions); using var client = owner.Client;
        var other = await MemberAsync("other", Permissions); using var otherClient = other.Client;
        var denied = await MemberAsync("denied", Permissions.Where(permission => permission != ImportExportPermissions.ImportTasksCancel).ToArray()); using var deniedClient = denied.Client;
        var template = await client.GetByteArrayAsync("/api/v1/import-export/schemas/organization.tenant_positions/worksheets/positions/template");
        await using var database = MyTasksApiTests.Open(provider, connection); await database.OpenAsync();
        var preview = await CreateAsync("preview"); var previewId = Id(preview); var initialVersion = Version(preview);
        // 切换租户会替换原会话的有效作用域；Host 拒绝必须使用另一条仍有效的 Host 会话。
        host.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin"));
        await SendAsync(host, HttpMethod.Post, $"{Tasks}/{previewId:D}/cancel", new CancelImportExportTaskRequest(initialVersion), HttpStatusCode.Forbidden);
        // 新登录遵守现有会话替换策略，管理员越权检查重新建立当前有效的租户会话。
        admin.DefaultRequestHeaders.Authorization = null; var currentAdminHost = await IntegrationTestAuthHelper.LoginAsHostUserAsync(admin, "admin");
        admin.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(admin, currentAdminHost, tenant.Id));
        await SendAsync(deniedClient, HttpMethod.Post, $"{Tasks}/{previewId:D}/cancel", new CancelImportExportTaskRequest(initialVersion), HttpStatusCode.Forbidden);
        await SendAsync(otherClient, HttpMethod.Post, $"{Tasks}/{previewId:D}/cancel", new CancelImportExportTaskRequest(initialVersion), HttpStatusCode.NotFound);
        await SendAsync(admin, HttpMethod.Post, $"{Tasks}/{previewId:D}/cancel", new CancelImportExportTaskRequest(initialVersion), HttpStatusCode.NotFound);
        await CancelAsync(previewId, 0, HttpStatusCode.BadRequest); await CancelAsync(previewId, initialVersion + 1, HttpStatusCode.Conflict);
        var canceled = await CancelAsync(previewId, initialVersion, HttpStatusCode.OK);
        Assert.AreEqual(ImportExportTaskStatusKeys.Canceled, canceled.GetProperty("statusKey").GetString());
        Assert.AreEqual(initialVersion + 1, Version(canceled));
        Assert.AreEqual(Version(canceled), Version(await CancelAsync(previewId, initialVersion, HttpStatusCode.OK)));
        Assert.AreEqual(0, await ProcessAsync());
        await SendAsync(client, HttpMethod.Post, $"{Tasks}/{previewId:D}/execute", null, HttpStatusCode.UnprocessableEntity);
        await SendAsync(client, HttpMethod.Post, $"{Tasks}/{previewId:D}/retry", null, HttpStatusCode.UnprocessableEntity);

        var queued = await CreateAsync("partial"); var queuedId = Id(queued);
        var beforeClaim = await SendAsync(client, HttpMethod.Post, $"{Tasks}/{queuedId:D}/execute", null, HttpStatusCode.OK);
        Assert.AreEqual(1, await ProcessAsync());
        var afterBatch = await DetailAsync(queuedId); Assert.AreEqual(ImportExportTaskStatusKeys.Queued, afterBatch.GetProperty("statusKey").GetString());
        Assert.AreEqual(1, afterBatch.GetProperty("succeededRowCount").GetInt32());
        var frozen = await ReadFrozenAsync(queuedId); Assert.IsTrue(frozen.ExecutionRowsJson.Contains("preparedPlanJson", StringComparison.Ordinal));
        await CancelAsync(queuedId, Version(beforeClaim), HttpStatusCode.Conflict);
        var stopped = await CancelAsync(queuedId, Version(afterBatch), HttpStatusCode.OK);
        Assert.AreEqual(1, stopped.GetProperty("processedRowCount").GetInt32()); Assert.AreEqual(1, stopped.GetProperty("nextLineNumber").GetInt32());
        Assert.AreEqual(frozen, await ReadFrozenAsync(queuedId)); Assert.AreEqual(0, await ProcessAsync());
        Assert.AreEqual(1, await PositionCountAsync("partial"));

        // 执行中状态即使租约将到期也不能覆盖；真实领取与取消只允许一个先推进任务版本。
        var race = await CreateAsync("race"); var raceId = Id(race);
        var raceQueued = await SendAsync(client, HttpMethod.Post, $"{Tasks}/{raceId:D}/execute", null, HttpStatusCode.OK);
        var cancelAttempt = client.PostAsJsonAsync($"{Tasks}/{raceId:D}/cancel", new CancelImportExportTaskRequest(Version(raceQueued)));
        var workerAttempt = ProcessAsync(); using var raceResponse = await cancelAttempt; var processed = await workerAttempt;
        Assert.IsTrue(raceResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, await raceResponse.Content.ReadAsStringAsync());
        if (raceResponse.StatusCode == HttpStatusCode.OK) { Assert.AreEqual(0, processed); Assert.AreEqual(0, await PositionCountAsync("race")); }
        else { Assert.AreEqual(1, processed); var latest = await DetailAsync(raceId); await CancelAsync(raceId, Version(latest), HttpStatusCode.OK); Assert.AreEqual(1, await PositionCountAsync("race")); }
        Assert.AreEqual(0, await ProcessAsync());

        var executing = await CreateAsync("executing"); var executingId = Id(executing);
        await SendAsync(client, HttpMethod.Post, $"{Tasks}/{executingId:D}/execute", null, HttpStatusCode.OK);
        await database.ExecuteAsync("UPDATE fn_import_export_task SET StatusKey='executing',LeaseId=@Lease,LeaseExpiresAtUtc=@Until,Version=Version+1 WHERE Id=@Id",
            new { Id = executingId, Lease = Guid.CreateVersion7(), Until = DateTime.UtcNow.AddMinutes(5) });
        var active = await DetailAsync(executingId); await CancelAsync(executingId, Version(active), HttpStatusCode.Conflict);
        Assert.AreEqual(ImportExportTaskStatusKeys.Executing, (await DetailAsync(executingId)).GetProperty("statusKey").GetString());

        var sourceRevoked = await CreateAsync("withdrawn"); var revokedId = Id(sourceRevoked);
        var revokedQueued = await SendAsync(client, HttpMethod.Post, $"{Tasks}/{revokedId:D}/execute", null, HttpStatusCode.OK);
        Assert.AreEqual(1, await database.ExecuteAsync("DELETE FROM fn_identity_role_permission WHERE RoleId IN (SELECT RoleId FROM fn_identity_user_role WHERE UserId=@UserId) AND PermissionCode=@Permission",
            new { UserId = owner.UserId, Permission = OrganizationPositionManagementPermissions.Import }));
        await CancelAsync(revokedId, Version(revokedQueued), HttpStatusCode.OK);
        await CreateAsync("denied-source", HttpStatusCode.Forbidden);
        // 当前账号仍有取消权限，但不能取消另一租户中同一所有者的记录。
        var foreignId = Guid.CreateVersion7();
        await database.ExecuteAsync("""
            INSERT INTO fn_import_export_task (Id,TenantId,SchemaKey,SchemaDisplayName,WorksheetKey,SourceFileId,StatusKey,TotalRows,ValidRowCount,InvalidRowCount,RequestedByUserId,CreatedAtUtc,Version)
            VALUES (@Id,@TenantId,'organization.tenant_positions','positions','positions',@FileId,'queued',0,0,0,@UserId,@Now,1)
            """, new { Id = foreignId, TenantId = Guid.CreateVersion7(), FileId = Guid.CreateVersion7(), UserId = owner.UserId, Now = DateTime.UtcNow });
        await CancelAsync(foreignId, 1, HttpStatusCode.NotFound);
        var mine = await SendAsync(client, HttpMethod.Get, Tasks + "/mine?statusKey=canceled", null, HttpStatusCode.OK);
        Assert.IsTrue(mine.GetProperty("items").GetArrayLength() >= 4);

        // 约束丢失的部分部署状态重放两次，保留已经取消的记录和全部任务输入。
        await using System.Data.Common.DbConnection ddl = provider == DatabaseProvider.MySql
            ? new MySqlConnection(MySqlConnectionStringPolicy.Create(connection, MySqlGuidStorageMode.Binary16, allowUserVariables: true)) : MyTasksApiTests.Open(provider, connection);
        await ddl.OpenAsync(); await ddl.ExecuteAsync(provider == DatabaseProvider.MySql
            ? "ALTER TABLE fn_import_export_task DROP CHECK CK_fn_import_export_task_StatusKey" : "ALTER TABLE dbo.fn_import_export_task DROP CONSTRAINT CK_fn_import_export_task_StatusKey");
        var migration = await File.ReadAllTextAsync(Path.Combine(FullNetApiFactory.FindRepositoryRoot(), "src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations",
            provider == DatabaseProvider.SqlServer ? "SqlServer" : "MySql", "358_ImportTaskCancellation.sql"));
        await ddl.ExecuteAsync(migration); await ddl.ExecuteAsync(migration);
        Assert.AreEqual(frozen, await ReadFrozenAsync(queuedId));

        async Task<(HttpClient Client, Guid UserId)> MemberAsync(string suffix, string[] permissions)
        {
            var name = "cancel." + suffix + "." + Guid.CreateVersion7().ToString("N"); var identity = await factory.CreateHostIdentityAsync(name, permissions, password: FullNetApiFactory.TestPassword);
            var email = name + "@example.com"; await factory.EnsureHostUserProfileEmailAsync(identity.UserId, email, name);
            var invitation = await SendAsync(admin, HttpMethod.Post, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member), HttpStatusCode.OK);
            var member = factory.CreateClientForHost("localhost"); var token = await IntegrationTestAuthHelper.LoginAsHostUserAsync(member, name); member.DefaultRequestHeaders.Authorization = new("Bearer", token);
            await SendAsync(member, HttpMethod.Post, $"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { }, HttpStatusCode.OK);
            member.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(member, token, tenant.Id)); return (member, identity.UserId);
        }
        async Task<JsonElement> CreateAsync(string suffix, HttpStatusCode expected = HttpStatusCode.Created)
        {
            var workbook = ImportProfilesApiTests.Workbook(template, [Headers, [Code(suffix, 1), "Position 1", "0", "", ""], [Code(suffix, 2), "Position 2", "0", "", ""]]);
            var mapping = new ImportColumnMappingRequest(Headers, Headers.Select(header => new ImportColumnMapping(header, header)).ToArray());
            using var form = new MultipartFormDataContent { { new StringContent("organization.tenant_positions"), "schemaKey" }, { new StringContent("positions"), "worksheetKey" },
                { new StringContent(JsonSerializer.Serialize(mapping, new JsonSerializerOptions(JsonSerializerDefaults.Web))), "columnMapping" }, { new ByteArrayContent(workbook), "file", "positions.xlsx" } };
            using var response = await client.PostAsync(Tasks, form); var json = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, json); return JsonDocument.Parse(json).RootElement.Clone();
        }
        async Task<int> ProcessAsync() { await using var scope = factory.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<ImportExportTaskRunner>().ProcessPendingAsync(default); }
        Task<JsonElement> DetailAsync(Guid id) => SendAsync(client, HttpMethod.Get, $"{Tasks}/{id:D}", null, HttpStatusCode.OK);
        Task<JsonElement> CancelAsync(Guid id, long version, HttpStatusCode expected) => SendAsync(client, HttpMethod.Post, $"{Tasks}/{id:D}/cancel", new CancelImportExportTaskRequest(version), expected);
        async Task<(string ColumnMappingJson, string ExecutionRowsJson)> ReadFrozenAsync(Guid id) => await database.QuerySingleAsync<(string, string)>("SELECT ColumnMappingJson,ExecutionRowsJson FROM fn_import_export_task WHERE Id=@Id", new { Id = id });
        Task<int> PositionCountAsync(string suffix) => database.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM fn_organization_position WHERE TenantId=@TenantId AND Code IN (@First,@Second)", new { TenantId = tenant.Id, First = Code(suffix, 1), Second = Code(suffix, 2) });
    }

    private static string Code(string suffix, int row) => "cancel-" + suffix + "-" + row;
    private static Guid Id(JsonElement task) => task.GetProperty("id").GetGuid();
    private static long Version(JsonElement task) => task.GetProperty("version").GetInt64();
    private static async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string path, object? body, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, path); if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request); var json = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, path + " " + json); return JsonDocument.Parse(json).RootElement.Clone();
    }
}
