using Full.NET.AI.Abstractions.Models;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Serialization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Full.NET.Modules.Calendar.Contracts;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>保留全部召回来源依赖；引用子集不能替代此权威快照。</summary>
internal sealed record AiKnowledgeRetrievalSnapshot(Guid Actor, Guid? TenantId, bool IsTenant,
    AiModelConfigRecord Model, IReadOnlyList<Guid> KnowledgeBaseIds, IReadOnlyList<AiKnowledgeIndexStatusRecord> Sources,
    IReadOnlyList<AiKnowledgeRankedChunk> Chunks)
{
    // 收费问答将生成目的地绑定到最后来源 SQL，避免独立模型查询等待形成撤权窗口。
    internal AiModelConfigRecord? GenerationModel { get; init; }
    // 非空表示独立搜索；空集合表示所选知识库的全部文档，筛选必须先于完整代次读取。
    internal IReadOnlyList<Guid>? SearchDocumentIds { get; init; }
    internal bool SearchDiagnostics { get; init; }
    internal bool SearchComparison { get; init; }
    internal bool SearchRefinement { get; init; }
    internal int CandidateCount { get; init; }
    internal AiKnowledgeSearchSource? ExpectedSearchSource { get; init; }
    internal IReadOnlyList<AiKnowledgeSearchSource>? ExpectedSearchSources { get; init; }
    internal bool SearchContextRead { get; init; }
    internal AiKnowledgeContextWindow? Context { get; init; }
    internal AiKnowledgeRetrievalComparison? Comparison { get; init; }
    internal AiKnowledgeSourceRelations? Relations { get; init; }
    // FAQ 节点保留全部生成依赖；这些叶来源只用于核证，不自动加入候选或模型正文。
    internal IReadOnlyDictionary<Guid, AiKnowledgeFaqAnswerSource> FaqSources { get; init; } = new Dictionary<Guid, AiKnowledgeFaqAnswerSource>();
    // 业务上下文独立于文档候选；即使未被模型引用，也必须逐次核证全部输入。
    internal IReadOnlyList<PersonalScheduleKnowledgeSnapshot> PersonalSchedules { get; init; } = [];
}

/// <summary>区分来源已替换与一般不可用，调用方必须清空旧上下文。</summary>
internal sealed class AiKnowledgeSearchSourceChangedException() : Exception("搜索来源发布身份已改变。");
internal sealed record AiKnowledgeRetrievalComparison(IReadOnlyList<AiKnowledgeRankedChunk> Keyword, IReadOnlyList<AiKnowledgeRankedChunk> Vector);
internal readonly record struct AiKnowledgeRetrievalSelection(IReadOnlyList<AiKnowledgeRankedChunk> Ranked, AiKnowledgeContextWindow? Context = null,
    AiKnowledgeRetrievalComparison? Comparison = null, AiKnowledgeSourceRelations? Relations = null);

