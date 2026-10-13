using Dapper;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库预算与派发来源投影；不派发模型，也不把已有 unknown 账本猜测为零用量。</summary>
internal static class AiKnowledgeIndexPolicySnapshotAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory, AiOperationBudgetAssertions.TestClock clock)
    {
        await using var db = AiOperationBudgetAssertions.Connection(factory);
        foreach (var scoped in new[] { false, true })
        {
            var p = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, scoped);
            clock.UtcNow = new DateTimeOffset((DateTime)p["Now"]!, TimeSpan.Zero);
            var binding = new AiKnowledgeIndexTaskBinding((Guid)p["KnowledgeBaseId"]!, (Guid)p["DocumentId"]!, (Guid)p["VersionId"]!, 1, 1, (Guid)p["OwnerUserId"]!);
            await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
            var context = services.GetRequiredService<ICurrentTenantContextWriter>();
            if (p["TenantId"] is Guid id) context.SetTenant(new(id, "policy", "策略测试")); else context.SetHost();
            var query = services.GetRequiredService<IQueryExecutor>(); var command = services.GetRequiredService<ICommandExecutor>(); var ids = services.GetRequiredService<IIdGenerator>();
            var tasks = new AiKnowledgeIndexTaskStore(query, command, services.GetRequiredService<ICurrentTenant>(), clock, ids);
            var task = await tasks.EnqueueAsync(binding, default); Assert.IsNotNull(task);
            var held = await tasks.AcquireAsync(binding, task.Id, default); Assert.IsNotNull(held); Assert.IsNotNull(held.LeaseToken);
            var source = await tasks.ReadDispatchAsync(binding, task.Id, held.LeaseToken.Value, default); Assert.IsNotNull(source);
            var originalModel = new { ModelConfigId = source.ModelConfigId, ProviderKey = source.ModelProviderKey, ModelId = source.ModelId,
                EndpointBaseUrl = source.ModelEndpointBaseUrl, OrganizationId = source.ModelOrganizationId };
            var originalClassification = source.DataClassification;
            var ordinal = 0;
            foreach (var fault in new[] { "classification", "provider", "model", "endpoint", "organization" })
            {
                // 恢复本夹具的原绑定再建立新预留；各批次身份独立，不占用原执行场景的任务或凭据。
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET DataClassification=@Classification WHERE Id=@Id", new { Classification = originalClassification, Id = binding.KnowledgeBaseId });
                await db.ExecuteAsync("UPDATE fn_ai_model_config SET ProviderKey=@ProviderKey, ModelId=@ModelId, EndpointBaseUrl=@EndpointBaseUrl, OrganizationId=@OrganizationId WHERE Id=@ModelConfigId", originalModel);
                var entered = false;
                var budget = new DriftBudget(services.GetRequiredService<IAiOperationBudgetStore>(), async () =>
                {
                    entered = true;
                    if (fault == "classification") await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET DataClassification='restricted' WHERE Id=@Id", new { Id = binding.KnowledgeBaseId });
                    else await db.ExecuteAsync(fault switch
                    {
                        "provider" => "UPDATE fn_ai_model_config SET ProviderKey='ollama' WHERE Id=@ModelConfigId",
                        "model" => "UPDATE fn_ai_model_config SET ModelId='changed-model' WHERE Id=@ModelConfigId",
                        "endpoint" => "UPDATE fn_ai_model_config SET EndpointBaseUrl='https://other.test/v1' WHERE Id=@ModelConfigId",
                        _ => "UPDATE fn_ai_model_config SET OrganizationId='policy-drift' WHERE Id=@ModelConfigId",
                    }, originalModel);
                });
                var admission = new AiKnowledgeIndexBudgetService(query, tasks,
                    new(query, command, tasks, ids, services.GetRequiredService<IOptions<DatabaseOptions>>()), budget);
                Assert.IsNull(await admission.ReserveAsync(binding, task.Id, held.LeaseToken.Value, ordinal, ["payload"], default));
                Assert.IsTrue(entered); Assert.AreEqual(1, budget.Reservations); Assert.AreEqual(1, budget.Settlements);
                var operation = await db.QuerySingleAsync<Guid>("SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@TaskId AND BatchOrdinal=@Ordinal", new { TaskId = task.Id, Ordinal = ordinal });
                Assert.AreEqual("known", await db.QuerySingleAsync<string>("SELECT UsageStatus FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = operation }));
                Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = operation }));
                Assert.AreEqual("cancelled", await db.QuerySingleAsync<string>("SELECT Outcome FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = operation }));
                ordinal++;
            }
        }
    }

    /// <summary>只在真实预留返回后改变 AI 权威；结算仍完全交给原生产账本。</summary>
    private sealed class DriftBudget(IAiOperationBudgetStore inner, Func<Task> drift) : IAiOperationBudgetStore
    {
        internal int Reservations, Settlements;
        public async Task<AiOperationReservation> ReserveAsync(AiOperationRequest request, CancellationToken cancellationToken = default)
        { var receipt = await inner.ReserveAsync(request, cancellationToken); Reservations++; await drift(); return receipt; }
        public Task SettleAsync(Guid operationId, AiOperationUsage usage, string outcome, CancellationToken cancellationToken = default)
        { Settlements++; return inner.SettleAsync(operationId, usage, outcome, cancellationToken); }
    }
}
