using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Diagnostics;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>复用知识 Worker 的本地解析调度；恢复原会话后仍由来源消费者复核，不能触发收费索引。</summary>
internal sealed class AiKnowledgeCorrectionPreparationCoordinator(IQueryExecutor query, IDataTransactionState transactions,
    IServiceScopeFactory scopes, IClock clock, IOptions<DatabaseOptions> database, IOptions<AiKnowledgeIndexWorkerOptions> options,
    ILogger<AiKnowledgeCorrectionPreparationCoordinator> logger)
{
    internal async Task<int> ProcessPendingAsync(CancellationToken token)
    {
        Guard(token); var configuration = options.Value;
        if (configuration.BatchSize is < 1 or > 32) throw new InvalidOperationException("Invalid preparation batch size.");
        if (!configuration.CorrectionPreparationEnabled) return 0;
        var rows = await query.QueryAsync<AiKnowledgeCorrectionPreparationCandidate>(database.Value.Provider == DatabaseProvider.MySql
            ? AiKnowledgeCorrectionPreparationSql.ScanMySql : AiKnowledgeCorrectionPreparationSql.ScanSqlServer,
            new Dictionary<string, object?> { ["BatchSize"] = configuration.BatchSize, ["Now"] = clock.UtcNow }, token).ConfigureAwait(false);
        Guard(token); if (rows.Count > configuration.BatchSize) throw new InvalidOperationException("Preparation scan exceeded its bound.");
        var acquired = 0;
        foreach (var row in rows)
        {
            Guard(token);
            if (row.Id == Guid.Empty || row.VersionId == Guid.Empty || row.KnowledgeBaseId == Guid.Empty || row.DocumentId == Guid.Empty
                || row.OwnerUserId == Guid.Empty || row.TenantId == Guid.Empty || row.DocumentVersion < 1) continue;
            await using var scope = scopes.CreateAsyncScope(); var provider = scope.ServiceProvider;
            var tenant = provider.GetRequiredService<ICurrentTenantContextWriter>();
            if (row.TenantId is { } id) tenant.SetTenant(new(id, id.ToString("N"), id.ToString("N"))); else tenant.SetHost();
            var consumer = provider.GetRequiredService<AiKnowledgeCorrectionPreparationService>();
            var queue = provider.GetRequiredService<AiKnowledgeCorrectionPreparationQueue>();
            var binding = new AiKnowledgeCorrectionStore.Binding(row.KnowledgeBaseId, row.DocumentId, row.VersionId, row.OwnerUserId, row.DocumentVersion);
            var held = await queue.AcquireAsync(binding, row.Id, consumer.RuntimeFor(row.ContentType), token).ConfigureAwait(false);
            Guard(token);
            if (held?.LeaseToken is not { } lease || held.ParserRuntimeId is null) continue;
            acquired++;
            try
            {
                // 使用持久行原身份，不以扫描时的新所有者或当前登录替代原会话。
                var actor = new SessionBindingSnapshot(held.ActorUserId, held.TenantId, held.SessionId, held.SecurityStamp,
                    held.ActorScope, held.EffectiveScope, held.SessionKind);
                var result = await consumer.PrepareAsync(binding, actor, held.Id, token,
                    new(held.Id, lease, held.RequestHash, held.ParserRuntimeId)).ConfigureAwait(false);
                Guard(token);
                if (result is null) await queue.FailAsync(binding, held.Id, lease, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (ServiceCapacityExceededException)
            {
                // 临时准入压力交给宿主退避，原准备记录由现有租约恢复路径接手。
                throw;
            }
            catch (Exception exception)
            {
                Guard(token); await queue.FailAsync(binding, held.Id, lease, token).ConfigureAwait(false);
                AiFailureLog.Write(logger, LogLevel.Warning, AiFailureStage.KnowledgeIndex, exception, held.Id);
            }
        }
        return acquired;
    }
    private void Guard(CancellationToken token) { token.ThrowIfCancellationRequested(); if (transactions.HasTransaction) throw new InvalidOperationException("Preparation cannot scan inside a transaction."); }
}