/// <summary>读取当前授权且完整的活动代次；禁止加入外部事务或触发收费模型调用。</summary>
internal sealed class AiKnowledgeRetrievalService(IQueryExecutor query, IDataTransactionState transactions, ICurrentTenant tenant,
    AiKnowledgeReadAccess access, AiKnowledgePermissionGuard permissions, AiKnowledgeIndexActivationStore activation,
    AiKnowledgeIndexPdfActivationStore pdfActivation, IOptions<DatabaseOptions> database, AiKnowledgeIndexDocumentActivationStore? docxActivation = null, AiDataProcessingPolicy? dataPolicy = null, AiKnowledgeGenerationLeases? generationLeases = null,
    AiKnowledgeCorrectionActivationStore? correctionActivation = null, AiKnowledgeFaqPublishedSourceReader? faqSources = null,
    AiPersonalScheduleSourceReader? personalSchedules = null)
{
    private static readonly string[] RequiredPermissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
        AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.IndexRead];
    private static readonly string[] DispatchPermissions = [.. RequiredPermissions, AiChatPermissions.Send];
    /// <summary>查询 Embedding 收费前证明全部来源可读且完整；模型和来源绑定在同一最终查询。</summary>
    internal async Task<AiKnowledgeRetrievalSnapshot> PreflightAsync(IReadOnlyList<Guid> knowledgeBaseIds, Guid actor,
        AiModelConfigRecord model, CancellationToken token, AiModelConfigRecord? generationModel = null, AiKnowledgeSearchAnswerScope? searchScope = null,
        bool searchDiagnostics = false, bool searchComparison = false, bool searchRefinement = false,
        IReadOnlyList<PersonalScheduleKnowledgeSnapshot>? schedules = null)
    {
        if ((searchDiagnostics || searchComparison || searchRefinement) && searchScope is null
            || searchComparison && generationModel is not null && !searchRefinement
            || searchRefinement && (!searchComparison || generationModel is null)) throw Invalid();
        var initial = BindSearchAnswer(Initial(knowledgeBaseIds, actor, model, token, generationModel), searchScope, token)
            with { SearchDiagnostics = searchDiagnostics, SearchComparison = searchComparison,
                SearchRefinement = searchRefinement, SearchContextRead = searchRefinement,
                PersonalSchedules = AiPersonalScheduleAnswerSources.FreezeSnapshots(schedules, actor, tenant.Id) };
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(25));
        return await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(async work =>
        {
            var first = await LoadAsync(initial, work).ConfigureAwait(false);
            var result = initial with { Sources = first.Rows, FaqSources = first.Faqs };
            await RevalidateCoreAsync(result, work).ConfigureAwait(false); return result;
        }, budget.Token).ConfigureAwait(false);
    }
    /// <summary>冻结输入后读取有界完整代次；未知、不可读、不完整或超限来源拒绝整次召回。</summary>
    /// <remarks>当前可信租户与会话须有效，禁止已有事务。只支持已批准的官方 Embedding 空间，不授予模型派发权。</remarks>
    internal async Task<AiKnowledgeRetrievalSnapshot> RetrieveAsync(IReadOnlyList<Guid> knowledgeBaseIds, Guid actor,
        AiModelConfigRecord model, ReadOnlyMemory<float> vector, CancellationToken token, AiModelConfigRecord? generationModel = null,
        AiKnowledgeSearchAnswerScope? searchScope = null, IReadOnlyList<PersonalScheduleKnowledgeSnapshot>? schedules = null)
    {
        Check(token); ArgumentNullException.ThrowIfNull(knowledgeBaseIds); ArgumentNullException.ThrowIfNull(model);
        if (vector.Length is not (1536 or 3072)) throw Invalid();
        var ids = FreezeSelection(knowledgeBaseIds, token); Check(token); var input = vector.ToArray();
        _ = AiKnowledgeVectorRanker.Rank(input, [], token);
        var scope = AiKnowledgeScope.IsTenant(tenant); var tenantId = tenant.Id;
        if (actor == Guid.Empty || tenantId == Guid.Empty || scope != tenantId.HasValue) throw Denied();
        if (model.Id == Guid.Empty || model.Version < 1 || !model.IsEnabled || model.ProviderKey != AiProviderKeys.OpenAiCompatible
            || model.ModelId is not ("text-embedding-3-small" or "text-embedding-3-large")
            || (model.ModelId == "text-embedding-3-small" ? 1536 : 3072) != input.Length
            || model.TenantId == Guid.Empty || model.TenantId.HasValue && model.TenantId != tenantId) throw Invalid();
        var initial = BindSearchAnswer(new AiKnowledgeRetrievalSnapshot(actor, tenantId, scope, model, Array.AsReadOnly(ids), [], [])
            { GenerationModel = generationModel, PersonalSchedules = AiPersonalScheduleAnswerSources.FreezeSnapshots(schedules, actor, tenantId) }, searchScope, token);
        return await RetrieveCoreAsync(initial, input.Length, (chunks, work) => new(AiKnowledgeVectorRanker.Rank(input, chunks, work)), token).ConfigureAwait(false);
    }
    private static AiKnowledgeRetrievalSnapshot BindSearchAnswer(AiKnowledgeRetrievalSnapshot initial, AiKnowledgeSearchAnswerScope? scope, CancellationToken token)
    {
        var frozen = AiKnowledgeSearchAnswerSelection.Freeze(scope, initial.KnowledgeBaseIds, token);
        if (frozen is null) return initial;
        if (frozen.EmbeddingModelVersion != initial.Model.Version) throw new AiKnowledgeSearchSourceChangedException();
        return initial with { SearchDocumentIds = frozen.DocumentIds, ExpectedSearchSources = frozen.Sources };
    }
    /// <summary>两组策略只消费一次解码的完整候选；相同分数不得跨策略混排，也不向公开响应返回向量。</summary>
    internal Task<AiKnowledgeRetrievalSnapshot> RetrieveComparisonAsync(IReadOnlyList<Guid> knowledgeBaseIds, Guid actor, AiModelConfigRecord model,
        AiKnowledgeSearchAnswerScope scope, string text, ReadOnlyMemory<float> vector, bool diagnostics, CancellationToken token)
    {
        _ = AiKnowledgeKeywordRanker.Terms(text, token); var input = vector.ToArray(); _ = AiKnowledgeVectorRanker.Rank(input, [], token);
        var initial = BindSearchAnswer(Initial(knowledgeBaseIds, actor, model, token, null), scope, token) with { SearchDiagnostics = diagnostics, SearchComparison = true };
        return RetrieveCoreAsync(initial, input.Length, (chunks, work) => new([], Comparison:
            new(AiKnowledgeKeywordRanker.Rank(text, chunks, work), AiKnowledgeVectorRanker.Rank(input, chunks, work))), token);
    }
    /// <summary>关键词搜索复用完整发布证明与租约，不需要查询向量或任何模型调用。</summary>
    internal Task<AiKnowledgeRetrievalSnapshot> RetrieveKeywordAsync(IReadOnlyList<Guid> knowledgeBaseIds, IReadOnlyList<Guid> documentIds,
        Guid actor, AiModelConfigRecord model, string text, bool diagnostics, CancellationToken token)
    {
        _ = AiKnowledgeKeywordRanker.Terms(text, token); ArgumentNullException.ThrowIfNull(documentIds);
        var selected = documentIds.Count == 0 ? Array.Empty<Guid>() : FreezeSelection(documentIds, token);
        var initial = Initial(knowledgeBaseIds, actor, model, token, null) with
        { SearchDocumentIds = Array.AsReadOnly(selected), SearchDiagnostics = diagnostics };
        return RetrieveCoreAsync(initial, model.ModelId == "text-embedding-3-small" ? 1536 : 3072,
            (chunks, work) => new(AiKnowledgeKeywordRanker.Rank(text, chunks, work)), token);
    }
    /// <summary>完整验证同一发布后读取相邻窗口；发布身份在任何载荷读取前比较，不能追随新发布。</summary>
    internal Task<AiKnowledgeRetrievalSnapshot> RetrieveContextAsync(AiKnowledgeSearchSource source, Guid actor, AiModelConfigRecord model,
        int anchor, int radius, CancellationToken token)
    {
        var initial = Initial([source.KnowledgeBaseId], actor, model, token, null) with
        { SearchDocumentIds = Array.AsReadOnly(new[] { source.DocumentId }), ExpectedSearchSource = source, SearchContextRead = true };
        return RetrieveCoreAsync(initial, model.ModelId == "text-embedding-3-small" ? 1536 : 3072,
            (chunks, work) => new([], AiKnowledgeContextWindow.Select(chunks, anchor, radius, work)), token);
    }
    /// <summary>从完整发布提取真实结构关系；与邻接窗口使用相同授权、策略和读取租约。</summary>
    internal Task<AiKnowledgeRetrievalSnapshot> RetrieveRelationsAsync(AiKnowledgeSearchSource source, Guid actor, AiModelConfigRecord model,
        int anchor, CancellationToken token)
    {
        var initial = Initial([source.KnowledgeBaseId], actor, model, token, null) with
        { SearchDocumentIds = Array.AsReadOnly(new[] { source.DocumentId }), ExpectedSearchSource = source, SearchContextRead = true };
        return RetrieveCoreAsync(initial, model.ModelId == "text-embedding-3-small" ? 1536 : 3072,
            (chunks, work) => new([], Relations: AiKnowledgeSourceRelations.Select(chunks, anchor, work)), token);
    }
    /// <summary>实验两组策略使用同一已批准处理模型与完整冻结来源，不接收客户端候选。</summary>
    internal Task<AiKnowledgeRetrievalSnapshot> RetrieveRefinementAsync(AiKnowledgeRetrievalSnapshot preflight, string text,
        ReadOnlyMemory<float> vector, CancellationToken token)
    {
        CheckScope(preflight, token); _ = AiKnowledgeKeywordRanker.Terms(text, token);
        if (!preflight.SearchRefinement || !preflight.SearchComparison || preflight.GenerationModel is null || vector.Length is not (1536 or 3072)) throw Invalid();
        var input = vector.ToArray(); _ = AiKnowledgeVectorRanker.Rank(input, [], token);
        return RetrieveCoreAsync(preflight with { Chunks = [], Comparison = null }, input.Length,
            (chunks, work) => new([], Comparison: new(AiKnowledgeKeywordRanker.Rank(text, chunks, work), AiKnowledgeVectorRanker.Rank(input, chunks, work))), token);
    }
    /// <summary>排序策略共享同一有界完整候选、读取租约和交付前权威复核。</summary>
    private async Task<AiKnowledgeRetrievalSnapshot> RetrieveCoreAsync(AiKnowledgeRetrievalSnapshot initial, int dimensions,
        Func<IReadOnlyList<AiKnowledgeRetrievalChunk>, CancellationToken, AiKnowledgeRetrievalSelection> select, CancellationToken token)
    {
        // 一次读取限时 25 秒；取消后仍须确认只读 IO 清理，不能把未确认的旧响应交给调用方。
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(25));
        return await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(async work =>
        {
            var first = await LoadAsync(initial, work).ConfigureAwait(false);
            if (generationLeases is null) throw Invalid();
            await using var held = await generationLeases.AcquireAsync(first.Rows, initial.Actor, work).ConfigureAwait(false);
            CheckScope(initial, work);
            first.Parameters["TaskIdsJson"] = JsonSerializer.Serialize(first.Rows.Select(r => r.TaskId!.Value).ToArray(), AiJsonSerializerContext.Default.GuidArray);
            var vectors = await query.QueryAsync<AiKnowledgeIndexVectorRecord>(AiKnowledgeRetrievalSql.VectorsFor(database.Value.Provider, initial.IsTenant), first.Parameters, work).ConfigureAwait(false);
            if (first.Faqs.Count > 0)
                vectors = vectors.Concat(await query.QueryAsync<AiKnowledgeIndexVectorRecord>(AiKnowledgeRetrievalSql.FaqVectorsFor(database.Value.Provider, initial.IsTenant), first.Parameters, work).ConfigureAwait(false)).ToArray();
            CheckScope(initial, work);
            if (vectors.Count is < 1 or > 32) throw Invalid();
            var frozen = new List<AiKnowledgeIndexVectorRecord>(vectors.Count); long size = 0;
            foreach (var row in vectors)
            {
                work.ThrowIfCancellationRequested();
                if (row.Payload is null || row.Payload.Length is < 8 or > AiKnowledgeIndexVectorCodec.MaxPayloadBytes) throw Invalid();
                size += row.Payload.Length; if (size > 8 * 1024 * 1024) throw Invalid();
                frozen.Add(row with { Payload = row.Payload.ToArray() });
            }
            BindCapturedReceipts(first.Rows, frozen, work);
            var chunks = await DecodeAsync(initial, first.Rows, frozen, initial.Model, dimensions, first.Parameters, work).ConfigureAwait(false);
            if (initial.SearchDocumentIds is not null) AiKnowledgeVectorRanker.ValidateCandidates(chunks, dimensions, work);
            var selection = select(chunks, work);
            var result = initial with { Sources = first.Rows, FaqSources = first.Faqs, Chunks = selection.Ranked, Context = selection.Context, Comparison = selection.Comparison, Relations = selection.Relations, CandidateCount = chunks.Count };
            // 载荷读取和解码等待后，复核完整代次及当前读取权；不复用原索引写入人的在线状态。
            await RevalidateCoreAsync(result, work).ConfigureAwait(false); held.EnsureValid(); return result;
        }, budget.Token).ConfigureAwait(false);
    }
    /// <summary>一次批量读取同版本标题；不逐命中查询，也不读取无权来源的元数据。</summary>
    internal async Task<IReadOnlyList<AiKnowledgeDocumentRecord>> ReadSearchDocumentsAsync(AiKnowledgeRetrievalSnapshot snapshot, CancellationToken token)
    {
        if (snapshot.SearchDocumentIds is null) throw Invalid();
        var captured = await LoadAsync(snapshot, token).ConfigureAwait(false); SameSources(snapshot.Sources, captured.Rows);
        captured.Parameters["TaskIdsJson"] = JsonSerializer.Serialize(captured.Rows.Select(row => row.TaskId!.Value).ToArray(), AiJsonSerializerContext.Default.GuidArray);
        var documents = await query.QueryAsync<AiKnowledgeDocumentRecord>(AiKnowledgeRetrievalSql.SearchDocumentsFor(database.Value.Provider, snapshot.IsTenant), captured.Parameters, token).ConfigureAwait(false);
        if (captured.Faqs.Count > 0)
            documents = documents.Concat(await query.QueryAsync<AiKnowledgeDocumentRecord>(AiKnowledgeRetrievalSql.FaqDocumentsFor(database.Value.Provider, snapshot.IsTenant), captured.Parameters, token).ConfigureAwait(false)).ToArray();
        CheckScope(snapshot, token);
        if (documents.Count != captured.Rows.Count || documents.Select(row => row.Id).Distinct().Count() != documents.Count
            || documents.Any(row => !captured.Rows.Any(source => source.DocumentId == row.Id && source.KnowledgeBaseId == row.KnowledgeBaseId && source.DocumentVersion <= row.Version))) throw Invalid();
        await RevalidateAsync(snapshot, token).ConfigureAwait(false); return documents;
    }
    /// <summary>交付正文前复核全部依赖与完整代次；失败不得释放先前缓冲的正文。</summary>
    internal async Task RevalidateAsync(AiKnowledgeRetrievalSnapshot snapshot, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(snapshot); CheckScope(snapshot, token);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(25));
        await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(async work =>
        { await RevalidateCoreAsync(snapshot, work).ConfigureAwait(false); return true; }, budget.Token).ConfigureAwait(false);
    }
    private async Task RevalidateCoreAsync(AiKnowledgeRetrievalSnapshot snapshot, CancellationToken token)
    {
        await VerifySchedulesAsync(snapshot, token).ConfigureAwait(false);
        var before = await LoadAsync(snapshot, token).ConfigureAwait(false); SameSources(snapshot.Sources, before.Rows); SameFaqs(snapshot.FaqSources, before.Faqs);
        await ProveAsync(before.Rows, before.Parameters, token).ConfigureAwait(false);
        var final = await LoadAsync(snapshot, token).ConfigureAwait(false); SameSources(snapshot.Sources, final.Rows); SameFaqs(snapshot.FaqSources, final.Faqs);
        var schedules = AiKnowledgeFaqBusinessSources.For(snapshot);
        if (schedules.Count > 0)
        {
            AiPersonalScheduleAnswerSources.RequireBudget(AiPersonalScheduleAnswerSources.CountKnowledgeDependencies(snapshot), schedules.Count);
            await VerifySchedulesAsync(snapshot, token).ConfigureAwait(false);
            CheckScope(snapshot, token);
        }
    }
    private async Task VerifySchedulesAsync(AiKnowledgeRetrievalSnapshot snapshot, CancellationToken token)
    {
        CheckScope(snapshot, token);
        var schedules = AiKnowledgeFaqBusinessSources.For(snapshot);
        if (schedules.Count > 0 && (personalSchedules is null
            || !await personalSchedules.RevalidateAsync(snapshot.Actor, schedules, token).ConfigureAwait(false)))
            throw new UnauthorizedAccessException("当前日程来源已不可用。");
        CheckScope(snapshot, token);
    }
    private async Task<(IReadOnlyList<AiKnowledgeIndexStatusRecord> Rows, Dictionary<string, object?> Parameters, IReadOnlyDictionary<Guid, AiKnowledgeFaqAnswerSource> Faqs)> LoadAsync(
        AiKnowledgeRetrievalSnapshot snapshot, CancellationToken token)
    {
        CheckScope(snapshot, token);
        var required = snapshot.SearchComparison ? [.. RequiredPermissions, AiKnowledgeSearchPermissions.Compare]
            : snapshot.GenerationModel is null ? RequiredPermissions : DispatchPermissions;
        if (snapshot.SearchRefinement) required = [.. required, AiKnowledgeSearchPermissions.Refine];
        if (snapshot.SearchDocumentIds is not null)
            required = snapshot.SearchDiagnostics ? [.. required, AiKnowledgeSearchPermissions.Read, AiKnowledgeSearchPermissions.Diagnostics]
                : [.. required, AiKnowledgeSearchPermissions.Read];
        if (!await permissions.IsAuthorizedAsync(required, snapshot.Actor, token).ConfigureAwait(false)) throw Denied();
        CheckScope(snapshot, token);
        var p = new Dictionary<string, object?>();
        p["FaqParentBindingsJson"] = "[]";
        p["ModelConfigId"] = snapshot.Model.Id; p["ModelConfigVersion"] = snapshot.Model.Version;
        p["ExpectedModelTenantId"] = snapshot.Model.TenantId; p["ExpectedEndpointBaseUrl"] = snapshot.Model.EndpointBaseUrl;
        p["ExpectedProviderKey"] = snapshot.Model.ProviderKey; p["ExpectedModelId"] = snapshot.Model.ModelId;
        p["ExpectedOrganizationId"] = snapshot.Model.OrganizationId; p["ExpectedApiKeyProtected"] = snapshot.Model.ApiKeyProtected;
        var generation = snapshot.GenerationModel;
        p["GenerationModelConfigId"] = generation?.Id; p["GenerationModelConfigVersion"] = generation?.Version;
        p["GenerationTenantId"] = generation?.TenantId; p["GenerationEndpointBaseUrl"] = generation?.EndpointBaseUrl;
        p["GenerationProviderKey"] = generation?.ProviderKey; p["GenerationModelId"] = generation?.ModelId;
        p["GenerationOrganizationId"] = generation?.OrganizationId; p["GenerationApiKeyProtected"] = generation?.ApiKeyProtected;
        p["KnowledgeBaseIdsJson"] = JsonSerializer.Serialize(snapshot.KnowledgeBaseIds.ToArray(), AiJsonSerializerContext.Default.GuidArray);
        if (snapshot.SearchDocumentIds is not null)
        {
            p["AllDocuments"] = snapshot.SearchDocumentIds.Count == 0 ? 1 : 0;
            p["DocumentIdsJson"] = JsonSerializer.Serialize(snapshot.SearchDocumentIds.ToArray(), AiJsonSerializerContext.Default.GuidArray);
        }
        var current = await query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(snapshot.IsTenant ? AiModelConfigSql.FindAvailableForTenantChat
            : AiModelConfigSql.FindAvailableForHostChat, p, token).ConfigureAwait(false);
        CheckScope(snapshot, token); if (!SameModel(snapshot.Model, current)) throw Invalid();
        // 最后一笔资源 SQL 必须晚于操作权限等待；同时绑定完整模型目的地，避免等待窗口内同版本漂移。
        if (!await permissions.IsAuthorizedAsync(required, snapshot.Actor, token).ConfigureAwait(false)) throw Denied();
        CheckScope(snapshot, token);
        // 角色和机构也可能在模型/操作权限等待中撤销；最新关系紧邻最后资源查询，不沿用等待前的列表。
        var relationships = await access.ParametersAsync(snapshot.Actor, token).ConfigureAwait(false); CheckScope(snapshot, token);
        foreach (var pair in relationships) p[pair.Key] = pair.Value;
        var includeFaq = faqSources is not null && await permissions.IsAuthorizedAsync([AiKnowledgeFaqPermissions.Read], snapshot.Actor, token).ConfigureAwait(false);
        p["FaqFeedbackReadAllowed"] = includeFaq && await permissions.IsAuthorizedAsync([AiKnowledgeFaqPermissions.Feedback], snapshot.Actor, token).ConfigureAwait(false) ? 1 : 0;
        var statement = includeFaq ? AiKnowledgeRetrievalSql.CombinedSourcesFor(database.Value.Provider, snapshot.IsTenant, snapshot.SearchDocumentIds is not null)
            : snapshot.SearchDocumentIds is null ? AiKnowledgeRetrievalSql.SourcesFor(database.Value.Provider, snapshot.IsTenant)
                : AiKnowledgeRetrievalSql.SearchSourcesFor(database.Value.Provider, snapshot.IsTenant);
        var rows = await query.QueryAsync<AiKnowledgeIndexStatusRecord>(statement, p, token).ConfigureAwait(false);
        CheckScope(snapshot, token);
        if (rows.Count is < 1 or > 8 || rows.Select(r => r.DocumentId).Distinct().Count() != rows.Count
            || snapshot.KnowledgeBaseIds.Any(id => !rows.Any(r => r.KnowledgeBaseId == id))
            || snapshot.SearchDocumentIds is { } selected && selected.Any(id => !rows.Any(row => row.DocumentId == id))) throw Invalid();
        foreach (var row in rows)
        {
            // 分类只限定合法处理状态，不替代业务批准；生成必须逐库绑定精确模型及版本。
            if (row.DataClassification is not ("public" or "internal" or "restricted")
                || generation is not null && !Domain.AiKnowledgePolicy.Allows(true, row.GenerationModelConfigId,
                    row.GenerationModelVersion, generation.Id, generation.Version)) throw Invalid();
            if (!snapshot.KnowledgeBaseIds.Contains(row.KnowledgeBaseId) || row.DocumentId == Guid.Empty || row.VersionId == Guid.Empty
                || row.OwnerUserId == Guid.Empty || row.DocumentVersion < 1 || row.KnowledgeBaseVersion < 1
                || row.IsDeleted != 0 || row.CleanupPending != 0 || row.IsCurrent != 1 || row.StateKey != "completed"
                || row.TaskId is not { } task || task == Guid.Empty || row.ActiveTaskId != task || row.ActiveLeaseToken is not { } lease || lease == Guid.Empty
                || row.ModelConfigId != snapshot.Model.Id || row.ModelConfigVersion != snapshot.Model.Version
                || row.ProviderKey != snapshot.Model.ProviderKey || row.ModelId != snapshot.Model.ModelId
                || !Domain.AiKnowledgeFilePolicy.ContentTypes.Contains(row.ContentType, StringComparer.Ordinal)) throw Invalid();
        }
        if (snapshot.ExpectedSearchSource is { } expected && (rows.Count != 1 || !SameExpected(expected, rows[0])))
            throw new AiKnowledgeSearchSourceChangedException();
        if (snapshot.ExpectedSearchSources is { } expectedSet && (rows.Count != expectedSet.Count
            || expectedSet.Any(source => !rows.Any(row => SameExpected(source, row))))) throw new AiKnowledgeSearchSourceChangedException();
        // 宿主去向策略先于正文、向量和 Files 证明读取；预检与交付复核均走同一门禁。
        RequireProcessing(snapshot, rows);
        var faqs = new Dictionary<Guid, AiKnowledgeFaqAnswerSource>();
        foreach (var row in rows.Where(row => row.FaqRevisionId.HasValue))
            faqs.Add(row.DocumentId, faqSources is null ? throw Invalid() : await faqSources.ReadAsync(row, snapshot.Actor, token, verifyPayloads: false).ConfigureAwait(false) ?? throw Invalid());
        // 证明叶来源有独立有界预算，但不成为排名候选；超限拒绝整次，不截断依赖。
        var proofLeaves = faqs.Values.SelectMany(value => value.Dependencies).Select(value => value.DocumentId).Distinct().Count();
        var proofParents = faqs.Values.SelectMany(value => value.Graph?.Nodes ?? []).Select(value => value.Status.DocumentId).Distinct().Count();
        if (proofParents > AiKnowledgeFaqGraphs.MaximumNodes || proofLeaves + proofParents > AiKnowledgeFaqGraphs.MaximumDependencies) throw Invalid();
        var expectedFaqs = snapshot.ExpectedSearchSources ?? (snapshot.ExpectedSearchSource is { } one ? [one] : []);
        if (expectedFaqs.Any(value => value.FaqRevisionId.HasValue && (!faqs.TryGetValue(value.DocumentId, out var faq) || value.FaqId != faq.FaqId)))
            throw new AiKnowledgeSearchSourceChangedException();
        if (includeFaq)
        {
            p["FaqParentBindingsJson"] = await faqSources!.CurrentParentsJsonAsync(faqs.Values, snapshot.Actor, token).ConfigureAwait(false) ?? throw Invalid();
            string[] finalRequired = [.. required, AiKnowledgeFaqPermissions.Read];
            if (faqs.Values.Any(value => value.RequiresFeedbackPermission)) finalRequired = [.. finalRequired, AiKnowledgeFaqPermissions.Feedback];
            if (!await permissions.IsAuthorizedAsync(finalRequired, snapshot.Actor, token).ConfigureAwait(false)) throw Denied();
            var finalRelationships = await access.ParametersAsync(snapshot.Actor, token).ConfigureAwait(false); CheckScope(snapshot, token);
            foreach (var pair in finalRelationships) p[pair.Key] = pair.Value;
            var finalRows = await query.QueryAsync<AiKnowledgeIndexStatusRecord>(statement, p, token).ConfigureAwait(false); CheckScope(snapshot, token);
            SameSources(rows.OrderBy(row => row.DocumentId).ToArray(), finalRows.OrderBy(row => row.DocumentId).ToArray());
        }
        return (Array.AsReadOnly(rows.OrderBy(r => r.DocumentId.ToString("N"), StringComparer.Ordinal).ToArray()), p, faqs);
    }
    /// <summary>未知问题最低按 restricted 处理；独立搜索将可信宿主拒绝映射为既有来源不可用契约。</summary>
    private void RequireProcessing(AiKnowledgeRetrievalSnapshot snapshot, IReadOnlyList<AiKnowledgeIndexStatusRecord> rows)
    {
        var policy = dataPolicy ?? AiDataProcessingPolicy.Default;
        var classification = AiDataProcessingPolicy.Strongest(rows.Select(row => row.DataClassification), "restricted");
        if (snapshot.SearchDocumentIds is not null && (!policy.Allows("rerank", classification)
            || snapshot.SearchContextRead && !policy.Allows("context", classification))) throw Invalid();
        policy.Require("rerank", classification);
        // 收费问答不能先支付查询 Embedding，随后才发现宿主禁止构造回答上下文。
        if (snapshot.GenerationModel is not null) policy.Require("context", classification);
    }
    private static bool SameExpected(AiKnowledgeSearchSource expected, AiKnowledgeIndexStatusRecord row) => AiKnowledgeSearchSources.Matches(expected, row);
    private async Task ProveAsync(IReadOnlyList<AiKnowledgeIndexStatusRecord> rows, Dictionary<string, object?> parameters, CancellationToken token)
    {
        foreach (var row in rows)
        {
            Check(token);
            if (row.FaqRevisionId.HasValue)
            {
                if (faqSources is null || !parameters.TryGetValue("OwnerUserId", out var actor) || actor is not Guid user
                    || await faqSources.ReadAsync(row, user, token, verifyPayloads: true).ConfigureAwait(false) is null) throw Invalid();
                continue;
            }
            var p = new Dictionary<string, object?>(parameters) { ["KnowledgeBaseId"] = row.KnowledgeBaseId,
                ["DocumentId"] = row.DocumentId, ["VersionId"] = row.VersionId };
            var ready = row.CorrectionRevisionId.HasValue
                ? correctionActivation is not null && await correctionActivation.IsReadyForReadAsync(row, p, token).ConfigureAwait(false)
                : AiKnowledgeDocumentFormats.IsDocument(row.ContentType) && docxActivation is not null && await docxActivation.IsReadyForReadAsync(row, p, token).ConfigureAwait(false)
                || (row.ContentType == "application/pdf" ? await pdfActivation.IsReadyForReadAsync(row, p, token).ConfigureAwait(false)
                : row.ContentType == Domain.AiKnowledgeFilePolicy.DocxContentType
                    ? docxActivation is not null && await docxActivation.IsReadyForReadAsync(row, p, token).ConfigureAwait(false)
                    : await activation.IsReadyForReadAsync(row, p, token).ConfigureAwait(false));
            Check(token); if (!ready) throw Invalid();
        }
    }
    private async Task<IReadOnlyList<AiKnowledgeRetrievalChunk>> DecodeAsync(AiKnowledgeRetrievalSnapshot snapshot, IReadOnlyList<AiKnowledgeIndexStatusRecord> sources,
        IReadOnlyList<AiKnowledgeIndexVectorRecord> vectors, AiModelConfigRecord model, int dimensions, Dictionary<string, object?> parameters, CancellationToken token)
    {
        var chunks = new List<AiKnowledgeRetrievalChunk>(); var seen = new HashSet<Guid>(); long manifestBytes = 0;
        foreach (var source in sources)
        {
            var rows = vectors.Where(r => r.IndexTaskId == source.TaskId).OrderBy(r => r.BatchOrdinal).ToArray();
            if (rows.Length == 0) throw Invalid();
            AiKnowledgeIndexDocumentVerifiedManifest? manifest = null;
            AiKnowledgeCorrectionIndexPlan? correction = null;
            if (source.CorrectionRevisionId.HasValue)
            {
                var p = new Dictionary<string, object?>(parameters) { ["DocumentId"] = source.DocumentId, ["VersionId"] = source.VersionId, ["TaskId"] = source.TaskId };
                var captured = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexTextManifestRecord>(AiKnowledgeRetrievalSql.DocumentManifestFor(database.Value.Provider, snapshot.IsTenant), p, token).ConfigureAwait(false);
                CheckScope(snapshot, token);
                if (captured is null || captured.Id != source.TaskId || captured.FormatKey != AiKnowledgeCorrectionManifestCodec.FormatKey
                    || captured.PayloadHash != source.ActiveManifestHash || captured.Payload is not { Length: >= 8 and <= AiKnowledgeCorrectionManifestCodec.MaxPayloadBytes }
                    || Convert.ToHexString(SHA256.HashData(captured.Payload)) != source.ActiveManifestHash
                    || AiKnowledgeDocumentFormats.Format(source.ContentType) is not { } format || AiKnowledgeDocumentFormats.Policy(source.ContentType, source.ParsingPolicyKey) is not { } policy
                    || source.SizeBytes is <= 0 or > int.MaxValue) throw Invalid();
                manifestBytes += captured.Payload.Length; if (manifestBytes > AiKnowledgeCorrectionManifestCodec.MaxPayloadBytes) throw Invalid();
                correction = AiKnowledgeCorrectionManifestCodec.Decode(captured.Payload, format, source.ContentHash, checked((int)source.SizeBytes), policy, token);
                var bound = correction.Model;
                if (correction.Snapshot.Identity.RevisionId != source.CorrectionRevisionId || correction.PlanHash != source.ActivePlanHash || correction.Batches.Count != rows.Length
                    || bound.KnowledgeBaseId != source.KnowledgeBaseId || bound.KnowledgeBaseVersion != source.KnowledgeBaseVersion || bound.TenantId != snapshot.TenantId
                    || bound.ModelConfigId != model.Id || bound.ModelConfigVersion != model.Version || bound.ProviderKey != model.ProviderKey || bound.ModelId != model.ModelId
                    || bound.DataClassification != source.DataClassification || bound.DestinationHash != AiKnowledgeDocumentModelChunker.Destination(model)) throw Invalid();
            }
            else if (rows.All(row => row.FormatKey == AiKnowledgeIndexDocumentVectorCodec.FormatKey))
            {
                var p = new Dictionary<string, object?>(parameters) { ["DocumentId"] = source.DocumentId, ["VersionId"] = source.VersionId, ["TaskId"] = source.TaskId };
                var captured = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexTextManifestRecord>(
                    AiKnowledgeRetrievalSql.DocumentManifestFor(database.Value.Provider, snapshot.IsTenant), p, token).ConfigureAwait(false);
                CheckScope(snapshot, token);
                if (captured is null || captured.Id != source.TaskId || captured.FormatKey != AiKnowledgeIndexDocumentManifestCodec.FormatKey
                    || captured.PayloadHash != source.ActiveManifestHash || captured.Payload is null
                    || captured.Payload.Length is < 8 or > AiKnowledgeIndexDocumentManifestCodec.MaxPayloadBytes) throw Invalid();
                manifestBytes += captured.Payload.Length; if (manifestBytes > AiKnowledgeIndexDocumentManifestCodec.MaxPayloadBytes) throw Invalid();
                manifest = AiKnowledgeIndexDocumentVerifiedManifest.Capture(captured.Payload, token); var identity = manifest.VectorIdentity;
                if (manifest.PayloadHash != source.ActiveManifestHash || manifest.Plan.Summary.PlanHash != source.ActivePlanHash
                    || manifest.Plan.Batches.Count != rows.Length || identity.KnowledgeBaseId != source.KnowledgeBaseId
                    || identity.KnowledgeBaseVersion != source.KnowledgeBaseVersion || identity.TenantId != snapshot.TenantId
                    || identity.ModelConfigId != model.Id || identity.ModelConfigVersion != model.Version
                    || identity.ProviderKey != model.ProviderKey || identity.ModelId != model.ModelId
                    || !AiKnowledgeDocumentFormats.Matches(manifest.Plan.Source.Document, source.ContentType, source.ParsingPolicyKey)
                    || manifest.Plan.Source.DataClassification != source.DataClassification || manifest.Plan.Source.DestinationHash != AiKnowledgeDocumentModelChunker.Destination(model)
                    || !string.Equals(identity.ContentHash, source.ContentHash, StringComparison.OrdinalIgnoreCase) || identity.SizeBytes != source.SizeBytes) throw Invalid();
            }
            for (var ordinal = 0; ordinal < rows.Length; ordinal++)
            {
                token.ThrowIfCancellationRequested(); var row = rows[ordinal];
                if (!seen.Add(row.Id) || row.Id == Guid.Empty || row.BatchOrdinal != ordinal || row.ModelConfigId != model.Id
                    || row.ModelConfigVersion != model.Version || row.ProviderKey != model.ProviderKey || row.ModelId != model.ModelId
                    || row.Dimensions != dimensions || row.DistanceKey != "cosine"
                    || row.PayloadHash != Convert.ToHexString(SHA256.HashData(row.Payload))) throw Invalid();
                var start = chunks.Count;
                if (correction is not null)
                {
                    if (row.FormatKey != AiKnowledgeCorrectionVectorCodec.FormatKey) throw Invalid();
                    var batch = AiKnowledgeCorrectionVectorCodec.ReadBatch(correction, ordinal, row.Payload, token);
                    foreach (var item in batch.Chunks)
                    {
                        var c = item.Source;
                        Add(new(source, c.Sequence, null, c.Offset, c.Text.Length, c.TokenCount, c.Text, item.Vector)
                        { Correction = AiKnowledgeCorrectionCitations.Create(correction, c, token) });
                    }
                }
                else if (source.ContentType == "application/pdf" && manifest is null)
                {
                    if (row.FormatKey != AiKnowledgeIndexPdfVectorCodec.FormatKey) throw Invalid();
                    var pdf = AiKnowledgeIndexPdfVectorCodec.Decode(row.Payload, token);
                    if (pdf.Ordinal != ordinal || pdf.Dimensions != dimensions || pdf.PlanHash != source.ActivePlanHash) throw Invalid();
                    foreach (var item in pdf.Chunks)
                    {
                        var c = item.Source; Add(new(source, c.Sequence, c.PageNumber, c.Offset, c.Length, c.TokenCount, c.Text, item.Vector));
                    }
                }
                else if (manifest is not null)
                {
                    if (row.FormatKey != AiKnowledgeIndexDocumentVectorCodec.FormatKey) throw Invalid();
                    var docx = AiKnowledgeIndexDocumentVectorCodec.ReadBatch(manifest, ordinal, row.Payload, token);
                    foreach (var item in docx.Chunks)
                    {
                        var c = item.Source; var b = item.Block;
                        Add(new(source, c.Sequence, c.Position.PageNumber, c.Offset, c.Length, c.TokenCount, c.Text, item.Vector)
                        { SourceLocation = AiKnowledgeSourceLocations.Create(b.Sequence, c.Position, manifest.Plan.Source.Document) });
                    }
                }
                else
                {
                    if (row.FormatKey != AiKnowledgeIndexVectorCodec.FormatKey) throw Invalid();
                    var text = AiKnowledgeIndexVectorCodec.Decode(row.Payload, token); if (text.Dimensions != dimensions) throw Invalid();
                    foreach (var item in text.Chunks)
                    {
                        var c = item.Source; Add(new(source, c.Sequence, null, c.Offset, c.Length, c.TokenCount, c.Text, item.Vector));
                    }
                }
                if (chunks.Count - start != row.InputCount) throw Invalid();
            }
        }
        if (seen.Count != vectors.Count) throw Invalid(); return chunks.AsReadOnly();
        void Add(AiKnowledgeRetrievalChunk chunk)
        { token.ThrowIfCancellationRequested(); if (chunks.Count == 256) throw Invalid(); chunks.Add(chunk); }
    }
    internal static void BindCapturedReceipts(IReadOnlyList<AiKnowledgeIndexStatusRecord> sources,
        IReadOnlyList<AiKnowledgeIndexVectorRecord> vectors, CancellationToken token)
    {
        foreach (var source in sources)
        {
            token.ThrowIfCancellationRequested();
            var receipts = vectors.Where(v => v.IndexTaskId == source.TaskId).OrderBy(v => v.BatchOrdinal).Select(v => new AiKnowledgeIndexVectorReceiptRecord
            {
                Id = v.Id, BatchOrdinal = v.BatchOrdinal, RequestHash = v.RequestHash, InputCount = v.InputCount,
                PayloadHash = v.PayloadHash, ProviderKey = v.ProviderKey, ModelId = v.ModelId, Dimensions = v.Dimensions,
                FormatKey = v.FormatKey, DistanceKey = v.DistanceKey
            }).ToArray();
            // 使用既有活动回执线格式；当前数据库恢复原载荷，也不能替代首次捕获载荷的精确证明。
            var json = JsonSerializer.Serialize(receipts, AiKnowledgeIndexActivationJsonContext.Default.AiKnowledgeIndexVectorReceiptRecordArray);
            if (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))) != source.ActiveReceiptHash) throw Invalid();
        }
    }
    private static void SameSources(IReadOnlyList<AiKnowledgeIndexStatusRecord> expected, IReadOnlyList<AiKnowledgeIndexStatusRecord> current)
    { if (!expected.SequenceEqual(current)) throw Invalid(); }
    private static void SameFaqs(IReadOnlyDictionary<Guid, AiKnowledgeFaqAnswerSource> expected, IReadOnlyDictionary<Guid, AiKnowledgeFaqAnswerSource> current)
    {
        if (expected.Count != current.Count || expected.Any(pair => !current.TryGetValue(pair.Key, out var value)
            || !AiKnowledgeFaqPublishedSourceReader.Same(pair.Value, value))) throw Invalid();
    }
    internal static bool SameModel(AiModelConfigRecord expected, AiModelConfigRecord? actual) => actual is not null && actual.IsEnabled
        && actual.Id == expected.Id && actual.TenantId == expected.TenantId && actual.Version == expected.Version
        && actual.ProviderKey == expected.ProviderKey && actual.ModelId == expected.ModelId
        && actual.EndpointBaseUrl == expected.EndpointBaseUrl && actual.OrganizationId == expected.OrganizationId && actual.ApiKeyProtected == expected.ApiKeyProtected;
    /// <summary>只读取一次外部计数，以最多八次索引冻结原顺序；空或重复标识在查询和收费前拒绝。</summary>
    /// <returns>调用方独占的标识副本，不与原集合共享可写存储。</returns>
    internal static Guid[] FreezeSelection(IReadOnlyList<Guid> knowledgeBaseIds, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(knowledgeBaseIds);
        var count = knowledgeBaseIds.Count; token.ThrowIfCancellationRequested();
        if (count is < 1 or > 8) throw Invalid();
        var ids = new Guid[count]; var seen = new HashSet<Guid>();
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested(); var id = knowledgeBaseIds[i]; token.ThrowIfCancellationRequested();
            if (id == Guid.Empty || !seen.Add(id)) throw Invalid(); ids[i] = id;
        }
        return ids;
    }
    private AiKnowledgeRetrievalSnapshot Initial(IReadOnlyList<Guid> knowledgeBaseIds, Guid actor, AiModelConfigRecord model,
        CancellationToken token, AiModelConfigRecord? generationModel)
    {
        Check(token); ArgumentNullException.ThrowIfNull(knowledgeBaseIds); ArgumentNullException.ThrowIfNull(model);
        var scope = AiKnowledgeScope.IsTenant(tenant); var tenantId = tenant.Id; var ids = FreezeSelection(knowledgeBaseIds, token); Check(token);
        if (actor == Guid.Empty || tenantId == Guid.Empty || scope != tenantId.HasValue) throw Denied();
        if (model.Id == Guid.Empty || model.Version < 1 || !model.IsEnabled || model.ProviderKey != AiProviderKeys.OpenAiCompatible
            || model.ModelId is not ("text-embedding-3-small" or "text-embedding-3-large") || model.TenantId == Guid.Empty
            || model.TenantId.HasValue && model.TenantId != tenantId) throw Invalid();
        if (generationModel is not null && (generationModel.Id == Guid.Empty || generationModel.Version < 1 || !generationModel.IsEnabled
            || generationModel.TenantId == Guid.Empty || generationModel.TenantId.HasValue && generationModel.TenantId != tenantId)) throw Invalid();
        return new(actor, tenantId, scope, model, Array.AsReadOnly(ids), [], []) { GenerationModel = generationModel };
    }
    private void CheckScope(AiKnowledgeRetrievalSnapshot snapshot, CancellationToken token)
    { Check(token); if (tenant.Id != snapshot.TenantId || AiKnowledgeScope.IsTenant(tenant) != snapshot.IsTenant) throw Denied(); }
    private void Check(CancellationToken token)
    { token.ThrowIfCancellationRequested(); if (transactions.HasTransaction) throw new InvalidOperationException("检索不能加入已有事务。"); }
    private static InvalidDataException Invalid() => new("当前检索来源不可用或不完整。");
    private static UnauthorizedAccessException Denied() => new("当前读取者没有检索来源权限。");
}
