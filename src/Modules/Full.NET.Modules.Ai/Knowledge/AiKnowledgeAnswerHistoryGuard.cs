using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>已确认所有权的历史消息绑定，原始元数据仅来自持久化行。</summary>
internal sealed record AiKnowledgeAnswerHistoryEntry(Guid MessageId, Guid SessionId, string? Json);

/// <summary>历史及后续模型输入共用完整来源复核；调用者先验证消息所有权，本边界不授予会话读取权。</summary>
/// <remarks>事务外使用；任何依赖不可用时返回空资格，不允许只检查展示引用。</remarks>
internal sealed class AiKnowledgeAnswerHistoryGuard(IQueryExecutor query, IDataTransactionState transactions, ICurrentTenant tenant,
    IOptions<DatabaseOptions> database, AiKnowledgeReadAccess access, AiKnowledgePermissionGuard permissions, IAiKnowledgeAnswerSourceProof proof)
{
    private static readonly string[] HistoryPermissions = [AiChatPermissions.Read, AiKnowledgePermissions.Read,
        AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.IndexRead];
    private static readonly string[] ModelPermissions = [AiChatPermissions.Send, AiKnowledgePermissions.Read,
        AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.IndexRead];

    internal const int MaxHistoryAnswers = 64;
    /// <summary>整批复核后再映射正文；最终查询覆盖所有消息，避免逐消息等待使前面的资格失效。</summary>
    internal async Task<IReadOnlyDictionary<Guid, AiKnowledgeAnswerLineage>> AuthorizeBatchAsync(
        IReadOnlyList<AiKnowledgeAnswerHistoryEntry> entries, Guid actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (transactions.HasTransaction) throw new InvalidOperationException("来源复核不能加入本地事务。");
        var scoped = AiKnowledgeScope.IsTenant(tenant); var tenantId = tenant.Id;
        var empty = new Dictionary<Guid, AiKnowledgeAnswerLineage>();
        if (actor == Guid.Empty || tenantId == Guid.Empty || scoped != tenantId.HasValue || entries.Count > MaxHistoryAnswers) return empty;
        var frozen = entries.ToArray();
        if (frozen.Select(entry => entry.MessageId).Distinct().Count() != frozen.Length) return empty;
        var candidates = new Dictionary<Guid, AiKnowledgeAnswerLineage>();
        foreach (var entry in frozen)
            try { candidates.Add(entry.MessageId, AiKnowledgeAnswerLineageCodec.Decode(entry.Json, entry.MessageId, entry.SessionId, actor, tenantId)); }
            catch (InvalidDataException) { /* 损坏记录独立隐藏，不能影响其他有效答案。 */ }
        if (candidates.Count == 0) return empty;
        var dependencies = candidates.Values.SelectMany(lineage => lineage.Dependencies).Distinct().ToArray();
        // 每条最多八项，整批不得通过截断依赖降低授权要求。
        if (dependencies.Length > MaxHistoryAnswers * 8) return empty;
        var dependenciesJson = JsonSerializer.Serialize(dependencies, AiKnowledgeAnswerLineageJsonContext.Default.AiKnowledgeAnswerDependencyArray);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(25));
        return await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(async work =>
        {
            var proven = new HashSet<AiKnowledgeAnswerDependency>();
            for (var pass = 0; pass < 2; pass++)
            {
                if (!SameScope() || !await permissions.IsAuthorizedAsync(HistoryPermissions, actor, work).ConfigureAwait(false) || !SameScope()) return empty;
                var parameters = await access.ParametersAsync(actor, work).ConfigureAwait(false);
                if (!SameScope()) return empty;
                parameters["DependenciesJson"] = dependenciesJson;
                var rows = await query.QueryAsync<AiKnowledgeIndexStatusRecord>(AiKnowledgeIndexStatusSql.HistoryFor(database.Value.Provider, scoped), parameters, work).ConfigureAwait(false);
                if (!SameScope() || rows.Select(row => (row.DocumentId, row.VersionId)).Distinct().Count() != rows.Count
                    || rows.Any(row => !dependencies.Any(dependency => dependency.KnowledgeBaseId == row.KnowledgeBaseId
                        && dependency.DocumentId == row.DocumentId && dependency.VersionId == row.VersionId))) return empty;
                foreach (var candidate in candidates.ToArray())
                    if (candidate.Value.Dependencies.Any(dependency => !rows.Any(row => AiKnowledgeAnswerLineageCodec.Matches(dependency, row))
                        || (pass == 1 && !proven.Contains(dependency)))) candidates.Remove(candidate.Key);
                if (pass == 0)
                    foreach (var dependency in candidates.Values.SelectMany(lineage => lineage.Dependencies).Distinct().ToArray())
                    {
                        var row = rows.Single(row => AiKnowledgeAnswerLineageCodec.Matches(dependency, row));
                        var sourceParameters = new Dictionary<string, object?>(parameters) { ["KnowledgeBaseId"] = row.KnowledgeBaseId,
                            ["DocumentId"] = row.DocumentId, ["VersionId"] = row.VersionId };
                        if (await proof.VerifyAsync(row, sourceParameters, work).ConfigureAwait(false)) proven.Add(dependency);
                        if (!SameScope()) return empty;
                    }
            }
            work.ThrowIfCancellationRequested(); return candidates;
        }, deadline.Token).ConfigureAwait(false);

        bool SameScope()
        {
            if (transactions.HasTransaction) throw new InvalidOperationException("来源复核不能加入本地事务。");
            return tenant.Id == tenantId && AiKnowledgeScope.IsTenant(tenant) == scoped;
        }
    }

    /// <summary>冻结持久化元数据并核对两轮权威关系及全部来源；未知、损坏、删除、撤权均拒绝释放正文。</summary>
    /// <param name="json">有界且绑定消息的持久化依赖，不能使用客户端提交的元数据。</param>
    /// <param name="messageId">已授权消息标识。</param>
    /// <param name="sessionId">已授权会话标识。</param>
    /// <param name="actor">可信当前读取人，必须是消息所有者。</param>
    /// <param name="forModel">后续模型输入复核使用 Send；历史展示使用 Read。</param>
    /// <param name="token">取消当前操作的令牌。</param>
    internal async Task<AiKnowledgeAnswerLineage?> AuthorizeAsync(string? json, Guid messageId, Guid sessionId, Guid actor,
        bool forModel, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (transactions.HasTransaction) throw new InvalidOperationException("来源复核不能加入本地事务。");
        var scoped = AiKnowledgeScope.IsTenant(tenant); var tenantId = tenant.Id;
        if (actor == Guid.Empty || tenantId == Guid.Empty || scoped != tenantId.HasValue) return null;
        AiKnowledgeAnswerLineage lineage;
        try { lineage = AiKnowledgeAnswerLineageCodec.Decode(json, messageId, sessionId, actor, tenantId); }
        catch (InvalidDataException) { return null; }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(25));
        return await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(async work =>
        {
            // 第二轮重新读取角色和机构，不能把第一轮等待前的资格作为交付依据。
            for (var pass = 0; pass < 2; pass++)
            {
                if (!SameScope()) return null;
                if (!await permissions.IsAuthorizedAsync(forModel ? ModelPermissions : HistoryPermissions, actor, work).ConfigureAwait(false) || !SameScope()) return null;
                var parameters = await access.ParametersAsync(actor, work).ConfigureAwait(false);
                if (!SameScope()) return null;
                parameters["DependenciesJson"] = JsonSerializer.Serialize(lineage.Dependencies.ToArray(), AiKnowledgeAnswerLineageJsonContext.Default.AiKnowledgeAnswerDependencyArray);
                var rows = await query.QueryAsync<AiKnowledgeIndexStatusRecord>(AiKnowledgeIndexStatusSql.HistoryFor(database.Value.Provider, scoped), parameters, work).ConfigureAwait(false);
                if (!SameScope() || rows.Count != lineage.Dependencies.Count || rows.Select(row => row.DocumentId).Distinct().Count() != rows.Count
                    || lineage.Dependencies.Any(dependency => !rows.Any(row => AiKnowledgeAnswerLineageCodec.Matches(dependency, row)))) return null;
                if (pass == 0)
                    foreach (var row in rows)
                    {
                        var sourceParameters = new Dictionary<string, object?>(parameters) { ["KnowledgeBaseId"] = row.KnowledgeBaseId,
                            ["DocumentId"] = row.DocumentId, ["VersionId"] = row.VersionId };
                        // 活动指针存在不代表回执、清单和载荷完整；证明实际派生数据后再进入最终权限/来源查询。
                        if (!await proof.VerifyAsync(row, sourceParameters, work).ConfigureAwait(false) || !SameScope()) return null;
                    }
            }
            work.ThrowIfCancellationRequested(); return lineage;
        }, deadline.Token).ConfigureAwait(false);

        bool SameScope()
        {
            if (transactions.HasTransaction) throw new InvalidOperationException("来源复核不能加入本地事务。");
            return tenant.Id == tenantId && AiKnowledgeScope.IsTenant(tenant) == scoped;
        }
    }
}
