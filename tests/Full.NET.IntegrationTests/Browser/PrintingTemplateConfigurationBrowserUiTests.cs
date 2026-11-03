using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Browser;

/// <summary>真实 Host 页面维护草稿、显式发布及只读选版式；不模拟业务数据绑定或后台转换。</summary>
[TestClass]
[TestCategory("BrowserUi")]
public sealed class PrintingTemplateConfigurationBrowserUiTests
{
    [TestMethod]
    public async Task Host_saves_drafts_explicitly_publishes_and_previews_immutable_layout_versions()
    {
        var repository = FullNetApiFactory.FindRepositoryRoot(); using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); var origin = $"http://localhost:{port}";
        var evidence = Environment.GetEnvironmentVariable("FULLNET_PRINTING_CONFIGURATION_UI_EVIDENCE");
        var temporary = evidence ?? Directory.CreateTempSubdirectory("fullnet-printing-config-ui-").FullName; Directory.CreateDirectory(temporary);
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            new Dictionary<string, string?> { ["Identity:AllowedOrigins:0"] = origin, ["Identity:AllowedOrigins:1"] = "http://localhost",
                ["Identity:RequireSecureCookies"] = "false", ["Tenancy:HostDomains:1"] = "127.0.0.1" });
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0)); Process? process = null; var passed = false;
        try
        {
            await factory.InitializeAsync(); var address = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(repository, "tests/e2e/admin-real-stack"), UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in new[] { Path.Combine(repository, "tests/e2e/admin-real-stack/node_modules/@playwright/test/cli.js"), "test", "printing-template-configuration.spec.mjs",
                "--project", "vue-admin", "--workers", "1", "--output", Path.Combine(temporary, "browser") }) start.ArgumentList.Add(argument);
            foreach (var key in new[] { "FULLNET_E2E_SKIP_BOOTSTRAP", "FULLNET_E2E_SKIP_VIEWER_PROVISION", "FULLNET_E2E_SKIP_OIDC_FIXTURES" }) start.Environment[key] = "1";
            start.Environment["FULLNET_E2E_API_URL"] = address; start.Environment["FULLNET_E2E_ADMIN_ORIGIN"] = origin;
            start.Environment["FULLNET_E2E_PASSWORD"] = FullNetApiFactory.TestPassword; start.Environment["FULLNET_PRINTING_CONFIGURATION_UI_EVIDENCE"] = temporary;
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
            if (passed && evidence is null) Directory.Delete(temporary, recursive: true); else Console.WriteLine("打印模板配置页面证据：" + temporary);
        }
    }
}
