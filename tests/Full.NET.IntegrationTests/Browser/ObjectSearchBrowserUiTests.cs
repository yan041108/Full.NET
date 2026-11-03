using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.EnterpriseRequest;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Browser;

/// <summary>真实浏览器按上下文检索两个来源并重新读取详情；不模拟来源响应。</summary>
[TestClass]
[TestCategory("BrowserUi")]
public sealed class ObjectSearchBrowserUiTests
{
    [TestMethod]
    public async Task Recent_documents_and_requests_are_discovered_via_real_global_search()
    {
        var repository = FullNetApiFactory.FindRepositoryRoot(); using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); var origin = $"http://localhost:{port}";
        var evidence = Environment.GetEnvironmentVariable("FULLNET_OBJECT_SEARCH_UI_EVIDENCE");
        var temporary = evidence ?? Directory.CreateTempSubdirectory("fullnet-object-search-ui-").FullName; Directory.CreateDirectory(temporary);
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            new Dictionary<string, string?> { ["Identity:AllowedOrigins:0"] = origin, ["Identity:AllowedOrigins:1"] = "http://localhost",
                ["Identity:RequireSecureCookies"] = "false", ["Tenancy:HostDomains:1"] = "127.0.0.1" }, includeEnterpriseRequest: true);
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0)); Process? process = null; var passed = false;
        try
        {
            await factory.InitializeAsync(); var address = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri(address) }; client.DefaultRequestHeaders.Host = "localhost";
            client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, "admin"));
            var suffix = Guid.CreateVersion7().ToString("N")[..12]; var query = "Find_%[~" + suffix;
            using (var document = await client.PostAsJsonAsync("/api/v1/document/host/items", new CreateHostDocumentItemRequest(query + " document", "Search detail text")))
                Assert.AreEqual(HttpStatusCode.Created, document.StatusCode, await document.Content.ReadAsStringAsync());
            var token = await EnterpriseRequestAssertions.LoginAndEnterAcmeTenantAsync(client, default); client.DefaultRequestHeaders.Authorization = new("Bearer", token);
            var unit = await EnterpriseRequestAssertions.CreateOrganizationUnitAsync(client, token, default); var actor = await EnterpriseRequestAssertions.GetCurrentUserIdAsync(client, token, default);
            using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/enterprise_request/enterprise-requests") { Content = JsonContent.Create(new {
                requestNumber = "SEARCH-" + Guid.CreateVersion7().ToString("N"), title = query + " request", status = "Draft", totalAmount = "0.00", applicantUserId = actor }) })
            {
                request.Headers.Add(OrganizationRequestHeaders.OrganizationUnitId, unit.ToString("D")); using var response = await client.SendAsync(request);
                Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
            }
            var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(repository, "tests/e2e/admin-real-stack"), UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in new[] { Path.Combine(repository, "tests/e2e/admin-real-stack/node_modules/@playwright/test/cli.js"), "test", "object-search.spec.mjs",
                "--project", "vue-admin", "--workers", "1", "--output", Path.Combine(temporary, "browser") }) start.ArgumentList.Add(argument);
            foreach (var key in new[] { "FULLNET_E2E_SKIP_BOOTSTRAP", "FULLNET_E2E_SKIP_VIEWER_PROVISION", "FULLNET_E2E_SKIP_OIDC_FIXTURES" }) start.Environment[key] = "1";
            start.Environment["FULLNET_E2E_API_URL"] = address; start.Environment["FULLNET_E2E_ADMIN_ORIGIN"] = origin;
            start.Environment["FULLNET_E2E_PASSWORD"] = FullNetApiFactory.TestPassword; start.Environment["FULLNET_E2E_SEARCH_QUERY"] = query;
            start.Environment["FULLNET_OBJECT_SEARCH_UI_EVIDENCE"] = temporary; start.Environment["FULLNET_E2E_L3_JSON_REPORT"] = Path.Combine(temporary, "browser.json");
            process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5)); await process.WaitForExitAsync(timeout.Token);
            Assert.AreEqual(0, process.ExitCode, await output + Environment.NewLine + await errors + Environment.NewLine + temporary);
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(temporary, "browser.json"))); var stats = report.RootElement.GetProperty("stats");
            Assert.AreEqual(1, stats.GetProperty("expected").GetInt32()); foreach (var key in new[] { "unexpected", "skipped", "flaky" }) Assert.AreEqual(0, stats.GetProperty(key).GetInt32()); passed = true;
        }
        finally
        {
            if (process is { HasExited: false }) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } process?.Dispose();
            if (passed && evidence is null) Directory.Delete(temporary, recursive: true); else Console.WriteLine("搜索浏览器证据：" + temporary);
        }
    }
}
