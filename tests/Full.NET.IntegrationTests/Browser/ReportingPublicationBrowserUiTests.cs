using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Reporting;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Features.ManageExportTasks;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Browser;

/// <summary>真实 Edge、Kestrel、SQL Server 与后台循环验证 Host 授权至租户导出；外部数据库连接使用受控端口。</summary>
[TestClass]
[TestCategory("BrowserUi")]
public sealed class ReportingPublicationBrowserUiTests
{
    [TestMethod]
    public Task Host_grants_published_report_and_tenant_exports_via_real_browser() => RunAsync(BrowserScenario.Export);

    [TestMethod]
    public Task Tenant_cancels_own_pending_report_and_finds_terminal_state_in_my_tasks() => RunAsync(BrowserScenario.Cancel);

    [TestMethod]
    public Task Host_edits_draft_recovers_conflict_and_explicitly_publishes_report() => RunAsync(BrowserScenario.Definition);

    [TestMethod]
    public Task Host_creates_edits_without_replacing_credentials_and_disables_data_source() => RunAsync(BrowserScenario.DataSource);

    [TestMethod]
    public Task Host_executes_published_version_while_draft_parameters_have_changed() => RunAsync(BrowserScenario.Execution);

    private enum BrowserScenario { Export, Cancel, Definition, DataSource, Execution }

