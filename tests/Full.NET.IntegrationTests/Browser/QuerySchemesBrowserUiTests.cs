using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.EnterpriseRequest;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Settings.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Browser;

/// <summary>真实浏览器及业务列表验证方案持久化、共享只读和浏览器常用入口，不替代业务接口。</summary>
[TestClass]
[TestCategory("BrowserUi")]
public sealed class QuerySchemesBrowserUiTests
{
    [TestMethod]
    public async Task Saved_query_default_sharing_and_personal_shortcuts_via_real_browser()
    {
        var repository = FullNetApiFactory.FindRepositoryRoot(); using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); var origin = $"http://localhost:{port}";
        var evidence = Environment.GetEnvironmentVariable("FULLNET_QUERY_SCHEMES_UI_EVIDENCE");
        var temporary = evidence ?? Directory.CreateTempSubdirectory("fullnet-query-schemes-ui-").FullName; Directory.CreateDirectory(temporary);
        var passed = false; Process? process = null;
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            new Dictionary<string, string?> { ["Identity:AllowedOrigins:0"] = origin, ["Identity:AllowedOrigins:1"] = "http://localhost",
                ["Identity:RequireSecureCookies"] = "false", ["Tenancy:HostDomains:1"] = "127.0.0.1" }, includeEnterpriseRequest: true);
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        try
        {
            await factory.InitializeAsync(); var address = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri(address) }; client.DefaultRequestHeaders.Host = "localhost";
            var token = await EnterpriseRequestAssertions.LoginAndEnterAcmeTenantAsync(client, default); client.DefaultRequestHeaders.Authorization = new("Bearer", token);
            var unit = await EnterpriseRequestAssertions.CreateOrganizationUnitAsync(client, token, default);
            var actor = await EnterpriseRequestAssertions.GetCurrentUserIdAsync(client, token, default);
            var suffix = Guid.CreateVersion7().ToString("N")[..12];
            foreach (var title in new[] { "Literal_%[~ " + suffix, "Literal ordinary " + suffix })
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/enterprise_request/enterprise-requests")
                { Content = JsonContent.Create(new { requestNumber = "QUERY-" + Guid.CreateVersion7().ToString("N"), title, status = "Draft", totalAmount = "0.00", applicantUserId = actor }) };
                request.Headers.Add(OrganizationRequestHeaders.OrganizationUnitId, unit.ToString("D"));
                using var created = await client.SendAsync(request); Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
            }
            var readerName = "query.ui." + Guid.CreateVersion7().ToString("N");
            var reader = await factory.CreateHostIdentityAsync(readerName, ["identity.navigation.read", "tenancy.tenants.read", "tenancy.tenants.switch",
                "enterprise_request.enterprise_requests.read", QuerySchemePermissions.Read, QuerySchemePermissions.SetDefault], password: FullNetApiFactory.TestPassword);
            var email = readerName + "@example.com"; await factory.EnsureHostUserProfileEmailAsync(reader.UserId, email, readerName);
            using var invite = await client.PostAsJsonAsync("/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member));
            Assert.AreEqual(HttpStatusCode.OK, invite.StatusCode, await invite.Content.ReadAsStringAsync()); var invitation = await invite.Content.ReadFromJsonAsync<JsonElement>();
            using var member = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri(address) }; member.DefaultRequestHeaders.Host = "localhost";
            member.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(member, readerName));
            using (var accepted = await member.PostAsJsonAsync($"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { }))
                Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode, await accepted.Content.ReadAsStringAsync());
            var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(repository, "tests/e2e/admin-real-stack"),
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in new[] { Path.Combine(repository, "tests/e2e/admin-real-stack/node_modules/@playwright/test/cli.js"), "test",
                "query-schemes.spec.mjs", "--project", "vue-admin", "--workers", "1", "--output", Path.Combine(temporary, "browser") }) start.ArgumentList.Add(argument);
            foreach (var key in new[] { "FULLNET_E2E_SKIP_BOOTSTRAP", "FULLNET_E2E_SKIP_VIEWER_PROVISION", "FULLNET_E2E_SKIP_OIDC_FIXTURES" }) start.Environment[key] = "1";
            start.Environment["FULLNET_E2E_API_URL"] = address; start.Environment["FULLNET_E2E_ADMIN_ORIGIN"] = origin;
            start.Environment["FULLNET_E2E_PASSWORD"] = FullNetApiFactory.TestPassword; start.Environment["FULLNET_E2E_QUERY_READER"] = readerName;
            start.Environment["FULLNET_E2E_QUERY_SUFFIX"] = suffix; start.Environment["FULLNET_QUERY_SCHEMES_UI_EVIDENCE"] = temporary;
            start.Environment["FULLNET_E2E_L3_JSON_REPORT"] = Path.Combine(temporary, "browser.json");
            process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5)); await process.WaitForExitAsync(timeout.Token);
            Assert.AreEqual(0, process.ExitCode, await output + Environment.NewLine + await errors + Environment.NewLine + temporary);
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(temporary, "browser.json")));
            var stats = report.RootElement.GetProperty("stats"); Assert.AreEqual(1, stats.GetProperty("expected").GetInt32());
            foreach (var key in new[] { "unexpected", "skipped", "flaky" }) Assert.AreEqual(0, stats.GetProperty(key).GetInt32(), key); passed = true;
        }
        finally
        {
            if (process is { HasExited: false }) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } process?.Dispose();
            if (passed && evidence is null) Directory.Delete(temporary, recursive: true); else Console.WriteLine("查询方案浏览器证据：" + temporary);
        }
    }
}
