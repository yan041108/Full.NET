using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Features.ManageKnowledgeFaqs;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Full.NET.Modules.Ai.Serialization;
using System.Security.Cryptography;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>历史保存 FAQ 节点及全部叶来源，不能把派生答案展平成没有父节点的文件。</summary>
internal sealed record AiKnowledgeFaqAnswerSource(Guid FaqId, Guid RevisionId, string SourceHash, string ContentHash,
    Guid ModelConfigId, int ModelConfigVersion, IReadOnlyList<AiKnowledgeAnswerDependency> Dependencies)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public AiKnowledgeFaqGraph? Graph { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<Full.NET.Modules.Calendar.Contracts.PersonalScheduleKnowledgeProof>? PersonalSchedules { get; init; }
    /// <summary>私人反馈源必须在后代与历史中保留，不能通过删除标记降级到旧谱系。</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool FeedbackDerived { get; init; }
    internal bool RequiresFeedbackPermission => FeedbackDerived || Graph?.Nodes.Any(node => node.Source.FeedbackDerived) == true;
    // 仅供服务器从当前完整发布捕获父身份；不进入来源/历史 JSON，也不是当前授权凭证。
    internal AiKnowledgeFaqParentPublicationProof? CurrentPublication { get; init; }
    // 同批历史按值合并完整证明，不因 JSON 产生了不同数组实例而重复读取相同叶载荷。
    public bool Equals(AiKnowledgeFaqAnswerSource? other) => AiKnowledgeFaqPublishedSourceReader.Same(this, other);
    public override int GetHashCode() => HashCode.Combine(FaqId, RevisionId, SourceHash, ContentHash, ModelConfigId, ModelConfigVersion, FeedbackDerived);
}

