using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Identity;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.IntegrationTests.NativeAot;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Agents.Mcp;
using Microsoft.IdentityModel.JsonWebTokens;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>正式 OIDC 授权码发行的资源受众必须与 MCP 端点一致。</summary>
[TestClass][DoNotParallelize]
public sealed class McpOidcAudienceAdmissionTests
{
    [TestMethod][DataRow(DatabaseProvider.SqlServer)][DataRow(DatabaseProvider.MySql)]
    public Task Metadata_is_unavailable_without_real_oidc(DatabaseProvider provider) => VerifyDisabledAsync(provider, false);

    internal static async Task VerifyDisabledAsync(DatabaseProvider provider, bool native)
    {
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var stamp = Guid.CreateVersion7().ToString("N");
        var settings = new Dictionary<string, string?>
        {
            ["Identity:Oidc:Enable"] = "false",
            ["FullNet:Ai:Mcp:ResourceIdentifier"] = "https://mcp.example.test/api/v1/ai/mcp",
            ["Files:Local:RootPath"] = Path.Combine(Path.GetTempPath(), "fullnet-a04-discovery-files", stamp),
            ["DataProtection:KeyRingPath"] = Path.Combine(Path.GetTempPath(), "fullnet-a04-discovery-keys", stamp),
        };
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        await db.OpenAsync();
        var registry = Environment.GetEnvironmentVariable("FULLNET_A04_AUDIENCE_DATABASE_REGISTRY")
            ?? throw new InvalidOperationException("必须准确登记本批数据库。");
        await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new { provider = provider.ToString(), database = db.Database,
            filesRoot = settings["Files:Local:RootPath"], keysRoot = settings["DataProtection:KeyRingPath"] }) + "\n");
        using var factory = new FullNetApiFactory(provider, connection, settings);
        await factory.InitializeAsync();
        await using var api = native ? await NativeApiProcessHost.StartAsync(NativeApiArtifactLocator.RequireArtifact(), provider,
            connection, settings, NativeAotTestTimeouts.ProcessStartup, listenAddress: new Uri("http://0.0.0.0:64215/")) : null;
        using var client = native ? api!.CreateClient("localhost:64215") : factory.CreateClientForHost("localhost");
        using var response = await client.GetAsync("/.well-known/oauth-protected-resource/ai/mcp");
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode, "未启用 OIDC 时不应发布虚假授权服务器。");
    }

    [TestMethod][DataRow(DatabaseProvider.SqlServer)][DataRow(DatabaseProvider.MySql)]
    public Task Issued_mcp_audience_is_accepted_and_general_api_audience_is_rejected(DatabaseProvider provider) => VerifyAsync(provider, false);

    internal static async Task VerifyAsync(DatabaseProvider provider, bool native)
    {
        const string resource = "https://mcp.example.test/api/v1/ai/mcp";
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var stamp = Guid.CreateVersion7().ToString("N");
        var settings = new Dictionary<string, string?>(IdentityOidcProtocolAssertions.Settings)
        {
            ["Identity:Oidc:Clients:0:ResourceAudience"] = resource,
            ["Identity:Oidc:Clients:1:ResourceAudience"] = "Full.NET.Api",
            ["Identity:Oidc:Clients:2:ClientId"] = "fixture-oidc-non-uri",
            ["Identity:Oidc:Clients:2:RedirectUris:0"] = "https://localhost:5003/signin-oidc",
            ["Identity:Oidc:Clients:2:Scopes:0"] = "openid",
            ["Identity:Oidc:Clients:2:Scopes:1"] = "profile",
            ["Identity:Oidc:Clients:2:IsFirstParty"] = "true",
            ["Identity:Oidc:Clients:2:ResourceAudience"] = "restricted-api",
            ["FullNet:Ai:Mcp:ResourceIdentifier"] = resource,
            ["Files:Local:RootPath"] = Path.Combine(Path.GetTempPath(), "fullnet-a04-audience-files", stamp),
            ["DataProtection:KeyRingPath"] = Path.Combine(Path.GetTempPath(), "fullnet-a04-audience-keys", stamp),
        };
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        await db.OpenAsync();
        var registry = Environment.GetEnvironmentVariable("FULLNET_A04_AUDIENCE_DATABASE_REGISTRY")
            ?? throw new InvalidOperationException("必须准确登记本批数据库。");
        await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new { provider = provider.ToString(), database = db.Database,
            filesRoot = settings["Files:Local:RootPath"], keysRoot = settings["DataProtection:KeyRingPath"] }) + "\n");
        using var factory = new FullNetApiFactory(provider, connection, settings);
        await factory.InitializeAsync();
        var originalBudgetCount = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
        await using var api = native ? await NativeApiProcessHost.StartAsync(NativeApiArtifactLocator.RequireArtifact(), provider,
            connection, settings, NativeAotTestTimeouts.ProcessStartup, listenAddress: new Uri("http://0.0.0.0:64215/")) : null;
        using var controlBrowser = CreateClient();
        using (var challenge = await controlBrowser.PostAsync("/api/v1/ai/mcp", new StringContent("{}", Encoding.UTF8, "application/json")))
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, challenge.StatusCode);
            var header = challenge.Headers.WwwAuthenticate.ToString();
            Assert.IsTrue(header.Contains("https://mcp.example.test/.well-known/oauth-protected-resource/ai/mcp", StringComparison.Ordinal),
                "401 challenge 必须指向资源的受保护元数据。");
        }
        using (var metadataResponse = await controlBrowser.GetAsync("/.well-known/oauth-protected-resource/ai/mcp"))
        {
            Assert.AreEqual(HttpStatusCode.OK, metadataResponse.StatusCode);
            using var metadata = JsonDocument.Parse(await metadataResponse.Content.ReadAsStringAsync());
            Assert.AreEqual(resource, metadata.RootElement.GetProperty("resource").GetString());
            Assert.AreEqual("https://localhost/identity", metadata.RootElement.GetProperty("authorization_servers")[0].GetString(),
                "资源元数据必须指向实际发行方，而不是会话 API。");
        }
        using (var discovery = await controlBrowser.GetAsync("/identity/.well-known/openid-configuration"))
        {
            Assert.AreEqual(HttpStatusCode.OK, discovery.StatusCode);
            using var metadata = JsonDocument.Parse(await discovery.Content.ReadAsStringAsync());
            Assert.AreEqual("https://localhost/identity", metadata.RootElement.GetProperty("issuer").GetString());
        }
        using (var negativeBrowser = CreateClient())
        {
            var authorize = "/connect/authorize?client_id=" + Uri.EscapeDataString(IdentityOidcRelyingPartyFixture.PublicClientId)
                + "&redirect_uri=" + Uri.EscapeDataString(IdentityOidcRelyingPartyFixture.PublicRedirectUri)
                + "&response_type=code&scope=openid%20profile&state=resource-negative"
                + "&code_challenge=" + new string('A', 43) + "&code_challenge_method=S256";
            foreach (var suffix in new[] { "", "&resource=" + Uri.EscapeDataString("https://wrong.example.test/mcp"),
                "&resource=" + Uri.EscapeDataString(resource) + "&resource=" + Uri.EscapeDataString("https://wrong.example.test/mcp") })
            {
                using var refused = await negativeBrowser.GetAsync(authorize + suffix);
                Assert.IsTrue(refused.StatusCode == HttpStatusCode.BadRequest ||
                    (refused.Headers.Location is not null && refused.Headers.Location.Query.Contains("error=", StringComparison.Ordinal)),
                    "缺失、错误或多值资源不能获得授权码。");
                Assert.IsFalse(refused.Headers.Location?.Query.Contains("code=", StringComparison.Ordinal) ?? false);
            }
            foreach (var tokenResource in new string?[] { null, "https://wrong.example.test/mcp" })
            {
                var pending = await IdentityOidcRelyingPartyFixture.BeginAuthorizationCodeFlowAsync(negativeBrowser,
                    IdentityOidcRelyingPartyFixture.PublicClientId, IdentityOidcRelyingPartyFixture.PublicRedirectUri,
                    "admin", FullNetApiFactory.TestPassword, false, resource: resource);
                var fields = new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["code"] = pending.Code,
                    ["client_id"] = pending.ClientId, ["redirect_uri"] = pending.RedirectUri, ["code_verifier"] = pending.Verifier };
                if (tokenResource is not null) fields["resource"] = tokenResource;
                using var refused = await negativeBrowser.PostAsync("/connect/token", new FormUrlEncodedContent(fields));
                Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode, "换码必须绑定授权的准确资源。");
                await AssertProtocolErrorAsync(refused);
            }
            foreach (var refreshResource in new string?[] { null, "https://wrong.example.test/mcp" })
            {
                var issued = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(negativeBrowser,
                    IdentityOidcRelyingPartyFixture.PublicClientId, IdentityOidcRelyingPartyFixture.PublicRedirectUri,
                    null, "admin", FullNetApiFactory.TestPassword, true, resource: resource);
                Assert.IsFalse(string.IsNullOrWhiteSpace(issued.RefreshToken));
                var fields = new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["client_id"] = IdentityOidcRelyingPartyFixture.PublicClientId,
                    ["refresh_token"] = issued.RefreshToken! };
                if (refreshResource is not null) fields["resource"] = refreshResource;
                using var refused = await negativeBrowser.PostAsync("/connect/token", new FormUrlEncodedContent(fields));
                Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode, "刷新必须绑定授权的准确资源。");
                await AssertProtocolErrorAsync(refused);
            }

            static async Task AssertProtocolErrorAsync(HttpResponseMessage response)
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.IsTrue(json.RootElement.TryGetProperty("error", out var error) && !string.IsNullOrWhiteSpace(error.GetString()),
                    "OIDC 错误必须保留标准 OAuth error，不能被通用状态页替换。");
            }
        }
        var control = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(controlBrowser,
            IdentityOidcRelyingPartyFixture.ConfidentialClientId, IdentityOidcRelyingPartyFixture.ConfidentialRedirectUri,
            IdentityOidcRelyingPartyFixture.ConfidentialClientSecret, "admin", FullNetApiFactory.TestPassword, requestOfflineAccess: false);
        Assert.IsFalse(string.IsNullOrWhiteSpace(control.AccessToken), "同配置的普通 audience 授权码签发对照必须成功。");
        const string ownedTitle = "MCP 受众所有权对照";
        Guid ownedSessionId;
        using (var createModel = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/model-configs"))
        {
            createModel.Headers.Authorization = new("Bearer", control.AccessToken);
            createModel.Content = JsonContent.Create(new CreateAiModelConfigRequest(null, "MCP 对照模型", "ollama", "https://provider.test", "model", null, null, false, true));
            using var modelResponse = await controlBrowser.SendAsync(createModel);
            Assert.AreEqual(HttpStatusCode.Created, modelResponse.StatusCode);
            var model = (await modelResponse.Content.ReadFromJsonAsync<AiModelConfigResponse>())!;
            using var createSession = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/chat/sessions");
            createSession.Headers.Authorization = new("Bearer", control.AccessToken);
            createSession.Content = JsonContent.Create(new CreateAiChatSessionRequest(model.Id, ownedTitle));
            using var sessionResponse = await controlBrowser.SendAsync(createSession);
            Assert.AreEqual(HttpStatusCode.Created, sessionResponse.StatusCode);
            ownedSessionId = (await sessionResponse.Content.ReadFromJsonAsync<AiChatSessionResponse>())!.Id;
        }
        var ownedResource = McpExposurePolicy.SessionSummaryUriTemplate.Replace("{sessionId}", ownedSessionId.ToString("D"), StringComparison.Ordinal);
        using (var nonUriBrowser = CreateClient())
        {
            var nonUri = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(nonUriBrowser,
                "fixture-oidc-non-uri", "https://localhost:5003/signin-oidc", null,
                "admin", FullNetApiFactory.TestPassword, requestOfflineAccess: false);
            Assert.IsFalse(string.IsNullOrWhiteSpace(nonUri.AccessToken), "普通非 URI 受众仍须能通过正式授权码签发。");
            Assert.IsTrue(new JsonWebToken(nonUri.AccessToken).Audiences.Contains("restricted-api", StringComparer.Ordinal));
            using var forbidden = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/mcp");
            forbidden.Headers.Authorization = new("Bearer", nonUri.AccessToken);
            forbidden.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await nonUriBrowser.SendAsync(forbidden);
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode, "非 MCP 受众不能进入 MCP。");
        }
        using var browser = CreateClient();
        var dedicated = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(browser,
            IdentityOidcRelyingPartyFixture.PublicClientId, IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null, "admin", FullNetApiFactory.TestPassword, requestOfflineAccess: true, resource: resource);
        if (string.IsNullOrWhiteSpace(dedicated.AccessToken))
        {
            using var response = JsonDocument.Parse(dedicated.RawTokenResponse);
            var error = response.RootElement.TryGetProperty("error", out var value) ? value.GetString() : "missing_access_token";
            Assert.Fail($"OIDC token endpoint did not issue an access token: {error}.");
        }
        var token = new JsonWebToken(dedicated.AccessToken);
        Assert.IsTrue(token.Audiences.Contains(resource, StringComparer.Ordinal));
        using (var ordinaryRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/identity/menus"))
        {
            ordinaryRequest.Headers.Authorization = new("Bearer", dedicated.AccessToken);
            using var ordinaryResponse = await browser.SendAsync(ordinaryRequest);
            Assert.AreEqual(HttpStatusCode.Unauthorized, ordinaryResponse.StatusCode, "MCP 令牌不得用于普通 API。");
        }
        var observedHandshake = false;
        string? issuedMcpSession = null;
        string? negotiatedRevision = null;
        var dedicatedStatus = await SendAsync(dedicated.AccessToken);
        Assert.AreEqual(HttpStatusCode.OK, dedicatedStatus, "正式发行的准确 MCP audience 应能进入本端点。");
        Assert.AreEqual("2025-11-25", negotiatedRevision, "必须记录真实协商版本，而非由 SDK 版本推断。");
        foreach (var (origin, expected) in new[]
        {
            ("http://localhost", HttpStatusCode.OK),
            ("http://attacker.invalid", HttpStatusCode.Forbidden),
            ("null", HttpStatusCode.Forbidden),
        })
        {
            using var originRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/mcp");
            originRequest.Headers.Authorization = new("Bearer", dedicated.AccessToken);
            originRequest.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            originRequest.Headers.TryAddWithoutValidation("Origin", origin);
            originRequest.Content = new StringContent("""{"jsonrpc":"2.0","id":9,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"origin-audience-fixture","version":"1"}}}""", Encoding.UTF8, "application/json");
            using var originResponse = await browser.SendAsync(originRequest);
            Assert.AreEqual(expected, originResponse.StatusCode, $"专用受众的 Origin 门禁：{origin}");
        }
        using (var malformed = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/mcp"))
        {
            malformed.Headers.Authorization = new("Bearer", dedicated.AccessToken);
            malformed.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            malformed.Content = new StringContent("{\"jsonrpc\":", Encoding.UTF8, "application/json");
            using var response = await browser.SendAsync(malformed);
            var body = await response.Content.ReadAsStringAsync();
            Assert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest
                || (response.StatusCode == HttpStatusCode.OK && body.Contains("\"error\"", StringComparison.Ordinal)),
                "畸形 JSON-RPC 必须返回明确解析错误。");
        }
        if (native)
        {
            using var oversized = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/mcp");
            oversized.Headers.Authorization = new("Bearer", dedicated.AccessToken);
            oversized.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            oversized.Headers.ExpectContinue = true;
            oversized.Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":10,\"method\":\"tools/list\",\"params\":{\"padding\":\""
                + new string('x', 30_100_000) + "\"}}", Encoding.UTF8, "application/json");
            using var response = await browser.SendAsync(oversized);
            Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode, "真实 Kestrel 必须拒绝超限主体。");
        }
        using (var list = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/mcp"))
        {
            list.Headers.Authorization = new("Bearer", dedicated.AccessToken);
            list.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            list.Content = new StringContent("""{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""", Encoding.UTF8, "application/json");
            using var listResponse = await browser.SendAsync(list);
            Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode, "真实专用会话应可查询经当前权限过滤的工具目录。");
            var body = await listResponse.Content.ReadAsStringAsync();
            Assert.IsTrue(body.Contains("\"tools\"", StringComparison.Ordinal), "工具目录不得以协议错误冒充成功。");
            Assert.IsTrue(body.Contains("ai.tools.ping", StringComparison.Ordinal), "当前授权的静态 ping 必须出现在专用会话目录中。");
        }
        using (var call = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/mcp"))
        {
            call.Headers.Authorization = new("Bearer", dedicated.AccessToken);
            call.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            call.Content = new StringContent("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"ai.tools.ping","arguments":{}}}""", Encoding.UTF8, "application/json");
            using var callResponse = await browser.SendAsync(call);
            Assert.AreEqual(HttpStatusCode.OK, callResponse.StatusCode);
            var body = await callResponse.Content.ReadAsStringAsync();
            Assert.IsTrue(body.Contains("ok", StringComparison.Ordinal) && !body.Contains("\"isError\":true", StringComparison.Ordinal),
                "专用资源令牌必须经过当前权限与统一执行器成功执行静态 ping。");
        }
        if (native)
        {
            using var sdkHttp = api!.CreateClient("localhost:64215");
            sdkHttp.DefaultRequestHeaders.Authorization = new("Bearer", dedicated.AccessToken);
            await using var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(api.BaseAddress, "/api/v1/ai/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            }, sdkHttp, ownsHttpClient: false);
            await using var sdk = await McpClient.CreateAsync(transport,
                new ModelContextProtocol.Client.McpClientOptions { ProtocolVersion = "2025-11-25" });
            var sdkTools = await sdk.ListToolsAsync();
            Assert.IsTrue(sdkTools.Any(tool => tool.Name == "ai.tools.ping"), "标准 SDK 必须读取到获准 ping。");
            var sdkPing = await sdk.CallToolAsync("ai.tools.ping", new Dictionary<string, object?>());
            Assert.IsFalse(sdkPing.IsError ?? false, "标准 SDK 必须通过当前会话和统一执行器执行 ping。");
            var summary = await sdk.ReadResourceAsync(ownedResource);
            var summaryText = (summary.Contents[0] as TextResourceContents)?.Text ?? string.Empty;
            using var parsedSummary = JsonDocument.Parse(summaryText);
            Assert.AreEqual(ownedSessionId, parsedSummary.RootElement.GetProperty("sessionId").GetGuid());
            Assert.AreEqual(ownedTitle, parsedSummary.RootElement.GetProperty("title").GetString());
        }
        var limitedName = "mcp-limited-" + stamp;
        await factory.CreateHostIdentityAsync(limitedName, [AiChatPermissions.Read], password: FullNetApiFactory.TestPassword);
        using (var limitedBrowser = CreateClient())
        {
            var limited = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(limitedBrowser,
                IdentityOidcRelyingPartyFixture.PublicClientId, IdentityOidcRelyingPartyFixture.PublicRedirectUri,
                null, limitedName, FullNetApiFactory.TestPassword, requestOfflineAccess: false, resource: resource);
            Assert.IsFalse(string.IsNullOrWhiteSpace(limited.AccessToken), "受限主体必须经过真实 OIDC 签发。");
            using var list = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/mcp");
            list.Headers.Authorization = new("Bearer", limited.AccessToken);
            list.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            if (issuedMcpSession is not null) list.Headers.TryAddWithoutValidation("Mcp-Session-Id", issuedMcpSession);
            list.Content = new StringContent("""{"jsonrpc":"2.0","id":4,"method":"tools/list","params":{}}""", Encoding.UTF8, "application/json");
            using var listResponse = await limitedBrowser.SendAsync(list);
            Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
            var body = await listResponse.Content.ReadAsStringAsync();
            Assert.IsTrue(body.Contains("\"tools\"", StringComparison.Ordinal));
            Assert.IsFalse(body.Contains("ai.tools.ping", StringComparison.Ordinal), "无权限主体不能看到管理员的工具。");
            using var call = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/mcp");
            call.Headers.Authorization = new("Bearer", limited.AccessToken);
            call.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            call.Content = new StringContent("""{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"ai.tools.ping","arguments":{}}}""", Encoding.UTF8, "application/json");
            using var callResponse = await limitedBrowser.SendAsync(call);
            Assert.AreEqual(HttpStatusCode.OK, callResponse.StatusCode);
            Assert.Contains("not authorized", await callResponse.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
            using var foreign = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/mcp");
            foreign.Headers.Authorization = new("Bearer", limited.AccessToken);
            foreign.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            foreign.Content = JsonContent.Create(new { jsonrpc = "2.0", id = 6, method = "resources/read", @params = new { uri = ownedResource } });
            using var foreignResponse = await limitedBrowser.SendAsync(foreign);
            Assert.AreEqual(HttpStatusCode.OK, foreignResponse.StatusCode);
            var foreignBody = await foreignResponse.Content.ReadAsStringAsync();
            Assert.Contains("not authorized", foreignBody, StringComparison.OrdinalIgnoreCase);
            Assert.IsFalse(foreignBody.Contains(ownedTitle, StringComparison.Ordinal), "同权限的其他用户不能读取所有者摘要。");
        }
        var mutableName = "mcp-revoked-" + stamp;
        var mutableUser = await factory.CreateHostIdentityAsync(mutableName, [AiAgentToolPermissions.CatalogRead],
            password: FullNetApiFactory.TestPassword);
        using (var mutableBrowser = CreateClient())
        {
            var mutable = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(mutableBrowser,
                IdentityOidcRelyingPartyFixture.PublicClientId, IdentityOidcRelyingPartyFixture.PublicRedirectUri,
                null, mutableName, FullNetApiFactory.TestPassword, requestOfflineAccess: false, resource: resource);
            Assert.IsFalse(string.IsNullOrWhiteSpace(mutable.AccessToken));
            Assert.Contains("ai.tools.ping", await ListMutableAsync(), StringComparison.Ordinal);
            var removed = await db.ExecuteAsync("""
                DELETE FROM fn_identity_role_permission WHERE RoleId IN
                    (SELECT Id FROM fn_identity_role WHERE Code=@Code AND ScopeKey='host') AND PermissionCode=@Permission
                """, new { Code = "test-" + mutableUser.UserId.ToString("N"), Permission = AiAgentToolPermissions.CatalogRead });
            Assert.AreEqual(1, removed, "测试夹具只能移除目标用户的精确权限。");
            Assert.AreEqual(HttpStatusCode.OK, await SendAsync(mutable.AccessToken), "权限行变化不能伪装成会话撤销。");
            Assert.IsFalse((await ListMutableAsync()).Contains("ai.tools.ping", StringComparison.Ordinal),
                "同一有效专用令牌必须在当前权限移除后失去 ping。");

            async Task<string> ListMutableAsync()
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/mcp");
                request.Headers.Authorization = new("Bearer", mutable.AccessToken);
                request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
                request.Content = new StringContent("""{"jsonrpc":"2.0","id":7,"method":"tools/list","params":{}}""", Encoding.UTF8, "application/json");
                using var response = await mutableBrowser.SendAsync(request);
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                return await response.Content.ReadAsStringAsync();
            }
        }
        Assert.IsFalse(string.IsNullOrWhiteSpace(dedicated.RefreshToken));
        string refreshed;
        using (var refreshResponse = await browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = IdentityOidcRelyingPartyFixture.PublicClientId,
            ["refresh_token"] = dedicated.RefreshToken!,
            ["resource"] = resource,
        })))
        {
            Assert.AreEqual(HttpStatusCode.OK, refreshResponse.StatusCode, "专用资源授权必须能使用正式刷新流程。");
            using var refreshedJson = JsonDocument.Parse(await refreshResponse.Content.ReadAsStringAsync());
            refreshed = refreshedJson.RootElement.GetProperty("access_token").GetString()!;
            Assert.IsTrue(new JsonWebToken(refreshed).Audiences.Contains(resource, StringComparer.Ordinal));
            Assert.AreEqual(HttpStatusCode.OK, await SendAsync(refreshed), "刷新后的专用受众必须仍可访问 MCP。");
        }
        using var otherBrowser = CreateClient();
        var generic = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(otherBrowser,
            IdentityOidcRelyingPartyFixture.ConfidentialClientId, IdentityOidcRelyingPartyFixture.ConfidentialRedirectUri,
            IdentityOidcRelyingPartyFixture.ConfidentialClientSecret, "admin", FullNetApiFactory.TestPassword, requestOfflineAccess: false);
        Assert.IsTrue(new JsonWebToken(generic.AccessToken).Audiences.Contains("Full.NET.Api", StringComparer.Ordinal));
        Assert.AreEqual(HttpStatusCode.Unauthorized, await SendAsync(generic.AccessToken), "通用 API 受众不能自动获得 MCP 资格。");
        var applicationSessionId = IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(dedicated.AccessToken,
            FullNetIdentityClaimTypes.ApplicationSessionId);
        Assert.IsTrue(Guid.TryParse(applicationSessionId, out var sessionId));
        using (var revoke = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/identity/online-sessions/{sessionId:D}/revoke"))
        {
            revoke.Headers.Authorization = new("Bearer", generic.AccessToken);
            revoke.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await otherBrowser.SendAsync(revoke);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, "正式在线会话撤销必须成功。");
        }
        Assert.AreEqual(HttpStatusCode.Unauthorized, await SendAsync(refreshed), "撤销后的专用令牌不得继续访问 MCP。");
        var hostToken = await NativeApiE2EAssertions.LoginAsync(browser, "A04 MCP tenant boundary");
        string tenantToken;
        using (var available = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenancy/available"))
        {
            available.Headers.Authorization = new("Bearer", hostToken);
            using var response = await browser.SendAsync(available);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            using var tenants = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var acme = tenants.RootElement.EnumerateArray().Single(item => item.GetProperty("identifier").GetString() == "acme");
            var tenantId = acme.GetProperty("id").GetGuid();
            using var change = new HttpRequestMessage(HttpMethod.Put, "/api/v1/tenancy/context");
            change.Headers.Authorization = new("Bearer", hostToken);
            change.Content = JsonContent.Create(new { tenantId });
            using var changed = await browser.SendAsync(change);
            Assert.AreEqual(HttpStatusCode.OK, changed.StatusCode);
            using var body = JsonDocument.Parse(await changed.Content.ReadAsStringAsync());
            tenantToken = body.RootElement.GetProperty("accessToken").GetString()!;
        }
        Assert.AreEqual(HttpStatusCode.Unauthorized, await SendAsync(tenantToken), "普通 Tenant 令牌不得取得 MCP 专用受众资格。");
        Assert.AreEqual(originalBudgetCount, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"),
            "创建静态对照及读取摘要不得产生模型预算或派发。");
        Console.WriteLine($"MCP OIDC resource audience: provider={provider}, native={native}, dedicated={dedicatedStatus}, protocol={negotiatedRevision}, sessionIssued={issuedMcpSession is not null}; actual authorization-code issuance.");

        HttpClient CreateClient() => native ? api!.CreateClient("localhost:64215") : factory.CreateClientForHost("localhost");

        async Task<HttpStatusCode> SendAsync(string accessToken)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/mcp");
            message.Headers.Authorization = new("Bearer", accessToken);
            message.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            message.Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"audience-fixture","version":"1"}}}""", Encoding.UTF8, "application/json");
            using var response = await browser.SendAsync(message);
            if (!observedHandshake && response.StatusCode == HttpStatusCode.OK)
            {
                observedHandshake = true;
                issuedMcpSession = response.Headers.TryGetValues("Mcp-Session-Id", out var ids) ? ids.SingleOrDefault() : null;
                var body = await response.Content.ReadAsStringAsync();
                if (body.StartsWith("event:", StringComparison.Ordinal) || body.StartsWith("data:", StringComparison.Ordinal))
                    body = body.Split('\n').Single(line => line.StartsWith("data:", StringComparison.Ordinal))[5..].Trim();
                using var handshake = JsonDocument.Parse(body);
                negotiatedRevision = handshake.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString();
            }
            return response.StatusCode;
        }
    }
}

/// <summary>在实际 Native API 上核验授权码发行及资源隔离。</summary>
[TestClass][DoNotParallelize]
public sealed class NativeMcpOidcAudienceAdmissionTests
{
    [TestMethod][DataRow(DatabaseProvider.SqlServer)][DataRow(DatabaseProvider.MySql)]
    public Task Metadata_is_unavailable_without_real_oidc(DatabaseProvider provider) =>
        McpOidcAudienceAdmissionTests.VerifyDisabledAsync(provider, true);

    [TestMethod][DataRow(DatabaseProvider.SqlServer)][DataRow(DatabaseProvider.MySql)]
    public Task Issued_mcp_audience_is_accepted_and_general_api_audience_is_rejected(DatabaseProvider provider) =>
        McpOidcAudienceAdmissionTests.VerifyAsync(provider, true);
}
