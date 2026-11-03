using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.EnterpriseRequest;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Browser;

/// <summary>通过真实详情确认取消并从本人任务入口复核终态，不模拟来源响应。</summary>
[TestClass]
[TestCategory("BrowserUi")]
public sealed class ImportTaskCancellationBrowserUiTests
{
    [TestMethod]
    public async Task Pending_import_is_canceled_in_details_and_discovered_in_my_tasks()
    {
        var repository = FullNetApiFactory.FindRepositoryRoot(); using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); var origin = $"http://localhost:{port}";
        var evidence = Environment.GetEnvironmentVariable("FULLNET_IMPORT_CANCELLATION_UI_EVIDENCE");
        var temporary = evidence ?? Directory.CreateTempSubdirectory("fullnet-import-cancel-ui-").FullName; Directory.CreateDirectory(temporary);
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            new Dictionary<string, string?> { ["Identity:AllowedOrigins:0"] = origin, ["Identity:AllowedOrigins:1"] = "http://localhost",
                ["Identity:RequireSecureCookies"] = "false", ["Tenancy:HostDomains:1"] = "127.0.0.1", ["FullNet:ImportExport:RunSynchronously"] = "false" });
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0)); Process? process = null; var passed = false;
        try
        {
            await factory.InitializeAsync(); var address = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri(address) }; client.DefaultRequestHeaders.Host = "localhost";
            client.DefaultRequestHeaders.Authorization = new("Bearer", await EnterpriseRequestAssertions.LoginAndEnterAcmeTenantAsync(client, default));
            var template = await client.GetByteArrayAsync("/api/v1/import-export/schemas/organization.tenant_positions/worksheets/positions/template");
            await File.WriteAllBytesAsync(Path.Combine(temporary, "cancel-positions.xlsx"), ImportProfilesApiTests.Workbook(template,
                [["code", "name", "displayOrder", "unitCode", "positionLevelCode"], ["cancel-ui-one", "Cancel preview", "0", "", ""]]));
            var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(repository, "tests/e2e/admin-real-stack"), UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in new[] { Path.Combine(repository, "tests/e2e/admin-real-stack/node_modules/@playwright/test/cli.js"), "test", "import-task-cancellation.spec.mjs",
                "--project", "vue-admin", "--workers", "1", "--output", Path.Combine(temporary, "browser") }) start.ArgumentList.Add(argument);
            foreach (var key in new[] { "FULLNET_E2E_SKIP_BOOTSTRAP", "FULLNET_E2E_SKIP_VIEWER_PROVISION", "FULLNET_E2E_SKIP_OIDC_FIXTURES" }) start.Environment[key] = "1";
            start.Environment["FULLNET_E2E_API_URL"] = address; start.Environment["FULLNET_E2E_ADMIN_ORIGIN"] = origin;
            start.Environment["FULLNET_E2E_PASSWORD"] = FullNetApiFactory.TestPassword; start.Environment["FULLNET_IMPORT_CANCELLATION_UI_EVIDENCE"] = temporary;
            start.Environment["FULLNET_E2E_L3_JSON_REPORT"] = Path.Combine(temporary, "browser.json");
            process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5)); await process.WaitForExitAsync(timeout.Token);
            Assert.AreEqual(0, process.ExitCode, await output + Environment.NewLine + await errors + Environment.NewLine + temporary);
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(temporary, "browser.json"))); var stats = report.RootElement.GetProperty("stats");
            Assert.AreEqual(1, stats.GetProperty("expected").GetInt32()); foreach (var key in new[] { "unexpected", "skipped", "flaky" }) Assert.AreEqual(0, stats.GetProperty(key).GetInt32()); passed = true;
        }
        finally
        {
            if (process is { HasExited: false }) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } process?.Dispose();
            if (passed && evidence is null) Directory.Delete(temporary, recursive: true); else Console.WriteLine("导入取消浏览器证据：" + temporary);
        }
    }
}
