using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Workflow;

public sealed partial class WorkflowCollaborationRequestApiTests
{
    /// <summary>复用真实资产准备夹具；版本比较只读固定版本，不调用执行器或展开人员。</summary>
    [TestMethod]
    [TestCategory("BrowserUi")]
    public async Task Workflow_published_versions_real_browser_with_sql_server()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var origin = $"http://localhost:{port}";
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            new Dictionary<string, string?> { ["Identity:AllowedOrigins:0"] = origin, ["Identity:AllowedOrigins:1"] = "http://localhost",
                ["Tenancy:HostDomains:1"] = "127.0.0.1", ["Identity:RequireSecureCookies"] = "false" });
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await factory.InitializeAsync(useSchemaTemplate: false);
        var username = "workflow.compare." + Guid.CreateVersion7().ToString("N");
        var identity = await factory.CreateHostIdentityAsync(username, ["identity.navigation.read", "platform.dashboard.read",
            WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsRead, WorkflowPermissions.FormsUpdate, WorkflowPermissions.FormsPublish,
            WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsRead, WorkflowPermissions.DefinitionsUpdate, WorkflowPermissions.DefinitionsPublish],
            password: FullNetApiFactory.TestPassword);
        using var source = BrowserSocketClient(factory); source.DefaultRequestHeaders.Authorization = new("Bearer", identity.AccessToken);
        var asset = await AssetAsync(source, approval: true);
        var originalForm = await source.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/form-versions/{asset.Form:D}");
        var formId = originalForm.GetProperty("formDefinitionId").GetGuid();
        var form = await source.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/forms/{formId:D}");
        using (var updated = await source.PutAsJsonAsync($"/api/v1/workflow/forms/{formId:D}/draft", new {
            expectedRevision = form.GetProperty("draftRevision").GetInt64(), draft = new { schemaVersion = 1, adapterVersion = 1,
                sections = new[] { new { sectionKey = "main", fields = new object[] {
                    new { fieldKey = "title", fieldTypeKey = "text", required = false, constraints = new { maxLength = 64 } },
                    new { fieldKey = "secret", fieldTypeKey = "text", required = false, constraints = new { } },
                    new { fieldKey = "answer", fieldTypeKey = "text", required = false, constraints = new { } } } } } } }))
            Assert.AreEqual(HttpStatusCode.OK, updated.StatusCode, await updated.Content.ReadAsStringAsync());
        var nextForm = (await PostAsync(source, $"/api/v1/workflow/forms/{formId:D}/publish",
            new { expectedRevision = form.GetProperty("draftRevision").GetInt64() + 1 })).GetProperty("id").GetGuid();
        var definition = await source.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/definitions/{asset.Definition:D}");
        var draftRevision = definition.GetProperty("draftRevision").GetInt64();
        using (var updated = await source.PutAsJsonAsync($"/api/v1/workflow/definitions/{asset.Definition:D}/draft", new {
            expectedRevision = draftRevision, draft = new { schemaVersion = 1, workflowKindKey = "approval", nodes = new object[] {
                new { nodeKey = "start", nodeTypeKey = "start", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "approve" } } },
                new { nodeKey = "approve", nodeTypeKey = "human.approval", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "end" },
                    nodeName = "Updated review", fieldPolicies = new { title = "readOnly", secret = "hidden", answer = "editable" } } },
                new { nodeKey = "end", nodeTypeKey = "end", nodeSchemaVersion = 1, config = new { } } } } }))
            Assert.AreEqual(HttpStatusCode.OK, updated.StatusCode, await updated.Content.ReadAsStringAsync());
        var nextVersion = (await PostAsync(source, $"/api/v1/workflow/definitions/{asset.Definition:D}/publish",
            new { expectedRevision = draftRevision + 1, formVersionId = nextForm })).GetProperty("id").GetGuid();
        Assert.AreNotEqual(asset.Version, nextVersion); Assert.AreNotEqual(asset.Form, nextForm);
        var temporary = Directory.CreateTempSubdirectory("fullnet-workflow-compare-ui-").FullName;
        var repository = FullNetApiFactory.FindRepositoryRoot(); var passed = false; Process? process = null;
        try
        {
            var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(repository, "tests", "e2e", "admin-real-stack"),
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in new[] { Path.Combine(start.WorkingDirectory, "node_modules", "@playwright", "test", "cli.js"),
                "test", "workflow-version-comparison.spec.mjs", "--project", "vue-admin", "--workers", "1", "--output", Path.Combine(temporary, "browser") }) start.ArgumentList.Add(argument);
            foreach (var key in new[] { "BOOTSTRAP", "VIEWER_PROVISION", "OIDC_FIXTURES" }) start.Environment["FULLNET_E2E_SKIP_" + key] = "1";
            start.Environment["FULLNET_E2E_API_URL"] = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            start.Environment["FULLNET_E2E_ADMIN_ORIGIN"] = origin; start.Environment["FULLNET_E2E_PASSWORD"] = FullNetApiFactory.TestPassword;
            start.Environment["FULLNET_E2E_COMPARE_USERNAME"] = username; start.Environment["FULLNET_E2E_COMPARE_DIRECTORY"] = temporary;
            start.Environment["FULLNET_E2E_COMPARE_KEY"] = asset.Key; start.Environment["FULLNET_E2E_COMPARE_DEFINITION"] = asset.Definition.ToString("D");
            start.Environment["FULLNET_E2E_COMPARE_BEFORE"] = asset.Version.ToString("D"); start.Environment["FULLNET_E2E_COMPARE_AFTER"] = nextVersion.ToString("D");
            start.Environment["FULLNET_E2E_COMPARE_FORM_BEFORE"] = asset.Form.ToString("D"); start.Environment["FULLNET_E2E_COMPARE_FORM_AFTER"] = nextForm.ToString("D");
            start.Environment["FULLNET_E2E_L3_JSON_REPORT"] = Path.Combine(temporary, "browser.json");
            process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6)); await process.WaitForExitAsync(timeout.Token);
            Assert.AreEqual(0, process.ExitCode, await output + Environment.NewLine + await errors + Environment.NewLine + temporary);
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(temporary, "browser.json")));
            var stats = report.RootElement.GetProperty("stats"); Assert.AreEqual(1, stats.GetProperty("expected").GetInt32());
            Assert.AreEqual(0, stats.GetProperty("unexpected").GetInt32()); Assert.AreEqual(0, stats.GetProperty("skipped").GetInt32());
            Assert.IsTrue(File.Exists(Path.Combine(temporary, "result.json"))); passed = true;
        }
        finally
        {
            if (process is { HasExited: false }) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            process?.Dispose(); if (passed) Directory.Delete(temporary, recursive: true); else Console.WriteLine("版本比较真实浏览器证据：" + temporary);
        }
    }
}
