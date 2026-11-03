using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Features.ManageKnowledgeFaqs;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>只读取一个父节点的不可变修订与当前模型；调用者授权整批，并在最后 SQL 联合核对所有节点。</summary>
/// <remarks>此内部结果不交付正文，也不是授权凭证；完整载荷证明按本次门禁执行，不能跨请求或阶段缓存。</remarks>
internal sealed class AiKnowledgeFaqNodeReader(IQueryExecutor query, IDataTransactionState transactions, ICurrentTenant tenant,
    IOptions<DatabaseOptions> database, AiKnowledgeReadAccess access, AiKnowledgeIndexActivationStore proof)
{
    internal async Task<AiKnowledgeFaqNodeRead?> ReadAsync(AiKnowledgeFaqParentNode node, Guid actor,
        CancellationToken token, bool verifyPayloads, IReadOnlyList<AiKnowledgeBaseRecord> libraries, AiKnowledgeFaqGraph? ancestors)
    {
        var tenantId = tenant.Id; var scoped = AiKnowledgeScope.IsTenant(tenant); Check(); var source = node.Status;
        if (source.FaqRevisionId is not { } revision || source.FileId is not null || source.PublicationId is null) return null;
        var model = await query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(scoped ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat,
            new Dictionary<string, object?> { ["ModelConfigId"] = source.ModelConfigId }, token).ConfigureAwait(false); Check();
        if (model is null || model.Id != source.ModelConfigId || model.Version != source.ModelConfigVersion) return null;
        if (!verifyPayloads)
        {
            // 阶段绑定不交付正文。父身份来自服务器冻结图；当前模型/分类重新读取，最终 SQL 仍核对发布、摘要及完整代次。
            var current = AiKnowledgeFaqClassification.ForParentLibraries(source.KnowledgeBaseId,
                node.Source.Dependencies.Select(d => d.KnowledgeBaseId).Concat(ancestors?.Nodes.Select(n => n.Status.KnowledgeBaseId) ?? []), libraries);
            if (current is null || current.Value != source.DataClassification
                || (node.Source.FeedbackDerived || node.Source.PersonalSchedules is not null || ancestors?.Nodes.Any(n => n.Source.FeedbackDerived) == true) && current.Root.DataClassification != "restricted") return null;
            return new(node.Source with { Graph = ancestors }, model, current.Root, current.Libraries, node.Publication);
        }
        var p = await access.ParametersAsync(actor, token).ConfigureAwait(false); Check();
        p["KnowledgeBaseId"] = source.KnowledgeBaseId; p["DocumentId"] = source.DocumentId; p["RevisionId"] = revision; p["FaqId"] = node.Source.FaqId;
        var root = await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqDraftRecord>(AiKnowledgeFaqLifecycleSql.RevisionFor(database.Value.Provider, scoped), p, token).ConfigureAwait(false);
        if (root is null || root.Id != node.Source.FaqId || root.TenantId != tenantId || root.RevisionId != revision
            || root.SourceHash != node.Source.SourceHash || root.ContentHash != node.Source.ContentHash || root.ReservedDocumentId != source.DocumentId) return null;
        try
        {
            p["Id"] = source.PublicationId;
            var published = await query.QuerySingleOrDefaultAsync<AiKnowledgePublicationRecord>(scoped ? AiKnowledgePublicationSql.FindTenant : AiKnowledgePublicationSql.FindHost, p, token).ConfigureAwait(false);
            if (published is null || published.StateKey != "published" || published.Id != node.Publication.Id
                || published.Version != node.Publication.Version || published.SnapshotHash != node.Publication.SnapshotHash) return null;
            // 这些是待联合核对的预期发布字段，不把客户端输入或构造对象当作真实 release 行。
            var expected = new AiKnowledgeFaqReleaseRecord { Id = source.PublicationId.Value, FaqId = root.Id, RevisionId = revision,
                VersionId = source.VersionId, SourceHash = root.SourceHash, ContentHash = root.ContentHash };
            var snapshot = AiKnowledgeFaqPublicationService.Decode(published, expected);
            if (snapshot.Source with { PublicationId = source.PublicationId } != source) return null;
            var content = AiKnowledgeFaqLifecycleAccess.Content(root);
            var body = AiKnowledgeFaqIndexContent.Create(content);
            if (body.Hash != source.ContentHash || body.SizeBytes != source.SizeBytes) return null;
            var provenance = AiKnowledgeFaqProvenanceCodec.Decode(root);
            var owned = new Dictionary<string, object?>(p) { ["OwnerUserId"] = source.OwnerUserId, ["ModelConfigId"] = source.ModelConfigId, ["VersionId"] = source.VersionId };
            var original = await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqIndexSourceRecord>(scoped ? AiKnowledgeFaqIndexSql.FindTenant : AiKnowledgeFaqIndexSql.FindHost, owned, token).ConfigureAwait(false);
            if (original is null || original.RevisionId != revision || original.SourceHash != root.SourceHash
                || original.RevisionHash != root.ContentHash || original.BodyHash != source.ContentHash || original.SizeBytes != source.SizeBytes
                || original.DataClassification != source.DataClassification || model.Id != source.ModelConfigId || model.Version != source.ModelConfigVersion
                || original.ModelFingerprint != AiKnowledgeFaqModelFingerprint.Create(model)) return null;
            var policy = AiKnowledgeFaqClassification.ForParent(root.KnowledgeBaseId, provenance, libraries);
            if (policy is null || policy.Value != source.DataClassification) return null;
            p["VersionId"] = source.VersionId; p["TaskId"] = source.TaskId; p["PublicationId"] = source.PublicationId;
            if (verifyPayloads && !await proof.IsReadyForReadAsync(source, p, token).ConfigureAwait(false)) return null;
            // 已校验快照摘要与内容；发布版本、完整代次和模型最后由整批 SQL 同时验证，不重复读取大快照。
            Check(); return new(new(root.Id, revision, root.SourceHash, root.ContentHash, model.Id, model.Version, provenance.Dependencies)
                { Graph = provenance.Graph, PersonalSchedules = provenance.PersonalSchedules, FeedbackDerived = provenance.Feedback is not null }, model, policy.Root, policy.Libraries, new(published.Id, published.Version, published.SnapshotHash));
        }
        catch (Exception error) when (error is InvalidDataException or JsonException) { return null; }
        void Check() { token.ThrowIfCancellationRequested(); if (transactions.HasTransaction || tenant.Id != tenantId || AiKnowledgeScope.IsTenant(tenant) != scoped) throw new UnauthorizedAccessException(); }
    }
}
