using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Browser;

/// <summary>复用真实双库夹具的SQL Server场景，验证Vue双方授权操作，不启动无关Worker和外部身份提供程序。</summary>
[TestClass]
[TestCategory("BrowserUi")]
public sealed class WorkflowCollaborationGrantBrowserUiTests
{
    [TestMethod]
    public async Task Real_browser_bilateral_grant_control_with_sql_server()
    {
        var repository = new DirectoryInfo(FullNetApiFactory.FindRepositoryRoot());
        var frontendPort = FreePort();
        var origin = $"http://localhost:{frontendPort}";
        var temporary = Directory.CreateTempSubdirectory("fullnet-workflow-grant-ui-").FullName;
        var passed = false;
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            new Dictionary<string, string?> { ["Identity:AllowedOrigins:0"] = origin, ["Identity:AllowedOrigins:1"] = "http://localhost",
                ["Tenancy:HostDomains:1"] = "127.0.0.1", ["Identity:RequireSecureCookies"] = "false" });
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        Process? process = null;
        try
        {
            await factory.InitializeAsync(useSchemaTemplate: false);
            var memberId = await PrepareTargetMemberAsync(factory);
            var address = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(repository.FullName, "tests", "e2e", "admin-real-stack"),
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var value in new[] { Path.Combine(repository.FullName, "tests", "e2e", "admin-real-stack", "node_modules", "@playwright", "test", "cli.js"),
                         "test", "workflow-collaboration-grants.spec.mjs", "--project", "vue-admin", "--workers", "1", "--output", Path.Combine(temporary, "browser") }) start.ArgumentList.Add(value);
            start.Environment["FULLNET_E2E_SKIP_BOOTSTRAP"] = "1";
            start.Environment["FULLNET_E2E_SKIP_VIEWER_PROVISION"] = "1";
            start.Environment["FULLNET_E2E_API_URL"] = address;
            start.Environment["FULLNET_E2E_PASSWORD"] = FullNetApiFactory.TestPassword;
            start.Environment["FULLNET_E2E_ADMIN_ORIGIN"] = origin;
            start.Environment["FULLNET_E2E_SKIP_OIDC_FIXTURES"] = "1";
            start.Environment["FULLNET_E2E_COLLABORATION_MEMBER_ID"] = memberId.ToString("D");
            start.Environment["FULLNET_E2E_L3_JSON_REPORT"] = Path.Combine(temporary, "browser.json");
            process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
            await process.WaitForExitAsync(timeout.Token);
            var detail = await output + Environment.NewLine + await errors;
            Assert.AreEqual(0, process.ExitCode, detail + Environment.NewLine + temporary);
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(temporary, "browser.json")));
            var stats = report.RootElement.GetProperty("stats");
            Assert.AreEqual(1, stats.GetProperty("expected").GetInt32());
            Assert.AreEqual(0, stats.GetProperty("unexpected").GetInt32());
            Assert.AreEqual(0, stats.GetProperty("skipped").GetInt32());
            passed = true;
        }
        finally
        {
            if (process is { HasExited: false }) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            process?.Dispose();
            // 只清理本例创建的临时产物；失败保留浏览器和原始请求证据。
            if (passed) Directory.Delete(temporary, recursive: true);
            else Console.WriteLine("协作授权真实浏览器证据：" + temporary);
        }
    }

    private static async Task<Guid> PrepareTargetMemberAsync(FullNetApiFactory factory)
    {
        var name = "collab.browser.member." + Guid.CreateVersion7().ToString("N");
        var identity = await factory.CreateHostIdentityAsync(name, [], password: FullNetApiFactory.TestPassword);
        var email = name + "@example.com";
        await factory.EnsureHostUserProfileEmailAsync(identity.UserId, email, name);
        using var owner = SocketClient(factory);
        owner.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var administrator = await IntegrationTestAuthHelper.LoginAsHostUserAsync(owner, "admin");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", administrator);
        var tenants = await owner.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available");
        var tenant = tenants!.Single(value => value.Identifier == "acme");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, administrator, tenant.Id));
        using var invited = await owner.PostAsJsonAsync("/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member));
        Assert.AreEqual(HttpStatusCode.OK, invited.StatusCode, await invited.Content.ReadAsStringAsync());
        var invitation = await invited.Content.ReadFromJsonAsync<JsonElement>();
        using var member = SocketClient(factory);
        member.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(member, name));
        // 管理员切换作用域不等于活动成员；政策绑定的发起人必须真正接受租户邀请。
        using var accepted = await member.PostAsJsonAsync($"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { });
        Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode, await accepted.Content.ReadAsStringAsync());
        return identity.UserId;
    }

    private static HttpClient SocketClient(FullNetApiFactory factory)
    {
        var address = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { BaseAddress = new Uri(address) };
        client.DefaultRequestHeaders.Host = "localhost";
        return client;
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
