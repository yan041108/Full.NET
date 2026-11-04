using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
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
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        => await VerifyAsync(provider, beforeRows, suspendTenant: false);

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, false)]
    [DataRow(DatabaseProvider.SqlServer, true)]
    [DataRow(DatabaseProvider.MySql, false)]
    [DataRow(DatabaseProvider.MySql, true)]
    public async Task Suspending_A_preserves_queue_or_lease_and_recovers_after_reactivation_without_replaying_B(DatabaseProvider provider, bool beforeRows)
        => await VerifyAsync(provider, beforeRows, suspendTenant: true);

    // 同一真实模板与逐行处理器覆盖成员退出和企业停用，只有撤权入口与恢复资格不同。
    private static async Task VerifyAsync(DatabaseProvider provider, bool beforeRows, bool suspendTenant)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120)); var token = deadline.Token;
        var database = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync(); var pause = new Pause(); var clock = new AdjustableClock();
        using var factory = new FullNetApiFactory(provider, database,
            new Dictionary<string, string?> { ["Identity:SessionLoginPolicy"] = "AllowMultiple" }, configureTestServices: services => {
                if (suspendTenant) { services.RemoveAll<IClock>(); services.AddSingleton<IClock>(clock); }
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
        TenantSummary? suspended = null;
        if (suspendTenant)
            host.DefaultRequestHeaders.Authorization = new("Bearer", await factory.CreateHostAccessTokenAsync(
                ["tenancy.host_tenants.read", "tenancy.tenant_lifecycle.suspend", "tenancy.tenant_lifecycle.reactivate"], token));
        pause.TaskId = beforeRows ? aTask.Id : null; Task<int>? processing = null;
        try
        {
            if (beforeRows) { processing = ProcessAsync(); await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15), token); }
            if (suspendTenant)
            {
                using var current = await host.GetAsync($"/api/v1/tenancy/tenants/{a.Id:D}", token);
                Assert.AreEqual(HttpStatusCode.OK, current.StatusCode);
                var tenant = await current.Content.ReadFromJsonAsync<TenantSummary>(token); Assert.IsNotNull(tenant);
                using var response = await host.PostAsJsonAsync($"/api/v1/tenancy/tenants/{a.Id:D}/suspend", new SuspendTenantRequest(tenant.Version), token);
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(token));
                suspended = await response.Content.ReadFromJsonAsync<TenantSummary>(token); Assert.IsNotNull(suspended); Assert.IsFalse(suspended.IsActive);
            }
            else
                using (var leave = await aClient.PostAsJsonAsync("/api/v1/identity/tenant-members/me/leave", new LeaveTenantMembershipRequest(1), token))
                    Assert.AreEqual(HttpStatusCode.OK, leave.StatusCode, await leave.Content.ReadAsStringAsync(token));
            pause.Release.TrySetResult(); processing ??= ProcessAsync(); Assert.AreEqual(suspendTenant && !beforeRows ? 1 : 2, await processing);
        }
        finally { pause.Release.TrySetResult(); if (processing is not null) { try { await processing; } catch { /* 收回已启动的测试执行任务，保留主断言原始失败。 */ } } }
        var aAfter = await ReadAsync(aContext, aTask.Id); var bAfter = await ReadAsync(bContext, bTask.Id);
        Assert.AreEqual(0, aAfter.SucceededRowCount);
        if (suspendTenant)
        {
            Assert.AreEqual(beforeRows ? ImportExportTaskStatusKeys.Executing : ImportExportTaskStatusKeys.Queued, aAfter.StatusKey);
            Assert.AreEqual(0, aAfter.NextLineNumber); Assert.AreEqual(0, aAfter.ProcessedRowCount);
            Assert.AreEqual(aBefore.ExecutionRowsJson, aAfter.ExecutionRowsJson);
            Assert.AreEqual(aBefore.Version + (beforeRows ? 1 : 0), aAfter.Version);
            Assert.IsNull(aAfter.ErrorReceiptFileId);
        }
        else if (beforeRows)
        {
            var rows = ImportExportTaskMapper.DeserializeExecutionState(aAfter.ExecutionRowsJson).Rows;
            Assert.AreEqual(1, rows.Count); Assert.IsFalse(rows[0].Succeeded); Assert.AreEqual(ImportTaskAuthorityErrorCodes.Denied, rows[0].ErrorCode);
        }
        else Assert.AreEqual(ImportTaskAuthorityErrorCodes.Denied, aAfter.ErrorCode);
        if (!suspendTenant) Assert.AreEqual(ImportExportTaskStatusKeys.ExecutionFailed, aAfter.StatusKey);
        Assert.AreEqual(ImportExportTaskStatusKeys.ExecutionSucceeded, bAfter.StatusKey); Assert.AreEqual(1, bAfter.SucceededRowCount);
        Assert.AreEqual(aBefore.ActorSessionBindingJson, aAfter.ActorSessionBindingJson); Assert.AreEqual(bBefore.ActorSessionBindingJson, bAfter.ActorSessionBindingJson);
        Assert.AreEqual(0, await PositionCountAsync(aContext)); Assert.AreEqual(1, await PositionCountAsync(bContext));
        Assert.AreEqual(0, await ProcessAsync());
        using var bRead = await bClient.GetAsync($"/api/v1/import-export/tasks/{bTask.Id:D}", token); Assert.AreEqual(HttpStatusCode.OK, bRead.StatusCode);
        using var aReceipt = await aClient.GetAsync($"/api/v1/import-export/tasks/{aTask.Id:D}/error-receipt", token);
        if (!suspendTenant) Assert.AreEqual(HttpStatusCode.Forbidden, aReceipt.StatusCode);
        else
        {
            Assert.IsFalse(aReceipt.IsSuccessStatusCode);
            using (var blocked = await aClient.PostAsync($"/api/v1/import-export/tasks/{aTask.Id:D}/retry", null, token))
                Assert.IsFalse(blocked.IsSuccessStatusCode, "停用时不能重新排队原任务。");
            var stillBlocked = await ReadAsync(aContext, aTask.Id);
            Assert.AreEqual(aAfter.Version, stillBlocked.Version); Assert.AreEqual(aAfter.ExecutionRowsJson, stillBlocked.ExecutionRowsJson);
            using var response = await host.PostAsJsonAsync($"/api/v1/tenancy/tenants/{a.Id:D}/reactivate", new ReactivateTenantRequest(suspended!.Version), token);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(token));
            // 停用不伪造失败或删除原队列；未到期租约不可抢占，到期后复用原检查点和绑定恢复。
            var restored = await ReadAsync(aContext, aTask.Id, true); Assert.AreEqual(aAfter.Version, restored.Version);
            if (beforeRows)
            {
                Assert.AreEqual(0, await ProcessAsync()); Assert.IsNotNull(aAfter.LeaseExpiresAtUtc);
                clock.Offset = aAfter.LeaseExpiresAtUtc.Value - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1);
            }
            pause.TaskId = null; Assert.AreEqual(1, await ProcessAsync());
            var completed = await ReadAsync(aContext, aTask.Id);
            Assert.AreEqual(ImportExportTaskStatusKeys.ExecutionSucceeded, completed.StatusKey); Assert.AreEqual(1, completed.SucceededRowCount);
            Assert.AreEqual(aBefore.ActorSessionBindingJson, completed.ActorSessionBindingJson);
            Assert.AreEqual(1, await PositionCountAsync(aContext)); Assert.AreEqual(1, await PositionCountAsync(bContext));
            var stableB = await ReadAsync(bContext, bTask.Id); Assert.AreEqual(bAfter.Version, stableB.Version);
            Assert.AreEqual(0, await ProcessAsync());
        }

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
    private sealed class AdjustableClock : IClock
    {
        internal TimeSpan Offset;
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow + Offset;
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
