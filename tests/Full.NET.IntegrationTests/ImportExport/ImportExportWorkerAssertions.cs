using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IO.Compression;
using System.Xml.Linq;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.EnterpriseRequest;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Features.ManageImportTasks;
using Full.NET.Modules.ImportExport.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Full.NET.IntegrationTests.ImportExport;

/// <summary>正式 Worker profile 的 HostedService 与双库验收；不使用同步 API Runner 或模拟业务处理器。</summary>
internal static class ImportExportWorkerAssertions
{
    internal static async Task VerifyPositionWorkerAsync(DatabaseProvider provider, string connectionString)
    {
        var settings = Settings();
        using var api = new FullNetApiFactory(provider, connectionString, settings);
        await ImportExportTaskAssertions.VerifyImportTaskPreviewContractAsync(api, executeQueued: async (task, token) =>
        {
            Assert.IsTrue(task.ValidRowCount > 0);
            using var worker = await BuildWorkerAsync(api, settings);
            await worker.StartAsync();
            try
            {
                var completed = await WaitForTaskAsync(api, task, ImportExportTaskStatusKeys.ExecutionSucceeded);
                var state = ImportExportTaskMapper.DeserializeExecutionState((await LoadTaskAsync(api, task)).ExecutionRowsJson);
                Assert.AreEqual(1, state.Rows.Count); Assert.IsTrue(state.Rows.Single().Succeeded);
                using var client = api.CreateClientForHost("localhost");
                using var request = Request(HttpMethod.Get, $"/api/v1/organization/positions/{state.Rows.Single().EntityId:D}", token);
                using var response = await client.SendAsync(request);
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                return completed;
            }
            finally { await worker.StopAsync(); }
        });
    }

