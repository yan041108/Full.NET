using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Ai;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Identity;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Full.NET.Abstractions.Time;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Abstractions.Tenancy;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>真实 Native HTTP、当前读者授权和 Worker 恢复闭包；合成已结算载荷不冒充文件解析或云模型派发。</summary>
internal static class NativePdfKnowledgeAssertions
{
    internal static Dictionary<string, string?> Settings(string executable) => new()
    {
        ["FullNet:Ai:KnowledgePdf:Enabled"] = "true",
        ["FullNet:Ai:KnowledgePdf:NodeExecutablePath"] = executable,
        ["FullNet:Ai:KnowledgePdf:CliScriptPath"] = executable,
        ["FullNet:Ai:KnowledgePdf:ImageId"] = "sha256:" + new string('a', 64)
    };

    internal static async Task VerifyAsync(HttpClient client, string token, DatabaseProvider provider, string connectionString, CancellationToken cancellation,
        bool nativeWorker = false)
    {
        string Claim(string name) => IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(token, name)!;
        var session = new SessionBindingSnapshot(Guid.Parse(Claim(FullNetIdentityClaimTypes.Subject)), null,
            Guid.Parse(Claim(FullNetIdentityClaimTypes.SessionId)), Claim(FullNetIdentityClaimTypes.SecurityStamp),
            Claim(FullNetIdentityClaimTypes.ActorScope), Claim(FullNetIdentityClaimTypes.Scope));
        var clock = new AiOperationBudgetAssertions.TestClock(); var controls = new AiKnowledgeIndexPdfVectorAssertions.WorkerControls();
        using var factory = new FullNetApiFactory(provider, connectionString, configureTestServices: services =>
        { services.RemoveAll<IClock>(); services.AddSingleton<IClock>(clock); controls.Register(services); });
        await using var db = AiOperationBudgetAssertions.Connection(factory);
        static string PathFor(AiKnowledgeIndexTaskBinding b) => $"/api/v1/ai/knowledge-bases/{b.KnowledgeBaseId}/documents/{b.DocumentId}/versions/{b.VersionId}/index";
        async Task<HttpResponseMessage> Send(HttpMethod method, string path)
        {
            using var request = new HttpRequestMessage(method, path); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await client.SendAsync(request, cancellation);
        }
        async Task Require(HttpResponseMessage response, HttpStatusCode expected)
        { Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync(cancellation)); }
        async Task ReadReady(AiKnowledgeIndexTaskBinding b)
        {
            using var response = await Send(HttpMethod.Get, PathFor(b)); await Require(response, HttpStatusCode.OK);
            var status = await response.Content.ReadFromJsonAsync<AiKnowledgeIndexStatusResponse>(cancellation);
            Assert.IsNotNull(status); Assert.AreEqual("ready", status.State);
        }
        var probe = new AiKnowledgeIndexPdfVectorAssertions.NativeProbe(session, async b =>
        {
            using var response = await Send(HttpMethod.Post, PathFor(b)); await Require(response, HttpStatusCode.Accepted);
            var queued = await response.Content.ReadFromJsonAsync<AiKnowledgeIndexEnqueueResponse>(cancellation); Assert.IsNotNull(queued); return queued.TaskId;
        }, ReadReady, async b =>
        {
            // 人工重试沿原会话冻结绑定运行，保留已结算清单和向量；Worker 不接触假 Node/CLI 路径。
            var task = await db.QuerySingleAsync<Guid>("SELECT Id FROM fn_ai_knowledge_index_task WHERE VersionId=@Id", new { Id = b.VersionId });
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET StateKey='failed',FailureCode='ai.knowledge.index_unavailable' WHERE Id=@Id", new { Id = task });
            using var retry = await Send(HttpMethod.Post, PathFor(b) + $"/{task}/retry"); await Require(retry, HttpStatusCode.Accepted);
            if (!nativeWorker)
            {
                // API 门禁只依赖 API 原生产物；独立 Worker 门禁另用真实原生进程验证恢复。
                await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
                services.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
                var coordinator = new AiKnowledgeIndexWorkerCoordinator(services.GetRequiredService<IQueryExecutor>(),
                    services.GetRequiredService<IDataTransactionState>(), services.GetRequiredService<IServiceScopeFactory>(), clock,
                    services.GetRequiredService<IOptions<DatabaseOptions>>(), Options.Create(new AiKnowledgeIndexWorkerOptions { Enabled = true, BatchSize = 1 }),
                    NullLogger<AiKnowledgeIndexWorkerCoordinator>.Instance, Options.Create(new AiKnowledgePdfRuntimeOptions { Enabled = true }));
                Assert.AreEqual(1, await coordinator.ProcessPendingAsync(cancellation)); await ReadReady(b); return;
            }
            Assert.IsTrue(NativeWorkerArtifactLocator.TryResolve(out var artifact, out var reason), reason);
            Assert.IsNotNull(artifact); var settings = Settings(artifact.ExecutablePath);
            settings["FullNet__Ai__KnowledgeIndexWorker__Enabled"] = "true";
            settings["FullNet__Ai__KnowledgeIndexWorker__BatchSize"] = "1";
            settings["FullNet__Ai__KnowledgeIndexWorker__PollMilliseconds"] = "250";
            await using var worker = await NativeWorkerProcessHost.StartAsync(artifact, provider, connectionString,
                NativeAotTestTimeouts.ProcessStartup, cancellation, additionalSettings: settings);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromMinutes(2));
            while (true)
            {
                var state = await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@Id", new { Id = task });
                if (state == "completed") break;
                Assert.AreNotEqual("failed", state, await File.ReadAllTextAsync(worker.LogFilePath, cancellation));
                deadline.Token.ThrowIfCancellationRequested(); await Task.Delay(250, deadline.Token);
            }
            await ReadReady(b);
        });
        await AiKnowledgeIndexPdfVectorAssertions.VerifyAsync(factory, clock, controls, probe);
    }
}
