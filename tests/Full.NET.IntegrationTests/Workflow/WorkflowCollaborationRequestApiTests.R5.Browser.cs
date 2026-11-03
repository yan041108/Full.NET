using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Workflow;

public sealed partial class WorkflowCollaborationRequestApiTests
{
    [TestMethod, TestCategory("BrowserUi")]
    public Task R5_simulation_operations_real_browser_with_sql_server() => VerifyR5BrowserAsync();

    [TestMethod, TestCategory("BrowserUi")]
    public Task R5_simulation_selections_real_browser_with_sql_server() => VerifyR5BrowserAsync(selectionsOnly: true);

    private static async Task VerifyR5BrowserAsync(bool selectionsOnly = false)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var origin = $"http://localhost:{port}";
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            new Dictionary<string, string?> { ["Identity:AllowedOrigins:0"] = origin, ["Identity:AllowedOrigins:1"] = "http://localhost", ["Tenancy:HostDomains:1"] = "127.0.0.1", ["Identity:RequireSecureCookies"] = "false" });
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0)); await factory.InitializeAsync(useSchemaTemplate: false);
        var name = "r5.browser." + Guid.CreateVersion7().ToString("N");
        var identity = await factory.CreateHostIdentityAsync(name, WorkflowPermissions.All.Concat(["identity.navigation.read", "platform.dashboard.read"]).ToArray(), password: FullNetApiFactory.TestPassword);
        using var source = BrowserSocketClient(factory); source.DefaultRequestHeaders.Authorization = new("Bearer", identity.AccessToken);
        var asset = await R5AssetAsync(source, false);
        var version = (await PostAsync(source, $"/api/v1/workflow/definitions/{asset.Definition:D}/publish", new { expectedRevision = 1, formVersionId = asset.Form })).GetProperty("id").GetGuid();
        await PostAsync(source, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "r5", businessId = "browser", initialValues = new { amount = 12 }, idempotencyKey = Guid.CreateVersion7().ToString("N") }, HttpStatusCode.Created);
        if (selectionsOnly)
        {
            using var update = await source.PutAsJsonAsync($"/api/v1/workflow/definitions/{asset.Definition:D}/draft", new { expectedRevision = 2,
                draft = SelectionDraft("reviewChoice", [identity.UserId]) });
            Assert.AreEqual(HttpStatusCode.OK, update.StatusCode, await update.Content.ReadAsStringAsync());
        }
        var temporary = Directory.CreateTempSubdirectory("fullnet-workflow-r5-ui-").FullName; var passed = false; Process? process = null;
        try
        {
            var repository = FullNetApiFactory.FindRepositoryRoot();
            var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(repository, "tests", "e2e", "admin-real-stack"), UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in new[] { Path.Combine(start.WorkingDirectory, "node_modules", "@playwright", "test", "cli.js"), "test", selectionsOnly ? "workflow-r5-selections.spec.mjs" : "workflow-r5.spec.mjs", "--project", "vue-admin", "--workers", "1", "--output", Path.Combine(temporary, "browser") }) start.ArgumentList.Add(argument);
            foreach (var key in new[] { "BOOTSTRAP", "VIEWER_PROVISION", "OIDC_FIXTURES" }) start.Environment["FULLNET_E2E_SKIP_" + key] = "1";
            start.Environment["FULLNET_E2E_API_URL"] = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            start.Environment["FULLNET_E2E_ADMIN_ORIGIN"] = origin; start.Environment["FULLNET_E2E_PASSWORD"] = FullNetApiFactory.TestPassword;
            start.Environment["FULLNET_E2E_R5_USERNAME"] = name; start.Environment["FULLNET_E2E_R5_DIRECTORY"] = temporary; start.Environment["FULLNET_E2E_R5_KEY"] = asset.Key;
            start.Environment["FULLNET_E2E_R5_FORM"] = asset.Form.ToString("D"); start.Environment["FULLNET_E2E_L3_JSON_REPORT"] = Path.Combine(temporary, "browser.json");
            start.Environment["FULLNET_E2E_R5_USER"] = identity.UserId.ToString("D");
            process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6)); await process.WaitForExitAsync(timeout.Token);
            Assert.AreEqual(0, process.ExitCode, await output + Environment.NewLine + await errors + Environment.NewLine + temporary);
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(temporary, "browser.json"))); var stats = report.RootElement.GetProperty("stats");
            Assert.AreEqual(1, stats.GetProperty("expected").GetInt32()); Assert.AreEqual(0, stats.GetProperty("unexpected").GetInt32()); Assert.AreEqual(0, stats.GetProperty("skipped").GetInt32());
            Assert.IsTrue(File.Exists(Path.Combine(temporary, "result.json"))); passed = true;
        }
        finally
        {
            if (process is { HasExited: false }) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            process?.Dispose(); if (passed) Directory.Delete(temporary, recursive: true); else Console.WriteLine("R5真实浏览器证据：" + temporary);
        }
    }
}