    /// <summary>业务行提交后故意让任务检查点写入失败；新宿主用到期租约重放，不能重复创建业务实体。</summary>
    internal static async Task VerifyCheckpointRestartAsync(DatabaseProvider provider, string connectionString)
    {
        var settings = Settings();
        using var api = new FullNetApiFactory(provider, connectionString, settings);
        await EnterpriseRequestAssertions.VerifyTenantDemoEnterpriseRequestsWorkbookImportAsync(api, executeQueued: async (task, _) =>
        {
            var interrupted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var firstWorker = await BuildWorkerAsync(api, settings, services =>
                   {
                       var descriptor = services.Single(item => item.ServiceType == typeof(ICommandExecutor));
                       services.Remove(descriptor);
                       services.AddScoped<ICommandExecutor>(scope => new InterruptedProgressExecutor(
                           (ICommandExecutor)descriptor.ImplementationFactory!(scope), task.Id, interrupted));
                   }))
            {
                await firstWorker.StartAsync();
                try { await interrupted.Task.WaitAsync(TimeSpan.FromSeconds(40)); }
                finally { await firstWorker.StopAsync(); }
            }
            var stranded = await LoadTaskAsync(api, task);
            Assert.AreEqual(ImportExportTaskStatusKeys.Executing, stranded.StatusKey);
            Assert.AreEqual(0, stranded.NextLineNumber); Assert.AreEqual(0, stranded.SucceededRowCount);
            Assert.IsNotNull(stranded.LeaseId);

            // 仅推进本测试新宿主时钟，避免等待真实租约超时或修改其他 Worker 的共享状态。
            using var restartedWorker = await BuildWorkerAsync(api, settings, services =>
            {
                services.RemoveAll<IClock>(); services.AddSingleton<IClock>(new AdvancedClock());
            });
            await restartedWorker.StartAsync();
            try
            {
                var recovered = await WaitForTaskAsync(api, task, ImportExportTaskStatusKeys.ExecutionSucceeded);
                Assert.AreEqual(1, recovered.NextLineNumber); Assert.AreEqual(1, recovered.SucceededRowCount);
                var state = ImportExportTaskMapper.DeserializeExecutionState((await LoadTaskAsync(api, task)).ExecutionRowsJson);
                Assert.IsNotNull(state.SessionBinding); Assert.AreEqual(1, state.Rows.Count);
                Assert.IsTrue(state.Rows.Single().Succeeded); Assert.IsNotNull(state.Rows.Single().EntityId);
                return recovered;
            }
            finally { await restartedWorker.StopAsync(); }
        });
    }

    /// <summary>排队后通过正式在线会话 API 撤销原会话，Worker 必须拒绝；重新登录并重试才允许执行。</summary>
    internal static async Task VerifyRevokedSessionRetryAsync(DatabaseProvider provider, string connectionString)
    {
        var settings = Settings();
        using var api = new FullNetApiFactory(provider, connectionString, settings);
        await api.InitializeAsync();
        using var client = api.CreateClientForHost("localhost");
        var oldToken = await ImportExportTaskAssertions.LoginAndEnterAcmeTenantAsync(client, default);
        using var templateRequest = Request(HttpMethod.Get,
            $"/api/v1/import-export/schemas/{StaticImportSchemaKeys.OrganizationTenantPositions}/worksheets/positions/template", oldToken);
        using var templateResponse = await client.SendAsync(templateRequest);
        Assert.AreEqual(HttpStatusCode.OK, templateResponse.StatusCode);
        var bytes = FillPositionTemplate(await templateResponse.Content.ReadAsByteArrayAsync());
        using var content = new MultipartFormDataContent
        {
            { new StringContent(StaticImportSchemaKeys.OrganizationTenantPositions), "schemaKey" },
            { new StringContent("positions"), "worksheetKey" },
            { new ByteArrayContent(bytes), "file", "positions.xlsx" },
        };
        using var createRequest = Request(HttpMethod.Post, "/api/v1/import-export/tasks", oldToken);
        createRequest.Content = content;
        using var createResponse = await client.SendAsync(createRequest);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode, await createResponse.Content.ReadAsStringAsync());
        var task = (await createResponse.Content.ReadFromJsonAsync<ImportExportTaskDetailResponse>())!;
        using var executeRequest = Request(HttpMethod.Post, $"/api/v1/import-export/tasks/{task.Id:D}/execute", oldToken);
        using var executeResponse = await client.SendAsync(executeRequest);
        Assert.AreEqual(HttpStatusCode.OK, executeResponse.StatusCode, await executeResponse.Content.ReadAsStringAsync());
        var queued = await LoadTaskAsync(api, task);
        Assert.AreEqual(ImportExportTaskStatusKeys.Queued, queued.StatusKey);
        var originalBinding = ImportExportTaskMapper.DeserializeExecutionState(queued.ExecutionRowsJson).SessionBinding!;
        Assert.IsNotNull(originalBinding);

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        { Content = JsonContent.Create(new LoginRequest("admin", FullNetApiFactory.TestPassword)) };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var hostToken = (await loginResponse.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;
        using var revokeRequest = Request(HttpMethod.Post,
            $"/api/v1/identity/online-sessions/{originalBinding.SessionId:D}/revoke", hostToken);
        using var revokeResponse = await client.SendAsync(revokeRequest);
        Assert.AreEqual(HttpStatusCode.OK, revokeResponse.StatusCode, await revokeResponse.Content.ReadAsStringAsync());

        using var worker = await BuildWorkerAsync(api, settings);
        await worker.StartAsync();
        try
        {
            var denied = await WaitForTaskAsync(api, task, ImportExportTaskStatusKeys.ExecutionFailed);
            Assert.AreEqual(CommonErrorCodes.PermissionDenied, denied.ErrorCode);
            Assert.AreEqual(0, denied.ProcessedRowCount); Assert.AreEqual(0, denied.SucceededRowCount);
            Assert.AreEqual(0, denied.NextLineNumber);
            var freshToken = await ImportExportTaskAssertions.LoginAndEnterAcmeTenantAsync(client, default);
            using var retryRequest = Request(HttpMethod.Post, $"/api/v1/import-export/tasks/{task.Id:D}/retry", freshToken);
            using var retryResponse = await client.SendAsync(retryRequest);
            Assert.AreEqual(HttpStatusCode.OK, retryResponse.StatusCode, await retryResponse.Content.ReadAsStringAsync());
            var retried = await WaitForTaskAsync(api, task, ImportExportTaskStatusKeys.ExecutionSucceeded);
            Assert.AreEqual(task.ValidRowCount, retried.SucceededRowCount);
            var rebound = ImportExportTaskMapper.DeserializeExecutionState((await LoadTaskAsync(api, task)).ExecutionRowsJson);
            Assert.AreNotEqual(originalBinding.SessionId, rebound.SessionBinding!.SessionId);
        }
        finally { await worker.StopAsync(); }
    }

    private static Dictionary<string, string?> Settings() => new()
    {
        ["FullNet:ImportExport:RunSynchronously"] = "false",
        ["FullNet:ImportExport:ExecutionEnabled"] = "true",
        ["FullNet:ImportExport:BatchSize"] = "1",
        ["FullNet:ImportExport:PollSeconds"] = "5",
        ["FullNet:ImportExport:LeaseSeconds"] = "30",
        ["Files:Local:RootPath"] = Path.Combine(Path.GetTempPath(), "fullnet-files-integration", $"import-{Guid.NewGuid():N}"),
    };

    /// <summary>正式下载模板默认只有表头；后台业务验收必须填入真实岗位行，不能把零行执行作为写入成功。</summary>
    internal static byte[] FillPositionTemplate(byte[] template)
    {
        using var workbook = new MemoryStream(); workbook.Write(template);
        using (var archive = new ZipArchive(workbook, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
            XDocument document; using (var input = entry.Open()) document = XDocument.Load(input);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var values = new[] { $"worker-{Guid.NewGuid():N}", "Worker position", "10", "", "" };
            document.Root!.Element(ns + "sheetData")!.Add(new XElement(ns + "row", new XAttribute("r", 2),
                values.Select((value, index) => new XElement(ns + "c", new XAttribute("r", $"{(char)('A' + index)}2"),
                    new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t", value))))));
            entry.Delete(); using var output = archive.CreateEntry("xl/worksheets/sheet1.xml").Open(); document.Save(output);
        }
        return workbook.ToArray();
    }

    private static Task<IHost> BuildWorkerAsync(FullNetApiFactory api, IReadOnlyDictionary<string, string?> settings,
        Action<IServiceCollection>? configure = null) =>
        IntegrationWorkerHostFactory.BuildAsync(api.Provider, api.ConnectionString, settings,
            "Full.NET.IntegrationTests.ImportExport.Worker", "ImportExportTaskHostedProcessor", configure);

    private static async Task<ImportExportTaskRecord> LoadTaskAsync(FullNetApiFactory api, ImportExportTaskDetailResponse task)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        tenant.SetTenant(new TenantContext(task.TenantId, "acme", "Acme"));
        try
        {
            return (await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<ImportExportTaskRecord>(
                ImportExportTaskSql.FindById, ImportExportSqlParameters.Create(("Id", task.Id))))!;
        }
        finally { tenant.Clear(); }
    }

    private static async Task<ImportExportTaskDetailResponse> WaitForTaskAsync(FullNetApiFactory api,
        ImportExportTaskDetailResponse task, string expectedStatus)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (true)
        {
            var record = await LoadTaskAsync(api, task);
            if (record.StatusKey == expectedStatus) return ImportExportTaskMapper.MapDetail(record);
            Assert.IsFalse(record.StatusKey is ImportExportTaskStatusKeys.ExecutionFailed
                or ImportExportTaskStatusKeys.ExecutionPartial, $"Unexpected terminal state: {record.StatusKey}, {record.ErrorCode}");
            await Task.Delay(100, timeout.Token);
        }
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private sealed class AdvancedClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow.AddMinutes(2);
    }

    private sealed class InterruptedProgressExecutor(ICommandExecutor inner, Guid taskId, TaskCompletionSource interrupted) : ICommandExecutor
    {
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (statement.Name == ImportExportTaskSql.UpdateExecutionProgress.Name
                && parameters is IReadOnlyDictionary<string, object?> values && Equals(values["Id"], taskId))
            {
                interrupted.TrySetResult();
                throw new IOException("测试故障：业务提交后、任务检查点提交前中断。");
            }
            return inner.ExecuteAsync(statement, parameters, cancellationToken);
        }
    }
}
