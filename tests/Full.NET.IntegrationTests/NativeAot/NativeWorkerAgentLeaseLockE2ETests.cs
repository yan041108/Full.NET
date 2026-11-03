using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Ai;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Caching;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>实际原生 Worker 在模型返回后等待运行根锁，数据库租约到期时不得提交旧进度。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeWorkerAgentLeaseLockE2ETests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Native_worker_does_not_commit_after_locked_lease_expires(DatabaseProvider provider)
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out var artifact, out var reason)) Assert.Fail(reason ?? "缺少 Native Worker");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var responded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var modelServer = builder.Build();
        modelServer.MapPost("/api/chat", async (HttpContext context) =>
        {
            Interlocked.Increment(ref calls);
            using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            entered.TrySetResult();
            await release.Task.WaitAsync(context.RequestAborted);
            context.Response.ContentType = "application/x-ndjson";
            await context.Response.WriteAsync("{\"message\":{\"role\":\"assistant\",\"content\":\"controlled\"},\"done\":true,\"prompt_eval_count\":5,\"eval_count\":2}\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            responded.TrySetResult();
        });
        await modelServer.StartAsync();
        var origin = modelServer.Urls.Single();
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var stamp = Guid.CreateVersion7().ToString("N");
        var files = Path.Combine(Path.GetTempPath(), "fullnet-a06-native-agent-files", stamp);
        var keys = Path.Combine(Path.GetTempPath(), "fullnet-a06-native-agent-keys", stamp);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        await db.OpenAsync();
        var registry = Environment.GetEnvironmentVariable("FULLNET_A06_AGENT_NATIVE_DATABASE_REGISTRY")
            ?? throw new InvalidOperationException("必须准确登记本批数据库。");
        await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new { provider = provider.ToString(), database = db.Database, filesRoot = files, keysRoot = keys }) + "\n");
        var settings = AiAgentRuntimeTestSettings.ForIntegrationTests();
        settings["FullNet:Ai:Network:OllamaAllowedOrigins:0"] = origin;
        settings["Files:Local:RootPath"] = files;
        settings["DataProtection:KeyRingPath"] = keys;
        using var factory = new FullNetApiFactory(provider, connection, settingsOverrides: settings);
        await factory.InitializeAsync();
        var username = "a06-native-lease-" + stamp;
        await factory.CreateHostIdentityAsync(username, [AiModelPermissions.Create, AiModelPermissions.Read, AiAgentRunPermissions.Read, AiAgentRunPermissions.Create], password: FullNetApiFactory.TestPassword);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await CacheBackgroundAuthorityFixture.LoginAsync(client, username));
        using var modelResponse = await client.PostAsJsonAsync("/api/v1/ai/model-configs", new CreateAiModelConfigRequest(null, "a06-native-lease", "ollama", origin, "model", null, null, false, true));
        Assert.AreEqual(HttpStatusCode.Created, modelResponse.StatusCode, await modelResponse.Content.ReadAsStringAsync());
        var model = (await modelResponse.Content.ReadFromJsonAsync<AiModelConfigResponse>())!;
        await AiAgentRunApiAssertions.SeedFreshWorkerHeartbeatAsync(factory);
        using var create = await client.PostAsJsonAsync("/api/v1/ai/agent/runs", new CreateAiAgentRunRequest(Guid.CreateVersion7(), "fullnet-single-text-v1", model.Id, "lock deadline", 100, 100));
        Assert.AreEqual(HttpStatusCode.Accepted, create.StatusCode, await create.Content.ReadAsStringAsync());
        var runId = (await create.Content.ReadFromJsonAsync<CreateAiAgentRunResponse>())!.RunId;
        settings["FullNet:Ai:AgentRuntime:PollMilliseconds"] = "5000";
        await using var worker = await NativeWorkerProcessHost.StartAsync(artifact!, provider, connection, NativeAotTestTimeouts.ProcessStartup, filesRootPath: files, additionalSettings: settings);
        Console.WriteLine($"A06_NATIVE_WORKER_PID={worker.ProcessId}");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(90));
            await using var transaction = await db.BeginTransactionAsync();
            var blockerId = await db.ExecuteScalarAsync<long>(provider == DatabaseProvider.SqlServer ? "SELECT @@SPID" : "SELECT CONNECTION_ID()", transaction: transaction);
            var expires = DateTime.UtcNow.AddSeconds(8);
            Assert.AreEqual(1, await db.ExecuteAsync("UPDATE fn_ai_agent_run SET LeaseExpiresAtUtc=@Expires WHERE Id=@Id",
                new { Id = runId, Expires = expires }, transaction));
            var events = await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_ai_agent_event WHERE RunId=@Id", new { Id = runId }, transaction);
            release.TrySetResult();
            await responded.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // 测试业务账号不具有 performance_schema 诊断权限；只读查询复用现有测试容器管理员连接。
            await using System.Data.Common.DbConnection probe = provider == DatabaseProvider.SqlServer
                ? Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(true, connection)
                : new MySqlConnection(new MySqlConnectionStringBuilder(await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync()) { Pooling = false }.ConnectionString);
            await probe.OpenAsync();
            var waitSql = provider == DatabaseProvider.SqlServer
                ? "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id=@Id"
                : "SELECT COUNT(*) FROM performance_schema.data_lock_waits w JOIN performance_schema.threads t ON w.BLOCKING_THREAD_ID=t.THREAD_ID WHERE t.PROCESSLIST_ID=@Id";
            var deadline = DateTime.UtcNow.AddSeconds(5);
            var waiting = 0L;
            while (DateTime.UtcNow < deadline && waiting == 0)
            {
                waiting = await probe.ExecuteScalarAsync<long>(waitSql, new { Id = blockerId });
                if (waiting == 0) await Task.Delay(100);
            }
            Assert.IsGreaterThan(0L, waiting, "必须实际观测到 Worker 数据库连接等待本测试持有的行锁。");
            var remaining = expires.AddSeconds(1) - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
            await transaction.CommitAsync();
            await Task.Delay(TimeSpan.FromSeconds(1));
            var state = await db.QuerySingleAsync<string>("SELECT StatusKey FROM fn_ai_agent_run WHERE Id=@Id", new { Id = runId });
            var after = await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_ai_agent_event WHERE RunId=@Id", new { Id = runId });
            Assert.AreEqual("running", state, worker.LogFilePath);
            Assert.AreEqual(events, after, "过期所有者不能追加进度事件。");
            Assert.AreEqual(1, Volatile.Read(ref calls), "不得自动重派模型。");
        }
        finally { release.TrySetResult(); }
        await worker.StopGracefullyAsync(TimeSpan.FromSeconds(30));
        worker.AssertNoFatalMarkersInLogs();
        Assert.AreEqual(0, worker.ExitCode);
    }
}
