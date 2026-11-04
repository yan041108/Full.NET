using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.EnterpriseRequest;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
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
    public Task Host_saves_drafts_explicitly_publishes_and_previews_immutable_layout_versions() => RunAsync(DatabaseProvider.SqlServer, false);

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Host_business_template_is_published_granted_and_previewed_from_the_actual_record(DatabaseProvider provider) => RunAsync(provider, true);

    private static async Task RunAsync(DatabaseProvider provider, bool business)
    {
        var repository = FullNetApiFactory.FindRepositoryRoot(); using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); var origin = $"http://localhost:{port}";
        var evidence = Environment.GetEnvironmentVariable("FULLNET_PRINTING_CONFIGURATION_UI_EVIDENCE");
        var temporary = evidence ?? Directory.CreateTempSubdirectory("fullnet-printing-config-ui-").FullName; Directory.CreateDirectory(temporary);
        var connection = provider == DatabaseProvider.MySql ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync() : await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection,
            new Dictionary<string, string?> { ["Identity:AllowedOrigins:0"] = origin, ["Identity:AllowedOrigins:1"] = "http://localhost",
                ["Identity:RequireSecureCookies"] = "false", ["Tenancy:HostDomains:1"] = "127.0.0.1" }, includeEnterpriseRequest: business);
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0)); Process? process = null; var passed = false;
        try
        {
            await factory.InitializeAsync(useSchemaTemplate: !business); var address = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var businessEnvironment = new Dictionary<string, string>();
            if (business)
            {
                // 只经真实业务入口准备当前操作者的记录，页面负责模板配置、发布、授权及预览。
                using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri(address) };
                client.DefaultRequestHeaders.Host = "localhost";
                var access = await EnterpriseRequestAssertions.LoginAndEnterAcmeTenantAsync(client, default);
                var organization = await EnterpriseRequestAssertions.CreateOrganizationUnitAsync(client, access, default);
                var actor = await EnterpriseRequestAssertions.GetCurrentUserIdAsync(client, access, default);
                client.DefaultRequestHeaders.Authorization = new("Bearer", access);
                using var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/enterprise_request/enterprise-requests")
                { Content = JsonContent.Create(new CreateEnterpriseRequestRequest("PRINT-" + Guid.CreateVersion7().ToString("N")[..12], "真实业务模板预览", "Draft", 42m, actor)) };
                create.Headers.Add(OrganizationRequestHeaders.OrganizationUnitId, organization.ToString("D"));
                using var created = await client.SendAsync(create); Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
                var record = (await created.Content.ReadFromJsonAsync<EnterpriseRequestResponse>())!;
                businessEnvironment["FULLNET_PRINTING_BUSINESS_RECORD_ID"] = record.Id.ToString("D");
                businessEnvironment["FULLNET_PRINTING_BUSINESS_TENANT_ID"] = new JwtSecurityTokenHandler().ReadJwtToken(access).Claims.Single(claim => claim.Type == FullNetIdentityClaimTypes.TenantId).Value;
            }
            var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(repository, "tests/e2e/admin-real-stack"), UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in new[] { Path.Combine(repository, "tests/e2e/admin-real-stack/node_modules/@playwright/test/cli.js"), "test", business ? "printing-business-template.spec.mjs" : "printing-template-configuration.spec.mjs",
                "--project", "vue-admin", "--workers", "1", "--output", Path.Combine(temporary, "browser") }) start.ArgumentList.Add(argument);
            foreach (var key in new[] { "FULLNET_E2E_SKIP_BOOTSTRAP", "FULLNET_E2E_SKIP_VIEWER_PROVISION", "FULLNET_E2E_SKIP_OIDC_FIXTURES" }) start.Environment[key] = "1";
            start.Environment["FULLNET_E2E_API_URL"] = address; start.Environment["FULLNET_E2E_ADMIN_ORIGIN"] = origin;
            start.Environment["FULLNET_E2E_PASSWORD"] = FullNetApiFactory.TestPassword; start.Environment["FULLNET_PRINTING_CONFIGURATION_UI_EVIDENCE"] = temporary;
            start.Environment["FULLNET_E2E_L3_JSON_REPORT"] = Path.Combine(temporary, "browser.json");
            foreach (var pair in businessEnvironment) start.Environment[pair.Key] = pair.Value;
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
