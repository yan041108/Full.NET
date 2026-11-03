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
using Microsoft.Extensions.DependencyInjection;
using Full.NET.Abstractions.Tenancy;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>受控暂停权威租户读取，验证租户停用后的换码边界。</summary>
[TestClass, DoNotParallelize]
public sealed class McpTenantDisableRaceTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Disabled_tenant_after_authority_read_cannot_exchange_code(DatabaseProvider provider) => VerifyAsync(provider, false);

    internal static async Task VerifyAsync(DatabaseProvider provider, bool native)
    {
        const string resource = "https://mcp.example.test/api/v1/ai/mcp";
        var registry = Environment.GetEnvironmentVariable("FULLNET_A04_TENANT_DISABLE_RACE_REGISTRY")
            ?? throw new InvalidOperationException("必须提供本批独立 registry。");
        var database = "fullnet_it_" + Guid.CreateVersion7().ToString("N");
        var stamp = Guid.CreateVersion7().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), "fullnet-a04-tenant-disable-race", stamp);
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
        var gate = new TenantDisableResolutionGate();
        using var factory = new FullNetApiFactory(provider, connection, settings, configureTestServices: services =>
        {
            var descriptor = services.Last(d => d.ServiceType == typeof(IActiveTenantContextResolver));
            var original = descriptor.ImplementationFactory ?? throw new InvalidOperationException("原租户解析注册必须为工厂。");
            services.Remove(descriptor);
            services.AddScoped<IActiveTenantContextResolver>(sp => new GatedDisabledTenantResolver((IActiveTenantContextResolver)original(sp), gate));
        });
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
        await using var db = provider == DatabaseProvider.SqlServer
            ? (System.Data.Common.DbConnection)new SqlConnection(connection)
            : new MySqlConnection(connection);
        await db.OpenAsync();
        gate.Arm();
        var submission = browser.PostAsync("/connect/authorize", new FormUrlEncodedContent(consent));
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var changed = await db.ExecuteAsync("UPDATE fn_tenancy_tenant SET IsActive=0 WHERE Identifier=@Identifier", new { Identifier = "acme" });
            Assert.AreEqual(1, changed, "只停用本批隔离库目标租户。");
        }
        finally { gate.Release.TrySetResult(); }
        using var response = await submission;
        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        if (query.TryGetValue("code", out var issuedCode))
        {
            using var tokenResponse = await browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["client_id"] = IdentityOidcRelyingPartyFixture.PublicClientId,
                ["redirect_uri"] = IdentityOidcRelyingPartyFixture.PublicRedirectUri,
                ["code_verifier"] = verifier, ["code"] = issuedCode.ToString(), ["resource"] = resource,
            }));
            Console.WriteLine($"目标租户停用后授权码已发出；换码状态 {(int)tokenResponse.StatusCode}。");
            Assert.IsFalse(tokenResponse.IsSuccessStatusCode, "租户停用后不得将竞态授权码换为令牌。");
            using var body = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
            Assert.AreEqual("invalid_grant", body.RootElement.GetProperty("error").GetString());
        }
        else
        {
            Console.WriteLine("目标租户停用后未签发授权码。");
            Assert.AreEqual("access_denied", query["error"].ToString());
        }
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

internal sealed class TenantDisableResolutionGate
{
    private int _armed;
    internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal void Arm() => Interlocked.Exchange(ref _armed, 1);
    internal bool Take() => Interlocked.Exchange(ref _armed, 0) == 1;
}

internal sealed class GatedDisabledTenantResolver(IActiveTenantContextResolver inner, TenantDisableResolutionGate gate) : IActiveTenantContextResolver
{
    public Task<IReadOnlyDictionary<Guid, TenantContext>> ResolveActiveByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) => inner.ResolveActiveByIdsAsync(ids, cancellationToken);
    public async Task<TenantContext?> ResolveActiveByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await inner.ResolveActiveByIdAsync(id, cancellationToken);
        if (gate.Take())
        {
            gate.Reached.TrySetResult();
            await gate.Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        return result;
    }
}