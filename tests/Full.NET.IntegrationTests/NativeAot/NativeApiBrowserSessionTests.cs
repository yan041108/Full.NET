using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Identity;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>Linux 真正原生 API、真实 TLS 与 RP 网络，不使用 TestServer 或令牌替身。</summary>
[TestClass]
public sealed class NativeApiBrowserSessionTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Native_browser_callback_cookie_context_refresh_and_logout_preserve_authority(DatabaseProvider provider)
    {
        var artifact = NativeApiArtifactLocator.RequireArtifact();
        var connectionString = await IdentityBrowserTestSupport.DatabaseAsync(provider);
        var root = Directory.CreateTempSubdirectory("fullnet-native-browser-").FullName;
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var pfx = Path.Combine(root, "tls.pfx"); var pem = Path.Combine(root, "tls.pem");
        const string password = "Native-Browser-Test-Only!";
        await File.WriteAllBytesAsync(pfx, certificate.Export(X509ContentType.Pfx, password));
        await File.WriteAllTextAsync(pem, certificate.ExportCertificatePem());
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var origin = $"https://localhost:{port}";
        var settings = IdentityBrowserTestSupport.Settings();
        settings["Identity:Oidc:Issuer"] = origin + "/identity";
        settings["Identity:Browser:PublicOrigin"] = origin;
        settings["Identity:AccessTokenMinutes"] = "1";
        settings["Identity:AllowedOrigins:0"] = origin;
        settings["DataProtection:KeyRingPath"] = Path.Combine(root, "keys");
        settings["Kestrel:Certificates:Default:Path"] = pfx;
        settings["Kestrel:Certificates:Default:Password"] = password;
        // Linux 原生进程只信任此测试根，不修改宿主机证书存储。
        settings["SSL_CERT_FILE"] = pem;
        try
        {
            await NativeApiMigratorRunner.MigrateAsync(provider, connectionString, default, settings);
            await using var host = await NativeApiProcessHost.StartAsync(artifact, provider, connectionString, settings,
                NativeAotTestTimeouts.ProcessStartup, listenAddress: new Uri(origin + "/"), certificateThumbprint: certificate.Thumbprint);
            using var browser = host.CreateClient("localhost:" + port, certificate.Thumbprint);
            var callback = await IdentityBrowserTestSupport.BeginBrowserLoginAsync(browser, NativeApiE2EAssertions.AdminPassword);
            using (var completed = await browser.GetAsync(callback)) await ExpectAsync(completed, HttpStatusCode.Redirect, host);
            using (var replay = await browser.GetAsync(callback)) await ExpectAsync(replay, HttpStatusCode.BadRequest, host);
            using var state = await browser.GetAsync("/api/v1/identity/browser/session"); await ExpectAsync(state, HttpStatusCode.OK, host);
            using var json = JsonDocument.Parse(await state.Content.ReadAsStringAsync()); Assert.AreEqual(2, json.RootElement.EnumerateObject().Count());
            var csrf = json.RootElement.GetProperty("csrfToken").GetString()!;
            using (var me = await browser.GetAsync("/api/v1/me")) await ExpectAsync(me, HttpStatusCode.OK, host);
            using (var change = new HttpRequestMessage(HttpMethod.Put, "/api/v1/tenancy/context"))
            {
                change.Headers.Add("Origin", origin); change.Headers.Add("X-CSRF-Token", csrf); change.Content = JsonContent.Create(new { tenantId = (Guid?)null });
                using var changed = await browser.SendAsync(change); await ExpectAsync(changed, HttpStatusCode.NoContent, host);
                Assert.AreEqual("", await changed.Content.ReadAsStringAsync());
            }
            await using DbConnection database = provider == DatabaseProvider.SqlServer ? new SqlConnection(connectionString)
                : new MySqlConnection(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
            var version = await database.QuerySingleAsync<long>("SELECT Version FROM fn_identity_browser_session");
            // 一分钟 access 到期前 30 秒进入真实主动刷新；不修改令牌、时钟或保护票据。
            await Task.Delay(TimeSpan.FromSeconds(35));
            using (var refreshed = await browser.GetAsync("/api/v1/identity/browser/session")) await ExpectAsync(refreshed, HttpStatusCode.OK, host);
            Assert.IsTrue(await database.QuerySingleAsync<long>("SELECT Version FROM fn_identity_browser_session") > version);
            using (var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/identity/browser/logout"))
            {
                logout.Headers.Add("Origin", origin); logout.Headers.Add("X-CSRF-Token", csrf);
                using var loggedOut = await browser.SendAsync(logout); await ExpectAsync(loggedOut, HttpStatusCode.NoContent, host);
            }
            using (var denied = await browser.GetAsync("/api/v1/me")) await ExpectAsync(denied, HttpStatusCode.Unauthorized, host);
            Assert.AreEqual(0, await database.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_identity_browser_session"));
            using var left = host.CreateClient("localhost:" + port, certificate.Thumbprint);
            using var right = host.CreateClient("localhost:" + port, certificate.Thumbprint);
            var current = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(left,
                IdentityOidcRelyingPartyFixture.PublicClientId, IdentityOidcRelyingPartyFixture.PublicRedirectUri, null,
                "admin", NativeApiE2EAssertions.AdminPassword, requestOfflineAccess: true);
            var other = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(right,
                IdentityOidcRelyingPartyFixture.PublicClientId, IdentityOidcRelyingPartyFixture.PublicRedirectUri, null,
                "admin", NativeApiE2EAssertions.AdminPassword, requestOfflineAccess: true);
            using (var noBearer = IdentityOidcSessionTestSupport.CreateSessionWriteRequest(HttpMethod.Post,
                "/api/v1/identity/oidc/logout/application", new { clientId = IdentityOidcRelyingPartyFixture.PublicClientId }, origin))
            using (var denied = await left.SendAsync(noBearer)) await ExpectAsync(denied, HttpStatusCode.Unauthorized, host);
            using (var applicationLogout = IdentityOidcSessionTestSupport.CreateSessionWriteRequest(HttpMethod.Post,
                "/api/v1/identity/oidc/logout/application", new { clientId = IdentityOidcRelyingPartyFixture.PublicClientId }, origin))
            {
                applicationLogout.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", current.AccessToken);
                using var response = await left.SendAsync(applicationLogout); await ExpectAsync(response, HttpStatusCode.NoContent, host);
            }
            foreach (var (token, expected) in new[] { (current.AccessToken, HttpStatusCode.Unauthorized), (other.AccessToken, HttpStatusCode.OK) })
            {
                using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
                me.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                using var response = await right.SendAsync(me); await ExpectAsync(response, expected, host);
            }
            foreach (var (refresh, expected) in new[] { (current.RefreshToken, HttpStatusCode.BadRequest), (other.RefreshToken, HttpStatusCode.OK) })
            {
                using var response = await right.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
                { ["grant_type"] = "refresh_token", ["client_id"] = IdentityOidcRelyingPartyFixture.PublicClientId, ["refresh_token"] = refresh! }));
                await ExpectAsync(response, expected, host);
            }
            await host.StopGracefullyAsync(); host.AssertNoFatalMarkersInLogs();
        }
        finally
        {
            Assert.IsTrue(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.Ordinal));
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task ExpectAsync(HttpResponseMessage response, HttpStatusCode expected, NativeApiProcessHost host)
    {
        if (response.StatusCode == expected) return;
        var body = await response.Content.ReadAsStringAsync();
        var log = await File.ReadAllTextAsync(host.LogFilePath);
        Assert.Fail($"Native browser {response.RequestMessage?.RequestUri?.AbsolutePath}: expected {expected}, actual {response.StatusCode}. {body}\n{log[^Math.Min(4000, log.Length)..]}");
    }
}
