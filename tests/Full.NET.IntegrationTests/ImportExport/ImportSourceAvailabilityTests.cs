using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Domain;
using Full.NET.Modules.ImportExport.Features.ManageImportTasks;
using Full.NET.Modules.ImportExport.ImportTasks;
using Full.NET.Modules.ImportExport.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.ImportExport;

/// <summary>真实上传、文件释放、队列和业务处理，证明来源丢失不会反复重置原任务。</summary>
[TestClass]
public sealed class ImportSourceAvailabilityTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Missing_source_preserves_preview_or_failure_while_available_source_executes(DatabaseProvider provider)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120)); var token = deadline.Token;
        var database = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, database, new Dictionary<string, string?> { ["FullNet:ImportExport:RunSynchronously"] = "false" });
        await factory.InitializeAsync(token); using var host = factory.CreateClientForHost("localhost"); using var client = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(client, token);
        var login = await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin", FullNetApiFactory.TestPassword, token);
        client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, login, tenant.Id, token));
        var preview = await CreateAsync("missing-preview"); await ReleaseAsync(preview);
        var before = await ReadAsync(preview.Id);
        await RejectAsync(preview.Id, "execute"); var unchanged = await ReadAsync(preview.Id); AssertUnchanged(before, unchanged);
        using (var cancelled = await client.PostAsJsonAsync($"/api/v1/import-export/tasks/{preview.Id:D}/cancel", new { version = before.Version }, token))
            Assert.AreEqual(HttpStatusCode.OK, cancelled.StatusCode, await cancelled.Content.ReadAsStringAsync(token));
        Assert.AreEqual(ImportExportTaskStatusKeys.Canceled, (await ReadAsync(preview.Id)).StatusKey);
        var late = await CreateAsync("missing-after-queue"); await QueueAsync(late.Id); await ReleaseAsync(late);
        Assert.AreEqual(1, await ProcessAsync()); var failed = await ReadAsync(late.Id);
        Assert.AreEqual(ImportExportTaskStatusKeys.ExecutionFailed, failed.StatusKey);
        await RejectAsync(late.Id, "retry"); AssertUnchanged(failed, await ReadAsync(late.Id));
        var valid = await CreateAsync("available"); await QueueAsync(valid.Id); Assert.AreEqual(1, await ProcessAsync());
        var completed = await ReadAsync(valid.Id); Assert.AreEqual(ImportExportTaskStatusKeys.ExecutionSucceeded, completed.StatusKey); Assert.AreEqual(1, completed.SucceededRowCount);
        Assert.AreEqual(0, await ProcessAsync());

        async Task<ImportExportTaskDetailResponse> CreateAsync(string purpose)
        {
            using var template = await client.GetAsync($"/api/v1/import-export/schemas/{StaticImportSchemaKeys.OrganizationTenantPositions}/worksheets/positions/template", token);
            Assert.AreEqual(HttpStatusCode.OK, template.StatusCode);
            var bytes = ImportExportWorkerAssertions.FillPositionTemplate(await template.Content.ReadAsByteArrayAsync(token), positionCode: purpose + "-" + Guid.CreateVersion7().ToString("N"));
            using var upload = new MultipartFormDataContent { { new StringContent(StaticImportSchemaKeys.OrganizationTenantPositions), "schemaKey" }, { new StringContent("positions"), "worksheetKey" }, { new ByteArrayContent(bytes), "file", "positions.xlsx" } };
            using var response = await client.PostAsync("/api/v1/import-export/tasks", upload, token);
            Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(token));
            var task = await response.Content.ReadFromJsonAsync<ImportExportTaskDetailResponse>(token); Assert.IsNotNull(task); Assert.AreEqual(1, task.ValidRowCount); return task;
        }
        async Task ReleaseAsync(ImportExportTaskDetailResponse task)
        {
            await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetTenant(new TenantContext(tenant.Id, "acme", "Acme"));
            await scope.ServiceProvider.GetRequiredService<ITenantResourceFileStore>().ReleaseAsync("import_export", task.Id, task.SourceFileId, token);
        }
        async Task<ImportExportTaskRecord> ReadAsync(Guid id)
        {
            await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetTenant(new TenantContext(tenant.Id, "acme", "Acme"));
            var task = await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<ImportExportTaskRecord>(ImportExportTaskSql.FindById, new Dictionary<string, object?> { ["Id"] = id }, token);
            Assert.IsNotNull(task); return task;
        }
        async Task QueueAsync(Guid id) { using var response = await client.PostAsync($"/api/v1/import-export/tasks/{id:D}/execute", null, token); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(token)); }
        async Task RejectAsync(Guid id, string action)
        {
            using var response = await client.PostAsync($"/api/v1/import-export/tasks/{id:D}/{action}", null, token);
            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode, await response.Content.ReadAsStringAsync(token));
            var error = await response.Content.ReadFromJsonAsync<JsonElement>(token); Assert.AreEqual(ImportExportErrorCodes.SourceUnavailable, error.GetProperty("code").GetString());
        }
        async Task<int> ProcessAsync() { await using var scope = factory.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<ImportExportTaskRunner>().ProcessPendingAsync(token); }
    }
    private static void AssertUnchanged(ImportExportTaskRecord before, ImportExportTaskRecord after)
    {
        Assert.AreEqual(before.StatusKey, after.StatusKey); Assert.AreEqual(before.Version, after.Version); Assert.AreEqual(before.SourceFileId, after.SourceFileId);
        Assert.AreEqual(before.ActorSessionBindingJson, after.ActorSessionBindingJson); Assert.AreEqual(before.ExecutionRowsJson, after.ExecutionRowsJson);
        Assert.AreEqual(before.NextLineNumber, after.NextLineNumber); Assert.AreEqual(before.SucceededRowCount, after.SucceededRowCount);
    }
}
