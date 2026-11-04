using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageTenantMembers.Persistence;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Domain;
using Full.NET.Modules.ImportExport.Features.ManageImportTasks;
using Full.NET.Modules.ImportExport.ImportTasks;
using Full.NET.Modules.ImportExport.Persistence;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Features.ImportExport;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.ImportExport;

/// <summary>真实模板、源文件、Schema 和逐行业务写入；暂停点只调度时序，不替代授权或处理器结果。</summary>
[TestClass]
public sealed class ImportMembershipRevocationTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, false)]
    [DataRow(DatabaseProvider.SqlServer, true)]
    [DataRow(DatabaseProvider.MySql, false)]
    [DataRow(DatabaseProvider.MySql, true)]
    public async Task Leaving_A_before_claim_or_row_execution_prevents_positions_and_preserves_B(DatabaseProvider provider, bool beforeRows)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120)); var token = deadline.Token;
        var database = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync(); var pause = new Pause();
        using var factory = new FullNetApiFactory(provider, database,
            new Dictionary<string, string?> { ["Identity:SessionLoginPolicy"] = "AllowMultiple" }, configureTestServices: services => {
                var descriptor = services.Single(item => item.ServiceType == typeof(IStaticImportSchemaHandler)
                    && item.ImplementationType == typeof(TenantPositionsStaticImportSchemaHandler));
                services.Remove(descriptor);
                services.AddScoped<IStaticImportSchemaHandler>(scope => new PausingHandler(
                    ActivatorUtilities.CreateInstance<TenantPositionsStaticImportSchemaHandler>(scope), pause));
            });
        await factory.InitializeAsync(token); using var host = factory.CreateClientForHost("localhost");
        using var aClient = factory.CreateClientForHost("acme.localhost");
        var a = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(aClient, token);
        // 自助切换声明不能以持久化平台切换权限代替；被测账号只有导入及本人退出的精确权限。
        var identity = await factory.CreateHostIdentityAsync("import-member-" + Guid.CreateVersion7().ToString("N"),
            [ImportExportPermissions.StaticSchemasRead, ImportExportPermissions.ImportTasksCreate, ImportExportPermissions.ImportTasksRead,
             ImportExportPermissions.ImportTasksExecute, OrganizationPositionManagementPermissions.Import, IdentityTenantMembershipPermissions.LeaveSelf],
            token, FullNetApiFactory.TestPassword);
        var bDomain = "import-b-" + Guid.CreateVersion7().ToString("N") + ".localhost"; TenantSummary b;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var current = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>(); current.SetHost();
            var created = await scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>().ProvisionAsync(
                new ProvisionTenantRequest("import-b-" + Guid.CreateVersion7().ToString("N"), "Import B", bDomain), token);
            Assert.IsTrue(created.IsSuccess, created.Error?.Code); b = created.Value!;
            foreach (var entry in new[] { (a.Id, "acme", "Acme"), (b.Id, b.Identifier, b.Name) })
            {
                current.SetTenant(new TenantContext(entry.Item1, entry.Item2, entry.Item3));
                await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(TenantMembershipSql.InsertMember,
                    IdentitySqlParameters.Create(("Id", Guid.CreateVersion7()), ("UserId", identity.UserId), ("MemberRole", TenantMemberRoles.Member),
                        ("Status", TenantMemberStatuses.Active), ("CreatedAtUtc", DateTimeOffset.UtcNow), ("UpdatedAtUtc", DateTimeOffset.UtcNow), ("Version", 1)), token);
            }
        }
        using var bClient = factory.CreateClientForHost(bDomain);
        foreach (var entry in new[] { (aClient, a.Id), (bClient, b.Id) })
        {
            var login = await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, identity.Username, FullNetApiFactory.TestPassword, token);
            entry.Item1.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, login, entry.Item2, token));
        }
        var code = "revoke-" + Guid.CreateVersion7().ToString("N");
        var aTask = await QueueAsync(aClient); var bTask = await QueueAsync(bClient);
        var aContext = new TenantContext(a.Id, "acme", "Acme"); var bContext = new TenantContext(b.Id, b.Identifier, b.Name);
        var aBefore = await ReadAsync(aContext, aTask.Id, true); var bBefore = await ReadAsync(bContext, bTask.Id, true);
        pause.TaskId = beforeRows ? aTask.Id : null; Task<int>? processing = null;
        try
        {
            if (beforeRows) { processing = ProcessAsync(); await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15), token); }
            using (var leave = await aClient.PostAsJsonAsync("/api/v1/identity/tenant-members/me/leave", new LeaveTenantMembershipRequest(1), token))
                Assert.AreEqual(HttpStatusCode.OK, leave.StatusCode, await leave.Content.ReadAsStringAsync(token));
            pause.Release.TrySetResult(); processing ??= ProcessAsync(); Assert.AreEqual(2, await processing);
        }
        finally { pause.Release.TrySetResult(); if (processing is not null) { try { await processing; } catch { /* 收回已启动的测试执行任务，保留主断言原始失败。 */ } } }
        var aAfter = await ReadAsync(aContext, aTask.Id); var bAfter = await ReadAsync(bContext, bTask.Id);
        Assert.AreEqual(ImportExportTaskStatusKeys.ExecutionFailed, aAfter.StatusKey); Assert.AreEqual(0, aAfter.SucceededRowCount);
        if (beforeRows)
        {
            var rows = ImportExportTaskMapper.DeserializeExecutionState(aAfter.ExecutionRowsJson).Rows;
            Assert.AreEqual(1, rows.Count); Assert.IsFalse(rows[0].Succeeded); Assert.AreEqual(ImportTaskAuthorityErrorCodes.Denied, rows[0].ErrorCode);
        }
        else Assert.AreEqual(ImportTaskAuthorityErrorCodes.Denied, aAfter.ErrorCode);
        Assert.AreEqual(ImportExportTaskStatusKeys.ExecutionSucceeded, bAfter.StatusKey); Assert.AreEqual(1, bAfter.SucceededRowCount);
        Assert.AreEqual(aBefore.ActorSessionBindingJson, aAfter.ActorSessionBindingJson); Assert.AreEqual(bBefore.ActorSessionBindingJson, bAfter.ActorSessionBindingJson);
        Assert.AreEqual(0, await PositionCountAsync(aContext)); Assert.AreEqual(1, await PositionCountAsync(bContext));
        Assert.AreEqual(0, await ProcessAsync());
        using var bRead = await bClient.GetAsync($"/api/v1/import-export/tasks/{bTask.Id:D}", token); Assert.AreEqual(HttpStatusCode.OK, bRead.StatusCode);
        using var aReceipt = await aClient.GetAsync($"/api/v1/import-export/tasks/{aTask.Id:D}/error-receipt", token);
        Assert.AreEqual(HttpStatusCode.Forbidden, aReceipt.StatusCode);

        async Task<ImportExportTaskDetailResponse> QueueAsync(HttpClient client)
        {
            using var template = await client.GetAsync($"/api/v1/import-export/schemas/{StaticImportSchemaKeys.OrganizationTenantPositions}/worksheets/positions/template", token);
            Assert.AreEqual(HttpStatusCode.OK, template.StatusCode);
            var bytes = ImportExportWorkerAssertions.FillPositionTemplate(await template.Content.ReadAsByteArrayAsync(token), positionCode: code);
            using var form = new MultipartFormDataContent { { new StringContent(StaticImportSchemaKeys.OrganizationTenantPositions), "schemaKey" }, { new StringContent("positions"), "worksheetKey" } };
            var file = new ByteArrayContent(bytes); file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"); form.Add(file, "file", "positions.xlsx");
            using var created = await client.PostAsync("/api/v1/import-export/tasks", form, token);
            Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync(token));
            var task = await created.Content.ReadFromJsonAsync<ImportExportTaskDetailResponse>(token); Assert.IsNotNull(task); Assert.AreEqual(1, task.ValidRowCount);
            using var queued = await client.PostAsync($"/api/v1/import-export/tasks/{task.Id:D}/execute", null, token);
            Assert.AreEqual(HttpStatusCode.OK, queued.StatusCode, await queued.Content.ReadAsStringAsync(token)); return task;
        }
        async Task<int> ProcessAsync() { await using var scope = factory.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<ImportExportTaskRunner>().ProcessPendingAsync(token); }
        async Task<ImportExportTaskRecord> ReadAsync(TenantContext context, Guid id, bool verifyAuthority = false)
        {
            await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetTenant(context);
            var task = await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<ImportExportTaskRecord>(ImportExportTaskSql.FindById,
                new Dictionary<string, object?> { ["Id"] = id }, token); Assert.IsNotNull(task);
            if (verifyAuthority) Assert.IsTrue((await scope.ServiceProvider.GetRequiredService<ImportTaskAuthority>().ValidateStoredAsync(task, token)).IsSuccess);
            return task;
        }
        async Task<int> PositionCountAsync(TenantContext context)
        {
            await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetTenant(context);
            return await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<int>(new SqlStatement("test.import.member_position_count",
                "SELECT COUNT(1) FROM fn_organization_position WHERE TenantId=@TenantId AND Code=@Code", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId),
                new Dictionary<string, object?> { ["Code"] = code }, token);
        }
    }
    private sealed class Pause
    {
        internal Guid? TaskId;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class PausingHandler(IStaticImportSchemaHandler inner, Pause pause) : IStaticImportSchemaHandler
    {
        public string SchemaKey => inner.SchemaKey;
        public IReadOnlyCollection<string> ExecutionCapabilityPermissions => inner.ExecutionCapabilityPermissions;
        public StaticImportSchemaDefinition GetDefinition() => inner.GetDefinition();
        public byte[] CreateTemplate(string worksheetKey) => inner.CreateTemplate(worksheetKey);
        public Task<Result<StaticImportPreviewResult>> PreviewAsync(Stream content, long contentLength, StaticImportPreviewContext context, CancellationToken cancellationToken = default) =>
            inner.PreviewAsync(content, contentLength, context, cancellationToken);
        public async Task<Result<StaticImportBatchExecutionResult>> ExecuteBatchAsync(Stream content, long contentLength, int startLineNumber, int batchSize,
            StaticImportPreviewContext context, CancellationToken cancellationToken = default)
        {
            // 处于调度器核证之后、实际逐行处理之前；恢复时仍调用原生产处理器重新授权每一行。
            if (context.TaskId == pause.TaskId) { pause.Entered.TrySetResult(); await pause.Release.Task.WaitAsync(cancellationToken); }
            return await inner.ExecuteBatchAsync(content, contentLength, startLineNumber, batchSize, context, cancellationToken);
        }
    }
}
