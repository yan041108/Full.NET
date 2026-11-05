using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.ImportExport.Configuration;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.ImportTasks;
using Full.NET.Modules.ImportExport.Persistence;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Api;

/// <summary>真实上传、到期拒绝、释放恢复与在途保护；同一旅程验证成对迁移的部分 DDL 恢复。</summary>
[TestClass]
public sealed class ImportSourceRetentionApiTests
{
    private const string Tasks = "/api/v1/import-export/tasks";
    private static readonly string[] Headers = ["code", "name", "displayOrder", "unitCode", "positionLevelCode"];

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Expired_sources_resume_cleanup_without_discarding_history_receipts_or_in_flight_work(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var failure = new CompletionFailure();
        using var factory = new FullNetApiFactory(provider, connection, new Dictionary<string, string?>
        { ["FullNet:ImportExport:RunSynchronously"] = "false", ["FullNet:ImportExport:SourceRetentionDays"] = "2", ["FullNet:ImportExport:SourceCleanupBatchSize"] = "1" },
            configureTestServices: services =>
            {
                var original = services.Last(item => item.ServiceType == typeof(ICommandExecutor)); services.Remove(original);
                services.AddScoped<ICommandExecutor>(sp => new FailCompletion((ICommandExecutor)(original.ImplementationInstance
                    ?? original.ImplementationFactory?.Invoke(sp) ?? ActivatorUtilities.CreateInstance(sp, original.ImplementationType!)), failure));
            });
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        var host = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, "admin"); client.DefaultRequestHeaders.Authorization = new("Bearer", host);
        var tenant = (await client.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(item => item.Identifier == "acme");
        client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, host, tenant.Id));
        var template = await client.GetByteArrayAsync("/api/v1/import-export/schemas/organization.tenant_positions/worksheets/positions/template");
        var first = await CreateAsync("first"); var second = await CreateAsync("second");
        var firstId = first.GetProperty("id").GetGuid(); var secondId = second.GetProperty("id").GetGuid();
        // 新期限按数据库微秒精度保存，旧 SQL Server 创建时间可能仍含第七位小数。
        AssertStoredDeadline(first.GetProperty("createdAtUtc").GetDateTimeOffset().AddDays(2),
            first.GetProperty("sourceRetention").GetProperty("expiresAtUtc").GetDateTimeOffset());
        factory.Services.GetRequiredService<IOptionsMonitor<ImportExportOptions>>().CurrentValue.SourceRetentionDays = 365;
        Assert.AreEqual(first.GetProperty("sourceRetention").ToString(), (await GetAsync(firstId)).GetProperty("sourceRetention").ToString());
        await using var db = MyTasksApiTests.Open(provider, connection); await db.OpenAsync();
        await VerifyMigrationAsync(provider, connection, db, firstId, secondId);
        var queued = await CreateAsync("queued"); var executing = await CreateAsync("executing");
        var queuedId = queued.GetProperty("id").GetGuid(); var executingId = executing.GetProperty("id").GetGuid();
        await PostAsync(client, $"{Tasks}/{queuedId:D}/execute", new { });
        await PostAsync(client, $"{Tasks}/{executingId:D}/execute", new { });
        await db.ExecuteAsync("UPDATE fn_import_export_task SET StatusKey='executing',LeaseId=@Lease,LeaseExpiresAtUtc=@Future WHERE Id=@Id",
            new { Lease = Guid.CreateVersion7(), Future = DateTimeOffset.UtcNow.AddHours(1), Id = executingId });
        foreach (var item in new[] { (Id: firstId, Days: -2), (Id: secondId, Days: -1), (Id: queuedId, Days: -3), (Id: executingId, Days: -4) })
            await db.ExecuteAsync("UPDATE fn_import_export_task SET SourceExpiresAtUtc=@Expiry WHERE Id=@Id", new { item.Id, Expiry = DateTimeOffset.UtcNow.AddDays(item.Days) });
        var receipt = await PrepareReceiptAsync();
        var frozen = await ReadFrozenAsync(firstId); var queuedFrozen = await ReadFrozenAsync(queuedId); var executingFrozen = await ReadFrozenAsync(executingId);
        failure.Armed = 1;
        Assert.AreEqual(0, await ProcessAsync());
        Assert.AreEqual("releasing", (await GetAsync(firstId)).GetProperty("sourceRetention").GetProperty("stateKey").GetString());
        Assert.IsNull(await db.ExecuteScalarAsync<DateTime?>("SELECT SourceReleasedAtUtc FROM fn_import_export_task WHERE Id=@Id", new { Id = firstId }));
        using (var expired = await client.PostAsJsonAsync($"{Tasks}/{firstId:D}/retry", new { }))
        { Assert.AreEqual(HttpStatusCode.Conflict, expired.StatusCode); Assert.AreEqual(ImportExportErrorCodes.SourceExpired, (await expired.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()); }
        Assert.AreEqual(frozen, await ReadFrozenAsync(firstId));
        await using (var other = factory.Services.CreateAsyncScope())
        {
            other.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetTenant(new(Guid.CreateVersion7(), "other", "Other"));
            Assert.AreEqual(0, await other.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(ImportSourceRetentionSql.CompleteRelease,
                new Dictionary<string, object?> { ["Id"] = firstId, ["FileId"] = first.GetProperty("sourceFileId").GetGuid(), ["Now"] = DateTimeOffset.UtcNow }));
        }
        Assert.AreEqual(1, await ProcessAsync()); Assert.AreEqual("released", (await GetAsync(secondId)).GetProperty("sourceRetention").GetProperty("stateKey").GetString());
        Assert.AreEqual(1, await ProcessAsync());
        var released = await GetAsync(firstId); Assert.AreEqual("released", released.GetProperty("sourceRetention").GetProperty("stateKey").GetString());
        Assert.AreEqual(frozen, await ReadFrozenAsync(firstId));
        CollectionAssert.AreEqual(template, await client.GetByteArrayAsync($"{Tasks}/{firstId:D}/error-receipt"));
        Assert.AreEqual(receipt, await db.ExecuteScalarAsync<Guid>("SELECT ErrorReceiptFileId FROM fn_import_export_task WHERE Id=@Id", new { Id = firstId }));
        Assert.AreEqual(queuedFrozen, await ReadFrozenAsync(queuedId)); Assert.AreEqual(executingFrozen, await ReadFrozenAsync(executingId));
        Assert.AreEqual("retained_for_execution", (await GetAsync(queuedId)).GetProperty("sourceRetention").GetProperty("stateKey").GetString());
        Assert.AreEqual("retained_for_execution", (await GetAsync(executingId)).GetProperty("sourceRetention").GetProperty("stateKey").GetString());
        await AssertReadyAsync(queued); await AssertReadyAsync(executing);
        var currentQueued = await GetAsync(queuedId);
        await PostAsync(client, $"{Tasks}/{queuedId:D}/cancel", new CancelImportExportTaskRequest(currentQueued.GetProperty("version").GetInt64()));
        Assert.AreEqual(1, await ProcessAsync()); Assert.AreEqual("released", (await GetAsync(queuedId)).GetProperty("sourceRetention").GetProperty("stateKey").GetString());
        Assert.AreEqual(0, await ProcessAsync()); await AssertReadyAsync(executing);

        async Task<JsonElement> CreateAsync(string suffix)
        {
            var workbook = ImportProfilesApiTests.Workbook(template, [Headers, ["retention-" + suffix, "Position", "0", "", ""]]);
            var mapping = new ImportColumnMappingRequest(Headers, Headers.Select(header => new ImportColumnMapping(header, header)).ToArray());
            using var content = new MultipartFormDataContent { { new StringContent("organization.tenant_positions"), "schemaKey" }, { new StringContent("positions"), "worksheetKey" },
                { new StringContent(JsonSerializer.Serialize(mapping, new JsonSerializerOptions(JsonSerializerDefaults.Web))), "columnMapping" }, { new ByteArrayContent(workbook), "file", "positions.xlsx" } };
            using var response = await client.PostAsync(Tasks, content); Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        Task<JsonElement> GetAsync(Guid id) => client.GetFromJsonAsync<JsonElement>($"{Tasks}/{id:D}");
        async Task<int> ProcessAsync()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<ImportSourceRetentionProcessor>().ProcessDueAsync(default);
            Assert.IsFalse(scope.ServiceProvider.GetRequiredService<ICurrentTenant>().IsAvailable); return result;
        }
        async Task<Guid> PrepareReceiptAsync()
        {
            await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetTenant(new(tenant.Id, tenant.Identifier, tenant.Name));
            await using var bytes = new MemoryStream(template);
            var result = await scope.ServiceProvider.GetRequiredService<ITenantResourceFileStore>().UploadAsync("import_export", firstId,
                first.GetProperty("requestedByUserId").GetGuid(), "errors.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", bytes, bytes.Length);
            Assert.IsTrue(result.IsSuccess, result.Error?.Code);
            // 通过正式 Files 合同准备隔离回执，直接验证寿命边界，不重复错误回执生成器旅程。
            await db.ExecuteAsync("UPDATE fn_import_export_task SET ErrorReceiptFileId=@File,StatusKey='execution_failed',ProcessedRowCount=1,ExecutionFailedRowCount=1,NextLineNumber=3 WHERE Id=@Id",
                new { File = result.Value!.FileId, Id = firstId }); return result.Value.FileId;
        }
        async Task AssertReadyAsync(JsonElement task)
        {
            await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetTenant(new(tenant.Id, tenant.Identifier, tenant.Name));
            var content = await scope.ServiceProvider.GetRequiredService<ITenantResourceFileStore>().OpenReadyContentAsync("import_export", task.GetProperty("id").GetGuid(), task.GetProperty("sourceFileId").GetGuid());
            Assert.IsTrue(content.IsSuccess, content.Error?.Code); await content.Value!.Content.DisposeAsync();
        }
        async Task<string> ReadFrozenAsync(Guid id)
        {
            var row = await db.QuerySingleAsync("SELECT StatusKey,ProcessedRowCount,SucceededRowCount,NextLineNumber,PreviewRowsJson,ColumnMappingJson,ExecutionRowsJson FROM fn_import_export_task WHERE Id=@Id", new { Id = id });
            return JsonSerializer.Serialize((IDictionary<string, object>)row);
        }
    }

    private static async Task VerifyMigrationAsync(DatabaseProvider provider, string connection, DbConnection db, Guid first, Guid second)
    {
        var expected = await db.ExecuteScalarAsync<DateTimeOffset>("SELECT SourceExpiresAtUtc FROM fn_import_export_task WHERE Id=@Id", new { Id = second });
        await db.ExecuteAsync("DROP INDEX ix_fn_import_export_task_source_retention ON fn_import_export_task");
        await db.ExecuteAsync("ALTER TABLE fn_import_export_task DROP COLUMN SourceReleasedAtUtc");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "ALTER TABLE fn_import_export_task ALTER COLUMN SourceExpiresAtUtc datetime2(6) NULL"
            : "ALTER TABLE fn_import_export_task MODIFY COLUMN SourceExpiresAtUtc datetime(6) NULL");
        await db.ExecuteAsync("UPDATE fn_import_export_task SET SourceExpiresAtUtc=NULL WHERE Id=@Id", new { Id = first });
        var runner = ReviewFixMigrationRecoverySupport.CreateRunner(provider, connection);
        for (var index = 0; index < 2; index++)
        { await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db, "375_ImportExportSourceRetention.sql", provider == DatabaseProvider.SqlServer); Assert.AreEqual(1, (await runner.MigrateAsync()).ExecutedScriptCount); }
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(expected, await db.ExecuteScalarAsync<DateTimeOffset>("SELECT SourceExpiresAtUtc FROM fn_import_export_task WHERE Id=@Id", new { Id = second }));
        var created = await db.ExecuteScalarAsync<DateTimeOffset>("SELECT CreatedAtUtc FROM fn_import_export_task WHERE Id=@Id", new { Id = first });
        AssertStoredDeadline(created.AddDays(7), await db.ExecuteScalarAsync<DateTimeOffset>("SELECT SourceExpiresAtUtc FROM fn_import_export_task WHERE Id=@Id", new { Id = first }));
    }

    private static void AssertStoredDeadline(DateTimeOffset expected, DateTimeOffset actual) =>
        Assert.IsTrue(Math.Abs((expected - actual).Ticks) <= 10, $"保留期限应为 {expected:O}，实际 {actual:O}，允许数据库一微秒精度。");

    private static async Task PostAsync(HttpClient client, string path, object body)
    { using var response = await client.PostAsJsonAsync(path, body); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, path + ": " + await response.Content.ReadAsStringAsync()); }
    private sealed class CompletionFailure { internal int Armed; }
    private sealed class FailCompletion(ICommandExecutor inner, CompletionFailure failure) : ICommandExecutor
    {
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(statement, ImportSourceRetentionSql.CompleteRelease) && Interlocked.Exchange(ref failure.Armed, 0) == 1)
                throw new IOException("Controlled failure after file release, before completion metadata.");
            return inner.ExecuteAsync(statement, parameters, cancellationToken);
        }
    }
}
