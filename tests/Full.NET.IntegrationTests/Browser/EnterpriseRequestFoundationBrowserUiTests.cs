extern alias workerhost;

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.Abstractions.Messaging;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.EnterpriseRequest;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.EnterpriseRequest;
using Full.NET.Modules.EnterpriseRequest.Features.Delivery;
using Full.NET.Modules.ImportExport.ImportTasks;
using Full.NET.Modules.Notifications.Execution;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using RealOutboxProcessor = workerhost::Full.NET.Host.Worker.OutboxProcessor;
using RealOutboxOptions = workerhost::Full.NET.Host.Worker.OutboxWorkerOptions;

namespace Full.NET.IntegrationTests.Browser;

/// <summary>以应用显式装配的 Kestrel、真实 SQL Server 和 Vue 验证主从/导入界面；不依赖脏工作区源码分发。</summary>
[TestClass]
[TestCategory("BrowserUi")]
public sealed class EnterpriseRequestFoundationBrowserUiTests
{
    [TestMethod]
    public Task Real_browser_aggregate_submit_and_xlsx_import_with_sql_server() => RunAsync(null);

    [TestMethod]
    public Task Real_browser_report_result_expiry_and_document_delivery_with_sql_server() => RunAsync("真实导入、附件下载、后台报表和同单据打印");

    [TestMethod]
    public Task Real_browser_return_round_replays_and_completes_business_with_sql_server() => RunAsync(null, returnFlow: true);