/// <summary>搜索及历史的专属 FAQ 权威入口；绑定检查不交付正文，完整证明仍核验全部叶载荷。</summary>
internal sealed class AiKnowledgeFaqPublishedSourceReader(IQueryExecutor query, IDataTransactionState transactions, ICurrentTenant tenant,
    IClock clock, IOptions<DatabaseOptions> database, AiKnowledgePermissionGuard permissions, AiKnowledgeReadAccess access,
    AiKnowledgeFaqDraftSourceGuard guard, AiKnowledgeFaqIndexInput input, AiKnowledgeIndexActivationStore proof)
{
    internal async Task<AiKnowledgeFaqAnswerSource?> ReadAsync(AiKnowledgeIndexStatusRecord source, Guid actor,
        CancellationToken token, bool verifyPayloads)
    {
        var tenantId = tenant.Id; var scoped = AiKnowledgeScope.IsTenant(tenant); Check();
        if (source.FaqRevisionId is not { } revision || source.FileId is not null || source.PublicationId is not { } publication
            || !await permissions.IsAuthorizedAsync(AiKnowledgeFaqLifecycleAccess.ReadPermissions, actor, token).ConfigureAwait(false)) return null;
        var p = await access.ParametersAsync(actor, token).ConfigureAwait(false); Check();
        p["FaqFeedbackReadAllowed"] = await permissions.IsAuthorizedAsync([AiKnowledgeFaqPermissions.Feedback], actor, token).ConfigureAwait(false) ? 1 : 0;
        p["KnowledgeBaseId"] = source.KnowledgeBaseId; p["DocumentId"] = source.DocumentId; p["RevisionId"] = revision;
        var faq = await query.QuerySingleOrDefaultAsync<Guid>(scoped ? AiKnowledgeRetrievalSql.FaqDocumentIdTenant : AiKnowledgeRetrievalSql.FaqDocumentIdHost, p, token).ConfigureAwait(false);
        if (faq == Guid.Empty) return null; p["FaqId"] = faq; p["Now"] = clock.UtcNow; p["Id"] = publication;
        var release = await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqReleaseRecord>(AiKnowledgeFaqLifecycleSql.PublishedFor(database.Value.Provider, scoped), p, token).ConfigureAwait(false);
        if (release is null || release.Id != publication || release.RevisionId != revision || release.VersionId != source.VersionId) return null;
        var root = await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqDraftRecord>(AiKnowledgeFaqLifecycleSql.RevisionFor(database.Value.Provider, scoped), p, token).ConfigureAwait(false);
        var published = await query.QuerySingleOrDefaultAsync<AiKnowledgePublicationRecord>(scoped ? AiKnowledgePublicationSql.FindTenant : AiKnowledgePublicationSql.FindHost, p, token).ConfigureAwait(false);
        if (root is null || published is null || root.Id != faq || root.TenantId != tenantId || root.RevisionId != revision
            || root.SourceHash != release.SourceHash || root.ContentHash != release.ContentHash || root.ReservedDocumentId != source.DocumentId) return null;
        try
        {
            var snapshot = AiKnowledgeFaqPublicationService.Decode(published, release);
            if (snapshot.Source with { PublicationId = publication } != source || published.StateKey != "published"
                || published.EffectiveFromUtc > clock.UtcNow || published.EffectiveToUtc <= clock.UtcNow) return null;
            _ = AiKnowledgeFaqLifecycleAccess.Content(root);
            var allowed = verifyPayloads ? await guard.AuthorizeAsync([root], actor, AiKnowledgeFaqPermissions.Read, token, requireOwner: false).ConfigureAwait(false)
                : await guard.AuthorizeReadBindingsAsync(root, actor, token).ConfigureAwait(false);
            if (!allowed.Contains(faq) || await input.ClassificationAsync(root, actor, token).ConfigureAwait(false) != source.DataClassification) return null;
            p["VersionId"] = source.VersionId; p["TaskId"] = source.TaskId; p["PublicationId"] = publication;
            if (verifyPayloads && !await proof.IsReadyForReadAsync(source, p, token).ConfigureAwait(false)) return null;
            var provenance = AiKnowledgeFaqProvenanceCodec.Decode(root);
            var owned = new Dictionary<string, object?>(p) { ["OwnerUserId"] = source.OwnerUserId, ["ModelConfigId"] = source.ModelConfigId };
            var original = await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqIndexSourceRecord>(scoped ? AiKnowledgeFaqIndexSql.FindTenant : AiKnowledgeFaqIndexSql.FindHost, owned, token).ConfigureAwait(false);
            var model = await query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(scoped ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat, owned, token).ConfigureAwait(false);
            if (original is null || model is null || original.RevisionId != revision || original.SourceHash != root.SourceHash
                || model.Id != source.ModelConfigId || model.Version != source.ModelConfigVersion
                || original.ModelFingerprint != AiKnowledgeFaqModelFingerprint.Create(model)) return null;
            var parents = await guard.ParentBindingsAsync(provenance, actor, token, verifyPayloads: false).ConfigureAwait(false);
            if (parents is null) return null;
            p["FaqParentBindingsJson"] = JsonSerializer.Serialize(parents.ToArray(), AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqParentAuthorizationArray);
            // 元数据查询等待后再次核对当前读者及发布资格，不能沿用旧角色或旧有效期。
            string[] required = provenance.RequiresFeedbackPermission
                ? [.. AiKnowledgeFaqLifecycleAccess.ReadPermissions, AiKnowledgeFaqPermissions.Feedback] : AiKnowledgeFaqLifecycleAccess.ReadPermissions;
            if (!await permissions.IsAuthorizedAsync(required, actor, token).ConfigureAwait(false)) return null;
            var final = await access.ParametersAsync(actor, token).ConfigureAwait(false); Check();
            foreach (var pair in final) p[pair.Key] = pair.Value; p["Now"] = clock.UtcNow;
            p["FaqModelId"] = model.Id; p["FaqModelVersion"] = model.Version; p["FaqModelTenantId"] = model.TenantId;
            p["FaqEndpoint"] = model.EndpointBaseUrl; p["FaqOrganization"] = model.OrganizationId; p["FaqProvider"] = model.ProviderKey;
            p["FaqModel"] = model.ModelId; p["FaqProtectedKey"] = model.ApiKeyProtected;

            p["DependenciesJson"] = JsonSerializer.Serialize(provenance.Dependencies.ToArray(), AiKnowledgeAnswerLineageJsonContext.Default.AiKnowledgeAnswerDependencyArray);
            p["FaqBindingsJson"] = JsonSerializer.Serialize(new[] { new AiKnowledgeFaqDraftBinding(root.Id, root.SourceHash, root.HeadRevisionId, root.HeadVersion) }, AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqDraftBindingArray);
            if (await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqReleaseRecord>(AiKnowledgeFaqLifecycleSql.PublishedFor(database.Value.Provider, scoped), p, token).ConfigureAwait(false) != release
                || await query.QuerySingleOrDefaultAsync<Guid>(AiKnowledgeRetrievalSql.FaqFinalFor(database.Value.Provider, scoped), p, token).ConfigureAwait(false) != faq
                || published.EffectiveToUtc <= clock.UtcNow) return null;
            if (!await guard.BusinessAsync(provenance, actor, token).ConfigureAwait(false)) return null;
            Check(); return new(faq, revision, root.SourceHash, root.ContentHash, source.ModelConfigId ?? throw new InvalidDataException(),
                source.ModelConfigVersion ?? throw new InvalidDataException(), provenance.Dependencies)
                { Graph = provenance.Graph, PersonalSchedules = provenance.PersonalSchedules, FeedbackDerived = provenance.Feedback is not null, CurrentPublication = new(published.Id, published.Version, published.SnapshotHash) };
        }
        catch (InvalidDataException) { return null; }

        void Check() { token.ThrowIfCancellationRequested(); if (transactions.HasTransaction || tenant.Id != tenantId || AiKnowledgeScope.IsTenant(tenant) != scoped) throw new UnauthorizedAccessException(); }
    }

    internal static bool Same(AiKnowledgeFaqAnswerSource? first, AiKnowledgeFaqAnswerSource? second) => first is not null && second is not null
        && first.FaqId == second.FaqId && first.RevisionId == second.RevisionId && first.SourceHash == second.SourceHash
        && first.ContentHash == second.ContentHash && first.ModelConfigId == second.ModelConfigId && first.ModelConfigVersion == second.ModelConfigVersion
        && first.FeedbackDerived == second.FeedbackDerived && first.Dependencies.SequenceEqual(second.Dependencies) && AiKnowledgeFaqGraphs.Same(first.Graph, second.Graph)
        && AiKnowledgeFaqBusinessSources.Same(first.PersonalSchedules, second.PersonalSchedules);

    /// <summary>最终批量查询重新捕获全部父节点的模型、发布及分类绑定，不沿用前序读取等待前的资格。</summary>
    internal async Task<string?> CurrentParentsJsonAsync(IEnumerable<AiKnowledgeFaqAnswerSource> sources, Guid actor, CancellationToken token)
    {
        var bindings = new List<AiKnowledgeFaqParentAuthorization>();
        foreach (var source in sources.Where(source => source.Graph is not null))
        {
            var current = await guard.ParentBindingsAsync(new(source.PersonalSchedules is null ? 2 : 4, "faq_derived", null, source.Dependencies)
                { Graph = source.Graph, PersonalSchedules = source.PersonalSchedules }, actor, token, false).ConfigureAwait(false);
            if (current is null) return null;
            bindings.AddRange(current);
        }
        return JsonSerializer.Serialize(bindings.ToArray(), AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqParentAuthorizationArray);
    }

    /// <summary>FAQ 引用匹配实际完整批次中的修订区间，不接受伪造的文件坐标或其他来源的引用。</summary>
    internal async Task<IReadOnlyList<AiKnowledgeAnswerReference>> MatchReferencesAsync(AiKnowledgeIndexStatusRecord source, Guid actor,
        IReadOnlyList<AiKnowledgeAnswerReference> references, CancellationToken token)
    {
        if (references.Count == 0) return [];
        if (references.Count > 8 || source.FaqRevisionId is null) return [];
        var scoped = AiKnowledgeScope.IsTenant(tenant); var tenantId = tenant.Id;
        var p = await access.ParametersAsync(actor, token).ConfigureAwait(false);
        p["FaqParentBindingsJson"] = "[]";
        p["FaqFeedbackReadAllowed"] = await permissions.IsAuthorizedAsync([AiKnowledgeFaqPermissions.Feedback], actor, token).ConfigureAwait(false) ? 1 : 0;
        p["ModelConfigId"] = source.ModelConfigId;
        var model = await query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(scoped ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat, p, token).ConfigureAwait(false);
        if (model is null || model.Id != source.ModelConfigId || model.Version != source.ModelConfigVersion) return [];
        p["ModelConfigVersion"] = model.Version; p["ExpectedModelTenantId"] = model.TenantId;
        p["ExpectedEndpointBaseUrl"] = model.EndpointBaseUrl; p["ExpectedProviderKey"] = model.ProviderKey; p["ExpectedModelId"] = model.ModelId;
        p["ExpectedOrganizationId"] = model.OrganizationId; p["ExpectedApiKeyProtected"] = model.ApiKeyProtected;
        foreach (var key in new[] { "GenerationModelConfigId", "GenerationModelConfigVersion", "GenerationTenantId", "GenerationEndpointBaseUrl", "GenerationProviderKey", "GenerationModelId", "GenerationOrganizationId", "GenerationApiKeyProtected" }) p[key] = null;
        p["TaskIdsJson"] = JsonSerializer.Serialize(new[] { source.TaskId ?? throw new InvalidDataException() }, AiJsonSerializerContext.Default.GuidArray);
        var rows = await query.QueryAsync<AiKnowledgeIndexVectorRecord>(AiKnowledgeRetrievalSql.FaqVectorsFor(database.Value.Provider, scoped), p, token).ConfigureAwait(false);
        if (rows.Count is < 1 or > 32 || rows.Sum(value => (long)(value.Payload?.Length ?? 0)) > 8 * 1024 * 1024) return [];
        try
        {
            AiKnowledgeRetrievalService.BindCapturedReceipts([source], rows, token);
            var positions = new Dictionary<int, AiKnowledgeStructuredChunk>();
            foreach (var row in rows)
            {
                if (row.FormatKey != AiKnowledgeIndexVectorCodec.FormatKey || row.Payload is not { Length: >= 8 and <= AiKnowledgeIndexVectorCodec.MaxPayloadBytes }
                    || row.PayloadHash != Convert.ToHexString(SHA256.HashData(row.Payload))) return [];
                var value = AiKnowledgeIndexVectorCodec.Decode(row.Payload, token);
                foreach (var chunk in value.Chunks) if (!positions.TryAdd(chunk.Source.Sequence, chunk.Source)) return [];
            }
            var accepted = references.Where(value => value.DocumentId == source.DocumentId && value.VersionId == source.VersionId
                && value.Correction is null && value.DocxLocation is null && value.SourceLocation is null && value.PageNumber is null
                && positions.TryGetValue(value.Sequence, out var chunk) && value.Offset == chunk.Offset && value.Length == chunk.Length).ToArray();
            if (transactions.HasTransaction || tenant.Id != tenantId || AiKnowledgeScope.IsTenant(tenant) != scoped
                || await ReadAsync(source, actor, token, verifyPayloads: false).ConfigureAwait(false) is null) return [];
            return accepted;
        }
        catch (InvalidDataException) { return []; }
    }
}
