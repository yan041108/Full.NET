using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>墓碑就是可重放的清理意图；每个任务独立提交，异常保留剩余载荷供新请求恢复。</summary>
internal sealed class AiKnowledgeIndexCleanupStore(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    IDataTransactionState transactions, ICurrentTenant tenant, IClock clock, IOptions<DatabaseOptions> database)
{
    internal async Task CleanupDocumentAsync(Guid knowledgeBaseId, Guid documentId, Guid actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (transactions.HasTransaction) throw new InvalidOperationException("Index cleanup requires an independent transaction boundary.");
        var scoped = AiKnowledgeScope.IsTenant(tenant); var tenantId = tenant.Id;
        if (knowledgeBaseId == Guid.Empty || documentId == Guid.Empty || actor == Guid.Empty) return;
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        { ["KnowledgeBaseId"] = knowledgeBaseId, ["DocumentId"] = documentId, ["OwnerUserId"] = actor };
        var tasks = await query.QueryAsync<Guid>(AiKnowledgeIndexCleanupSql.ListFor(database.Value.Provider, scoped), parameters, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (scoped != AiKnowledgeScope.IsTenant(tenant) || tenantId != tenant.Id)
            throw new InvalidOperationException("The trusted cleanup scope changed.");
        foreach (var task in tasks)
        {
            await CleanupTaskAsync(knowledgeBaseId, documentId, actor, task, token).ConfigureAwait(false);
            if (scoped != AiKnowledgeScope.IsTenant(tenant) || tenantId != tenant.Id)
                throw new InvalidOperationException("The trusted cleanup scope changed.");
        }
    }

    /// <summary>后台逐项恢复与 HTTP 有界清理共享同一任务锁和墓碑检查。</summary>
    internal async Task CleanupTaskAsync(Guid knowledgeBaseId, Guid documentId, Guid actor, Guid task, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (transactions.HasTransaction) throw new InvalidOperationException("Index cleanup requires an independent transaction boundary.");
        var scoped = AiKnowledgeScope.IsTenant(tenant); var tenantId = tenant.Id;
        if (knowledgeBaseId == Guid.Empty || documentId == Guid.Empty || actor == Guid.Empty || task == Guid.Empty) return;
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        { ["KnowledgeBaseId"] = knowledgeBaseId, ["DocumentId"] = documentId, ["OwnerUserId"] = actor, ["TaskId"] = task };
        _ = await transaction.ExecuteAsync(async cancellation =>
        {
            Check(); cancellation.ThrowIfCancellationRequested();
            var locked = await query.QuerySingleOrDefaultAsync<Guid?>(AiKnowledgeIndexCleanupSql.LockFor(database.Value.Provider, scoped), parameters, cancellation).ConfigureAwait(false);
            Check();
            if (locked != task) return false;
            parameters["Now"] = clock.UtcNow;
            // 锁内只访问 AI 表，终态记录不改写；Files 补偿由调用方在本方法返回后执行。
            foreach (var statement in new[]
            {
                scoped ? AiKnowledgeIndexCleanupSql.CancelTenant : AiKnowledgeIndexCleanupSql.CancelHost,
                scoped ? AiKnowledgeIndexCleanupSql.PointerTenant : AiKnowledgeIndexCleanupSql.PointerHost,
                scoped ? AiKnowledgeIndexCleanupSql.VectorsTenant : AiKnowledgeIndexCleanupSql.VectorsHost,
                scoped ? AiKnowledgeIndexCleanupSql.ManifestTenant : AiKnowledgeIndexCleanupSql.ManifestHost,
            })
            {
                _ = await command.ExecuteAsync(statement, parameters, cancellation).ConfigureAwait(false);
                Check(); cancellation.ThrowIfCancellationRequested();
            }
            return true;
        }, token).ConfigureAwait(false);
        Check();

        void Check()
        {
            token.ThrowIfCancellationRequested();
            if (scoped != AiKnowledgeScope.IsTenant(tenant) || tenantId != tenant.Id)
                throw new InvalidOperationException("The trusted cleanup scope changed.");
        }
    }
}
