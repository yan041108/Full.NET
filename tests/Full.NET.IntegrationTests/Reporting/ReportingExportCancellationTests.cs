using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Features.ManageExportTasks;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Reporting;

/// <summary>通过真实发布、成员、任务、领取和文件通道验证排队取消，只替代外部数据连接。</summary>
[TestClass]
public sealed class ReportingExportCancellationTests
{
    private const string Tasks = "/api/v1/reporting/export-tasks";
    private static readonly string[] Permissions = [ReportingExportTaskPermissions.Create, ReportingExportTaskPermissions.Read, ReportingExportTaskPermissions.Download,
        ReportingExportTaskPermissions.Cancel, ReportingDefinitionGrantPermissions.PublishedRead, ReportingExecutionPermissions.ColumnSchemaName,
        "identity.navigation.read", "tenancy.tenants.switch"];

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Queued_owner_export_cancellation_serializes_with_claim_and_preserves_frozen_request(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var external = new ReportingTenantPublicationTests.ControlledExternalDatabase();
        using var factory = new FullNetApiFactory(provider, connection, configureTestServices: services => services.AddSingleton<IExternalDatabaseConnectionFactory>(external));
        await factory.InitializeAsync(useSchemaTemplate: false); using var host = factory.CreateClientForHost("localhost");
        var hostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin"); host.DefaultRequestHeaders.Authorization = new("Bearer", hostToken);
        var tenant = (await host.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(item => item.Identifier == "acme");
        var source = await SendAsync(host, HttpMethod.Post, "/api/v1/reporting/data-sources", new CreateReportingDataSourceRequest(null, "Cancel source",
            provider == DatabaseProvider.SqlServer ? "sql_server" : "mysql", "reports.example.com", provider == DatabaseProvider.SqlServer ? 1433 : 3306,
            "reporting", "readonly", "Test!Password2026", false, true), HttpStatusCode.Created);
        var group = await SendAsync(host, HttpMethod.Post, "/api/v1/reporting/groups", new CreateReportingGroupRequest(null, "Cancel group", 1, true), HttpStatusCode.Created);
        var definition = await SendAsync(host, HttpMethod.Post, "/api/v1/reporting/definitions", new CreateReportingDefinitionRequest(group.GetProperty("id").GetGuid(),
            source.GetProperty("id").GetGuid(), "cancel_source", "Cancelable report", null, "reporting.schema_inventory",
            [new("topN", "Limit", ReportingParameterDataTypeKeys.Integer, true, "20")],
            JsonSerializer.Serialize(new { columns = new[] { new { key = "SchemaName", displayName = "Schema", requiredPermission = ReportingExecutionPermissions.ColumnSchemaName } } }), true), HttpStatusCode.Created);
        var definitionId = definition.GetProperty("id").GetGuid();
        await SendAsync(host, HttpMethod.Post, $"/api/v1/reporting/definitions/{definitionId:D}/publish", new PublishReportingDefinitionRequest("Cancel test", definition.GetProperty("version").GetInt32()), HttpStatusCode.OK);
        var grantPath = $"/api/v1/reporting/definitions/{definitionId:D}/tenant-grants/{tenant.Id:D}";
        var grant = await SendAsync(host, HttpMethod.Put, grantPath, new SetReportingDefinitionTenantGrantRequest(true, 0), HttpStatusCode.OK);
        host.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, hostToken, tenant.Id));
        var owner = await MemberAsync("owner", Permissions); using var client = owner.Client;
        var other = await MemberAsync("other", Permissions); using var otherClient = other.Client;
        var denied = await MemberAsync("denied", Permissions.Where(permission => permission != ReportingExportTaskPermissions.Cancel).ToArray()); using var deniedClient = denied.Client;
        var queued = await CreateAsync(); var id = Id(queued); Assert.AreEqual(1L, Version(queued));
        await using var database = MyTasksApiTests.Open(provider, connection); await database.OpenAsync(); var frozen = await FrozenAsync(id);
        await SendAsync(otherClient, HttpMethod.Post, $"{Tasks}/{id:D}/cancel", new CancelReportingExportTaskRequest(1), HttpStatusCode.NotFound);
        await SendAsync(deniedClient, HttpMethod.Post, $"{Tasks}/{id:D}/cancel", new CancelReportingExportTaskRequest(1), HttpStatusCode.Forbidden);
        await SendAsync(host, HttpMethod.Post, $"{Tasks}/{id:D}/cancel", new CancelReportingExportTaskRequest(1), HttpStatusCode.NotFound);
        await CancelAsync(id, 0, HttpStatusCode.BadRequest); await CancelAsync(id, 2, HttpStatusCode.Conflict);
        var canceled = await CancelAsync(id, 1, HttpStatusCode.OK);
        Assert.AreEqual(ReportingExportTaskStatusKeys.Canceled, canceled.GetProperty("statusKey").GetString()); Assert.AreEqual(2L, Version(canceled));
        Assert.AreEqual(2L, Version(await CancelAsync(id, 1, HttpStatusCode.OK))); Assert.AreEqual(frozen, await FrozenAsync(id));
        Assert.AreEqual(0, await ProcessAsync()); Assert.AreEqual(0, external.QueryCount);
        var unavailable = await SendAsync(client, HttpMethod.Get, $"{Tasks}/{id:D}/download", null, HttpStatusCode.BadRequest);
        Assert.AreEqual(ReportingErrorCodes.ExportTaskNotReady, unavailable.GetProperty("code").GetString());

