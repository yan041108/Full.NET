using System.Net;
using System.Text;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.IntegrationTests.NativeAot;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>正式 MCP 端点必须逐请求拒绝未批准 Origin，不能仅依靠浏览器 CORS。</summary>
[TestClass][DoNotParallelize]
public sealed class McpRealOriginAdmissionTests
{
    [TestMethod][DataRow(DatabaseProvider.SqlServer)][DataRow(DatabaseProvider.MySql)]
    public Task Invalid_origins_are_forbidden_on_actual_tcp_for_authenticated_requests(DatabaseProvider provider) => VerifyAsync(provider, false);

    internal static async Task VerifyAsync(DatabaseProvider provider, bool native)
    {
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var settings = AiAgentRuntimeTestSettings.ForIntegrationTests();
        settings["Tenancy:HostDomains:1"] = "127.0.0.1";
        var stamp = Guid.CreateVersion7().ToString("N");
        settings["Files:Local:RootPath"] = Path.Combine(Path.GetTempPath(), "fullnet-a04-origin-files", stamp);
        settings["DataProtection:KeyRingPath"] = Path.Combine(Path.GetTempPath(), "fullnet-a04-origin-keys", stamp);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        await db.OpenAsync();
        var registry = Environment.GetEnvironmentVariable("FULLNET_A04_ORIGIN_DATABASE_REGISTRY")
            ?? throw new InvalidOperationException("必须准确登记本批数据库。");
        await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new { provider = provider.ToString(), database = db.Database,
            filesRoot = settings["Files:Local:RootPath"], keysRoot = settings["DataProtection:KeyRingPath"] }) + "\n");
        using var factory = new FullNetApiFactory(provider, connection, settings);
        if (!native) factory.UseKestrel(options => options.ListenAnyIP(64215));
        await factory.InitializeAsync();
        await using var api = native ? await NativeApiProcessHost.StartAsync(NativeApiArtifactLocator.RequireArtifact(), provider,
            connection, settings, NativeAotTestTimeouts.ProcessStartup, listenAddress: new Uri("http://0.0.0.0:64215/")) : null;
        using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:64215"), Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.Host = "localhost:64215";
        var token = await NativeApiE2EAssertions.LoginAsync(client, "A04 MCP Origin Kestrel");
        var initial = await Post(null);
        Assert.AreEqual(HttpStatusCode.OK, initial, "无 Origin 的标准非浏览器客户端应保持可用。");
        var allowed = await Post("http://localhost");
        Assert.AreEqual(HttpStatusCode.OK, allowed, "使用现有测试宿主已配置 Origin 的正向对照。");
        var same = await Post("http://localhost:64215");
        Assert.AreEqual(HttpStatusCode.Forbidden, same, "仅同请求 Host 相等不能绕过既有来源白名单。");
        var invalid = await Post("http://attacker.invalid");
        Assert.AreEqual(HttpStatusCode.Forbidden, invalid, "未批准 Origin 的已认证 POST 必须拒绝。");
        Assert.AreEqual(HttpStatusCode.Forbidden, await Post("null"), "不透明 Origin 不能通过。");
        using var get = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ai/mcp");
        get.Headers.Authorization = new("Bearer", token);
        get.Headers.TryAddWithoutValidation("Origin", "http://attacker.invalid");
        get.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
        using var forbiddenGet = await client.SendAsync(get, HttpCompletionOption.ResponseHeadersRead);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenGet.StatusCode, "未批准 Origin 的已认证 GET 也必须拒绝。");
        using var options = new HttpRequestMessage(HttpMethod.Options, "/api/v1/ai/mcp");
        options.Headers.TryAddWithoutValidation("Origin", "http://attacker.invalid");
        options.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
        using var forbiddenPreflight = await client.SendAsync(options);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenPreflight.StatusCode, "非法跨源预检也必须拒绝。");
        Console.WriteLine($"MCP Origin real TCP: provider={provider}, native={native}, protocol=2025-11-25, noOrigin={initial}, approved={allowed}, same={same}, invalidPost={invalid}, invalidGet={forbiddenGet.StatusCode}, invalidOptions={forbiddenPreflight.StatusCode}");

        async Task<HttpStatusCode> Post(string? origin)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/mcp");
            message.Headers.Authorization = new("Bearer", token);
            message.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            if (origin is not null) message.Headers.TryAddWithoutValidation("Origin", origin);
            message.Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"origin-fixture","version":"1"}}}""", Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(message);
            return response.StatusCode;
        }
    }
}

/// <summary>实际 Native API 执行相同双库来源门禁。</summary>
[TestClass][DoNotParallelize]
public sealed class NativeMcpRealOriginAdmissionTests
{
    [TestMethod][DataRow(DatabaseProvider.SqlServer)][DataRow(DatabaseProvider.MySql)]
    public Task Invalid_origins_are_forbidden_on_actual_tcp_for_authenticated_requests(DatabaseProvider provider) =>
        McpRealOriginAdmissionTests.VerifyAsync(provider, true);
}
