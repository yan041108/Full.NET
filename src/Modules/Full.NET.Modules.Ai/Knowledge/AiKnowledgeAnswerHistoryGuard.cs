using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>历史及后续模型输入共用完整来源复核；调用者先验证消息所有权，本边界不授予会话读取权。</summary>
/// <remarks>事务外使用；任何依赖不可用时返回空资格，不允许只检查展示引用。尚未注册公开消费者。</remarks>
internal sealed class AiKnowledgeAnswerHistoryGuard(IQueryExecutor query, IDataTransactionState transactions, ICurrentTenant tenant,
    IOptions<DatabaseOptions> database, AiKnowledgeReadAccess access, AiKnowledgePermissionGuard permissions, IAiKnowledgeAnswerSourceProof proof)
{
    private static readonly string[] HistoryPermissions = [AiChatPermissions.Read, AiKnowledgePermissions.Read,
        AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.IndexRead];
    private static readonly string[] ModelPermissions = [AiChatPermissions.Send, AiKnowledgePermissions.Read,
        AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.IndexRead];

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