    private static async Task RunAsync(BrowserScenario scenario)
    {
        var cancelPending = scenario == BrowserScenario.Cancel;
        var repository = FullNetApiFactory.FindRepositoryRoot(); using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); var origin = $"http://localhost:{port}";
        var evidence = Environment.GetEnvironmentVariable("FULLNET_REPORTING_UI_EVIDENCE");
        var temporary = evidence ?? Directory.CreateTempSubdirectory("fullnet-reporting-publication-ui-").FullName;
        Directory.CreateDirectory(temporary); var passed = false; Process? process = null;
        var external = new ReportingTenantPublicationTests.ControlledExternalDatabase();
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            new Dictionary<string, string?> { ["Identity:AllowedOrigins:0"] = origin, ["Identity:AllowedOrigins:1"] = "http://localhost",
                ["Identity:RequireSecureCookies"] = "false", ["Tenancy:HostDomains:1"] = "127.0.0.1", ["Files:Local:RootPath"] = Path.Combine(temporary, "files") },
            configureTestServices: services => services.AddSingleton<IExternalDatabaseConnectionFactory>(external));
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        try
        {
            await factory.InitializeAsync();
            var address = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri(address) };
            client.DefaultRequestHeaders.Host = "localhost"; var token = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, "admin");
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);
            var tenant = (await client.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(value => value.Identifier == "acme");
            var source = await CreateAsync<ReportingDataSourceResponse>("/api/v1/reporting/data-sources",
                new CreateReportingDataSourceRequest(null, "Browser source", "sql_server", "reports.example.com", 1433, "reports", "readonly", "Test!Password2026", false, true));
            var group = await CreateAsync<ReportingGroupResponse>("/api/v1/reporting/groups", new CreateReportingGroupRequest(null, "Browser reports", 1, true));
            var name = "Published UI " + Guid.CreateVersion7().ToString("N")[..12];
            var definition = await CreateAsync<ReportingDefinitionResponse>("/api/v1/reporting/definitions", new CreateReportingDefinitionRequest(
                group.Id, source.Id, "browser_version", name, null, "reporting.database_engine_version", [], null, true));
            using (var published = await client.PostAsJsonAsync($"/api/v1/reporting/definitions/{definition.Id:D}/publish", new PublishReportingDefinitionRequest("Browser proof", definition.Version)))
                Assert.AreEqual(HttpStatusCode.OK, published.StatusCode, await published.Content.ReadAsStringAsync());
            if (scenario == BrowserScenario.Execution)
            {
                // 未发布草稿换成另一个参数契约，执行页仍须读取并明确执行原发布版本。
                var currentDraft = (await client.GetFromJsonAsync<ReportingDefinitionResponse>($"/api/v1/reporting/definitions/{definition.Id:D}"))!;
                using var changed = await client.PutAsJsonAsync($"/api/v1/reporting/definitions/{definition.Id:D}", new UpdateReportingDefinitionRequest(
                    group.Id, source.Id, name, null, "reporting.schema_inventory",
                    [new ReportingParameterSchemaEntry("topN", "未发布草稿参数", "integer", true, "5")], null, true, currentDraft.Version));
                Assert.AreEqual(HttpStatusCode.OK, changed.StatusCode, await changed.Content.ReadAsStringAsync());
            }
            var memberName = "report.ui." + Guid.CreateVersion7().ToString("N");
            var member = await factory.CreateHostIdentityAsync(memberName, ["identity.navigation.read", "tenancy.tenants.read", "tenancy.tenants.switch",
                ReportingDefinitionGrantPermissions.PublishedRead, ReportingExportTaskPermissions.Create, ReportingExportTaskPermissions.Read,
                ReportingExportTaskPermissions.Download, .. (cancelPending ? new[] { ReportingExportTaskPermissions.Cancel } : []), "platform.my_tasks.read"], password: FullNetApiFactory.TestPassword);
            var email = memberName + "@example.com"; await factory.EnsureHostUserProfileEmailAsync(member.UserId, email, memberName);
            client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, token, tenant.Id));
            using var invite = await client.PostAsJsonAsync("/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member));
            Assert.AreEqual(HttpStatusCode.OK, invite.StatusCode, await invite.Content.ReadAsStringAsync());
            var invitation = await invite.Content.ReadFromJsonAsync<JsonElement>();
            using var memberClient = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri(address) };
            memberClient.DefaultRequestHeaders.Host = "localhost";
            memberClient.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(memberClient, memberName));
            using (var accept = await memberClient.PostAsJsonAsync($"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { }))
                Assert.AreEqual(HttpStatusCode.OK, accept.StatusCode, await accept.Content.ReadAsStringAsync());
            using var worker = await IntegrationWorkerHostFactory.BuildAsync(DatabaseProvider.SqlServer, factory.ConnectionString,
                new Dictionary<string, string?> { ["FullNet:Reporting:Export:PollSeconds"] = "5" }, "Reporting.Browser.Worker", nameof(ReportingExportTaskHostedProcessor),
                fileRootPath: Path.Combine(temporary, "files"), configureServices: services =>
                {
                    services.AddSingleton(factory.Services.GetRequiredService<IDataProtectionProvider>());
                    services.AddSingleton<IExternalDatabaseConnectionFactory>(external);
                });
            // 排队取消场景不启动消费者；真实领取竞争由双库接口用例覆盖。
            var runWorker = scenario == BrowserScenario.Export;
            if (runWorker) await worker.StartAsync();
            try
            {
                var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(repository, "tests/e2e/admin-real-stack"),
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                foreach (var argument in new[] { Path.Combine(repository, "tests/e2e/admin-real-stack/node_modules/@playwright/test/cli.js"), "test",
                    scenario switch { BrowserScenario.Execution => "reporting-published-execution.spec.mjs", BrowserScenario.DataSource => "reporting-data-source-configuration.spec.mjs",
                        BrowserScenario.Definition => "reporting-template-configuration.spec.mjs", BrowserScenario.Cancel => "reporting-task-cancellation.spec.mjs", _ => "reporting-publication.spec.mjs" },
                    "--project", "vue-admin", "--workers", "1", "--output", Path.Combine(temporary, "browser") }) start.ArgumentList.Add(argument);
                foreach (var key in new[] { "FULLNET_E2E_SKIP_BOOTSTRAP", "FULLNET_E2E_SKIP_VIEWER_PROVISION", "FULLNET_E2E_SKIP_OIDC_FIXTURES" }) start.Environment[key] = "1";
                start.Environment["FULLNET_E2E_API_URL"] = address; start.Environment["FULLNET_E2E_ADMIN_ORIGIN"] = origin;
                start.Environment["FULLNET_E2E_PASSWORD"] = FullNetApiFactory.TestPassword;
                start.Environment["FULLNET_E2E_REPORTING_DEFINITION_NAME"] = name; start.Environment["FULLNET_E2E_REPORTING_TENANT_NAME"] = tenant.Name;
                start.Environment["FULLNET_E2E_REPORTING_MEMBER"] = memberName;
                start.Environment["FULLNET_REPORTING_UI_EVIDENCE"] = temporary;
                start.Environment["FULLNET_E2E_L3_JSON_REPORT"] = Path.Combine(temporary, "browser.json");
                process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5)); await process.WaitForExitAsync(timeout.Token);
                Assert.AreEqual(0, process.ExitCode, await output + Environment.NewLine + await errors + Environment.NewLine + temporary);
                using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(temporary, "browser.json")));
                var stats = report.RootElement.GetProperty("stats"); Assert.AreEqual(1, stats.GetProperty("expected").GetInt32());
                foreach (var key in new[] { "unexpected", "skipped", "flaky" }) Assert.AreEqual(0, stats.GetProperty(key).GetInt32(), key);
                Assert.AreEqual(runWorker || scenario == BrowserScenario.Execution ? 1 : 0, external.QueryCount); passed = true;
            }
            finally { if (runWorker) await worker.StopAsync(); }

            async Task<T> CreateAsync<T>(string path, object body)
            {
                using var response = await client.PostAsJsonAsync(path, body); Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
                return (await response.Content.ReadFromJsonAsync<T>())!;
            }
        }
        finally
        {
            if (process is { HasExited: false }) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } process?.Dispose();
            if (passed && evidence is null) Directory.Delete(temporary, recursive: true); else Console.WriteLine("报表浏览器证据：" + temporary);
        }
    }
}
