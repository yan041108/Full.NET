using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>删除意图提交后分批清空校对正文；不调用 Files、Identity 或模型，不删除审计和费用凭据。</summary>
internal sealed class AiKnowledgeCorrectionCleanupStore(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    IDataTransactionState transactions, ICurrentTenant tenant, IClock clock, IOptions<DatabaseOptions> database)
{
    /// <summary>每轮一个来源及最多二十五份修订，各份在独立根锁事务提交，失败或取消后可从持久状态恢复。</summary>
    internal async Task CleanupSourceAsync(Guid knowledgeId, Guid documentId, Guid owner, Guid sourceId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (transactions.HasTransaction) throw new InvalidOperationException("Correction cleanup requires an independent transaction boundary.");
        if (knowledgeId == Guid.Empty || documentId == Guid.Empty || owner == Guid.Empty || sourceId == Guid.Empty) return;
        var scoped = AiKnowledgeScope.IsTenant(tenant); var tenantId = tenant.Id;
        var p = new Dictionary<string, object?> { ["KnowledgeBaseId"] = knowledgeId, ["DocumentId"] = documentId,
            ["OwnerUserId"] = owner, ["SourceId"] = sourceId, ["Now"] = clock.UtcNow, ["EmptyPayload"] = Array.Empty<byte>() };
        var ready = await transaction.ExecuteAsync(async ct =>
        {
            if (!await Lock(ct).ConfigureAwait(false)) return false;
            _ = await command.ExecuteAsync(scoped ? AiKnowledgeCorrectionCleanupSql.MarkSourceTenant : AiKnowledgeCorrectionCleanupSql.MarkSourceHost, p, ct).ConfigureAwait(false);
            Stable(); ct.ThrowIfCancellationRequested();
            return true;
        }, token).ConfigureAwait(false);
        Stable(); if (!ready) return;
        var revisions = await query.QueryAsync<Guid>(AiKnowledgeCorrectionCleanupSql.RevisionsFor(database.Value.Provider, scoped), p, token).ConfigureAwait(false);
        Stable();
        if (revisions.Count > 25 || revisions.Any(id => id == Guid.Empty) || revisions.Distinct().Count() != revisions.Count)
            throw new InvalidDataException("Correction cleanup exceeded its bounded revision batch.");
        foreach (var revision in revisions)
        {
            p["RevisionId"] = revision;
            _ = await transaction.ExecuteAsync(async ct =>
            {
                if (!await Lock(ct).ConfigureAwait(false)) return false;
                _ = await command.ExecuteAsync(scoped ? AiKnowledgeCorrectionCleanupSql.MarkRevisionTenant : AiKnowledgeCorrectionCleanupSql.MarkRevisionHost, p, ct).ConfigureAwait(false);
                _ = await command.ExecuteAsync(scoped ? AiKnowledgeCorrectionCleanupSql.EraseRevisionTenant : AiKnowledgeCorrectionCleanupSql.EraseRevisionHost, p, ct).ConfigureAwait(false);
                Stable(); ct.ThrowIfCancellationRequested();
                return true;
            }, token).ConfigureAwait(false);
            Stable();
        }
        _ = await transaction.ExecuteAsync(async ct =>
        {
            if (!await Lock(ct).ConfigureAwait(false)) return false;
            // 只有该来源的全部修订已清空才擦除原解析；不能因二十五项上限而丢下无法继续清理的孤儿正文。
            _ = await command.ExecuteAsync(scoped ? AiKnowledgeCorrectionCleanupSql.EraseSourceTenant : AiKnowledgeCorrectionCleanupSql.EraseSourceHost, p, ct).ConfigureAwait(false);
            Stable(); ct.ThrowIfCancellationRequested();
            return true;
        }, token).ConfigureAwait(false);
        Stable();

        async Task<bool> Lock(CancellationToken ct)
        {
            Stable(); ct.ThrowIfCancellationRequested(); p["Now"] = clock.UtcNow;
            if (await query.QuerySingleOrDefaultAsync<Guid>(AiKnowledgePublicationSql.LockFor(database.Value.Provider, scoped), p, ct).ConfigureAwait(false) != documentId) return false;
            var locked = await query.QuerySingleOrDefaultAsync<Guid>(AiKnowledgeCorrectionCleanupSql.LockFor(database.Value.Provider, scoped), p, ct).ConfigureAwait(false);
            Stable(); return locked == sourceId;
        }
        void Stable()
        {
            token.ThrowIfCancellationRequested();
            if (scoped != AiKnowledgeScope.IsTenant(tenant) || tenantId != tenant.Id)
                throw new InvalidOperationException("The trusted correction cleanup scope changed.");
        }
    }
}
