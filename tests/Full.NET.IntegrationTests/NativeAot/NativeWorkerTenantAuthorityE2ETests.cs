using System.Collections.Concurrent;
using System.Diagnostics;
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

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>原生 API 创建租户运行；原生 Worker 逐次重查租户准入并在派发模型之前拒绝停用租户。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeWorkerTenantAuthorityE2ETests
{
    [TestMethod]
    public Task SqlServer_native_worker_rejects_disabled_tenant_before_model_dispatch() => VerifyAsync(DatabaseProvider.SqlServer);

    [TestMethod]
    public Task MySql_native_worker_rejects_disabled_tenant_before_model_dispatch() => VerifyAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out var workerArtifact, out var workerReason)) Assert.Inconclusive(workerReason ?? "Native Worker artifact unavailable.");
        if (!NativeApiArtifactLocator.TryResolve(out var apiArtifact, out var apiReason)) Assert.Inconclusive(apiReason ?? "Native API artifact unavailable.");
        var received = new ConcurrentQueue<string>();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var providerHost = builder.Build();
        providerHost.MapPost("/api/chat", async (HttpContext context) =>
        {
            using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            received.Enqueue(body.RootElement.GetProperty("messages").EnumerateArray()
                .Single(message => message.GetProperty("role").GetString() == "user").GetProperty("content").GetString()!);
            context.Response.ContentType = "application/x-ndjson";
            await context.Response.WriteAsync("{\"message\":{\"role\":\"assistant\",\"content\":\"native-ok\"},\"done\":true,\"prompt_eval_count\":5,\"eval_count\":2}\n", context.RequestAborted);
        });
        await providerHost.StartAsync();
        var origin = providerHost.Urls.Single();
        var settings = AiAgentRuntimeTestSettings.ForIntegrationTests();
        settings["FullNet:Ai:Network:OllamaAllowedOrigins:0"] = origin;
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection, settingsOverrides: settings);
        await factory.InitializeAsync();
        await CacheBackgroundAuthorityFixture.EnsureAnotherActiveTenantAsync(factory);
        var username = $"native-authority-control-{Guid.CreateVersion7():N}";
        await factory.CreateHostIdentityAsync(username,
            [AiModelPermissions.Create, AiModelPermissions.Read, AiAgentRunPermissions.Read, AiAgentRunPermissions.Create],
            password: FullNetApiFactory.TestPassword);
        await using var api = await NativeApiProcessHost.StartAsync(apiArtifact, provider, connection, settings, NativeAotTestTimeouts.ProcessStartup);
        using var controlClient = api.CreateClient();
        controlClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await CacheBackgroundAuthorityFixture.LoginAsync(controlClient, username));
        using var tenantClient = api.CreateClient();
        var hostToken = await CacheBackgroundAuthorityFixture.LoginAsync(tenantClient, "admin");
        var tenant = await CacheBackgroundAuthorityFixture.GetAcmeTenantAsync(tenantClient, hostToken);
        var tenantToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(tenantClient, hostToken, tenant.Id);
        tenantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantToken);
        await AiAgentRunApiAssertions.SeedFreshWorkerHeartbeatAsync(factory);
        var controlId = await CreateRunAsync(controlClient, "host-authority-control");
        var deniedId = await CreateRunAsync(tenantClient, "disabled-tenant-must-not-dispatch", tenant.Id);

        // 不重新登录或撤销会话；只提交租户停用，再启动实际原生 Worker 领取两条运行。
        await CacheBackgroundAuthorityFixture.SetActiveAsync(factory, tenant.Id, false);
        await using var worker = await NativeWorkerProcessHost.StartAsync(workerArtifact, provider, connection,
            NativeAotTestTimeouts.ProcessStartup, additionalSettings: new Dictionary<string, string?>
            {
                ["FullNet:Ai:AgentRuntime:PollMilliseconds"] = "250",
                ["FullNet:Ai:Network:OllamaAllowedOrigins:0"] = origin,
            });
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        var timer = Stopwatch.StartNew();
        string? denied = null, control = null, deniedBudget = null, controlBudget = null;
        while (timer.Elapsed < TimeSpan.FromSeconds(60))
        {
            denied = await db.QuerySingleAsync<string>("SELECT StatusKey FROM fn_ai_agent_run WHERE Id=@Id", new { Id = deniedId });
            control = await db.QuerySingleAsync<string>("SELECT StatusKey FROM fn_ai_agent_run WHERE Id=@Id", new { Id = controlId });
            deniedBudget = await db.QuerySingleAsync<string>("SELECT Outcome FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = deniedId });
            controlBudget = await db.QuerySingleAsync<string>("SELECT Outcome FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = controlId });
            if (denied == "authorization_required" && deniedBudget == "cancelled" && control == "completed" && controlBudget == "succeeded") break;
            await Task.Delay(100);
        }
        Assert.AreEqual("authorization_required", denied, worker.LogFilePath);
        Assert.AreEqual("cancelled", deniedBudget, worker.LogFilePath);
        Assert.AreEqual("completed", control, worker.LogFilePath);
        Assert.AreEqual("succeeded", controlBudget, worker.LogFilePath);
        var eventPayload = await db.QuerySingleAsync<string>(
            "SELECT Payload FROM fn_ai_agent_event WHERE RunId=@Id AND EventType='run.authorization_required'", new { Id = deniedId });
        using var rejection = JsonDocument.Parse(eventPayload);
        Assert.AreEqual("session_invalid", rejection.RootElement.GetProperty("reason").GetString());
        await worker.StopGracefullyAsync(TimeSpan.FromSeconds(30));
        worker.AssertNoFatalMarkersInLogs();
        Assert.AreEqual(0, worker.ExitCode);
        CollectionAssert.AreEqual(new[] { "host-authority-control" }, received.ToArray(), "真实提供程序只收到健康 Host 对照，停用租户没有模型派发。");
        await api.StopGracefullyAsync();
        api.AssertNoFatalMarkersInLogs();
        Assert.AreEqual(0, api.ExitCode);

        async Task<Guid> CreateRunAsync(HttpClient client, string prompt, Guid? modelTenantId = null)
        {
            // 模型管理是 Host 权限；由 Host 创建指定归属，租户仍以自己的身份建立待拒绝运行。
            using var modelResponse = await controlClient.PostAsJsonAsync("/api/v1/ai/model-configs",
                new CreateAiModelConfigRequest(modelTenantId, "Native Authority", "ollama", origin, "model", null, null, false, true));
            Assert.AreEqual(HttpStatusCode.Created, modelResponse.StatusCode, await modelResponse.Content.ReadAsStringAsync());
            var model = (await modelResponse.Content.ReadFromJsonAsync<AiModelConfigResponse>())!;
            Assert.AreEqual(modelTenantId, model.TenantId, "准备模型必须属于运行对应的 Host 或租户作用域。");
            using var create = await client.PostAsJsonAsync("/api/v1/ai/agent/runs",
                new CreateAiAgentRunRequest(Guid.CreateVersion7(), "fullnet-single-text-v1", model.Id, prompt, 100, 100));
            Assert.AreEqual(HttpStatusCode.Accepted, create.StatusCode, await create.Content.ReadAsStringAsync());
            return (await create.Content.ReadFromJsonAsync<CreateAiAgentRunResponse>())!.RunId;
        }
    }
}
