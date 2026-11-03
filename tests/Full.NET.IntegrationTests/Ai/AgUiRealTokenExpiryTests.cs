using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.IntegrationTests.NativeAot;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Calendar.Contracts;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.IdentityModel.Tokens;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>正式签发的短期 JWT 在真实 TCP 长连接中到期，不恢复或执行原 Run。</summary>
[TestClass][DoNotParallelize]
public sealed class AgUiRealTokenExpiryTests
{
    [TestMethod][DataRow(DatabaseProvider.SqlServer)][DataRow(DatabaseProvider.MySql)]
    public Task Stream_stops_when_the_original_signed_token_expires(DatabaseProvider provider) => VerifyAsync(provider, false);

    internal static async Task VerifyAsync(DatabaseProvider provider, bool native)
    {
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var settings = AiAgentRuntimeTestSettings.ForIntegrationTests();
        settings["Identity:AccessTokenMinutes"] = "1";
        settings["Tenancy:HostDomains:1"] = "127.0.0.1";
        var stamp = Guid.CreateVersion7().ToString("N");
        settings["Files:Local:RootPath"] = Path.Combine(Path.GetTempPath(), "fullnet-a04-expiry-files", stamp);
        settings["DataProtection:KeyRingPath"] = Path.Combine(Path.GetTempPath(), "fullnet-a04-expiry-keys", stamp);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        await db.OpenAsync();
        var registry = Environment.GetEnvironmentVariable("FULLNET_A04_EXPIRY_DATABASE_REGISTRY")
            ?? throw new InvalidOperationException("必须准确登记本批数据库。");
        await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new
        { provider = provider.ToString(), database = db.Database, filesRoot = settings["Files:Local:RootPath"], keysRoot = settings["DataProtection:KeyRingPath"] }) + "\n");
        using var factory = new FullNetApiFactory(provider, connection, settings);
        if (!native) factory.UseKestrel(options => options.ListenAnyIP(64215));
        await factory.InitializeAsync();
        await using var api = native ? await NativeApiProcessHost.StartAsync(NativeApiArtifactLocator.RequireArtifact(), provider,
            connection, settings, NativeAotTestTimeouts.ProcessStartup, listenAddress: new Uri("http://0.0.0.0:64215/")) : null;
        using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:64215"), Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.Host = "localhost";
        var token = await NativeApiE2EAssertions.LoginAsync(client, "A04 expiry Kestrel");
        var model = await Send<AiModelConfigResponse>(HttpMethod.Post, "/api/v1/ai/model-configs", token,
            new CreateAiModelConfigRequest(null, "过期验证元数据", "ollama", "https://provider.test", "metadata", null, null, false, true), HttpStatusCode.Created);
        var now = DateTimeOffset.UtcNow;
        var schedule = await Send<PersonalScheduleResponse>(HttpMethod.Post, "/api/v1/calendar/my-personal-schedules", token,
            new CreatePersonalScheduleRequest("PRIVATE_EXPIRY_TARGET", now, now.AddMinutes(10)), HttpStatusCode.Created);
        await AiAgentRunApiAssertions.SeedFreshWorkerHeartbeatAsync(factory);
        var run = (await Send<CreateAiAgentRunResponse>(HttpMethod.Post, "/api/v1/ai/agent/runs", token,
            new CreateAiAgentRunRequest(Guid.CreateVersion7(), "fullnet-personal-schedule-complete-v1", model.Id, "", 1, 1,
                PersonalScheduleTarget: new(schedule.Id, schedule.Version)), HttpStatusCode.Accepted)).RunId;
        var originalEvents = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_agent_event WHERE RunId=@Id", new { Id = run });
        using var claims = JsonDocument.Parse(Base64UrlEncoder.Decode(token.Split('.')[1]));
        var expires = claims.RootElement.GetProperty("exp").GetInt64();
        Assert.IsTrue(DateTimeOffset.UtcNow.ToUnixTimeSeconds() < expires);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/ai/agent/runs/{run}/events/stream");
        request.Headers.Authorization = new("Bearer", token);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var stream = new StreamReader(await response.Content.ReadAsStreamAsync(deadline.Token));
        var sawSnapshot = false;
        var sawDenied = false;
        while (await stream.ReadLineAsync(deadline.Token) is { } line)
        {
            if (line == "event: STATE_SNAPSHOT") sawSnapshot = true;
            if (line.StartsWith("data: ", StringComparison.Ordinal) && line.Contains("authorization.permission_denied", StringComparison.Ordinal))
            { sawDenied = true; break; }
        }
        Assert.IsTrue(sawSnapshot, "过期前必须实际收到当前运行快照。");
        Assert.IsTrue(sawDenied, "过期后必须结束流并返回安全拒绝码。");
        Assert.IsTrue(DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expires);
        Assert.AreEqual(originalEvents, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_agent_event WHERE RunId=@Id", new { Id = run }));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_schedule_completion_intent"));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
        Console.WriteLine($"Real signed JWT expired during TCP stream: provider={provider}, native={native}, expiry={expires}; no tool or model dispatch.");

        async Task<T> Send<T>(HttpMethod method, string path, string accessToken, object? body, HttpStatusCode expected)
        {
            using var message = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
            message.Headers.Authorization = new("Bearer", accessToken);
            using var result = await client.SendAsync(message);
            Assert.AreEqual(expected, result.StatusCode, await result.Content.ReadAsStringAsync());
            return (await result.Content.ReadFromJsonAsync<T>())!;
        }
    }
}

/// <summary>正式 Native API 使用相同真实签名令牌和双库核验。</summary>
[TestClass][DoNotParallelize]
public sealed class NativeAgUiRealTokenExpiryTests
{
    [TestMethod][DataRow(DatabaseProvider.SqlServer)][DataRow(DatabaseProvider.MySql)]
    public Task Stream_stops_when_the_original_signed_token_expires(DatabaseProvider provider) => AgUiRealTokenExpiryTests.VerifyAsync(provider, true);
}
