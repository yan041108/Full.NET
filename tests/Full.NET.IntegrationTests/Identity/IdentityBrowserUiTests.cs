using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>真实 Kestrel、HTTPS 终止代理、Vue 与浏览器；RP 只信任本轮证书指纹。</summary>
[TestClass]
[TestCategory("BrowserUi")]
public sealed class IdentityBrowserUiTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Real_browser_https_bff_desktop_and_mobile_preserve_server_side_credentials(DatabaseProvider provider)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Full.NET.slnx"))) repository = repository.Parent;
        Assert.IsNotNull(repository);
        var root = Directory.CreateTempSubdirectory("fullnet-browser-ui-").FullName;
        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback);
        certificateRequest.CertificateExtensions.Add(names.Build());
        certificateRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var pem = Path.Combine(root, "tls.pem"); var key = Path.Combine(root, "tls.key");
        await File.WriteAllTextAsync(pem, certificate.ExportCertificatePem());
        await File.WriteAllTextAsync(key, rsa.ExportPkcs8PrivateKeyPem());
        var frontendPort = FreePort(); var relyingPartyPort = FreePort();
        var origin = $"https://localhost:{frontendPort}";
        var secondCallback = $"https://127.0.0.1:{relyingPartyPort}/server-callback.html";
        var settings = IdentityBrowserTestSupport.Settings();
        settings["Identity:Oidc:Issuer"] = origin + "/identity";
        settings["Identity:Browser:PublicOrigin"] = origin;
        settings["Identity:Oidc:MinimumReadyApiNodes"] = "2";
        settings["Identity:AllowedOrigins:0"] = origin;
        settings["TrustedProxy:Enabled"] = "true";
        settings["TrustedProxy:KnownProxies:0"] = "127.0.0.1";
        settings["DataProtection:KeyRingPath"] = Path.Combine(root, "keys");
        settings["Identity:Oidc:Clients:2:ClientId"] = "browser-second-rp";
        settings["Identity:Oidc:Clients:2:ClientSecret"] = "browser-second-rp-fixture-secret";
        settings["Identity:Oidc:Clients:2:RedirectUris:0"] = secondCallback;
        settings["Identity:Oidc:Clients:2:Scopes:0"] = "openid";
        settings["Identity:Oidc:Clients:2:Scopes:1"] = "profile";
        settings["Identity:Oidc:Clients:2:IsFirstParty"] = "true";
        var connection = await IdentityBrowserTestSupport.DatabaseAsync(provider);
        Guid bootstrapNode;
        // 先完成旧材料的受控初始化，再部署新候选材料；Migrator 不得自动覆盖后续治理事实。
        using (var bootstrap = new FullNetApiFactory(provider, connection, settings))
        {
            await bootstrap.InitializeAsync();
            bootstrapNode = bootstrap.Services.GetRequiredService<IdentityOidcSigningNodeIdentity>().Id;
        }
        using var nextSigningKey = RSA.Create(2048);
        settings["Identity:Oidc:SigningKeys:browser-next:PublicKeyPem"] = nextSigningKey.ExportRSAPublicKeyPem();
        settings["Identity:Oidc:SigningKeys:browser-next:PrivateKeyPem"] = nextSigningKey.ExportRSAPrivateKeyPem();
        var protocolDiagnostics = new ProtocolDiagnostics(Path.Combine(root, "protocol-network.log"), certificate.Thumbprint);
        void Configure(IServiceCollection services)
            {
                services.AddSingleton<IHttpMessageHandlerBuilderFilter>(protocolDiagnostics);
            }
        using var factory = new FullNetApiFactory(provider, connection, settings, configureTestServices: Configure);
        using var callbackFactory = new FullNetApiFactory(provider, connection, settings, configureTestServices: Configure);
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        callbackFactory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        Process? vite = null;
        Process? relyingParty = null;
        Task<string>? viteOutput = null; Task<string>? viteErrors = null;
        Task<string>? relyingPartyOutput = null; Task<string>? relyingPartyErrors = null;
        var passed = false;
        try
        {
            await factory.InitializeAsync();
            await callbackFactory.InitializeAsync();
            await using (var drain = factory.Services.CreateAsyncScope())
            {
                drain.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
                // 已停止的初始化进程不再接流量；仅推进它自己的租约，避免夹具等待真实 90 秒。
                await drain.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(new SqlStatement("test.drain_browser_bootstrap_node",
                    "UPDATE fn_identity_oidc_signing_node SET ExpiresAtUtc = @Before WHERE Id = @Id", SqlDataScope.HostOnly),
                    IdentitySqlParameters.Create(("Before", DateTimeOffset.UtcNow.AddSeconds(-1)), ("Id", bootstrapNode)));
            }
            var backend = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var callbackBackend = callbackFactory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var environment = new Dictionary<string, string>
            {
                ["VITE_HTTPS_CERT"] = pem, ["VITE_HTTPS_KEY"] = key,
                ["VITE_API_PROXY_TARGET"] = backend,
                // 只将 RP 回调送往另一个真实 API，证明状态令牌与保护票据不是进程内偶然共享。
                ["VITE_BFF_CALLBACK_PROXY_TARGET"] = callbackBackend,
                ["VITE_IDENTITY_AUTH_MODE"] = "oidc-center", ["VITE_IDENTITY_OIDC_CLIENT_ID"] = "fullnet-admin-bff",
                ["NODE_EXTRA_CA_CERTS"] = pem, ["FULLNET_BFF_UI_ORIGIN"] = origin,
                ["FULLNET_BFF_UI_PASSWORD"] = FullNetApiFactory.TestPassword,
                ["FULLNET_BFF_SECOND_RP_CALLBACK"] = secondCallback,
                ["FULLNET_E2E_TLS_CERT"] = pem, ["FULLNET_E2E_TLS_KEY"] = key, ["FULLNET_E2E_BIND_HOST"] = "127.0.0.1",
                ["FULLNET_BFF_UI_OUTPUT"] = Path.Combine(root, "playwright")
            };
            relyingParty = Start(repository.FullName, environment, "tests/e2e/admin-real-stack/scripts/serve-oidc-fixture.mjs",
                relyingPartyPort.ToString(), Path.Combine(repository.FullName, "tests", "e2e", "admin-real-stack", "fixtures", "oidc-rp-b"));
            relyingPartyOutput = relyingParty.StandardOutput.ReadToEndAsync(); relyingPartyErrors = relyingParty.StandardError.ReadToEndAsync();
            vite = Start(repository.FullName, environment, "ui/admin/node_modules/vite/bin/vite.js", "ui/admin", "--host", "localhost", "--port", frontendPort.ToString());
            viteOutput = vite.StandardOutput.ReadToEndAsync(); viteErrors = vite.StandardError.ReadToEndAsync();
            using var handler = new HttpClientHandler { UseProxy = false,
                ServerCertificateCustomValidationCallback = (_, remote, _, _) => remote?.Thumbprint == certificate.Thumbprint };
            using var probe = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
            var ready = false;
            for (var attempt = 0; attempt < 120 && !vite.HasExited; attempt++)
            {
                try { using var response = await probe.GetAsync(origin); ready = response.IsSuccessStatusCode; if (ready) break; }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
                await Task.Delay(500);
            }
            Assert.IsTrue(ready, "HTTPS Vue 开发服务器未就绪。");
            using var browser = Start(repository.FullName, environment, "tests/e2e/admin-real-stack/node_modules/@playwright/test/cli.js",
                "test", "--config", "tests/e2e/admin-real-stack/playwright.bff.config.mjs");
            var browserOutput = browser.StandardOutput.ReadToEndAsync(); var browserErrors = browser.StandardError.ReadToEndAsync();
            try { await browser.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(6)); }
            finally { if (!browser.HasExited) { browser.Kill(entireProcessTree: true); await browser.WaitForExitAsync(); } }
            var diagnostic = SafeLog(await browserOutput + "\n" + await browserErrors);
            await File.WriteAllTextAsync(Path.Combine(root, "playwright.log"), diagnostic);
            Assert.AreEqual(0, browser.ExitCode, "真实浏览器 BFF 验收失败：\n" + diagnostic[^Math.Min(6000, diagnostic.Length)..]);
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "playwright", "results.json")));
            var stats = report.RootElement.GetProperty("stats");
            Assert.AreEqual(2, stats.GetProperty("expected").GetInt32());
            Assert.AreEqual(0, stats.GetProperty("skipped").GetInt32());
            Assert.AreEqual(0, stats.GetProperty("unexpected").GetInt32());
            passed = true;
        }
        finally
        {
            if (vite is { HasExited: false }) { vite.Kill(entireProcessTree: true); await vite.WaitForExitAsync(); }
            if (viteOutput is not null && viteErrors is not null)
                await File.WriteAllTextAsync(Path.Combine(root, "vite.log"), SafeLog(await viteOutput + "\n" + await viteErrors));
            if (relyingParty is { HasExited: false }) { relyingParty.Kill(entireProcessTree: true); await relyingParty.WaitForExitAsync(); }
            if (relyingPartyOutput is not null && relyingPartyErrors is not null)
                await File.WriteAllTextAsync(Path.Combine(root, "second-rp.log"), SafeLog(await relyingPartyOutput + "\n" + await relyingPartyErrors));
            relyingParty?.Dispose();
            vite?.Dispose();
            // 只清理本测试创建且已停止使用的证书、DP 和 Playwright 临时输出。
            if (passed) Directory.Delete(root, recursive: true);
            else
            {
                File.Delete(pem); File.Delete(key);
                var protection = Path.Combine(root, "keys");
                if (Directory.Exists(protection)) Directory.Delete(protection, recursive: true);
                Console.WriteLine("浏览器失败最小记录：" + Path.Combine(root, "playwright"));
            }
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static Process Start(string repository, Dictionary<string, string> environment, params string[] arguments)
    {
        var info = new ProcessStartInfo("node") { WorkingDirectory = repository, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        foreach (var pair in environment) info.Environment[pair.Key] = pair.Value;
        return Process.Start(info)!;
    }

    private static string SafeLog(string value) => Regex.Replace(value,
        @"(?i)(code|state|nonce|id_token|access_token|refresh_token|client_secret|code_verifier)=([^&\s]+)", "$1=[redacted]");

    private sealed class ProtocolDiagnostics(string log, string thumbprint) : IHttpMessageHandlerBuilderFilter
    {
        private readonly SemaphoreSlim _writer = new(1, 1);
        internal async Task WriteAsync(string message)
        {
            await _writer.WaitAsync();
            try { await File.AppendAllTextAsync(log, message + "\n"); }
            finally { _writer.Release(); }
        }
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            if (builder.Name?.StartsWith("OpenIddict.Client.SystemNetHttp", StringComparison.Ordinal) == true)
            {
                // 在全部宿主配置之后，只给回环测试 RP 的真实 TLS 传输建立精确指纹信任。
                if (builder.PrimaryHandler is HttpClientHandler handler)
                {
                    handler.UseProxy = false;
                    handler.ServerCertificateCustomValidationCallback = (_, remote, _, _) => remote?.Thumbprint == thumbprint;
                }
                else if (builder.PrimaryHandler is SocketsHttpHandler sockets)
                {
                    sockets.UseProxy = false;
                    sockets.SslOptions.RemoteCertificateValidationCallback = (_, remote, _, _) => remote?.GetCertHashString() == thumbprint;
                }
                builder.AdditionalHandlers.Add(new NetworkLog(this));
            }
        };
    }

    private sealed class NetworkLog(ProtocolDiagnostics diagnostics) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            // 只记录方法、路径及传输错误，不记录 query、Cookie、请求/令牌正文。
            var path = request.Method + " " + request.RequestUri?.AbsolutePath;
            try
            {
                var response = await base.SendAsync(request, token);
                await diagnostics.WriteAsync(path + " => " + (int)response.StatusCode);
                return response;
            }
            catch (HttpRequestException error)
            {
                await diagnostics.WriteAsync(path + " => " + SafeLog(error.Message + " " + error.InnerException?.Message));
                throw;
            }
        }
    }
}
