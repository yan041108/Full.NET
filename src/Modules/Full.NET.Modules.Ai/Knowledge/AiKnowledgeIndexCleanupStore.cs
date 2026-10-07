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
    IDataTransactionState transactions, ICurrentTenant tenant, IClock clock, IOptions<DatabaseOptions> database,
    AiKnowledgeIndexActivationStore activation, AiKnowledgeIndexPdfActivationStore pdfActivation)
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
        if (retiredBefore.HasValue) parameters["RetiredBefore"] = retiredBefore.Value;
        if (retiredBefore.HasValue)
        {
            var version = await query.QuerySingleOrDefaultAsync<Guid?>(scoped ? AiKnowledgeRetiredGenerationSql.CurrentVersionTenant
                : AiKnowledgeRetiredGenerationSql.CurrentVersionHost, parameters, token).ConfigureAwait(false);
            Check(); if (version is null || version == Guid.Empty) return;
            var readParameters = new Dictionary<string, object?>(parameters, StringComparer.Ordinal)
            { ["VersionId"] = version.Value, ["ActiveRoleIdsJson"] = "[]", ["ActiveOrganizationUnitIdsJson"] = "[]" };
            var current = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexStatusRecord>(AiKnowledgeIndexStatusSql.For(database.Value.Provider, scoped),
                readParameters, token).ConfigureAwait(false);
            Check(); if (current is null || current.OwnerUserId != actor) return;
            // 证明锁定当前来源、模型、会话、清单、批次与收费，旧载荷删除在它的同一事务内提交。
            if (current.ContentType == "application/pdf")
                _ = await pdfActivation.WithReadyGenerationAsync(current, readParameters, CleanupLocked, token).ConfigureAwait(false);
            else if (current.ContentType is "text/plain" or "text/markdown")
                _ = await activation.WithReadyGenerationAsync(current, readParameters, CleanupLocked, token).ConfigureAwait(false);
        }
        else _ = await transaction.ExecuteAsync(CleanupLocked, token).ConfigureAwait(false);
        Check();

        async Task<bool> CleanupLocked(CancellationToken cancellation)
        {
            Check(); cancellation.ThrowIfCancellationRequested();
            var locked = await query.QuerySingleOrDefaultAsync<Guid?>(retiredBefore.HasValue
                ? AiKnowledgeRetiredGenerationSql.LockFor(database.Value.Provider, scoped) : AiKnowledgeIndexCleanupSql.LockFor(database.Value.Provider, scoped), parameters, cancellation).ConfigureAwait(false);
            Check();
            if (locked != task) return false;
            parameters["Now"] = clock.UtcNow;
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
            return true;
        }

        void Check()
        {
            token.ThrowIfCancellationRequested();
            if (scoped != AiKnowledgeScope.IsTenant(tenant) || tenantId != tenant.Id)
                throw new InvalidOperationException("The trusted cleanup scope changed.");
        }
    }
}
