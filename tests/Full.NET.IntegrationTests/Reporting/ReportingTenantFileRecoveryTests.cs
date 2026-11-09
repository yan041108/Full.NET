using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.EnterpriseRequest;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;
using Full.NET.Modules.Reporting.Features.ManageExportTasks;
using Full.NET.Modules.Reporting.Persistence;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Full.NET.IntegrationTests.Reporting;

/// <summary>验证真实企业停用时，报表 Worker 保留并恢复已上传文件。</summary>
[TestClass]
public sealed class ReportingTenantFileRecoveryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Suspended_tenant_preserves_uploaded_report_until_reactivation(DatabaseProvider provider) =>
        VerifyRecoveryAsync(provider, crashProcess: false);

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Worker_process_crash_reattaches_original_uploaded_report(DatabaseProvider provider) =>
        VerifyRecoveryAsync(provider, crashProcess: true);

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Legacy_uploaded_report_remains_pending_without_duplicate_output(DatabaseProvider provider) =>
        VerifyRecoveryAsync(provider, crashProcess: false, legacyOutput: true);

    private static async Task VerifyRecoveryAsync(DatabaseProvider provider, bool crashProcess, bool legacyOutput = false)
    {
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var filesRoot = new FilesRoot();
        Directory.CreateDirectory(filesRoot.Path);
        var storage = Path.Combine(filesRoot.Path, "files");
        using var api = new FullNetApiFactory(provider, connection,
            new Dictionary<string, string?>
            {
                ["Files:Local:RootPath"] = storage,
                ["DataProtection:KeyRingPath"] = Path.Combine(filesRoot.Path, "keys")
            });
        await api.InitializeAsync();
        using var host = api.CreateClientForHost("localhost");
        using var tenantClient = api.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(tenantClient);
        var hostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin");
        host.DefaultRequestHeaders.Authorization = new("Bearer", hostToken);

        var source = await SendAsync(host, HttpMethod.Post, "/api/v1/reporting/data-sources",
            new CreateReportingDataSourceRequest(null, "Recovery source", provider == DatabaseProvider.SqlServer ? "sql_server" : "mysql",
                "reports.example.com", provider == DatabaseProvider.SqlServer ? 1433 : 3306, "reporting", "readonly",
                "Fixture!2026", false, true), HttpStatusCode.Created);
        var group = await SendAsync(host, HttpMethod.Post, "/api/v1/reporting/groups",
            new CreateReportingGroupRequest(null, "Recovery group", 1, true), HttpStatusCode.Created);
        var definition = await SendAsync(host, HttpMethod.Post, "/api/v1/reporting/definitions",
            new CreateReportingDefinitionRequest(group.GetProperty("id").GetGuid(), source.GetProperty("id").GetGuid(),
                "file_recovery", "Recovery report", null, "reporting.schema_inventory",
                [new("topN", "Limit", ReportingParameterDataTypeKeys.Integer, true, "20")],
                JsonSerializer.Serialize(new { columns = new[] { new { key = "SchemaName", displayName = "Schema", requiredPermission = ReportingExecutionPermissions.ColumnSchemaName } } }),
                true), HttpStatusCode.Created);
        var definitionId = definition.GetProperty("id").GetGuid();
        await SendAsync(host, HttpMethod.Post, $"/api/v1/reporting/definitions/{definitionId:D}/publish",
            new PublishReportingDefinitionRequest("Recovery fixture", definition.GetProperty("version").GetInt32()), HttpStatusCode.OK);
        await SendAsync(host, HttpMethod.Put, $"/api/v1/reporting/definitions/{definitionId:D}/tenant-grants/{tenant.Id:D}",
            new SetReportingDefinitionTenantGrantRequest(true, 0), HttpStatusCode.OK);
        tenantClient.DefaultRequestHeaders.Authorization = new("Bearer",
            await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, hostToken, tenant.Id));
        host.DefaultRequestHeaders.Authorization = new("Bearer", await api.CreateHostAccessTokenAsync(
            ["tenancy.host_tenants.read", "tenancy.tenant_lifecycle.suspend", "tenancy.tenant_lifecycle.reactivate"]));
        var createdTask = await SendAsync(tenantClient, HttpMethod.Post, "/api/v1/reporting/export-tasks",
            new CreateReportingExportTaskRequest(definitionId, "excel", 1, [new("topN", "20")]), HttpStatusCode.Created);
        var taskId = createdTask.GetProperty("id").GetGuid();
        var tenantContext = new TenantContext(tenant.Id, "acme", "Acme");
        if (crashProcess)
        {
            await VerifyProcessCrashAsync();
            return;
        }
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var current = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
            current.SetHost();
            var identifier = "report-spare-" + Guid.NewGuid().ToString("N")[..10];
            var spare = await scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>().ProvisionAsync(
                new ProvisionTenantRequest(identifier, "Report spare", identifier + ".localhost"));
            Assert.IsTrue(spare.IsSuccess, spare.Error?.Code);
        }

        var workbook = new Workbook();
        var gate = new FileGate(taskId);
        var firstCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var worker = await BuildWorkerAsync(0,
            legacyOutput ? FileMode.LegacyFailAfterUpload : FileMode.FailAfterUpload, firstCompleted))
        {
            await worker.StartAsync();
            try
            {
                await gate.Uploaded.Task.WaitAsync(TimeSpan.FromSeconds(40));
                await firstCompleted.Task.WaitAsync(TimeSpan.FromSeconds(40));
            }
            finally { await worker.StopAsync(); }
        }
        var before = await ReadTaskAsync();
        Assert.AreEqual(ReportingExportTaskStatusKeys.Processing, before.StatusKey);
        Assert.IsNull(before.OutputFileId);
        var existing = await ListFilesAsync();
        Assert.HasCount(1, existing);
        Assert.AreEqual(gate.Uploaded.Task.Result, existing.Single().FileId);
        if (legacyOutput)
        {
            var original = existing.Single();
            Assert.AreEqual("recovery.xlsx", original.OriginalFileName);
            var bytes = await ReadReadyBytesAsync(original.FileId);
            var previousLease = before.LeaseId;
            // 两次后续领取使用真实持久化文件，以证实旧名不会让恢复路径猜测行数或重复上传。
            foreach (var minutes in new[] { 2, 4 })
            {
                var retryCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var worker = await BuildWorkerAsync(minutes, FileMode.Observe, retryCompleted);
                await worker.StartAsync();
                try { await retryCompleted.Task.WaitAsync(TimeSpan.FromSeconds(40)); }
                finally { await worker.StopAsync(); }

                var pending = await ReadTaskAsync();
                Assert.AreEqual(ReportingExportTaskStatusKeys.Processing, pending.StatusKey);
                Assert.AreEqual(before.RowCount, pending.RowCount);
                Assert.IsNull(pending.OutputFileId);
                Assert.IsNull(pending.ErrorCode);
                Assert.IsNotNull(pending.LeaseId);
                Assert.AreNotEqual(previousLease, pending.LeaseId);
                previousLease = pending.LeaseId;
            }
            Assert.AreEqual(original.FileId, (await ListFilesAsync()).Single().FileId);
            Assert.AreEqual(1, Volatile.Read(ref gate.Uploads));
            Assert.AreEqual(1, Volatile.Read(ref workbook.Calls));
            CollectionAssert.AreEqual(bytes, await ReadReadyBytesAsync(original.FileId));
            using var unavailable = await tenantClient.GetAsync($"/api/v1/reporting/export-tasks/{taskId:D}/download");
            Assert.AreEqual(HttpStatusCode.BadRequest, unavailable.StatusCode);
            using var problem = JsonDocument.Parse(await unavailable.Content.ReadAsStringAsync());
            Assert.AreEqual(ReportingErrorCodes.ExportTaskNotReady, problem.RootElement.GetProperty("code").GetString());
            return;
        }

        var secondCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TenantSummary? suspended = null;
        using (var worker = await BuildWorkerAsync(2, FileMode.PauseList, secondCompleted))
        {
            await worker.StartAsync();
            try
            {
                await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(40));
                using var currentResponse = await host.GetAsync($"/api/v1/tenancy/tenants/{tenant.Id:D}");
                Assert.AreEqual(HttpStatusCode.OK, currentResponse.StatusCode);
                var current = await currentResponse.Content.ReadFromJsonAsync<TenantSummary>();
                Assert.IsNotNull(current);
                using var response = await host.PostAsJsonAsync($"/api/v1/tenancy/tenants/{tenant.Id:D}/suspend",
                    new SuspendTenantRequest(current.Version));
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
                suspended = await response.Content.ReadFromJsonAsync<TenantSummary>();
                Assert.IsNotNull(suspended);
                Assert.IsFalse(suspended.IsActive);
                gate.Release.TrySetResult();
                await gate.Unavailable.Task.WaitAsync(TimeSpan.FromSeconds(40));
                await secondCompleted.Task.WaitAsync(TimeSpan.FromSeconds(40));
                var pending = await ReadTaskAsync();
                Assert.AreEqual(ReportingExportTaskStatusKeys.Processing, pending.StatusKey);
                Assert.IsNull(pending.OutputFileId);
                Assert.IsNull(pending.ErrorCode);
                Assert.IsNotNull(pending.LeaseId);
                Assert.AreEqual(1, Volatile.Read(ref gate.Uploads));
                Assert.AreEqual(1, Volatile.Read(ref workbook.Calls));
            }
            finally
            {
                gate.Release.TrySetResult();
                await worker.StopAsync();
            }
        }
        using (var response = await host.PostAsJsonAsync($"/api/v1/tenancy/tenants/{tenant.Id:D}/reactivate",
            new ReactivateTenantRequest(suspended!.Version)))
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var finalCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var worker = await BuildWorkerAsync(4, FileMode.Observe, finalCompleted))
        {
            await worker.StartAsync();
            try { await finalCompleted.Task.WaitAsync(TimeSpan.FromSeconds(40)); }
            finally { await worker.StopAsync(); }
        }
        var completed = await ReadTaskAsync();
        Assert.AreEqual(ReportingExportTaskStatusKeys.Succeeded, completed.StatusKey);
        Assert.AreEqual(existing.Single().FileId, completed.OutputFileId);
        Assert.AreEqual(1, completed.RowCount);
        Assert.AreEqual("recovery.xlsx", completed.OutputFileName);
        Assert.IsNull(completed.LeaseId);
        var finalFiles = await ListFilesAsync();
        Assert.HasCount(1, finalFiles);
        Assert.AreEqual(existing.Single().FileId, finalFiles.Single().FileId);
        Assert.AreEqual(1, Volatile.Read(ref gate.Uploads));
        Assert.AreEqual(1, Volatile.Read(ref workbook.Calls));
        using var download = await tenantClient.GetAsync($"/api/v1/reporting/export-tasks/{taskId:D}/download");
        Assert.AreEqual(HttpStatusCode.OK, download.StatusCode);
        Assert.AreEqual("recovery.xlsx", download.Content.Headers.ContentDisposition?.FileNameStar);
        var content = await download.Content.ReadAsByteArrayAsync();
        Assert.IsGreaterThan(100, content.Length);
        Assert.AreEqual((byte)'P', content[0]);
        Assert.AreEqual((byte)'K', content[1]);

        async Task<IHost> BuildWorkerAsync(int minutes, FileMode mode, TaskCompletionSource completion) =>
            await IntegrationWorkerHostFactory.BuildAsync(provider, connection,
                new Dictionary<string, string?>
                {
                    ["FullNet:Reporting:Export:PollSeconds"] = "5",
                    ["FullNet:Reporting:Export:LeaseSeconds"] = "30",
                }, "Reporting.Recovery.Worker", nameof(ReportingExportTaskHostedProcessor), storage,
                configureServices: services => services.AddSingleton(api.Services.GetRequiredService<IDataProtectionProvider>()),
                configureTargetServices: services =>
                {
                    services.RemoveAll<IClock>();
                    services.AddSingleton<IClock>(new RecoveryClock(minutes));
                    services.RemoveAll<IReportingExportWorkbookSource>();
                    services.AddSingleton<IReportingExportWorkbookSource>(workbook);
                    services.AddSingleton<ILogger<ReportingExportTaskHostedProcessor>>(new IterationLogger(completion));
                    var descriptor = services.Single(item => item.ServiceType == typeof(ITenantResourceFileStore));
                    services.Remove(descriptor);
                    services.AddScoped<ITenantResourceFileStore>(scope => new ControlledFiles(
                        (ITenantResourceFileStore)(descriptor.ImplementationFactory?.Invoke(scope)
                            ?? ActivatorUtilities.CreateInstance(scope, descriptor.ImplementationType!)), gate, mode));
                });
        async Task<ReportingExportTaskRecord> ReadTaskAsync()
        {
            await using var scope = api.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetTenant(tenantContext);
            var result = await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<ReportingExportTaskRecord>(
                ReportingExportTaskSql.FindByIdFor(provider), new Dictionary<string, object?> { ["Id"] = taskId });
            Assert.IsNotNull(result);
            return result;
        }
        async Task<IReadOnlyList<TenantResourceFileReadyItem>> ListFilesAsync()
        {
            await using var scope = api.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetTenant(tenantContext);
            return await scope.ServiceProvider.GetRequiredService<ITenantResourceFileStore>().ListReadyAsync("reporting", taskId);
        }

        async Task<byte[]> ReadReadyBytesAsync(Guid fileId)
        {
            await using var scope = api.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetTenant(tenantContext);
            var result = await scope.ServiceProvider.GetRequiredService<ITenantResourceFileStore>()
                .OpenReadyContentAsync("reporting", taskId, fileId);
            Assert.IsTrue(result.IsSuccess, result.Error?.Code);
            await using var original = result.Value!.Content;
            using var copy = new MemoryStream();
            await original.CopyToAsync(copy);
            return copy.ToArray();
        }

        async Task VerifyProcessCrashAsync()
        {
            var uploaded = Path.Combine(filesRoot.Path, "uploaded.gate");
            var reuploaded = Path.Combine(filesRoot.Path, "reuploaded.gate");
            int firstPid;
            Guid originalId;
            DateTimeOffset leaseExpires;
            byte[] originalBytes;
            await using (var first = await EnterpriseRequestWorkerProcess.StartAsync(api, filesRoot.Path, "reporting-upload", uploaded, 1,
                dataTaskId: taskId))
            {
                firstPid = first.Id;
                using var checkpointTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                while (!File.Exists(uploaded)) await Task.Delay(100, checkpointTimeout.Token);
                originalId = Guid.Parse(await File.ReadAllTextAsync(uploaded, checkpointTimeout.Token));
                var pending = await ReadTaskAsync();
                Assert.AreEqual(ReportingExportTaskStatusKeys.Processing, pending.StatusKey);
                Assert.IsNull(pending.OutputFileId);
                Assert.IsNotNull(pending.LeaseExpiresAtUtc);
                leaseExpires = pending.LeaseExpiresAtUtc.Value;
                Assert.AreEqual(originalId, (await ListFilesAsync()).Single().FileId);
                await first.CrashAsync();
            }
            originalBytes = await ReadReadyBytesAsync(originalId);
            Assert.HasCount(1, await File.ReadAllLinesAsync(uploaded + ".queries"));
            await using var replacement = await EnterpriseRequestWorkerProcess.StartAsync(api, filesRoot.Path, "reporting-recovery", reuploaded, 2,
                dataTaskId: taskId);
            Assert.AreNotEqual(firstPid, replacement.Id);
            using var recoveryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(55));
            var completed = await ReadTaskAsync();
            // 通过正式轮询等待数据库租约自然到期，测试不快进时间也不改写租约。
            while (completed.StatusKey == ReportingExportTaskStatusKeys.Processing)
            {
                await Task.Delay(200, recoveryTimeout.Token);
                completed = await ReadTaskAsync();
            }
            Assert.AreEqual(ReportingExportTaskStatusKeys.Succeeded, completed.StatusKey);
            Assert.IsTrue(completed.CompletedAtUtc >= leaseExpires);
            Assert.AreEqual(originalId, completed.OutputFileId);
            Assert.AreEqual(1, completed.RowCount);
            Assert.IsNull(completed.LeaseId);
            Assert.AreEqual(originalId, (await ListFilesAsync()).Single().FileId);
            Assert.IsFalse(File.Exists(reuploaded));
            Assert.IsFalse(File.Exists(reuploaded + ".queries"));
            using var downloaded = await tenantClient.GetAsync($"/api/v1/reporting/export-tasks/{taskId:D}/download", recoveryTimeout.Token);
            Assert.AreEqual(HttpStatusCode.OK, downloaded.StatusCode);
            Assert.AreEqual($"file_recovery-v1-{completed.CreatedAtUtc:yyyyMMddHHmmss}.xlsx", downloaded.Content.Headers.ContentDisposition?.FileNameStar);
            CollectionAssert.AreEqual(originalBytes, await downloaded.Content.ReadAsByteArrayAsync(recoveryTimeout.Token));
        }
    }

    private static async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string path, object body, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(expected, response.StatusCode, path + " " + text);
        using var json = JsonDocument.Parse(text);
        return json.RootElement.Clone();
    }

    private enum FileMode { FailAfterUpload, LegacyFailAfterUpload, PauseList, Observe }
    private sealed class FilesRoot : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fullnet-report-recovery-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
    private sealed class RecoveryClock(int minutes) : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow.AddMinutes(minutes);
    }
    private sealed class FileGate(Guid taskId)
    {
        internal Guid TaskId { get; } = taskId;
        internal int Uploads;
        internal TaskCompletionSource<Guid> Uploaded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Unavailable { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class Workbook : IReportingExportWorkbookSource
    {
        internal int Calls;
        public Task<Result<ReportingExportGeneratedFile>> GenerateAsync(ReportingExportTaskRecord task, ClaimsPrincipal principal, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            var bytes = ReportingExcelExportRenderer.Render([new("SchemaName", "Schema")],
                [new(new Dictionary<string, string?> { ["SchemaName"] = "recovery fixture" })]);
            return Task.FromResult(Result<ReportingExportGeneratedFile>.Success(new(1, "recovery.xlsx", bytes)));
        }
    }
    private sealed class IterationLogger(TaskCompletionSource completed) : ILogger<ReportingExportTaskHostedProcessor>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id == 6711 && state is IEnumerable<KeyValuePair<string, object?>> values
                && values.Any(value => value.Key == "ProcessedCount" && Equals(value.Value, 1))) completed.TrySetResult();
        }
    }
    private sealed class ControlledFiles(ITenantResourceFileStore inner, FileGate gate, FileMode mode) : ITenantResourceFileStore
    {
        public async Task<Result<TenantResourceFileReference>> UploadAsync(string ownerModuleKey, Guid resourceId, Guid actorUserId,
            string originalFileName, string contentType, Stream content, long contentLength, CancellationToken cancellationToken = default)
        {
            var isTargetReport = ownerModuleKey == "reporting" && resourceId == gate.TaskId;
            if (isTargetReport) Interlocked.Increment(ref gate.Uploads);
            if (isTargetReport && mode == FileMode.LegacyFailAfterUpload) originalFileName = "recovery.xlsx";
            var result = await inner.UploadAsync(ownerModuleKey, resourceId, actorUserId, originalFileName, contentType, content, contentLength, cancellationToken);
            if (isTargetReport && result.IsSuccess && mode is FileMode.FailAfterUpload or FileMode.LegacyFailAfterUpload)
            {
                gate.Uploaded.TrySetResult(result.Value!.FileId);
                throw new IOException("测试上传成功后丢失响应。");
            }
            return result;
        }
        public Task<Result<TenantResourceFileContent>> OpenReadyContentAsync(string ownerModuleKey, Guid resourceId, Guid fileId,
            CancellationToken cancellationToken = default) => inner.OpenReadyContentAsync(ownerModuleKey, resourceId, fileId, cancellationToken);
        public async Task<IReadOnlyList<TenantResourceFileReadyItem>> ListReadyAsync(string ownerModuleKey, Guid resourceId,
            CancellationToken cancellationToken = default)
        {
            if (mode != FileMode.PauseList || ownerModuleKey != "reporting" || resourceId != gate.TaskId)
                return await inner.ListReadyAsync(ownerModuleKey, resourceId, cancellationToken);
            gate.Entered.TrySetResult();
            await gate.Release.Task.WaitAsync(cancellationToken);
            try { return await inner.ListReadyAsync(ownerModuleKey, resourceId, cancellationToken); }
            catch (TenantResourceFileUnavailableException)
            {
                gate.Unavailable.TrySetResult();
                throw;
            }
        }
        public Task ReleaseAsync(string ownerModuleKey, Guid resourceId, Guid fileId, CancellationToken cancellationToken = default) =>
            inner.ReleaseAsync(ownerModuleKey, resourceId, fileId, cancellationToken);
    }
}
