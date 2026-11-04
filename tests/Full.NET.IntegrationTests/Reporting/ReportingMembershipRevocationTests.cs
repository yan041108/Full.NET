using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageTenantMembers.Persistence;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;
using Full.NET.Modules.Reporting.Features.ManageExportTasks;
using Full.NET.Modules.Reporting.Persistence;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Reporting;

/// <summary>真实队列、冻结会话、职责读取与文件落库；仅用可暂停工作簿代替外部数据查询。</summary>
[TestClass]
public sealed class ReportingMembershipRevocationTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, false)]
    [DataRow(DatabaseProvider.SqlServer, true)]
    [DataRow(DatabaseProvider.MySql, false)]
    [DataRow(DatabaseProvider.MySql, true)]
    public async Task Leaving_A_blocks_queued_or_generated_output_while_B_completes(DatabaseProvider provider, bool duringGeneration)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90)); var token = deadline.Token;
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync(); var workbook = new ControlledWorkbook();
        using var factory = new FullNetApiFactory(provider, connection,
            new Dictionary<string, string?> { ["Identity:SessionLoginPolicy"] = "AllowMultiple" }, configureTestServices: services =>
                services.Replace(ServiceDescriptor.Singleton<IReportingExportWorkbookSource>(workbook)));
        await factory.InitializeAsync(token); using var host = factory.CreateClientForHost("localhost");
        using var aClient = factory.CreateClientForHost("acme.localhost");
        var a = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(aClient, token);
        var admin = await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin", FullNetApiFactory.TestPassword, token);
        host.DefaultRequestHeaders.Authorization = new("Bearer", admin);
        var source = await SendAsync(host, HttpMethod.Post, "/api/v1/reporting/data-sources", new CreateReportingDataSourceRequest(null, "Membership source",
            provider == DatabaseProvider.SqlServer ? "sql_server" : "mysql", "reports.example.com", provider == DatabaseProvider.SqlServer ? 1433 : 3306,
            "reporting", "readonly", "Fixture!2026", false, true), HttpStatusCode.Created, token);
        var group = await SendAsync(host, HttpMethod.Post, "/api/v1/reporting/groups", new CreateReportingGroupRequest(null, "Membership group", 1, true), HttpStatusCode.Created, token);
        var definition = await SendAsync(host, HttpMethod.Post, "/api/v1/reporting/definitions", new CreateReportingDefinitionRequest(group.GetProperty("id").GetGuid(),
            source.GetProperty("id").GetGuid(), "member_export", "Membership export", null, "reporting.schema_inventory",
            [new("topN", "Limit", ReportingParameterDataTypeKeys.Integer, true, "20")],
            JsonSerializer.Serialize(new { columns = new[] { new { key = "SchemaName", displayName = "Schema", requiredPermission = ReportingExecutionPermissions.ColumnSchemaName } } }), true), HttpStatusCode.Created, token);
        var definitionId = definition.GetProperty("id").GetGuid();
        await SendAsync(host, HttpMethod.Post, $"/api/v1/reporting/definitions/{definitionId:D}/publish",
            new PublishReportingDefinitionRequest("Membership fixture", definition.GetProperty("version").GetInt32()), HttpStatusCode.OK, token);
        // 不授予平台 tenancy.tenants.switch，防止平台准入掩盖普通成员撤权。
        var identity = await factory.CreateHostIdentityAsync("report-member-" + Guid.CreateVersion7().ToString("N"),
            [ReportingExportTaskPermissions.Create, ReportingExportTaskPermissions.Read, ReportingExportTaskPermissions.Download,
             ReportingDefinitionGrantPermissions.PublishedRead, ReportingExecutionPermissions.ColumnSchemaName, IdentityTenantMembershipPermissions.LeaveSelf],
            token, FullNetApiFactory.TestPassword);
        var bDomain = "report-b-" + Guid.CreateVersion7().ToString("N") + ".localhost"; TenantSummary b;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var current = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>(); current.SetHost();
            var created = await scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>().ProvisionAsync(
                new ProvisionTenantRequest("report-b-" + Guid.CreateVersion7().ToString("N"), "Reporting B", bDomain), token);
            Assert.IsTrue(created.IsSuccess, created.Error?.Code); b = created.Value!;
            foreach (var entry in new[] { (a.Id, "acme", "Acme"), (b.Id, b.Identifier, b.Name) })
            {
                current.SetTenant(new TenantContext(entry.Item1, entry.Item2, entry.Item3));
                await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(TenantMembershipSql.InsertMember,
                    IdentitySqlParameters.Create(("Id", Guid.CreateVersion7()), ("UserId", identity.UserId), ("MemberRole", TenantMemberRoles.Member),
                        ("Status", TenantMemberStatuses.Active), ("CreatedAtUtc", DateTimeOffset.UtcNow), ("UpdatedAtUtc", DateTimeOffset.UtcNow), ("Version", 1)), token);
            }
        }
        foreach (var id in new[] { a.Id, b.Id })
            await SendAsync(host, HttpMethod.Put, $"/api/v1/reporting/definitions/{definitionId:D}/tenant-grants/{id:D}",
                new SetReportingDefinitionTenantGrantRequest(true, 0), HttpStatusCode.OK, token);
        using var bClient = factory.CreateClientForHost(bDomain);
        foreach (var entry in new[] { (aClient, a.Id), (bClient, b.Id) })
        {
            var login = await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, identity.Username, FullNetApiFactory.TestPassword, token);
            entry.Item1.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, login, entry.Item2, token));
        }
        var aTask = await CreateAsync(aClient); var bTask = await CreateAsync(bClient);
        var aId = aTask.GetProperty("id").GetGuid(); var bId = bTask.GetProperty("id").GetGuid();
        var aContext = new TenantContext(a.Id, "acme", "Acme"); var bContext = new TenantContext(b.Id, b.Identifier, b.Name);
        var aBefore = await ReadAsync(aContext, aId, checkAuthority: true); var bBefore = await ReadAsync(bContext, bId, checkAuthority: true);
        workbook.HoldTask = duringGeneration ? aId : null; Task<int>? processing = null;
        try
        {
            if (duringGeneration) { processing = ProcessAsync(); await workbook.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15), token); }
            await SendAsync(aClient, HttpMethod.Post, "/api/v1/identity/tenant-members/me/leave", new LeaveTenantMembershipRequest(1), HttpStatusCode.OK, token);
            workbook.Release.TrySetResult(); processing ??= ProcessAsync(); Assert.AreEqual(2, await processing);
        }
        finally { workbook.Release.TrySetResult(); if (processing is not null) { try { await processing; } catch { /* 主断言保留原始失败，清理只收回本测试持有的执行任务。 */ } } }
        var aAfter = await ReadAsync(aContext, aId); var bAfter = await ReadAsync(bContext, bId);
        Assert.AreEqual(ReportingExportTaskStatusKeys.Failed, aAfter.StatusKey); Assert.AreEqual(ReportingErrorCodes.ExportAuthorityDenied, aAfter.ErrorCode);
        Assert.IsNull(aAfter.OutputFileId); Assert.AreEqual(ReportingExportTaskStatusKeys.Succeeded, bAfter.StatusKey); Assert.IsNotNull(bAfter.OutputFileId);
        Assert.AreEqual(aBefore.ActorSessionBindingJson, aAfter.ActorSessionBindingJson); Assert.AreEqual(bBefore.ActorSessionBindingJson, bAfter.ActorSessionBindingJson);
        Assert.AreEqual(aBefore.ActorPermissionCodesJson, aAfter.ActorPermissionCodesJson);
        CollectionAssert.AreEquivalent(duringGeneration ? new[] { aId, bId } : new[] { bId }, workbook.Calls.ToArray());
        Assert.AreEqual(0, await FilesAsync(aContext, aId)); Assert.AreEqual(1, await FilesAsync(bContext, bId));
        Assert.AreEqual(0, await ProcessAsync());
        using var download = await bClient.GetAsync($"/api/v1/reporting/export-tasks/{bId:D}/download", token);
        Assert.AreEqual(HttpStatusCode.OK, download.StatusCode);

        Task<JsonElement> CreateAsync(HttpClient client) => SendAsync(client, HttpMethod.Post, "/api/v1/reporting/export-tasks",
            new CreateReportingExportTaskRequest(definitionId, "excel", 1, [new("topN", "20")]), HttpStatusCode.Created, token);
        async Task<int> ProcessAsync() { await using var scope = factory.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<ReportingExportTaskRunner>().ProcessPendingAsync(token); }
        async Task<ReportingExportTaskRecord> ReadAsync(TenantContext context, Guid id, bool checkAuthority = false)
        {
            await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetTenant(context);
            var task = await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<ReportingExportTaskRecord>(
                ReportingExportTaskSql.FindByIdFor(provider), new Dictionary<string, object?> { ["Id"] = id }, token); Assert.IsNotNull(task);
            if (checkAuthority) Assert.IsNull(await scope.ServiceProvider.GetRequiredService<IReportingExportTaskAuthority>().ValidateAsync(task, token));
            return task;
        }
        async Task<int> FilesAsync(TenantContext context, Guid id)
        {
            await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetTenant(context);
            return (await scope.ServiceProvider.GetRequiredService<ITenantResourceFileStore>().ListReadyAsync("reporting", id, token)).Count;
        }
    }
    private static async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string path, object? body, HttpStatusCode expected, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, path); if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, token); var json = await response.Content.ReadAsStringAsync(token);
        Assert.AreEqual(expected, response.StatusCode, path + " " + json); using var document = JsonDocument.Parse(json); return document.RootElement.Clone();
    }
    private sealed class ControlledWorkbook : IReportingExportWorkbookSource
    {
        internal Guid? HoldTask;
        internal readonly ConcurrentBag<Guid> Calls = [];
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<Result<ReportingExportGeneratedFile>> GenerateAsync(ReportingExportTaskRecord task, ClaimsPrincipal principal, CancellationToken cancellationToken)
        {
            Calls.Add(task.Id); if (task.Id == HoldTask) { Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
            var bytes = ReportingExcelExportRenderer.Render([new("SchemaName", "Schema")], [new(new Dictionary<string, string?> { ["SchemaName"] = "membership fixture" })]);
            return Result<ReportingExportGeneratedFile>.Success(new(1, "membership.xlsx", bytes));
        }
    }
}