        var race = await CreateAsync(); var raceId = Id(race);
        var attempt = client.PostAsJsonAsync($"{Tasks}/{raceId:D}/cancel", new CancelReportingExportTaskRequest(Version(race)));
        var worker = ProcessAsync(); using var response = await attempt; var processed = await worker;
        Assert.IsTrue(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        var latest = await SendAsync(client, HttpMethod.Get, $"{Tasks}/{raceId:D}", null, HttpStatusCode.OK);
        if (response.StatusCode == HttpStatusCode.OK) { Assert.AreEqual(0, processed); Assert.AreEqual("canceled", latest.GetProperty("statusKey").GetString()); Assert.AreEqual(0, external.QueryCount); }
        else { Assert.AreEqual(1, processed); Assert.AreEqual("succeeded", latest.GetProperty("statusKey").GetString()); Assert.AreEqual(1, external.QueryCount); }
        var running = await CreateAsync(); var runningId = Id(running);
        await database.ExecuteAsync("UPDATE fn_reporting_export_task SET StatusKey='processing',LeaseId=@Lease,LeaseExpiresAtUtc=@Until,Version=Version+1 WHERE Id=@Id",
            new { Id = runningId, Lease = Guid.CreateVersion7(), Until = DateTime.UtcNow.AddMinutes(5) });
        await CancelAsync(runningId, 2, HttpStatusCode.Conflict);
        var withdraw = await CreateAsync(); var withdrawId = Id(withdraw); var withdrawFrozen = await FrozenAsync(withdrawId);
        // 发布授权丢失不应阻止本人停止待执行工作，取消权限本身仍必须有效。
        host.DefaultRequestHeaders.Authorization = null; hostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin"); host.DefaultRequestHeaders.Authorization = new("Bearer", hostToken);
        await SendAsync(host, HttpMethod.Post, $"{Tasks}/{withdrawId:D}/cancel", new CancelReportingExportTaskRequest(1), HttpStatusCode.Forbidden);
        await SendAsync(host, HttpMethod.Put, grantPath, new SetReportingDefinitionTenantGrantRequest(false, grant.GetProperty("version").GetInt32()), HttpStatusCode.OK);
        await CancelAsync(withdrawId, 1, HttpStatusCode.OK); Assert.AreEqual(withdrawFrozen, await FrozenAsync(withdrawId));
        await SendAsync(client, HttpMethod.Post, Tasks, Request(), HttpStatusCode.NotFound);
        var foreign = Guid.CreateVersion7();
        await database.ExecuteAsync("""
            INSERT INTO fn_reporting_export_task (Id,TenantId,DefinitionId,VersionNumber,DefinitionKey,DefinitionName,FormatKey,ParametersJson,StatusKey,RequestedByUserId,CreatedAtUtc,Version)
            VALUES (@Id,@TenantId,@DefinitionId,1,'cancel_source','Other tenant','excel','[]','queued',@UserId,@Now,1)
            """, new { Id = foreign, TenantId = Guid.CreateVersion7(), DefinitionId = definitionId, UserId = owner.UserId, Now = DateTime.UtcNow });
        await CancelAsync(foreign, 1, HttpStatusCode.NotFound);
        var mine = await SendAsync(client, HttpMethod.Get, Tasks + "/mine?statusKey=canceled", null, HttpStatusCode.OK); Assert.IsTrue(mine.GetProperty("items").GetArrayLength() >= 2);
        // CHECK 按真实迁移名称恢复，避免 MySQL 模板复制的自动约束名影响证据。
        await using DbConnection ddl = provider == DatabaseProvider.MySql ? new MySqlConnection(MySqlConnectionStringPolicy.Create(connection, MySqlGuidStorageMode.Binary16, allowUserVariables: true)) : MyTasksApiTests.Open(provider, connection);
        await ddl.OpenAsync(); await ddl.ExecuteAsync(provider == DatabaseProvider.MySql ? "ALTER TABLE fn_reporting_export_task DROP CHECK CK_fn_reporting_export_task_StatusKey" : "ALTER TABLE dbo.fn_reporting_export_task DROP CONSTRAINT CK_fn_reporting_export_task_StatusKey");
        var script = await File.ReadAllTextAsync(Path.Combine(FullNetApiFactory.FindRepositoryRoot(), "src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations", provider == DatabaseProvider.SqlServer ? "SqlServer" : "MySql", "359_ReportingExportTaskCancellation.sql"));
        await ddl.ExecuteAsync(script); await ddl.ExecuteAsync(script); Assert.AreEqual(frozen, await FrozenAsync(id));

        CreateReportingExportTaskRequest Request() => new(definitionId, "excel", 1, [new("topN", "20")]);
        Task<JsonElement> CreateAsync() => SendAsync(client, HttpMethod.Post, Tasks, Request(), HttpStatusCode.Created);
        Task<JsonElement> CancelAsync(Guid taskId, long version, HttpStatusCode expected) => SendAsync(client, HttpMethod.Post, $"{Tasks}/{taskId:D}/cancel", new CancelReportingExportTaskRequest(version), expected);
        Task<(string Parameters, string Permissions, string Binding, int Publication)> FrozenAsync(Guid taskId) => database.QuerySingleAsync<(string, string, string, int)>("SELECT ParametersJson,ActorPermissionCodesJson,ActorSessionBindingJson,VersionNumber FROM fn_reporting_export_task WHERE Id=@Id", new { Id = taskId });
        async Task<int> ProcessAsync() { await using var scope = factory.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<ReportingExportTaskRunner>().ProcessPendingAsync(default); }
        async Task<(HttpClient Client, Guid UserId)> MemberAsync(string suffix, string[] permissions)
        {
            var name = "report.cancel." + suffix + "." + Guid.CreateVersion7().ToString("N"); var identity = await factory.CreateHostIdentityAsync(name, permissions, password: FullNetApiFactory.TestPassword);
            var email = name + "@example.com"; await factory.EnsureHostUserProfileEmailAsync(identity.UserId, email, name);
            var invitation = await SendAsync(host, HttpMethod.Post, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member), HttpStatusCode.OK);
            var member = factory.CreateClientForHost("localhost"); var token = await IntegrationTestAuthHelper.LoginAsHostUserAsync(member, name); member.DefaultRequestHeaders.Authorization = new("Bearer", token);
            await SendAsync(member, HttpMethod.Post, $"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { }, HttpStatusCode.OK);
            member.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(member, token, tenant.Id)); return (member, identity.UserId);
        }
    }
    private static Guid Id(JsonElement task) => task.GetProperty("id").GetGuid();
    private static long Version(JsonElement task) => task.GetProperty("version").GetInt64();
    private static async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string path, object? body, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, path); if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request); var json = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, path + " " + json); return JsonDocument.Parse(json).RootElement.Clone();
    }
}
