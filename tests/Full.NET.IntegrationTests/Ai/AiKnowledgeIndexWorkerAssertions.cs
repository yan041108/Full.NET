using System.Data.Common;
using Dapper;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Ai;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>实际 Worker DI/扫描/领取/消费者只替换外部模型、Files 和身份 Port，账本与事务保留生产实现。</summary>
internal static class AiKnowledgeIndexWorkerAssertions
{
    internal sealed class Controls
    {
        internal AiKnowledgeIndexEmbeddingAssertions.ControlledAuthorization Authorization = null!;
        internal AiKnowledgeIndexEmbeddingAssertions.ControlledFiles Files = null!;
        internal AiKnowledgeIndexEmbeddingAssertions.ControlledFactory Model = null!;
        internal Action? OnGenerate;

        internal void Register(IServiceCollection services)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { [$"{AiKnowledgeIndexWorkerOptions.SectionName}:Enabled"] = "true", [$"{AiKnowledgeIndexWorkerOptions.SectionName}:BatchSize"] = "1" }).Build();
            new AiModule().AddBackgroundServices(services, config);
            services.Configure<AiKnowledgeIndexWorkerOptions>(options => { options.Enabled = true; options.BatchSize = 1; });
            // 本测试精确执行一轮生产 Coordinator；Hosted 注册本身另由 Unit 和 AOT 闭包验证。
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService)
                && (d.ImplementationType == typeof(AiKnowledgeIndexHostedProcessor) || d.ImplementationType == typeof(AiAgentRunHostedProcessor))).ToArray()) services.Remove(descriptor);
            services.RemoveAll<IBackgroundSessionAuthorization>(); services.AddScoped<IBackgroundSessionAuthorization>(_ => Authorization);
            services.RemoveAll<IHostFileReferenceReader>(); services.AddScoped<IHostFileReferenceReader>(_ => Files);
            services.RemoveAll<IHostFileContentReader>(); services.AddScoped<IHostFileContentReader>(_ => Files);
            services.RemoveAll<ITenantResourceFileStore>(); services.AddScoped<ITenantResourceFileStore>(_ => Files);
            services.RemoveAll<IAiModelClientFactory>(); services.AddScoped<IAiModelClientFactory>(_ => Model);
        }
    }

    internal static async Task VerifyAsync(DbConnection db, AiKnowledgeIndexTaskBinding binding, Guid taskId,
        Func<CancellationToken, Task<int>> run, Controls controls, AiOperationBudgetAssertions.TestClock clock, string scenario)
    {
        var p = new { TaskId = taskId, DocumentId = binding.DocumentId };
        // 已创建的正确绑定临时发生作用域漂移，扫描不能把它当成可信租户引导。
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_session SET TenantId=@Wrong WHERE IndexTaskId=@TaskId", new { TaskId = taskId, Wrong = Guid.CreateVersion7() });
        Assert.AreEqual(0, await run(default)); Assert.AreEqual(0, controls.Model.Calls);
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_session SET TenantId=@Tenant WHERE IndexTaskId=@TaskId", new { TaskId = taskId, Tenant = controls.Authorization.ExpectedTenantId });
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET Version=2 WHERE Id=@DocumentId", p);
        Assert.AreEqual(0, await run(default));
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET Version=1 WHERE Id=@DocumentId", p);
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET AttemptCount=3 WHERE Id=@TaskId", p);
        Assert.AreEqual(0, await run(default));
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET AttemptCount=0,StateKey='running',LeaseToken=@Lease,LeaseExpiresAtUtc=@Expiry WHERE Id=@TaskId",
            new { TaskId = taskId, Lease = Guid.CreateVersion7(), Expiry = clock.UtcNow.UtcDateTime.AddSeconds(30) });
        Assert.AreEqual(0, await run(default));
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET StateKey='queued',LeaseToken=NULL,LeaseExpiresAtUtc=NULL WHERE Id=@TaskId", p);
        Assert.AreEqual(0, controls.Files.Opens); Assert.AreEqual(0, controls.Model.Calls);
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel(); await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => run(cancelled.Token));
        }
        controls.Authorization.Denied = scenario == "denied";
        if (scenario == "shutdown")
        {
            using var stop = new CancellationTokenSource(); controls.OnGenerate = stop.Cancel;
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => run(stop.Token)); controls.OnGenerate = null;
            Assert.AreEqual("running", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", p));
            Assert.AreEqual(1, controls.Model.Calls);
            clock.UtcNow = clock.UtcNow.AddSeconds(31);
        }
        Assert.AreEqual(1, await run(default));
        var success = scenario is "known" or "unmetered";
        Assert.AreEqual(success ? "completed" : "failed", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", p));
        Assert.AreEqual(success ? 2 : scenario == "denied" ? 0 : 1, controls.Model.Calls);
        Assert.AreEqual(success ? 1L : 0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId", p));
        Assert.AreEqual((long)controls.Model.Calls, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_operation_budget AS budget INNER JOIN fn_ai_knowledge_index_batch AS batch ON batch.Id=budget.Id WHERE batch.IndexTaskId=@TaskId", p));
        if (!success) Assert.AreEqual("ai.knowledge.index_unavailable", await db.QuerySingleAsync<string>("SELECT FailureCode FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", p));
        else Assert.AreEqual(2L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_operation_budget AS budget INNER JOIN fn_ai_knowledge_index_batch AS batch ON batch.Id=budget.Id WHERE batch.IndexTaskId=@TaskId AND budget.Outcome='succeeded' AND budget.UsageStatus=@Usage",
            new { TaskId = taskId, Usage = scenario == "known" ? "known" : "unknown" }));
        var calls = controls.Model.Calls; var opens = controls.Files.Opens;
        Assert.AreEqual(0, await run(default)); Assert.AreEqual(calls, controls.Model.Calls); Assert.AreEqual(opens, controls.Files.Opens);
        controls.Authorization.Denied = false;
    }
}
