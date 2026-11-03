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

/// <summary>同意期间撤销切换权限时，发行必须重新验证当前权限。</summary>
[TestClass, DoNotParallelize]
public sealed class McpTenantDatabaseRaceTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Revoked_host_switch_permission_blocks_pending_tenant_consent(DatabaseProvider provider) => VerifyAsync(provider, false);

    internal static async Task VerifyAsync(DatabaseProvider provider, bool native)
    {
        const string resource = "https://mcp.example.test/api/v1/ai/mcp";
        var registry = Environment.GetEnvironmentVariable("FULLNET_A04_TENANT_NATIVE_RACE_REGISTRY")
            ?? throw new InvalidOperationException("必须提供本批独立 registry。");
        var database = "fullnet_it_" + Guid.CreateVersion7().ToString("N");
        var stamp = Guid.CreateVersion7().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), "fullnet-a04-tenant-native-race", stamp);
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
        // 仅锁定本次登记的隔离库，观察真实授权请求在租户权威读取处等待后再撤权。
        var blockerRoot = provider == DatabaseProvider.MySql ? await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync() : null;
        var blockerConnection = provider == DatabaseProvider.SqlServer ? connection
            : new MySqlConnectionStringBuilder(blockerRoot!) { Database = database, Pooling = false }.ConnectionString;
        await using System.Data.Common.DbConnection blocker = provider == DatabaseProvider.SqlServer
            ? new SqlConnection(blockerConnection) : new MySqlConnection(blockerConnection);
        await using System.Data.Common.DbConnection observer = provider == DatabaseProvider.SqlServer
            ? new SqlConnection(blockerConnection) : new MySqlConnection(blockerConnection);
        await blocker.OpenAsync();
        await observer.OpenAsync();
        await using var transaction = provider == DatabaseProvider.SqlServer ? await blocker.BeginTransactionAsync() : null;
        var blockerId = provider == DatabaseProvider.SqlServer ? await blocker.ExecuteScalarAsync<int>("SELECT @@SPID", transaction: transaction) : 0;
        if (provider == DatabaseProvider.SqlServer)
            await blocker.QuerySingleAsync<Guid>("SELECT Id FROM fn_tenancy_tenant WITH (XLOCK, HOLDLOCK, ROWLOCK) WHERE Id=@Id", new { Id = tenantId }, transaction);
        else
            await blocker.ExecuteAsync("LOCK TABLES fn_tenancy_tenant WRITE");
        var submission = browser.PostAsync("/connect/authorize", new FormUrlEncodedContent(consent));
        try
        {
            var observed = false;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            while (DateTimeOffset.UtcNow < deadline)
            {
                var waiting = provider == DatabaseProvider.SqlServer
                    ? await observer.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.dm_exec_requests WHERE database_id=DB_ID(@Database) AND blocking_session_id=@Blocker", new { Database = database, Blocker = blockerId })
                    : await observer.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.PROCESSLIST WHERE DB=@Database AND INFO LIKE '%fn_tenancy_tenant%' AND STATE LIKE '%lock%'", new { Database = database });
                if (waiting > 0) { observed = true; break; }
                await Task.Delay(100);
            }
            Assert.IsTrue(observed, "必须观察到隔离库的租户读取被本测试阻塞，不能用延时冒充竞态。 ");
            await using System.Data.Common.DbConnection db = provider == DatabaseProvider.SqlServer
                ? new SqlConnection(connection) : new MySqlConnection(connection);
            await db.OpenAsync();
            var removed = await db.ExecuteAsync("""
                DELETE FROM fn_identity_role_permission WHERE RoleId IN
                    (SELECT Id FROM fn_identity_role WHERE Code=@Code AND ScopeKey='host') AND PermissionCode=@Permission
                """, new { Code = "test-" + limited.UserId.ToString("N"), Permission = "tenancy.tenants.switch" });
            Assert.AreEqual(1, removed, "只能撤销此用户的精确切换权限。");
        }
        finally
        {
            if (provider == DatabaseProvider.SqlServer) await transaction!.RollbackAsync();
            else await blocker.ExecuteAsync("UNLOCK TABLES");
        }
        using var denied = await submission;
        Assert.AreEqual(HttpStatusCode.Redirect, denied.StatusCode, "撤权后的同意不得签发授权码。");
        var redirect = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(denied.Headers.Location!.Query);
        Assert.IsFalse(redirect.ContainsKey("code"), "并发撤权后不得发码。");
        Assert.AreEqual("access_denied", redirect["error"].ToString());
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

/// <summary>真实 Native API 验证同意期间撤销权限。</summary>
[TestClass, DoNotParallelize]
public sealed class NativeMcpTenantDatabaseRaceTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Revoked_host_switch_permission_blocks_pending_tenant_consent(DatabaseProvider provider) => McpTenantDatabaseRaceTests.VerifyAsync(provider, true);
}
