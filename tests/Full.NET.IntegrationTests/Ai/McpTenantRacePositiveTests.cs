using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Identity;
using Full.NET.IntegrationTests.NativeAot;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.JsonWebTokens;
using MySqlConnector;
using Microsoft.AspNetCore.Hosting;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>验证持锁授权不会拒绝仍有权限的租户发行。</summary>
[TestClass, DoNotParallelize]
public sealed class McpTenantRacePositiveTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Authorized_tenant_can_issue_and_use_token(DatabaseProvider provider) => VerifyAsync(provider, false);

    internal static async Task VerifyAsync(DatabaseProvider provider, bool native)
    {
        const string resource = "https://mcp.example.test/api/v1/ai/mcp";
        var registry = Environment.GetEnvironmentVariable("FULLNET_A04_TENANT_RACE_POSITIVE_REGISTRY")
            ?? throw new InvalidOperationException("必须提供本批独立 registry。");
        var database = "fullnet_it_" + Guid.CreateVersion7().ToString("N");
        var stamp = Guid.CreateVersion7().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), "fullnet-a04-tenant-race-positive", stamp);
        var keys = Path.Combine(directory, "keys");
        var files = Path.Combine(directory, "files");
        var otherIdentifier = "other-" + Guid.CreateVersion7().ToString("N")[..10];
        var otherDomain = otherIdentifier + ".localhost";
        await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new { provider = provider.ToString(), database, directory, keys, files }) + "\n");
        Directory.CreateDirectory(directory);
        string connection;
        if (provider == DatabaseProvider.SqlServer)
        {
            var master = await SharedDatabaseFixture.GetSqlServerMasterConnectionStringAsync();
            await using var admin = new SqlConnection(master);
            await admin.OpenAsync();
            Assert.AreEqual(0, await admin.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.databases WHERE name=@Name", new { Name = database }));
            await admin.ExecuteAsync($"CREATE DATABASE [{database}]");
            connection = new SqlConnectionStringBuilder(master) { InitialCatalog = database, Pooling = false }.ConnectionString;
        }
        else
        {
            var root = await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync();
            await using var admin = new MySqlConnection(new MySqlConnectionStringBuilder(root) { Database = "mysql", Pooling = false }.ConnectionString);
            await admin.OpenAsync();
            Assert.AreEqual(0, await admin.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME=@Name", new { Name = database }));
            await admin.ExecuteAsync($"CREATE DATABASE `{database}`");
            await admin.ExecuteAsync($"GRANT ALL PRIVILEGES ON `{database}`.* TO 'fullnet'@'%'");
            connection = new MySqlConnectionStringBuilder(root) { UserID = "fullnet", Database = database, Pooling = false }.ConnectionString;
        }
        var settings = new Dictionary<string, string?>(IdentityOidcProtocolAssertions.Settings)
        {
            ["Identity:Oidc:Clients:0:ResourceAudience"] = resource,
            ["FullNet:Ai:Mcp:ResourceIdentifier"] = resource,
            ["Files:Local:RootPath"] = files,
            ["DataProtection:KeyRingPath"] = keys,
        };
        // 中心 Cookie 要求 Secure；本批使用独立证书及精确指纹校验，不放宽 TLS。
        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddDnsName(otherDomain);
        names.AddIpAddress(IPAddress.Loopback);
        certificateRequest.CertificateExtensions.Add(names.Build());
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        var certificatePath = Path.Combine(directory, "server.pfx");
        var certificatePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        await File.WriteAllBytesAsync(certificatePath, certificate.Export(X509ContentType.Pfx, certificatePassword));
        settings["Kestrel:Certificates:Default:Path"] = certificatePath;
        settings["Kestrel:Certificates:Default:Password"] = certificatePassword;
        using var factory = new FullNetApiFactory(provider, connection, settings);
        if (!native) factory.UseKestrel(options => options.ListenAnyIP(64215, endpoint => endpoint.UseHttps(certificate)));
        await factory.InitializeAsync();
        await using var api = native ? await NativeApiProcessHost.StartAsync(NativeApiArtifactLocator.RequireArtifact(), provider,
            connection, settings, NativeAotTestTimeouts.ProcessStartup, listenAddress: new Uri("https://0.0.0.0:64215/"), certificateThumbprint: certificate.Thumbprint) : null;
        using var control = Browser();
        var hostToken = await NativeApiE2EAssertions.LoginAsync(control, "A04 tenant resource repro");
        using var available = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenancy/available");
        available.Headers.Authorization = new("Bearer", hostToken);
        using var tenantsResponse = await control.SendAsync(available);
        Assert.AreEqual(HttpStatusCode.OK, tenantsResponse.StatusCode);
        using var tenants = JsonDocument.Parse(await tenantsResponse.Content.ReadAsStringAsync());
        var tenantId = tenants.RootElement.EnumerateArray().Single(item => item.GetProperty("identifier").GetString() == "acme").GetProperty("id").GetGuid();
        using var provision = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tenancy/tenants");
        provision.Headers.Authorization = new("Bearer", hostToken);
        provision.Content = JsonContent.Create(new { identifier = otherIdentifier, name = "Other tenant", domain = otherDomain });
        using var provisionResult = await control.SendAsync(provision);
        Assert.AreEqual(HttpStatusCode.Created, provisionResult.StatusCode);
        using var otherBody = JsonDocument.Parse(await provisionResult.Content.ReadAsStringAsync());
        var otherTenantId = otherBody.RootElement.GetProperty("id").GetGuid();
        var limitedName = "tenant-switch-" + stamp;
        var limited = await factory.CreateHostIdentityAsync(limitedName, ["tenancy.tenants.switch"], password: FullNetApiFactory.TestPassword);
        using var browser = Browser();
        var verifier = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var state = Guid.CreateVersion7().ToString("N");
        var nonce = Guid.CreateVersion7().ToString("N");
        var authorize = "/connect/authorize?client_id=" + Uri.EscapeDataString(IdentityOidcRelyingPartyFixture.PublicClientId)
            + "&redirect_uri=" + Uri.EscapeDataString(IdentityOidcRelyingPartyFixture.PublicRedirectUri)
            + "&response_type=code&scope=openid%20profile%20offline_access&state=" + state + "&nonce=" + nonce
            + "&code_challenge=" + challenge + "&code_challenge_method=S256&resource=" + Uri.EscapeDataString(resource)
            + "&fullnet_tenant_id=" + tenantId.ToString("D");
        using var login = await browser.GetAsync(authorize);
        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
        var fields = IdentityBrowserTestSupport.Hidden(await login.Content.ReadAsStringAsync());
        fields["username"] = limitedName;
        fields["password"] = FullNetApiFactory.TestPassword;
        using var loggedIn = await browser.PostAsync("/connect/authorize", new FormUrlEncodedContent(fields));
        Assert.AreEqual(HttpStatusCode.OK, loggedIn.StatusCode, "租户授权须先展示独立同意页。");
        var html = await loggedIn.Content.ReadAsStringAsync();
        StringAssert.Contains(html, tenantId.ToString("D"));
        StringAssert.Contains(html, resource);
        var consent = IdentityBrowserTestSupport.Hidden(html);
        Assert.IsTrue(consent.ContainsKey("fullnet_tenant_consent"), "必须收到包含绑定凭据的真实同意页。");
        consent["consent"] = "accept";
        using var accepted = await browser.PostAsync("/connect/authorize", new FormUrlEncodedContent(consent));
        Assert.AreEqual(HttpStatusCode.Redirect, accepted.StatusCode);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(accepted.Headers.Location!.Query);
        var code = query["code"].ToString();
        Assert.IsFalse(string.IsNullOrEmpty(code), "有切换权限的用户必须取得授权码。");
        var issued = await IdentityOidcRelyingPartyFixture.ExchangeAuthorizationCodeAsync(browser, code, verifier,
            IdentityOidcRelyingPartyFixture.PublicClientId, IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null, expectedNonce: nonce, resource: resource);
        var jwt = new JsonWebToken(issued.AccessToken);
        Assert.AreEqual(tenantId, Guid.Parse(jwt.GetClaim(FullNetIdentityClaimTypes.TenantId).Value));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/mcp");
        request.Headers.Authorization = new("Bearer", issued.AccessToken);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"positive-probe","version":"1"}}}""", Encoding.UTF8, "application/json");
        using var response = await browser.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        HttpClient Browser()
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true };
            handler.ServerCertificateCustomValidationCallback = (_, presented, _, errors) =>
                presented is not null && (errors & SslPolicyErrors.RemoteCertificateNameMismatch) == 0
                && string.Equals(presented.Thumbprint, certificate.Thumbprint, StringComparison.OrdinalIgnoreCase);
            var client = new HttpClient(handler)
            { BaseAddress = new Uri("https://127.0.0.1:64215"), Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.Host = "localhost:64215";
            return client;
        }
    }
}

/// <summary>真实 Native API 验证持锁授权的正常发行。</summary>
[TestClass, DoNotParallelize]
public sealed class NativeMcpTenantRacePositiveTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Authorized_tenant_can_issue_and_use_token(DatabaseProvider provider) => McpTenantRacePositiveTests.VerifyAsync(provider, true);
}
