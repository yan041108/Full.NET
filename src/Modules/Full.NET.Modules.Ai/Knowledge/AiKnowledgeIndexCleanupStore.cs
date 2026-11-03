using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>墓碑和已退休代次分别重验；每个任务独立提交，异常保留载荷供后续恢复。</summary>
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
        { ["KnowledgeBaseId"] = knowledgeBaseId, ["DocumentId"] = documentId, ["OwnerUserId"] = actor, ["Now"] = clock.UtcNow };
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
    internal Task CleanupTaskAsync(Guid knowledgeBaseId, Guid documentId, Guid actor, Guid task, CancellationToken token)
        => CleanupCoreAsync(knowledgeBaseId, documentId, actor, task, null, token);

    /// <summary>只清理已被后继替换且保留期已过的完成代次，不删除活动指针或原文件。</summary>
    internal Task CleanupRetiredTaskAsync(Guid knowledgeBaseId, Guid documentId, Guid actor, Guid task, DateTimeOffset retiredBefore, CancellationToken token)
    {
        if (retiredBefore > clock.UtcNow.AddHours(-24)) throw new ArgumentOutOfRangeException(nameof(retiredBefore));
        return CleanupCoreAsync(knowledgeBaseId, documentId, actor, task, retiredBefore, token);
    }

    private async Task CleanupCoreAsync(Guid knowledgeBaseId, Guid documentId, Guid actor, Guid task, DateTimeOffset? retiredBefore, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (transactions.HasTransaction) throw new InvalidOperationException("Index cleanup requires an independent transaction boundary.");
        var scoped = AiKnowledgeScope.IsTenant(tenant); var tenantId = tenant.Id;
        if (knowledgeBaseId == Guid.Empty || documentId == Guid.Empty || actor == Guid.Empty || task == Guid.Empty) return;
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        { ["KnowledgeBaseId"] = knowledgeBaseId, ["DocumentId"] = documentId, ["OwnerUserId"] = actor, ["TaskId"] = task };
        parameters["Now"] = clock.UtcNow;
        if (retiredBefore.HasValue) parameters["RetiredBefore"] = retiredBefore.Value;
        // 旧完成证明器不参与删除授权；资产保护与发布指针由同一文档根锁约束。
        var marked = await transaction.ExecuteAsync(async cancellation =>
        {
            if (!await LockEligible(cancellation).ConfigureAwait(false)) return false;
            _ = await command.ExecuteAsync(scoped ? AiKnowledgePublicationCleanupSql.MarkerTenant : AiKnowledgePublicationCleanupSql.MarkerHost, parameters, cancellation).ConfigureAwait(false);
            _ = await command.ExecuteAsync(scoped ? AiKnowledgePublicationCleanupSql.MarkTenant : AiKnowledgePublicationCleanupSql.MarkHost, parameters, cancellation).ConfigureAwait(false);
            return true;
        }, token).ConfigureAwait(false);
        if (marked) _ = await transaction.ExecuteAsync(CleanupLocked, token).ConfigureAwait(false);
        Check();

        async Task<bool> CleanupLocked(CancellationToken cancellation)
        {
            Check(); cancellation.ThrowIfCancellationRequested();
            if (!await LockEligible(cancellation).ConfigureAwait(false)) return false;
            // 锁内只访问 AI 表，终态记录不改写；Files 补偿由调用方在本方法返回后执行。
            var statements = retiredBefore.HasValue ? new[]
            {
                scoped ? AiKnowledgeRetiredGenerationSql.VectorsTenant : AiKnowledgeRetiredGenerationSql.VectorsHost,
                scoped ? AiKnowledgeRetiredGenerationSql.ManifestTenant : AiKnowledgeRetiredGenerationSql.ManifestHost,
                scoped ? AiKnowledgeRetiredGenerationSql.DocxManifestTenant : AiKnowledgeRetiredGenerationSql.DocxManifestHost,
            } : new[]
            {
                scoped ? AiKnowledgeIndexCleanupSql.CancelTenant : AiKnowledgeIndexCleanupSql.CancelHost,
                scoped ? AiKnowledgeIndexCleanupSql.PointerTenant : AiKnowledgeIndexCleanupSql.PointerHost,
                scoped ? AiKnowledgeIndexCleanupSql.VectorsTenant : AiKnowledgeIndexCleanupSql.VectorsHost,
                scoped ? AiKnowledgeIndexCleanupSql.ManifestTenant : AiKnowledgeIndexCleanupSql.ManifestHost,
                scoped ? AiKnowledgeIndexCleanupSql.DocxManifestTenant : AiKnowledgeIndexCleanupSql.DocxManifestHost,
            };
            foreach (var statement in statements)
            {
                _ = await command.ExecuteAsync(statement, parameters, cancellation).ConfigureAwait(false);
                Check(); cancellation.ThrowIfCancellationRequested();
            }
            _ = await command.ExecuteAsync(scoped ? AiKnowledgePublicationCleanupSql.FinishTenant : AiKnowledgePublicationCleanupSql.FinishHost, parameters, cancellation).ConfigureAwait(false);
            return true;
        }

        async Task<bool> LockEligible(CancellationToken cancellation)
        {
            Check(); parameters["Now"] = clock.UtcNow;
            if (await query.QuerySingleOrDefaultAsync<Guid>(AiKnowledgePublicationSql.LockFor(database.Value.Provider, scoped), parameters, cancellation).ConfigureAwait(false) != documentId) return false;
            var locked = await query.QuerySingleOrDefaultAsync<Guid?>(retiredBefore.HasValue
                ? AiKnowledgeRetiredGenerationSql.LockFor(database.Value.Provider, scoped) : AiKnowledgeIndexCleanupSql.LockFor(database.Value.Provider, scoped), parameters, cancellation).ConfigureAwait(false);
            Check(); return locked == task;
        }

        void Check()
        {
            token.ThrowIfCancellationRequested();
            if (scoped != AiKnowledgeScope.IsTenant(tenant) || tenantId != tenant.Id)
                throw new InvalidOperationException("The trusted cleanup scope changed.");
        }
    }
}
