using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Serialization;
using Microsoft.Extensions.Options;
using Full.NET.AI.Abstractions.Models;

namespace Full.NET.Modules.Ai.Knowledge;

internal sealed record AiKnowledgeFaqPreparedFeedback(AiKnowledgeFaqProvenance Origin, string Answer, AiKnowledgeBaseRecord Target);

/// <summary>窄用途授权后捕获服务器原回答和完整谱系；与静态来源门禁分开，避免历史服务与 FAQ 循环依赖。</summary>
internal sealed class AiKnowledgeFaqFeedbackPreparation(IQueryExecutor query, IDataTransactionState transactions, ICurrentTenant tenant,
    IOptions<DatabaseOptions> database, AiKnowledgeReadAccess access, AiKnowledgePermissionGuard permissions,
    AiKnowledgeAnswerHistoryGuard history, AiKnowledgeFaqPublishedSourceReader faqs, AiKnowledgeFaqDraftSourceGuard guard, AiDataProcessingPolicy processing)
{
    private static readonly string[] WriteRequired = [AiKnowledgePermissions.Read, AiKnowledgeFaqPermissions.Read, AiKnowledgeFaqPermissions.Write, AiKnowledgeFaqPermissions.Feedback,
        AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.IndexRead];
    private static readonly string[] Required = [.. WriteRequired, AiKnowledgeAnswerFeedbackReviewPermissions.Read, AiChatPermissions.Read];

    internal async Task<AiKnowledgeFaqPreparedFeedback?> PrepareAsync(Guid knowledge, Guid feedback, Guid actor, CancellationToken token)
    {
        var scope = tenant.Id; var scoped = AiKnowledgeScope.IsTenant(tenant); Check();
        if (knowledge == Guid.Empty || feedback == Guid.Empty || !await permissions.IsAuthorizedAsync(Required, actor, token).ConfigureAwait(false)
            || !processing.Allows("context", "restricted")) return null;
        Check();
        // 先核对目标所有权和私人分类，不能为无效或低分类目标提前读取原回答正文。
        var targetParameters = await access.ParametersAsync(actor, token).ConfigureAwait(false); targetParameters["Id"] = knowledge;
        var target = await query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(AiKnowledgeSql.ReadFor(database.Value.Provider, scoped), targetParameters, token).ConfigureAwait(false);
        Check(); if (target is not { IsEnabled: true, DataClassification: "restricted" } || target.OwnerUserId != actor) return null;
        var record = await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqFeedbackRecord>(AiKnowledgeFaqFeedbackSql.FindFor(scoped),
            new Dictionary<string, object?> { ["FeedbackId"] = feedback }, token).ConfigureAwait(false);
        Check(); if (record is null || record.TenantId != scope || record.Answer.Length is < 1 or > 8000
            // 反馈既有协议保存小写 SHA-256；FAQ 内容摘要使用大写，先按反馈协议比较，不能因大小写误拒绝真实批准记录。
            || AiKnowledgeFaqDraftPolicy.Hash(record.KnowledgeLineage).ToLowerInvariant() != record.LineageHash) return null;
        var proven = await history.AuthorizeFeedbackReviewBatchAsync([new(record.MessageId, record.SessionId, record.OwnerUserId, record.KnowledgeLineage)], actor, token).ConfigureAwait(false);
        Check(); if (!proven.TryGetValue(record.MessageId, out var lineage)) return null;
        // 问题只是正文规范化的固定占位，不读取原会话的用户问题，也不把占位值持久化。
        var answer = AiKnowledgeFaqDraftPolicy.Freeze(new("待人工填写标准问题", [], record.Answer, "反馈", null, null)).Answer;
        var p = await access.ParametersAsync(actor, token).ConfigureAwait(false); Check();
        p["DependenciesJson"] = JsonSerializer.Serialize(lineage.Dependencies.ToArray(), AiKnowledgeAnswerLineageJsonContext.Default.AiKnowledgeAnswerDependencyArray);
        var includeFaq = lineage.Dependencies.Any(value => value.Faq is not null);
        var parents = includeFaq ? await faqs.CurrentParentsJsonAsync(lineage.Dependencies.Where(value => value.Faq is not null).Select(value => value.Faq!), actor, token).ConfigureAwait(false) : "[]";
        Check(); if (parents is null) return null;
        p["FaqParentBindingsJson"] = parents; p["FaqFeedbackReadAllowed"] = 1;
        var rows = await query.QueryAsync<AiKnowledgeIndexStatusRecord>(includeFaq ? AiKnowledgeRetrievalSql.CombinedHistoryFor(database.Value.Provider, scoped)
            : AiKnowledgeIndexStatusSql.HistoryFor(database.Value.Provider, scoped), p, token).ConfigureAwait(false);
        Check(); if (rows.Count != lineage.Dependencies.Count || lineage.Dependencies.Any(value => rows.Count(row => AiKnowledgeAnswerLineageCodec.Matches(value, row)) != 1)) return null;
        var captured = new Dictionary<Guid, AiKnowledgeFaqAnswerSource>();
        foreach (var row in rows.Where(row => row.FaqRevisionId.HasValue))
        {
            var original = lineage.Dependencies.Single(value => value.DocumentId == row.DocumentId).Faq;
            var current = await faqs.ReadAsync(row, actor, token, verifyPayloads: true).ConfigureAwait(false);
            Check(); if (current is null || !AiKnowledgeFaqPublishedSourceReader.Same(original, current)) return null; captured.Add(row.DocumentId, current);
        }
        var basis = includeFaq ? AiKnowledgeFaqGraphs.Capture(rows, captured)
            : new AiKnowledgeFaqProvenance(1, "document_derived", null, lineage.Dependencies.ToArray());
        var binding = new AiKnowledgeFaqFeedbackBinding(record.FeedbackId, record.FeedbackVersion, record.ReviewId, record.ReviewVersion,
            record.MessageId, record.SessionId, record.OwnerUserId, record.TenantId, record.LineageHash,
            AiKnowledgeFaqFeedbackBinding.UnicodeHash(record.KnowledgeLineage), AiKnowledgeFaqDraftPolicy.Hash(record.Answer), AiKnowledgeFaqFeedbackBinding.UnicodeHash(record.Answer));
        var origin = new AiKnowledgeFaqProvenance(lineage.PersonalSchedules is null ? 3 : 4, "feedback_derived", null, basis.Dependencies)
            { Feedback = binding, Graph = basis.Graph, PersonalSchedules = AiKnowledgeFaqBusinessSources.Freeze(lineage.PersonalSchedules) };
        var json = AiKnowledgeFaqProvenanceCodec.Encode(origin);
        var candidate = new AiKnowledgeFaqDraftRecord { Id = Guid.CreateVersion7(), KnowledgeBaseId = knowledge, TenantId = scope,
            OriginKey = origin.Origin, SourceJson = json, SourceHash = AiKnowledgeFaqDraftPolicy.Hash(json) };
        if (!(await guard.AuthorizeAsync([candidate], actor, AiKnowledgeFaqPermissions.Feedback, token, requireStored: false).ConfigureAwait(false)).Contains(candidate.Id)) return null;
        Check();
        var write = await WriteParametersAsync(knowledge, origin, actor, token).ConfigureAwait(false);
        return write is null ? null : new(origin, answer, write.Value.Root);
        void Check() { token.ThrowIfCancellationRequested(); if (transactions.HasTransaction || tenant.Id != scope || AiKnowledgeScope.IsTenant(tenant) != scoped) throw new UnauthorizedAccessException(); }
    }

    /// <summary>事务前捕获权威权限/机构/模型，事务内固定根锁之后以联合 SQL 复核当前反馈、父及叶。</summary>
    internal async Task<(AiKnowledgeBaseRecord Root, Dictionary<string, object?> Parameters)?> WriteParametersAsync(Guid knowledge,
        AiKnowledgeFaqProvenance origin, Guid actor, CancellationToken token)
    {
        var scope = tenant.Id; var scoped = AiKnowledgeScope.IsTenant(tenant);
        if (transactions.HasTransaction) throw new InvalidOperationException("反馈来源不能在事务内调用授权端口。");
        var required = origin.RequiresFeedbackPermission ? WriteRequired : WriteRequired.Where(value => value != AiKnowledgeFaqPermissions.Feedback).ToArray();
        if (!(origin.RequiresFeedbackPermission || origin.PersonalSchedules is not null) || !await permissions.IsAuthorizedAsync(required, actor, token).ConfigureAwait(false)) return null;
        var parents = await guard.ParentBindingsAsync(origin, actor, token, verifyPayloads: false).ConfigureAwait(false);
        if (parents is null || !await permissions.IsAuthorizedAsync(required, actor, token).ConfigureAwait(false)) return null;
        var p = await access.ParametersAsync(actor, token).ConfigureAwait(false); p["Id"] = knowledge;
        var root = await query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(AiKnowledgeSql.ReadFor(database.Value.Provider, scoped), p, token).ConfigureAwait(false);
        if (transactions.HasTransaction || tenant.Id != scope || AiKnowledgeScope.IsTenant(tenant) != scoped) throw new UnauthorizedAccessException();
        if (root is not { IsEnabled: true, DataClassification: "restricted" } || root.OwnerUserId != actor) return null;
        p["KnowledgeBaseId"] = knowledge; p["OwnerUserId"] = actor; p["TargetKnowledgeVersion"] = root.Version; p["TargetClassification"] = root.DataClassification;
        p["FaqFeedbackReadAllowed"] = origin.RequiresFeedbackPermission ? 1 : 0;
        p["FaqParentBindingsJson"] = JsonSerializer.Serialize(parents.ToArray(), AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqParentAuthorizationArray);
        p["SourceJson"] = AiKnowledgeFaqProvenanceCodec.Encode(origin); p["OriginKey"] = origin.Origin;
        return (root, p);
    }
    internal static string Digest(AiKnowledgeFaqPreparedFeedback prepared, Guid actor) => AiKnowledgeFaqDraftPolicy.Hash("faq_feedback_preview_v1\n"
        + actor.ToString("D") + "\n" + prepared.Target.Id.ToString("D") + "\n" + prepared.Target.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + "\n" + prepared.Target.DataClassification + "\n" + AiKnowledgeFaqProvenanceCodec.Encode(prepared.Origin));
}
