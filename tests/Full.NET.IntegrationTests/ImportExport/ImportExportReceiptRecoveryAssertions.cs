using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.ImportTasks;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Full.NET.IntegrationTests.ImportExport;

/// <summary>回执故障仅注入当前任务的存储或检查点边界，业务处理使用正式岗位 Schema。</summary>
internal static partial class ImportExportWorkerAssertions
{
    /// <summary>预览后停用机构制造真实失败行，停止故障宿主后由新宿主恢复唯一可下载回执。</summary>
    internal static async Task VerifyReceiptRecoveryAsync(DatabaseProvider provider, string connectionString, bool progressFailure)
    {
        var settings = Settings();
        using var api = new FullNetApiFactory(provider, connectionString, settings);
        await api.InitializeAsync();
        using var client = api.CreateClientForHost("localhost");
        var token = await ImportExportTaskAssertions.LoginAndEnterAcmeTenantAsync(client, default);
        var unitCode = $"receipt-{Guid.NewGuid():N}";
        using var unitRequest = Request(HttpMethod.Post, "/api/v1/organization/units", token);
        unitRequest.Content = JsonContent.Create(new CreateOrganizationUnitRequest(null, unitCode, "Receipt recovery unit", 10));
        using var unitResponse = await client.SendAsync(unitRequest);
        Assert.AreEqual(HttpStatusCode.Created, unitResponse.StatusCode, await unitResponse.Content.ReadAsStringAsync());
        using var unitJson = JsonDocument.Parse(await unitResponse.Content.ReadAsStringAsync());
        var unitId = unitJson.RootElement.GetProperty("id").GetGuid();
        using var templateRequest = Request(HttpMethod.Get,
            $"/api/v1/import-export/schemas/{StaticImportSchemaKeys.OrganizationTenantPositions}/worksheets/positions/template", token);
        using var templateResponse = await client.SendAsync(templateRequest);
        Assert.AreEqual(HttpStatusCode.OK, templateResponse.StatusCode);
        using var content = new MultipartFormDataContent
        {
            { new StringContent(StaticImportSchemaKeys.OrganizationTenantPositions), "schemaKey" },
            { new StringContent("positions"), "worksheetKey" },
            { new ByteArrayContent(FillPositionTemplate(await templateResponse.Content.ReadAsByteArrayAsync(), unitCode)), "file", "receipt-positions.xlsx" },
        };
        using var createRequest = Request(HttpMethod.Post, "/api/v1/import-export/tasks", token);
        createRequest.Content = content;
        using var createResponse = await client.SendAsync(createRequest);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode, await createResponse.Content.ReadAsStringAsync());
        var task = (await createResponse.Content.ReadFromJsonAsync<ImportExportTaskDetailResponse>())!;
        Assert.AreEqual(1, task.ValidRowCount);
        using var executeRequest = Request(HttpMethod.Post, $"/api/v1/import-export/tasks/{task.Id:D}/execute", token);
        using var executeResponse = await client.SendAsync(executeRequest);
        Assert.AreEqual(HttpStatusCode.OK, executeResponse.StatusCode);
        Assert.AreEqual(ImportExportTaskStatusKeys.Queued,
            (await executeResponse.Content.ReadFromJsonAsync<ImportExportTaskDetailResponse>())!.StatusKey);
        using var disableRequest = Request(HttpMethod.Post, $"/api/v1/organization/units/{unitId:D}/disable", token);
        using var disableResponse = await client.SendAsync(disableRequest);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode, await disableResponse.Content.ReadAsStringAsync());

        var interrupted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var iterationCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var worker = await BuildWorkerAsync(api, settings, services =>
        {
            services.AddSingleton<ILogger<ImportExportTaskHostedProcessor>>(new ReceiptIterationLogger(iterationCompleted));
            if (progressFailure)
            {
                var descriptor = services.Single(item => item.ServiceType == typeof(ICommandExecutor));
                services.Remove(descriptor);
                services.AddScoped<ICommandExecutor>(scope => new InterruptedProgressExecutor(
                    (ICommandExecutor)descriptor.ImplementationFactory!(scope), task.Id, interrupted));
            }
            else
            {
                var descriptor = services.Single(item => item.ServiceType == typeof(ITenantResourceFileStore));
                services.Remove(descriptor);
                services.AddScoped<ITenantResourceFileStore>(scope => new ReceiptUploadFailureStore(
                    (ITenantResourceFileStore)ActivatorUtilities.CreateInstance(scope, descriptor.ImplementationType!), task.Id, interrupted));
            }
        }))
        {
            await worker.StartAsync();
            try
            {
                await interrupted.Task.WaitAsync(TimeSpan.FromSeconds(40));
                // 明确等待本轮处理结束；不能用宿主取消掩盖旧实现尚未完成的终态写入。
                await iterationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(40));
                var stranded = await LoadTaskAsync(api, task);
                Assert.AreEqual(ImportExportTaskStatusKeys.Executing, stranded.StatusKey);
                Assert.AreEqual(0, stranded.NextLineNumber);
                Assert.AreEqual(0, stranded.ExecutionFailedRowCount);
                Assert.IsNull(stranded.ErrorReceiptFileId);
            }
            finally { await worker.StopAsync(); }
        }
        var filesBefore = await ListReceiptFilesAsync(api, task);
        Assert.AreEqual(progressFailure ? 1 : 0, filesBefore.Count);

        using var restarted = await BuildWorkerAsync(api, settings, services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(new AdvancedClock());
        });
        await restarted.StartAsync();
        try
        {
            var recovered = await WaitForTaskAsync(api, task, ImportExportTaskStatusKeys.ExecutionFailed);
            Assert.AreEqual(1, recovered.NextLineNumber);
            Assert.AreEqual(1, recovered.ProcessedRowCount);
            Assert.AreEqual(0, recovered.SucceededRowCount);
            Assert.AreEqual(1, recovered.ExecutionFailedRowCount);
            var record = await LoadTaskAsync(api, task);
            Assert.IsNotNull(record.ErrorReceiptFileId);
            Assert.IsNull(record.LeaseId);
            var filesAfter = await ListReceiptFilesAsync(api, task);
            Assert.AreEqual(1, filesAfter.Count);
            Assert.AreEqual(filesAfter.Single().FileId, record.ErrorReceiptFileId);
            if (progressFailure) Assert.AreEqual(filesBefore.Single().FileId, record.ErrorReceiptFileId);
            using var receiptRequest = Request(HttpMethod.Get, $"/api/v1/import-export/tasks/{task.Id:D}/error-receipt", token);
            using var receiptResponse = await client.SendAsync(receiptRequest);
            Assert.AreEqual(HttpStatusCode.OK, receiptResponse.StatusCode, await receiptResponse.Content.ReadAsStringAsync());
            using var workbook = new MemoryStream(await receiptResponse.Content.ReadAsByteArrayAsync());
            using var zip = new ZipArchive(workbook, ZipArchiveMode.Read);
            using var sheet = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
            StringAssert.Contains(await sheet.ReadToEndAsync(), OrganizationErrorCodes.UnitNotFound);
        }
        finally { await restarted.StopAsync(); }
    }

    /// <summary>仅观察本测试 Worker 的迭代完成事件，避免固定延迟制造假绿窗口。</summary>
    private sealed class ReceiptIterationLogger(TaskCompletionSource completed) : ILogger<ImportExportTaskHostedProcessor>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id == 6611 && state is IEnumerable<KeyValuePair<string, object?>> values
                && values.Any(value => value.Key == "ProcessedCount" && Equals(value.Value, 1)))
                completed.TrySetResult();
        }
    }

    /// <summary>通过 Files Port 检查资源对象，不直接读取另一个模块的表。</summary>
    private static async Task<IReadOnlyList<TenantResourceFileReadyItem>> ListReceiptFilesAsync(FullNetApiFactory api, ImportExportTaskDetailResponse task)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        tenant.SetTenant(new TenantContext(task.TenantId, "acme", "Acme"));
        try
        {
            var record = await LoadTaskAsync(api, task);
            var files = await scope.ServiceProvider.GetRequiredService<ITenantResourceFileStore>().ListReadyAsync("import_export", task.Id);
            return files.Where(file => file.FileId != record.SourceFileId).ToArray();
        }
        finally { tenant.Clear(); }
    }

    /// <summary>只拒绝本测试任务的回执上传；源文件读取与资源查询仍使用真实存储。</summary>
    private sealed class ReceiptUploadFailureStore(ITenantResourceFileStore inner, Guid taskId, TaskCompletionSource interrupted) : ITenantResourceFileStore
    {
        public Task<Result<TenantResourceFileReference>> UploadAsync(string ownerModuleKey, Guid resourceId, Guid actorUserId,
            string originalFileName, string contentType, Stream content, long contentLength, CancellationToken cancellationToken = default)
        {
            if (ownerModuleKey != "import_export" || resourceId != taskId)
                return inner.UploadAsync(ownerModuleKey, resourceId, actorUserId, originalFileName, contentType, content, contentLength, cancellationToken);
            interrupted.TrySetResult();
            return Task.FromResult(Result<TenantResourceFileReference>.Failure(new Error("probe.upload", "测试回执存储失败。", ErrorType.Validation)));
        }
        public Task<Result<TenantResourceFileContent>> OpenReadyContentAsync(string ownerModuleKey, Guid resourceId, Guid fileId,
            CancellationToken cancellationToken = default) => inner.OpenReadyContentAsync(ownerModuleKey, resourceId, fileId, cancellationToken);
        public Task<IReadOnlyList<TenantResourceFileReadyItem>> ListReadyAsync(string ownerModuleKey, Guid resourceId,
            CancellationToken cancellationToken = default) => inner.ListReadyAsync(ownerModuleKey, resourceId, cancellationToken);
        public Task ReleaseAsync(string ownerModuleKey, Guid resourceId, Guid fileId,
            CancellationToken cancellationToken = default) => inner.ReleaseAsync(ownerModuleKey, resourceId, fileId, cancellationToken);
    }
}
