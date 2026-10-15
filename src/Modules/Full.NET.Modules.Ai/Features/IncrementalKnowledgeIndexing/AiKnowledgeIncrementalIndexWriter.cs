using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;

/// <summary>只持久化最新确认来源的增量意图；权威文档锁串行化乱序、重复与删除竞争。</summary>
internal sealed class AiKnowledgeIncrementalIndexWriter(IQueryExecutor query, ICommandExecutor command,
    ICommandTransaction transaction, ICurrentTenant tenant, IOptions<DatabaseOptions> database, IClock clock, IIdGenerator ids)
{
    internal Task<bool> ApplyAsync(AiKnowledgeSourceConfirmed source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (source.KnowledgeBaseId == Guid.Empty || source.DocumentId == Guid.Empty || source.VersionId == Guid.Empty
            || source.TenantId == Guid.Empty || source.TenantId != tenant.Id
            || AiKnowledgeScope.IsTenant(tenant) != source.TenantId.HasValue)
            throw new InvalidOperationException("Knowledge source event scope or identity is invalid.");
        var scoped = AiKnowledgeScope.IsTenant(tenant);
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["KnowledgeBaseId"] = source.KnowledgeBaseId, ["DocumentId"] = source.DocumentId,
            ["VersionId"] = source.VersionId, ["Id"] = ids.NewId(), ["Now"] = clock.UtcNow,
        };
        return transaction.ExecuteAsync(async cancellation =>
        {
            // 在本地短事务中重新读取来源；旧事件、禁用与墓碑均不能复活待处理工作。
            var number = await query.QuerySingleOrDefaultAsync<int>(AiKnowledgeIncrementalIndexSql.LockFor(database.Value.Provider, scoped), parameters, cancellation).ConfigureAwait(false);
            CheckScope();
            if (number <= 0) return false;
            parameters["VersionNumber"] = number;
            var updated = await command.ExecuteAsync(scoped ? AiKnowledgeIncrementalIndexSql.UpdateTenant : AiKnowledgeIncrementalIndexSql.UpdateHost, parameters, cancellation).ConfigureAwait(false);
            CheckScope();
            var inserted = await command.ExecuteAsync(scoped ? AiKnowledgeIncrementalIndexSql.InsertTenant : AiKnowledgeIncrementalIndexSql.InsertHost, parameters, cancellation).ConfigureAwait(false);
            CheckScope();
            return updated + inserted > 0;
        }, token);

        // 交互恢复也复用该写入器；任何等待后的范围变化都必须回滚单文档事务。
        void CheckScope()
        {
            token.ThrowIfCancellationRequested();
            if (source.TenantId != tenant.Id || AiKnowledgeScope.IsTenant(tenant) != scoped)
                throw new InvalidOperationException("Knowledge source scope changed while applying the intent.");
        }
    }
}
