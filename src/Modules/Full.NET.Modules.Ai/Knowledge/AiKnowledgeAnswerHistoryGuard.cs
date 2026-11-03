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

/// <summary>服务器 JOIN 证明的反馈所属人；仅用于解码历史绑定，不能替代当前评审者权限。</summary>
internal sealed record AiKnowledgeAnswerFeedbackHistoryEntry(Guid MessageId, Guid SessionId, Guid OwnerUserId, string? Json);

/// <summary>历史及后续模型输入共用完整来源复核；调用者先验证消息所有权，本边界不授予会话读取权。</summary>
/// <remarks>事务外使用；任何依赖不可用时返回空资格，不允许只检查展示引用。</remarks>
internal sealed class AiKnowledgeAnswerHistoryGuard(IQueryExecutor query, IDataTransactionState transactions, ICurrentTenant tenant,
    IOptions<DatabaseOptions> database, AiKnowledgeReadAccess access, AiKnowledgePermissionGuard permissions, IAiKnowledgeAnswerSourceProof proof,
    AiKnowledgeFaqPublishedSourceReader? faqSources = null, AiPersonalScheduleSourceReader? personalSchedules = null)
{
    private static readonly string[] HistoryPermissions = [AiChatPermissions.Read, AiKnowledgePermissions.Read,
        AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.IndexRead];
    private static readonly string[] ModelPermissions = [AiChatPermissions.Send, AiKnowledgePermissions.Read,
        AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.IndexRead];

    internal const int MaxHistoryAnswers = 64;
    internal Task<IReadOnlyDictionary<Guid, AiKnowledgeAnswerLineage>> AuthorizeFeedbackReviewBatchAsync(
        IReadOnlyList<AiKnowledgeAnswerFeedbackHistoryEntry> entries, Guid actor, CancellationToken token)
        => AuthorizeBoundBatchAsync(entries, actor, token);

    /// <summary>审核最终联合查询使用的新鲜评审者参数；只可在事务外读取权威端口。</summary>
    internal async Task<Dictionary<string, object?>?> CurrentReviewParametersAsync(Guid actor, CancellationToken token, IReadOnlyList<AiKnowledgeAnswerDependency>? expected = null)
    {
        if (transactions.HasTransaction) throw new InvalidOperationException("来源授权参数不能加入本地事务。");
        var scoped = AiKnowledgeScope.IsTenant(tenant); var tenantId = tenant.Id;
        var sources = expected ?? [];
        string[] required = sources.Any(source => source.Faq is not null) ? [.. HistoryPermissions, AiKnowledgeFaqPermissions.Read] : HistoryPermissions;
        var privateSource = sources.Any(source => source.Faq?.RequiresFeedbackPermission == true);
        if (privateSource) required = [.. required, AiKnowledgeFaqPermissions.Feedback];
        if (actor == Guid.Empty || tenantId == Guid.Empty || scoped != tenantId.HasValue
            || !await permissions.IsAuthorizedAsync(required, actor, token).ConfigureAwait(false)
            || tenant.Id != tenantId || AiKnowledgeScope.IsTenant(tenant) != scoped) return null;
        var parents = sources.Any(source => source.Faq is not null) && faqSources is not null
            ? await faqSources.CurrentParentsJsonAsync(sources.Where(source => source.Faq is not null).Select(source => source.Faq!), actor, token).ConfigureAwait(false) : "[]";
        if (parents is null || !await permissions.IsAuthorizedAsync(required, actor, token).ConfigureAwait(false)) return null;
        var parameters = await access.ParametersAsync(actor, token).ConfigureAwait(false);
        parameters["FaqParentBindingsJson"] = parents; parameters["FaqFeedbackReadAllowed"] = privateSource ? 1 : 0;
        if (transactions.HasTransaction || tenant.Id != tenantId || AiKnowledgeScope.IsTenant(tenant) != scoped) return null;
        return parameters;
    }
    /// <summary>整批复核后再映射正文；最终查询覆盖所有消息，避免逐消息等待使前面的资格失效。</summary>
    internal Task<IReadOnlyDictionary<Guid, AiKnowledgeAnswerLineage>> AuthorizeBatchAsync(
        IReadOnlyList<AiKnowledgeAnswerHistoryEntry> entries, Guid actor, CancellationToken token)
        => AuthorizeBoundBatchAsync(entries.Select(entry => new AiKnowledgeAnswerFeedbackHistoryEntry(entry.MessageId,
            entry.SessionId, actor, entry.Json)).ToArray(), actor, token);

    /// <summary>继续对话使用Send用途，不能借历史Read权限取得模型派发资格。</summary>
    internal Task<IReadOnlyDictionary<Guid, AiKnowledgeAnswerLineage>> AuthorizeModelBatchAsync(
        IReadOnlyList<AiKnowledgeAnswerHistoryEntry> entries, Guid actor, CancellationToken token) =>
        AuthorizeBoundBatchAsync(entries.Select(entry => new AiKnowledgeAnswerFeedbackHistoryEntry(entry.MessageId,
            entry.SessionId, actor, entry.Json)).ToArray(), actor, token, forModel: true);

    // 所属人只影响持久化绑定解码；两轮权限、关系和载荷证明始终使用当前读取人。
    private async Task<IReadOnlyDictionary<Guid, AiKnowledgeAnswerLineage>> AuthorizeBoundBatchAsync(
        IReadOnlyList<AiKnowledgeAnswerFeedbackHistoryEntry> entries, Guid actor, CancellationToken token, bool forModel = false)
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
            try { candidates.Add(entry.MessageId, AiKnowledgeAnswerLineageCodec.Decode(entry.Json, entry.MessageId, entry.SessionId, entry.OwnerUserId, tenantId)); }
            catch (InvalidDataException) { /* 损坏记录独立隐藏，不能影响其他有效答案。 */ }
        if (candidates.Count == 0) return empty;
        var dependencies = candidates.Values.SelectMany(lineage => lineage.Dependencies).Distinct().ToArray();
        // 每条最多八项，整批不得通过截断依赖降低授权要求。
        if (dependencies.Length > MaxHistoryAnswers * 8) return empty;
        var dependenciesJson = JsonSerializer.Serialize(dependencies, AiKnowledgeAnswerLineageJsonContext.Default.AiKnowledgeAnswerDependencyArray);
        var includesFaq = dependencies.Any(value => value.Faq is not null);
        var basePermissions = forModel ? ModelPermissions : HistoryPermissions;
        string[] required = includesFaq ? [.. basePermissions, AiKnowledgeFaqPermissions.Read] : basePermissions;
        var requiresFeedback = dependencies.Any(value => value.Faq?.RequiresFeedbackPermission == true);
        if (requiresFeedback) required = [.. required, AiKnowledgeFaqPermissions.Feedback];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(25));
        return await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(async work =>
        {
            var proven = new HashSet<AiKnowledgeAnswerDependency>();
            for (var pass = 0; pass < 2; pass++)
            {
                if (!SameScope() || !await permissions.IsAuthorizedAsync(required, actor, work).ConfigureAwait(false) || !SameScope()) return empty;
                await FilterSchedulesAsync(candidates, actor, work).ConfigureAwait(false);
                if (!SameScope()) return empty;
                var parameters = await access.ParametersAsync(actor, work).ConfigureAwait(false);
                if (!SameScope()) return empty;
                parameters["DependenciesJson"] = dependenciesJson;
                parameters["FaqParentBindingsJson"] = "[]";
                parameters["FaqFeedbackReadAllowed"] = requiresFeedback ? 1 : 0;
                var statement = includesFaq && faqSources is not null ? AiKnowledgeRetrievalSql.CombinedHistoryFor(database.Value.Provider, scoped) : AiKnowledgeIndexStatusSql.HistoryFor(database.Value.Provider, scoped);
                var rows = await query.QueryAsync<AiKnowledgeIndexStatusRecord>(statement, parameters, work).ConfigureAwait(false);
                if (!SameScope() || rows.Select(row => (row.DocumentId, row.VersionId)).Distinct().Count() != rows.Count
                    || rows.Any(row => !dependencies.Any(dependency => dependency.KnowledgeBaseId == row.KnowledgeBaseId
                        && dependency.DocumentId == row.DocumentId && dependency.VersionId == row.VersionId))) return empty;
                foreach (var candidate in candidates.ToArray())
                    if (candidate.Value.Dependencies.Any(dependency => !rows.Any(row => AiKnowledgeAnswerLineageCodec.Matches(dependency, row))
                        || (pass == 1 && !proven.Contains(dependency)))) candidates.Remove(candidate.Key);
                if (pass == 1)
                    foreach (var dependency in candidates.Values.SelectMany(value => value.Dependencies).Where(value => value.Faq is not null).Distinct().ToArray())
                    {
                        var row = rows.Single(value => AiKnowledgeAnswerLineageCodec.Matches(dependency, value));
                        var current = faqSources is null ? null : await faqSources.ReadAsync(row, actor, work, verifyPayloads: false).ConfigureAwait(false);
                        if (!AiKnowledgeFaqPublishedSourceReader.Same(dependency.Faq, current))
                            foreach (var candidate in candidates.Where(value => value.Value.Dependencies.Contains(dependency)).ToArray()) candidates.Remove(candidate.Key);
                    }
                if (pass == 0)
                    foreach (var dependency in candidates.Values.SelectMany(lineage => lineage.Dependencies).Distinct().ToArray())
                    {
                        var row = rows.Single(row => AiKnowledgeAnswerLineageCodec.Matches(dependency, row));
                        var sourceParameters = new Dictionary<string, object?>(parameters) { ["KnowledgeBaseId"] = row.KnowledgeBaseId,
                            ["DocumentId"] = row.DocumentId, ["VersionId"] = row.VersionId };
                        var faq = dependency.Faq is not null && faqSources is not null ? await faqSources.ReadAsync(row, actor, work, verifyPayloads: true).ConfigureAwait(false) : null;
                        if (dependency.Faq is not null ? AiKnowledgeFaqPublishedSourceReader.Same(dependency.Faq, faq)
                            : await proof.VerifyAsync(row, sourceParameters, work).ConfigureAwait(false))
                        {
                            var relevant = candidates.Values.Where(c => c.Dependencies.Contains(dependency)).SelectMany(c => c.References)
                                .Where(r => r.DocumentId == row.DocumentId && r.VersionId == row.VersionId).ToArray();
                            var accepted = dependency.Faq is not null && faqSources is not null
                                ? await faqSources.MatchReferencesAsync(row, actor, relevant, work).ConfigureAwait(false)
                                : await proof.MatchReferencesAsync(row, sourceParameters, relevant, work).ConfigureAwait(false);
                            foreach (var candidate in candidates.ToArray())
                                if (candidate.Value.Dependencies.Contains(dependency) && candidate.Value.References.Any(r => r.DocumentId == row.DocumentId
                                    && r.VersionId == row.VersionId && !accepted.Contains(r))) candidates.Remove(candidate.Key);
                            proven.Add(dependency);
                        }
                        if (!SameScope()) return empty;
                    }
                if (pass == 1 && includesFaq)
                {
                    var parents = await faqSources!.CurrentParentsJsonAsync(candidates.Values.SelectMany(value => value.Dependencies)
                        .Where(value => value.Faq is not null).Select(value => value.Faq!).Distinct(), actor, work).ConfigureAwait(false);
                    if (parents is null) return empty;
                    if (!await permissions.IsAuthorizedAsync(required, actor, work).ConfigureAwait(false)) return empty;
                    var final = await access.ParametersAsync(actor, work).ConfigureAwait(false); final["DependenciesJson"] = dependenciesJson;
                    final["FaqParentBindingsJson"] = parents;
                    final["FaqFeedbackReadAllowed"] = requiresFeedback ? 1 : 0;
                    var current = await query.QueryAsync<AiKnowledgeIndexStatusRecord>(statement, final, work).ConfigureAwait(false);
                    if (!SameScope()) return empty;
                    foreach (var candidate in candidates.ToArray())
                        if (candidate.Value.Dependencies.Any(value => current.Count(row => AiKnowledgeAnswerLineageCodec.Matches(value, row)) != 1)) candidates.Remove(candidate.Key);
                }
            }
            await FilterSchedulesAsync(candidates, actor, work).ConfigureAwait(false);
            if (!SameScope()) return empty;
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
        var includesFaq = lineage.Dependencies.Any(value => value.Faq is not null);
        string[] required = forModel ? ModelPermissions : HistoryPermissions;
        if (includesFaq) required = [.. required, AiKnowledgeFaqPermissions.Read];
        var requiresFeedback = lineage.Dependencies.Any(value => value.Faq?.RequiresFeedbackPermission == true);
        if (requiresFeedback) required = [.. required, AiKnowledgeFaqPermissions.Feedback];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(25));
        return await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(async work =>
        {
            // 第二轮重新读取角色和机构，不能把第一轮等待前的资格作为交付依据。
            for (var pass = 0; pass < 2; pass++)
            {
                if (!SameScope()) return null;
                if (!await permissions.IsAuthorizedAsync(required, actor, work).ConfigureAwait(false) || !SameScope()) return null;
                if (!await VerifySchedulesAsync(lineage, actor, work).ConfigureAwait(false) || !SameScope()) return null;
                var parameters = await access.ParametersAsync(actor, work).ConfigureAwait(false);
                if (!SameScope()) return null;
                parameters["DependenciesJson"] = JsonSerializer.Serialize(lineage.Dependencies.ToArray(), AiKnowledgeAnswerLineageJsonContext.Default.AiKnowledgeAnswerDependencyArray);
                parameters["FaqParentBindingsJson"] = "[]";
                parameters["FaqFeedbackReadAllowed"] = requiresFeedback ? 1 : 0;
                var statement = includesFaq && faqSources is not null ? AiKnowledgeRetrievalSql.CombinedHistoryFor(database.Value.Provider, scoped) : AiKnowledgeIndexStatusSql.HistoryFor(database.Value.Provider, scoped);
                var rows = await query.QueryAsync<AiKnowledgeIndexStatusRecord>(statement, parameters, work).ConfigureAwait(false);
                if (!SameScope() || rows.Count != lineage.Dependencies.Count || rows.Select(row => row.DocumentId).Distinct().Count() != rows.Count
                    || lineage.Dependencies.Any(dependency => !rows.Any(row => AiKnowledgeAnswerLineageCodec.Matches(dependency, row)))) return null;
                if (pass == 1)
                    foreach (var dependency in lineage.Dependencies.Where(value => value.Faq is not null))
                    {
                        var source = rows.Single(value => AiKnowledgeAnswerLineageCodec.Matches(dependency, value));
                        if (faqSources is null || !AiKnowledgeFaqPublishedSourceReader.Same(dependency.Faq,
                            await faqSources.ReadAsync(source, actor, work, verifyPayloads: false).ConfigureAwait(false))) return null;
                    }
                if (pass == 0)
                    foreach (var row in rows)
                    {
                        var sourceParameters = new Dictionary<string, object?>(parameters) { ["KnowledgeBaseId"] = row.KnowledgeBaseId,
                            ["DocumentId"] = row.DocumentId, ["VersionId"] = row.VersionId };
                        // 活动指针存在不代表回执、清单和载荷完整；证明实际派生数据后再进入最终权限/来源查询。
                        var dependency = lineage.Dependencies.Single(value => AiKnowledgeAnswerLineageCodec.Matches(value, row));
                        if (dependency.Faq is not null)
                        {
                            if (faqSources is null || !AiKnowledgeFaqPublishedSourceReader.Same(dependency.Faq,
                                await faqSources.ReadAsync(row, actor, work, verifyPayloads: true).ConfigureAwait(false))) return null;
                        }
                        else if (!await proof.VerifyAsync(row, sourceParameters, work).ConfigureAwait(false)) return null;
                        if (!SameScope()) return null;
                        var relevant = lineage.References.Where(r => r.DocumentId == row.DocumentId && r.VersionId == row.VersionId).ToArray();
                        var accepted = dependency.Faq is not null && faqSources is not null
                            ? await faqSources.MatchReferencesAsync(row, actor, relevant, work).ConfigureAwait(false)
                            : await proof.MatchReferencesAsync(row, sourceParameters, relevant, work).ConfigureAwait(false);
                        if (!SameScope() || relevant.Any(r => !accepted.Contains(r))) return null;
                    }
                if (pass == 1 && includesFaq)
                {
                    var parents = await faqSources!.CurrentParentsJsonAsync(lineage.Dependencies.Where(value => value.Faq is not null).Select(value => value.Faq!), actor, work).ConfigureAwait(false);
                    if (parents is null) return null;
                    if (!await permissions.IsAuthorizedAsync(required, actor, work).ConfigureAwait(false)) return null;
                    var final = await access.ParametersAsync(actor, work).ConfigureAwait(false);
                    final["DependenciesJson"] = parameters["DependenciesJson"];
                    final["FaqParentBindingsJson"] = parents;
                    final["FaqFeedbackReadAllowed"] = requiresFeedback ? 1 : 0;
                    var current = await query.QueryAsync<AiKnowledgeIndexStatusRecord>(statement, final, work).ConfigureAwait(false);
                    if (!SameScope() || current.Count != lineage.Dependencies.Count
                        || lineage.Dependencies.Any(value => current.Count(row => AiKnowledgeAnswerLineageCodec.Matches(value, row)) != 1)) return null;
                }
            }
            if (!await VerifySchedulesAsync(lineage, actor, work).ConfigureAwait(false) || !SameScope()) return null;
            work.ThrowIfCancellationRequested(); return lineage;
        }, deadline.Token).ConfigureAwait(false);

        bool SameScope()
        {
            if (transactions.HasTransaction) throw new InvalidOperationException("来源复核不能加入本地事务。");
            return tenant.Id == tenantId && AiKnowledgeScope.IsTenant(tenant) == scoped;
        }
    }
    // 相同完整来源集合在一轮历史中只核证一次；不同版本绝不按 UUID 合并或忽略未引用项。
    private async Task FilterSchedulesAsync(Dictionary<Guid, AiKnowledgeAnswerLineage> candidates, Guid actor, CancellationToken token)
    {
        foreach (var candidate in candidates.Where(value => value.Value.PersonalSchedules is { Count: > 0 }).ToArray())
            if (candidate.Value.PersonalSchedules!.Any(source => !AiPersonalScheduleAnswerSources.Valid(source, actor, tenant.Id))) candidates.Remove(candidate.Key);
        var groups = candidates.Where(value => value.Value.PersonalSchedules is { Count: > 0 }).GroupBy(value => string.Join("|",
            value.Value.PersonalSchedules!.OrderBy(source => source.Id).Select(source => $"{source.Id:D}:{source.Version}:{source.ProjectionHash}")));
        foreach (var group in groups.ToArray())
            if (!await VerifySchedulesAsync(group.First().Value, actor, token).ConfigureAwait(false))
                foreach (var candidate in group) candidates.Remove(candidate.Key);
    }

    private async Task<bool> VerifySchedulesAsync(AiKnowledgeAnswerLineage lineage, Guid actor, CancellationToken token)
    {
        if (lineage.PersonalSchedules is not { Count: > 0 } proofs) return true;
        if (personalSchedules is null) return false;
        try { return await personalSchedules.RevalidateAsync(actor, proofs.ToArray(), token).ConfigureAwait(false); }
        catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); return false; }
        catch (Exception) { return false; }
    }
}
