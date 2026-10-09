using Full.NET.Modules.Identity.Contracts;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Collections.Concurrent;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Ai;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>原生 API 创建精确版本绑定，原生 Worker 拒绝旧代次并执行当前代次对照。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeWorkerPromptE2ETests
{
    [TestMethod] public Task MySql_native_worker_rejects_republished_old_prompt_run() => VerifyAsync(DatabaseProvider.MySql);
    [TestMethod] public Task SqlServer_native_worker_rejects_republished_old_prompt_run() => VerifyAsync(DatabaseProvider.SqlServer);
    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out var artifact, out var reason)) Assert.Inconclusive(reason ?? "Native Worker artifact unavailable.");
        if (!NativeApiArtifactLocator.TryResolve(out var apiArtifact, out var apiReason)) Assert.Inconclusive(apiReason ?? "Native API artifact unavailable.");
        var requests = new ConcurrentQueue<string>();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        await using var providerHost = builder.Build();
        providerHost.MapPost("/api/chat", async (HttpContext context) =>
        {
            using var body = await System.Text.Json.JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            requests.Enqueue(body.RootElement.GetProperty("messages").EnumerateArray().Single(m => m.GetProperty("role").GetString() == "user").GetProperty("content").GetString()!);
            context.Response.ContentType = "application/x-ndjson";
            await context.Response.WriteAsync("{\"message\":{\"role\":\"assistant\",\"content\":\"native-ok\"},\"done\":true,\"prompt_eval_count\":5,\"eval_count\":2}\n", context.RequestAborted);
        });
        await providerHost.StartAsync(); var origin = providerHost.Urls.Single();
        var connection = provider == DatabaseProvider.MySql ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync() : await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var settings = AiAgentRuntimeTestSettings.ForIntegrationTests(); settings["FullNet:Ai:Network:OllamaAllowedOrigins:0"] = origin;
        using var factory = new FullNetApiFactory(provider, connection, settingsOverrides: settings);
        await factory.InitializeAsync();
        var username = $"native-prompt-{Guid.CreateVersion7():N}";
        await factory.CreateHostIdentityAsync(username, [AiModelPermissions.Create, AiModelPermissions.Read, AiAgentRunPermissions.Read, AiAgentRunPermissions.Create, AiPromptPermissions.Read, AiPromptPermissions.Create, AiPromptPermissions.Publish, AiPromptPermissions.Deactivate, AiPromptPermissions.Rollback, AiPromptPermissions.Use], password: FullNetApiFactory.TestPassword);
        await using var apiHost = await NativeApiProcessHost.StartAsync(apiArtifact, provider, connection, settings, NativeAotTestTimeouts.ProcessStartup);
        using var client = apiHost.CreateClient();
        // 原生 API 使用自己的签名密钥；实际登录获得该进程令牌，后台只重验持久会话与权限。
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new LoginRequest(username, FullNetApiFactory.TestPassword)) };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var login = await client.SendAsync(loginRequest); Assert.AreEqual(HttpStatusCode.OK, login.StatusCode, apiHost.LogFilePath);
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var modelResponse = await client.PostAsJsonAsync("/api/v1/ai/model-configs", new CreateAiModelConfigRequest(null, "Native Prompt", "ollama", origin, "model", null, null, false, true));
        Assert.AreEqual(HttpStatusCode.Created, modelResponse.StatusCode); var model = (await modelResponse.Content.ReadFromJsonAsync<AiModelConfigResponse>())!;
        var head = await WriteAsync("/api/v1/ai/prompts", new CreateAiPromptRequest(Guid.CreateVersion7(), "Native epoch", new(model.Id, "en-US", "Native {{input}}", 100, 100)), HttpStatusCode.Created);
        var root = $"/api/v1/ai/prompts/{head.Id}";
        var versions = (await client.GetFromJsonAsync<PagedResult<AiPromptVersionResponse>>(root + "/versions"))!;
        var version = versions.Items.Single(); head = await WriteAsync(root + "/publish", new SelectAiPromptVersionRequest(head.Revision, version.Id));
        await AiAgentRunApiAssertions.SeedFreshWorkerHeartbeatAsync(factory);
        using var create = await client.PostAsJsonAsync("/api/v1/ai/agent/runs", new CreateAiAgentRunRequest(Guid.CreateVersion7(), "fullnet-single-text-v1", model.Id, "input", 100, 100, version.Id));
        Assert.AreEqual(HttpStatusCode.Accepted, create.StatusCode); var runId = (await create.Content.ReadFromJsonAsync<CreateAiAgentRunResponse>())!.RunId;
        head = await WriteAsync(root + "/deactivate", new DeactivateAiPromptRequest(head.Revision));
        head = await WriteAsync(root + "/rollback", new SelectAiPromptVersionRequest(head.Revision, version.Id)); Assert.AreEqual(1, head.Epoch);
        // 同版、当前代次的正向对照排除所有 Prompt 都因物化或正文解码失败而被拒绝的假阳性。
        using var control = await client.PostAsJsonAsync("/api/v1/ai/agent/runs", new CreateAiAgentRunRequest(Guid.CreateVersion7(), "fullnet-single-text-v1", model.Id, "current epoch", 100, 100, version.Id));
        Assert.AreEqual(HttpStatusCode.Accepted, control.StatusCode); var controlId = (await control.Content.ReadFromJsonAsync<CreateAiAgentRunResponse>())!.RunId;
        await using var host = await NativeWorkerProcessHost.StartAsync(artifact, provider, connection, NativeAotTestTimeouts.ProcessStartup,
            additionalSettings: new Dictionary<string, string?> { ["FullNet:Ai:AgentRuntime:PollMilliseconds"] = "250", ["FullNet:Ai:Network:OllamaAllowedOrigins:0"] = origin });
        await using var db = Full.NET.IntegrationTests.Migrations.Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        var timer = Stopwatch.StartNew(); AiAgentRunResponse? run = null; AiAgentRunResponse? current = null;
        string? outcome = null; string? controlOutcome = null;
        while (timer.Elapsed < TimeSpan.FromSeconds(45))
        {
            run = await client.GetFromJsonAsync<AiAgentRunResponse>($"/api/v1/ai/agent/runs/{runId}");
            current = await client.GetFromJsonAsync<AiAgentRunResponse>($"/api/v1/ai/agent/runs/{controlId}");
            // 终态/事件先提交，预算随后独立结算；在同一截止时间内观察两个权威结果。
            if (run?.StatusKey == "failed") outcome = await Dapper.SqlMapper.QuerySingleAsync<string>(db, "SELECT Outcome FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = runId });
            if (current?.StatusKey == "completed") controlOutcome = await Dapper.SqlMapper.QuerySingleAsync<string>(db, "SELECT Outcome FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = controlId });
            if (run?.StatusKey == "failed" && outcome == "failed" && current?.StatusKey == "completed" && controlOutcome == "succeeded") break;
            await Task.Delay(100);
        }
        Assert.AreEqual("failed", run?.StatusKey, host.LogFilePath);
        Assert.AreEqual("failed", outcome, host.LogFilePath);
        Assert.AreEqual("completed", current?.StatusKey, host.LogFilePath); Assert.AreEqual("succeeded", controlOutcome, host.LogFilePath);
        var payload = await Dapper.SqlMapper.QuerySingleAsync<string>(db, "SELECT Payload FROM fn_ai_agent_event WHERE RunId=@Id AND EventType='run.failed'", new { Id = runId });
        using var failure = System.Text.Json.JsonDocument.Parse(payload);
        Assert.AreEqual("ai.prompts.unavailable", failure.RootElement.GetProperty("errorCode").GetString());
        await host.StopGracefullyAsync(TimeSpan.FromSeconds(30)); host.AssertNoFatalMarkersInLogs(); Assert.AreEqual(0, host.ExitCode);
        CollectionAssert.AreEqual(new[] { "Native current epoch" }, requests.ToArray());
        await apiHost.StopGracefullyAsync(); apiHost.AssertNoFatalMarkersInLogs(); Assert.AreEqual(0, apiHost.ExitCode);
        async Task<AiPromptResponse> WriteAsync<T>(string url, T request, HttpStatusCode status = HttpStatusCode.OK)
        {
            using var response = await client.PostAsJsonAsync(url, request); Assert.AreEqual(status, response.StatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<AiPromptResponse>())!;
        }
    }
}
