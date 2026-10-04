using Dapper;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实执行器和预算账本验证并发、租约接替与未知用量保留，不调用供应商。</summary>
internal static class AiKnowledgeIndexBudgetAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory, AiOperationBudgetAssertions.TestClock clock)
    {
        await factory.InitializeAsync();
        await using var db = AiOperationBudgetAssertions.Connection(factory);
        foreach (var tenant in new[] { false, true })
        {
            var p = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, tenant);
            clock.UtcNow = new DateTimeOffset((DateTime)p["Now"]!, TimeSpan.Zero);
            // 租户可以使用已批准的宿主模型，但预算仍属于可信租户。
            if (tenant) await db.ExecuteAsync("UPDATE fn_ai_model_config SET TenantId=NULL WHERE Id=@ModelConfigId", p);
            var binding = new AiKnowledgeIndexTaskBinding((Guid)p["KnowledgeBaseId"]!, (Guid)p["DocumentId"]!,
                (Guid)p["VersionId"]!, 1, 1, (Guid)p["OwnerUserId"]!);
            var task = await Use((tasks, _, _) => tasks.EnqueueAsync(binding, CancellationToken.None));
            Assert.IsNotNull(task);
            var held = await Use((tasks, _, _) => tasks.AcquireAsync(binding, task.Id, CancellationToken.None));
            Assert.IsNotNull(held); Assert.IsNotNull(held.LeaseToken);
            var lease = held.LeaseToken.Value;
            var competing = await Task.WhenAll(Reserve(lease, 0, ["第一块", "second"]), Reserve(lease, 0, ["第一块", "second"]));
            Assert.IsTrue(competing.All(item => item is not null));
            Assert.AreEqual(1, competing.Count(item => item!.Reservation.IsNew));
            Assert.AreEqual(1, competing.Count(item => item!.Source is not null));
            var winner = competing.Single(item => item!.Reservation.IsNew)!;
            Assert.AreEqual(winner.Request.OperationId, competing[0]!.Request.OperationId);
            Assert.AreEqual(winner.Request.OperationId, competing[1]!.Request.OperationId);
            Assert.AreEqual(1L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@Id", new { Id = task.Id }));
            Assert.AreEqual(1L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_operation_budget AS budget INNER JOIN fn_ai_knowledge_index_batch AS batch ON batch.Id=budget.Id WHERE batch.IndexTaskId=@Id", new { Id = task.Id }));
            Assert.AreEqual(tenant ? ((Guid)p["TenantId"]!).ToString("N") : "host", await db.QuerySingleAsync<string>("SELECT ScopeKey FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = winner.Request.OperationId }));
            await Use(async (_, budget, _) => { await budget.SettleAsync(winner.Request.OperationId, new(null, 0), "failed"); return true; });
            var charged = await db.QuerySingleAsync<long>("SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = winner.Request.OperationId });
            Assert.IsTrue(charged > 0);
            clock.UtcNow = clock.UtcNow.AddSeconds(30);
            Assert.IsNull(await Reserve(lease, 1, ["expired"]));
            var next = await Use((tasks, _, _) => tasks.AcquireAsync(binding, task.Id, CancellationToken.None));
            Assert.IsNotNull(next); Assert.IsNotNull(next.LeaseToken);
            Assert.AreNotEqual(lease, next.LeaseToken.Value);
            Assert.IsNull(await Reserve(lease, 0, ["第一块", "second"]));
            var recovered = await Reserve(next.LeaseToken.Value, 0, ["第一块", "second"]);
            Assert.IsNotNull(recovered); Assert.IsFalse(recovered.Reservation.IsNew); Assert.IsNull(recovered.Source);
            Assert.AreEqual(winner.Request.OperationId, recovered.Request.OperationId);
            Assert.AreEqual("unknown", recovered.Reservation.UsageStatus);
            Assert.AreEqual(charged, await db.QuerySingleAsync<long>("SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = recovered.Request.OperationId }));
            Assert.IsNull(await Reserve(next.LeaseToken.Value, 0, ["changed", "second"]));
            var separate = await Reserve(next.LeaseToken.Value, 1, ["third"]);
            Assert.IsNotNull(separate); Assert.IsTrue(separate.Reservation.IsNew);
            Assert.AreNotEqual(winner.Request.OperationId, separate.Request.OperationId);
            Assert.AreEqual(2L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_operation_budget AS budget INNER JOIN fn_ai_knowledge_index_batch AS batch ON batch.Id=budget.Id WHERE batch.IndexTaskId=@Id", new { Id = task.Id }));
            var wrong = binding with { OwnerUserId = Guid.CreateVersion7() };
            Assert.IsNull(await Use((_, _, services) => Service(services).ReserveAsync(wrong, task.Id, next.LeaseToken.Value, 2, ["forbidden"], CancellationToken.None)));

            Task<AiKnowledgeIndexBudgetAdmission?> Reserve(Guid token, int ordinal, string[] inputs) =>
                Use((_, _, services) => Service(services).ReserveAsync(binding, task.Id, token, ordinal, inputs, CancellationToken.None));
            AiKnowledgeIndexBudgetService Service(IServiceProvider services)
            {
                var query = services.GetRequiredService<IQueryExecutor>();
                var ids = services.GetRequiredService<IIdGenerator>();
                var tasks = Tasks(services);
                return new(query, tasks, new(query, services.GetRequiredService<ICommandExecutor>(), tasks, ids), services.GetRequiredService<IAiOperationBudgetStore>());
            }
            async Task<T> Use<T>(Func<AiKnowledgeIndexTaskStore, IAiOperationBudgetStore, IServiceProvider, Task<T>> action)
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
                if (tenant) context.SetTenant(new TenantContext((Guid)p["TenantId"]!, "index-budget", "索引预算")); else context.SetHost();
                return await action(Tasks(scope.ServiceProvider), scope.ServiceProvider.GetRequiredService<IAiOperationBudgetStore>(), scope.ServiceProvider);
            }
        }
    }

    private static AiKnowledgeIndexTaskStore Tasks(IServiceProvider services) => new(services.GetRequiredService<IQueryExecutor>(),
        services.GetRequiredService<ICommandExecutor>(), services.GetRequiredService<ICurrentTenant>(), services.GetRequiredService<IClock>(), services.GetRequiredService<IIdGenerator>());
}