    private static async Task RunAsync(string? browserFilter, bool returnFlow = false)
    {
        var repository = FullNetApiFactory.FindRepositoryRoot();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var origin = $"http://localhost:{port}";
        var evidence = Environment.GetEnvironmentVariable(returnFlow ? "FULLNET_ENTERPRISE_RETURN_UI_EVIDENCE" : "FULLNET_ENTERPRISE_EXPIRY_UI_EVIDENCE");
        var temporary = evidence ?? Directory.CreateTempSubdirectory("fullnet-enterprise-foundation-ui-").FullName; Directory.CreateDirectory(temporary);
        var passed = false; Process? process = null; IHost? worker = null;
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            new Dictionary<string, string?> { ["Identity:AllowedOrigins:0"] = origin, ["Identity:AllowedOrigins:1"] = "http://localhost",
                ["Tenancy:HostDomains:1"] = "127.0.0.1", ["Identity:RequireSecureCookies"] = "false", ["FullNet:ImportExport:RunSynchronously"] = "false" },
            includeEnterpriseRequest: true);
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        try
        {
            await factory.InitializeAsync(useSchemaTemplate: false);
            var address = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri(address) };
            client.DefaultRequestHeaders.Host = "localhost";
            var token = await EnterpriseRequestAssertions.LoginAndEnterAcmeTenantAsync(client, default);
            await EnterpriseRequestWorkflowBootstrap.PublishDemoApprovalDefinitionInTenantAsync(client, token, withReturnStage: returnFlow);
            var unit = await EnterpriseRequestAssertions.CreateOrganizationUnitAsync(client, token, default);
            var actor = await EnterpriseRequestAssertions.GetCurrentUserIdAsync(client, token, default);
            var hostedServices = new List<string> { typeof(RealOutboxProcessor).Name, nameof(NotificationDeliveryHostedProcessor), nameof(EnterpriseRequestDeliveryWorker) };
            if (returnFlow) hostedServices.Add("WorkflowRecoveryHostedProcessor");
            // 使用生产 Outbox、通知和导入循环完成交付，不直接调用 Handler 或同步执行任务。
            worker = await IntegrationWorkerHostFactory.BuildAsync(factory.Provider, factory.ConnectionString,
                new Dictionary<string, string?> { ["FullNet:ImportExport:ExecutionEnabled"] = "true", ["FullNet:ImportExport:PollSeconds"] = "5",
                    ["Notifications:Worker:PollMilliseconds"] = "100", ["Workflow:RecoveryWorker:PollMilliseconds"] = "100" },
                "Full.NET.IntegrationTests.EnterpriseRequest.Foundation.Worker", nameof(ImportExportTaskHostedProcessor),
                fileRootPath: factory.Services.GetRequiredService<IConfiguration>()["Files:Local:RootPath"]!,
                additionalHostedServiceNames: hostedServices,
                configureServices: services =>
                {
                    // 测试宿主复用 API 的实际保护器，等价于部署时 API/Worker 共享 Key Ring。
                    services.AddSingleton(factory.Services.GetRequiredService<IDataProtectionProvider>());
                    services.AddOptions<RealOutboxOptions>().Configure(options =>
                    {
                        options.PollMilliseconds = 100; options.MaximumIdlePollMilliseconds = 1000;
                        options.BatchSize = 20; options.MaxConcurrency = 1; options.LeaseSeconds = 10; options.LeaseRenewalSeconds = 3;
                    });
                    services.AddHostedService<RealOutboxProcessor>();
                }, applicationModules: [new EnterpriseRequestModule()]);
            await using (var scope = worker.Services.CreateAsyncScope())
            {
                // 先验证真实处理器依赖，避免后台循环重复记录同一装配错误掩盖根因。
                _ = scope.ServiceProvider.GetServices<IIntegrationEventHandler>().ToArray();
                _ = scope.ServiceProvider.GetRequiredService<EnterpriseRequestDeliveryProcessor>();
                _ = scope.ServiceProvider.GetRequiredService<NotificationDeliveryBatchProcessor>();
            }
            Assert.IsTrue(worker.Services.GetServices<IHostedService>().Any(service => service is RealOutboxProcessor),
                "真实 Outbox 循环必须保留；类型别名不等于实现类型名。");
            if (returnFlow) Assert.IsTrue(worker.Services.GetServices<IHostedService>().Any(service => service.GetType().Name == "WorkflowRecoveryHostedProcessor"),
                "退回轮次必须由生产后台活动循环推进，不能直接派发恢复处理器。");
            await worker.StartAsync();
            var number = "IMP-UI-" + Guid.CreateVersion7().ToString("N")[..16];
            var renderer = factory.Services.GetRequiredService<IReportingWorkbookRenderer>();
            var headers = new[] { "requestNumber", "title", "totalAmount", "applicantUserId", "organizationUnitId" };
            foreach (var (file, amount) in new[] { ("valid.xlsx", "10.00"), ("invalid.xlsx", "1.234") })
            {
                var workbook = renderer.Render(headers.Select(header => new ReportingExecutionColumnDefinition(header, header)).ToArray(),
                    [new ReportingExecutionRow(new Dictionary<string, string?> { ["requestNumber"] = number, ["title"] = "真实 UI 导入",
                        ["totalAmount"] = amount, ["applicantUserId"] = actor.ToString("D"), ["organizationUnitId"] = unit.ToString("D") })]);
                Assert.IsTrue(workbook.IsSuccess); await File.WriteAllBytesAsync(Path.Combine(temporary, file), workbook.Value!);
            }
            var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(repository, "tests/e2e/admin-real-stack"),
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in new[] { Path.Combine(repository, "tests/e2e/admin-real-stack/node_modules/@playwright/test/cli.js"), "test",
                         returnFlow ? "enterprise-request-return.spec.mjs" : "enterprise-request-foundation.spec.mjs", "--project", "vue-admin", "--workers", "1", "--output", Path.Combine(temporary, "browser") }) start.ArgumentList.Add(argument);
            if (browserFilter is not null) { start.ArgumentList.Add("--grep"); start.ArgumentList.Add(browserFilter); }
            start.Environment["FULLNET_E2E_SKIP_BOOTSTRAP"] = "1"; start.Environment["FULLNET_E2E_SKIP_VIEWER_PROVISION"] = "1";
            start.Environment["FULLNET_E2E_SKIP_OIDC_FIXTURES"] = "1"; start.Environment["FULLNET_E2E_API_URL"] = address;
            start.Environment["FULLNET_E2E_ADMIN_ORIGIN"] = origin; start.Environment["FULLNET_E2E_PASSWORD"] = FullNetApiFactory.TestPassword;
            start.Environment["FULLNET_E2E_ENTERPRISE_UNIT_ID"] = unit.ToString("D"); start.Environment["FULLNET_E2E_ENTERPRISE_IMPORT_NUMBER"] = number;
            start.Environment["FULLNET_E2E_ENTERPRISE_VALID_WORKBOOK"] = Path.Combine(temporary, "valid.xlsx");
            start.Environment["FULLNET_E2E_ENTERPRISE_INVALID_WORKBOOK"] = Path.Combine(temporary, "invalid.xlsx");
            start.Environment["FULLNET_E2E_L3_JSON_REPORT"] = Path.Combine(temporary, "browser.json");
            start.Environment["FULLNET_ENTERPRISE_EXPIRY_UI_EVIDENCE"] = temporary;
            start.Environment["FULLNET_ENTERPRISE_RETURN_UI_EVIDENCE"] = temporary;
            process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
            await process.WaitForExitAsync(timeout.Token);
            Assert.AreEqual(0, process.ExitCode, await output + Environment.NewLine + await errors + Environment.NewLine + temporary);
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(temporary, "browser.json")));
            var stats = report.RootElement.GetProperty("stats");
            Assert.AreEqual(!returnFlow && browserFilter is null ? 3 : 1, stats.GetProperty("expected").GetInt32()); Assert.AreEqual(0, stats.GetProperty("unexpected").GetInt32());
            Assert.AreEqual(0, stats.GetProperty("skipped").GetInt32()); Assert.AreEqual(0, stats.GetProperty("flaky").GetInt32()); passed = true;
        }
        finally
        {
            if (process is { HasExited: false }) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            process?.Dispose();
            if (worker is not null) { await worker.StopAsync(); worker.Dispose(); }
            if (passed && evidence is null) Directory.Delete(temporary, recursive: true); else Console.WriteLine("企业样板真实浏览器证据：" + temporary);
        }
    }
}
