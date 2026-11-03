using System.Text.Json;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Features.ManageAnswerFeedbackReview;
using Full.NET.Modules.Ai.Features.ManageKnowledgeFaqs;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Serialization;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>评估只读取私人答案以计算服务器摘要；公开交付始终只有人工脱敏字段，不复用 FAQ 私人正文权限。</summary>
internal sealed class AiKnowledgeEvaluationSourceAuthority(IQueryExecutor query, IDataTransactionState transactions,
    ICurrentTenant tenant, IOptions<DatabaseOptions> database, AiKnowledgeReadAccess access,
    AiKnowledgePermissionGuard permissions, AiFeedbackEvaluationToken candidates, AiKnowledgeAnswerFeedbackReviewService reviews,
    AiKnowledgeAnswerHistoryGuard history, AiKnowledgeFaqPublishedSourceReader faqs, AiKnowledgeFaqDraftSourceGuard parents,
    AiDataProcessingPolicy processing)
{
    private static readonly string[] PrivatePermissions = [AiKnowledgeEvaluationPermissions.Feedback, AiFeedbackEvaluationPermissions.Admit,
        AiKnowledgeAnswerFeedbackReviewPermissions.Read, AiChatPermissions.Read, AiKnowledgePermissions.Read,
        AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.IndexRead];

    /// <summary>最后原会话授权必须包含真实父 FAQ 的额外权限；直接反馈不隐式获得 FAQ 私人正文权。</summary>
    internal static IReadOnlyList<string> RequiredFor(AiKnowledgeEvaluationFootprint footprint)
    {
        if (footprint.Sources.Count == 0) return [];
        var origins = footprint.Sources.Select(AiKnowledgeEvaluationPolicy.Origin).ToArray();
        return PrivatePermissions.Concat(origins.Any(origin => origin.Graph is not null) ? AiKnowledgeFaqLifecycleAccess.ReadPermissions : Array.Empty<string>())
            .Concat(origins.Any(origin => origin.Graph?.Nodes.Any(node => node.Source.FeedbackDerived) == true) ? new[] { AiKnowledgeFaqPermissions.Feedback } : Array.Empty<string>())
            .Distinct(StringComparer.Ordinal).ToArray();
    }

    internal async Task<(AiFeedbackEvaluationSample Sample, string Source, DateTimeOffset ExpiresAtUtc)?> CaptureOriginalAsync(string candidate, Guid actor,
        Guid session, CancellationToken token)
    {
        using var phase = CancellationTokenSource.CreateLinkedTokenSource(token); phase.CancelAfter(TimeSpan.FromSeconds(25)); token = phase.Token;
        var scope = tenant.Id; var scoped = AiKnowledgeScope.IsTenant(tenant); Outside();
        if (!await PrivateAsync(actor, token).ConfigureAwait(false)) return null;
        var envelope = candidates.Read(candidate, actor, scope, session);
        if (envelope is null) return null;
        var approved = await reviews.ProveEvaluationAsync(envelope.FeedbackId, actor, envelope.FeedbackVersion,
            envelope.ReviewVersion, AiFeedbackEvaluationPermissions.Admit, session, token).ConfigureAwait(false);
        Check();
        if (approved is null || AiFeedbackEvaluationService.SourceHash(approved) != envelope.SourceBindingHash) return null;
        var row = await FindAsync(envelope.FeedbackId, token).ConfigureAwait(false); Check();
        if (row is null || row.OwnerUserId == actor || row.MessageId != approved.MessageId || row.SessionId != approved.SessionId
            || row.FeedbackVersion != envelope.FeedbackVersion || row.ReviewVersion != envelope.ReviewVersion) return null;
        var proven = await history.AuthorizeFeedbackReviewBatchAsync([new(row.MessageId, row.SessionId, row.OwnerUserId, row.KnowledgeLineage)], actor, token).ConfigureAwait(false);
        Check(); if (!proven.TryGetValue(row.MessageId, out var lineage)) return null;
        var parameters = await access.ParametersAsync(actor, token).ConfigureAwait(false); Check();
        parameters["DependenciesJson"] = JsonSerializer.Serialize(lineage.Dependencies.ToArray(), AiKnowledgeAnswerLineageJsonContext.Default.AiKnowledgeAnswerDependencyArray);
        var includesFaq = lineage.Dependencies.Any(value => value.Faq is not null);
        var graph = includesFaq ? await faqs.CurrentParentsJsonAsync(lineage.Dependencies.Where(value => value.Faq is not null).Select(value => value.Faq!), actor, token).ConfigureAwait(false) : "[]";
        Check(); if (graph is null) return null;
        parameters["FaqParentBindingsJson"] = graph;
        parameters["FaqFeedbackReadAllowed"] = lineage.Dependencies.Any(value => value.Faq?.RequiresFeedbackPermission == true) ? 1 : 0;
        var rows = await query.QueryAsync<AiKnowledgeIndexStatusRecord>(includesFaq
            ? AiKnowledgeRetrievalSql.CombinedHistoryFor(database.Value.Provider, scoped)
            : AiKnowledgeIndexStatusSql.HistoryFor(database.Value.Provider, scoped), parameters, token).ConfigureAwait(false);
        Check();
        if (rows.Count != lineage.Dependencies.Count || lineage.Dependencies.Any(source => rows.Count(value => AiKnowledgeAnswerLineageCodec.Matches(source, value)) != 1)) return null;
        var captured = new Dictionary<Guid, AiKnowledgeFaqAnswerSource>();
        foreach (var source in lineage.Dependencies.Where(value => value.Faq is not null))
        {
            var current = await faqs.ReadAsync(rows.Single(row => AiKnowledgeAnswerLineageCodec.Matches(source, row)), actor, token, verifyPayloads: true).ConfigureAwait(false);
            Check(); if (current is null || !AiKnowledgeFaqPublishedSourceReader.Same(source.Faq, current)) return null;
            captured.Add(source.DocumentId, current);
        }
        var basis = includesFaq ? AiKnowledgeFaqGraphs.Capture(rows, captured)
            : new AiKnowledgeFaqProvenance(1, "document_derived", null, lineage.Dependencies.ToArray());
        var origin = new AiKnowledgeFaqProvenance(lineage.PersonalSchedules is null ? 3 : 4, "feedback_derived", null, basis.Dependencies)
        {
            Graph = basis.Graph,
            Feedback = Binding(row),
            PersonalSchedules = AiKnowledgeFaqBusinessSources.Freeze(lineage.PersonalSchedules)
        };
        var json = AiKnowledgeFaqProvenanceCodec.Encode(origin);
        // 凭据只用于本次捕获，绝不持久化；写入前和交付前仍以完整服务器来源重新核证。
        if (candidates.Read(candidate, actor, scope, session) != envelope || !await PrivateAsync(actor, token).ConfigureAwait(false)) return null;
        Check(); return (envelope.Sample, json, envelope.ExpiresAtUtc);

        void Check() { token.ThrowIfCancellationRequested(); Outside(); if (tenant.Id != scope || AiKnowledgeScope.IsTenant(tenant) != scoped) throw new UnauthorizedAccessException(); }
    }

    /// <summary>返回事务前已核证的全部父节点参数；最后联合 SQL 必须继续核对父、叶及反馈，不能只采用本返回值。</summary>
    internal async Task<string?> ProveAsync(AiKnowledgeEvaluationFootprint footprint, Guid actor, CancellationToken token)
    {
        using var phase = CancellationTokenSource.CreateLinkedTokenSource(token); phase.CancelAfter(TimeSpan.FromSeconds(25)); token = phase.Token;
        Outside(); var scope = tenant.Id; var scoped = AiKnowledgeScope.IsTenant(tenant);
        _ = AiKnowledgeEvaluationPolicy.Encode(footprint);
        if (footprint.Sources.Count == 0) return "[]";
        if (!await PrivateAsync(actor, token).ConfigureAwait(false)) return null;
        var result = new List<AiKnowledgeFaqParentAuthorization>();
        foreach (var source in footprint.Sources)
        {
            var origin = AiKnowledgeEvaluationPolicy.Origin(source);
            if (!await parents.BusinessAsync(origin, actor, token).ConfigureAwait(false)) return null;
            if (origin.Feedback is not { } binding || binding.TenantId != scope) return null;
            var row = await FindAsync(binding.FeedbackId, token).ConfigureAwait(false); Check();
            if (row is null || row.OwnerUserId == actor || Binding(row) != binding) return null;
            var proven = await history.AuthorizeFeedbackReviewBatchAsync([new(row.MessageId, row.SessionId, row.OwnerUserId, row.KnowledgeLineage)], actor, token).ConfigureAwait(false);
            Check(); if (!proven.ContainsKey(row.MessageId)) return null;
            var bound = await parents.ParentBindingsAsync(origin, actor, token, verifyPayloads: true).ConfigureAwait(false);
            Check(); if (bound is null) return null;
            result.AddRange(bound);
        }
        if (!await PrivateAsync(actor, token).ConfigureAwait(false)) return null;
        Check();
        // 相同祖先可以由多个反馈共享；保留全部证明，不按文档或标题合并不同修订。
        return JsonSerializer.Serialize(result.ToArray(), AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqParentAuthorizationArray);

        void Check() { token.ThrowIfCancellationRequested(); Outside(); if (tenant.Id != scope || AiKnowledgeScope.IsTenant(tenant) != scoped) throw new UnauthorizedAccessException(); }
    }

    private async Task<bool> PrivateAsync(Guid actor, CancellationToken token)
    {
        Outside();
        return processing.Allows("context", "restricted") && await permissions.IsAuthorizedAsync(PrivatePermissions, actor, token).ConfigureAwait(false);
    }

    private Task<AiKnowledgeFaqFeedbackRecord?> FindAsync(Guid feedback, CancellationToken token) => query.QuerySingleOrDefaultAsync<AiKnowledgeFaqFeedbackRecord>(
        AiKnowledgeFaqFeedbackSql.FindFor(AiKnowledgeScope.IsTenant(tenant)), new Dictionary<string, object?> { ["FeedbackId"] = feedback }, token);

    private static AiKnowledgeFaqFeedbackBinding Binding(AiKnowledgeFaqFeedbackRecord row) => new(row.FeedbackId, row.FeedbackVersion, row.ReviewId,
        row.ReviewVersion, row.MessageId, row.SessionId, row.OwnerUserId, row.TenantId, row.LineageHash,
        AiKnowledgeFaqFeedbackBinding.UnicodeHash(row.KnowledgeLineage), AiKnowledgeFaqDraftPolicy.Hash(row.Answer), AiKnowledgeFaqFeedbackBinding.UnicodeHash(row.Answer));
    private void Outside() { if (transactions.HasTransaction) throw new InvalidOperationException("评估来源授权不能加入事务。"); }
}
