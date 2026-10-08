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

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>保留全部召回来源依赖；引用子集不能替代此权威快照。</summary>
internal sealed record AiKnowledgeRetrievalSnapshot(Guid Actor, Guid? TenantId, bool IsTenant,
    AiModelConfigRecord Model, IReadOnlyList<Guid> KnowledgeBaseIds, IReadOnlyList<AiKnowledgeIndexStatusRecord> Sources,
    IReadOnlyList<AiKnowledgeRankedChunk> Chunks)
{
    // 收费问答将生成目的地绑定到最后来源 SQL，避免独立模型查询等待形成撤权窗口。
    internal AiModelConfigRecord? GenerationModel { get; init; }
}

/// <summary>读取当前授权且完整的活动代次；禁止加入外部事务或触发收费模型调用。</summary>
internal sealed class AiKnowledgeRetrievalService(IQueryExecutor query, IDataTransactionState transactions, ICurrentTenant tenant,
    AiKnowledgeReadAccess access, AiKnowledgePermissionGuard permissions, AiKnowledgeIndexActivationStore activation,
    AiKnowledgeIndexPdfActivationStore pdfActivation, IOptions<DatabaseOptions> database, AiKnowledgeIndexDocxActivationStore? docxActivation = null)
{
    private static readonly string[] RequiredPermissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
        AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.IndexRead];
    private static readonly string[] DispatchPermissions = [.. RequiredPermissions, AiChatPermissions.Send];
    /// <summary>查询 Embedding 收费前证明全部来源可读且完整；模型和来源绑定在同一最终查询。</summary>
    internal async Task<AiKnowledgeRetrievalSnapshot> PreflightAsync(IReadOnlyList<Guid> knowledgeBaseIds, Guid actor,
        AiModelConfigRecord model, CancellationToken token, AiModelConfigRecord? generationModel = null)
    {
        var initial = Initial(knowledgeBaseIds, actor, model, token, generationModel);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(25));
        return await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(async work =>
        {
            var first = await LoadAsync(initial, work).ConfigureAwait(false);
            var result = initial with { Sources = first.Rows };
            await RevalidateCoreAsync(result, work).ConfigureAwait(false); return result;
        }, budget.Token).ConfigureAwait(false);
    }
    /// <summary>冻结输入后读取有界完整代次；未知、不可读、不完整或超限来源拒绝整次召回。</summary>
    /// <remarks>当前可信租户与会话须有效，禁止已有事务。只支持已批准的官方 Embedding 空间，不授予模型派发权。</remarks>
    internal async Task<AiKnowledgeRetrievalSnapshot> RetrieveAsync(IReadOnlyList<Guid> knowledgeBaseIds, Guid actor,
        AiModelConfigRecord model, ReadOnlyMemory<float> vector, CancellationToken token, AiModelConfigRecord? generationModel = null)
    {
        Check(token); ArgumentNullException.ThrowIfNull(knowledgeBaseIds); ArgumentNullException.ThrowIfNull(model);
        if (knowledgeBaseIds.Count is < 1 or > 8 || vector.Length is not (1536 or 3072)) throw Invalid();
        var ids = knowledgeBaseIds.ToArray(); var input = vector.ToArray();
        if (ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length) throw Invalid();
        _ = AiKnowledgeVectorRanker.Rank(input, [], token);
        var scope = AiKnowledgeScope.IsTenant(tenant); var tenantId = tenant.Id;
        if (actor == Guid.Empty || tenantId == Guid.Empty || scope != tenantId.HasValue) throw Denied();
        if (model.Id == Guid.Empty || model.Version < 1 || !model.IsEnabled || model.ProviderKey != AiProviderKeys.OpenAiCompatible
            || model.ModelId is not ("text-embedding-3-small" or "text-embedding-3-large")
            || (model.ModelId == "text-embedding-3-small" ? 1536 : 3072) != input.Length
            || model.TenantId == Guid.Empty || model.TenantId.HasValue && model.TenantId != tenantId) throw Invalid();
        // 一次读取限时 25 秒；取消后仍须确认只读 IO 清理，不能把未确认的旧响应交给调用方。
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(25));
        return await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(async work =>
        {
            var initial = new AiKnowledgeRetrievalSnapshot(actor, tenantId, scope, model, Array.AsReadOnly(ids), [], []) { GenerationModel = generationModel };
            var first = await LoadAsync(initial, work).ConfigureAwait(false);
            CheckScope(initial, work);
            first.Parameters["TaskIdsJson"] = JsonSerializer.Serialize(first.Rows.Select(r => r.TaskId!.Value).ToArray(), AiJsonSerializerContext.Default.GuidArray);
            var vectors = await query.QueryAsync<AiKnowledgeIndexVectorRecord>(AiKnowledgeRetrievalSql.VectorsFor(database.Value.Provider, scope), first.Parameters, work).ConfigureAwait(false);
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
            var chunks = await DecodeAsync(initial, first.Rows, frozen, model, input.Length, first.Parameters, work).ConfigureAwait(false);
            var result = initial with { Sources = first.Rows, Chunks = AiKnowledgeVectorRanker.Rank(input, chunks, work) };
            // 载荷读取和解码等待后，复核完整代次及当前读取权；不复用原索引写入人的在线状态。
            await RevalidateCoreAsync(result, work).ConfigureAwait(false); return result;
        }, budget.Token).ConfigureAwait(false);
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
        var before = await LoadAsync(snapshot, token).ConfigureAwait(false); SameSources(snapshot.Sources, before.Rows);
        await ProveAsync(before.Rows, before.Parameters, token).ConfigureAwait(false);
        var final = await LoadAsync(snapshot, token).ConfigureAwait(false); SameSources(snapshot.Sources, final.Rows);
    }
    private async Task<(IReadOnlyList<AiKnowledgeIndexStatusRecord> Rows, Dictionary<string, object?> Parameters)> LoadAsync(
        AiKnowledgeRetrievalSnapshot snapshot, CancellationToken token)
    {
        CheckScope(snapshot, token);
        var required = snapshot.GenerationModel is null ? RequiredPermissions : DispatchPermissions;
        if (!await permissions.IsAuthorizedAsync(required, snapshot.Actor, token).ConfigureAwait(false)) throw Denied();
        CheckScope(snapshot, token);
        var p = new Dictionary<string, object?>();
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
        var current = await query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(snapshot.IsTenant ? AiModelConfigSql.FindAvailableForTenantChat
            : AiModelConfigSql.FindAvailableForHostChat, p, token).ConfigureAwait(false);
        CheckScope(snapshot, token); if (!SameModel(snapshot.Model, current)) throw Invalid();
        // 最后一笔资源 SQL 必须晚于操作权限等待；同时绑定完整模型目的地，避免等待窗口内同版本漂移。
        if (!await permissions.IsAuthorizedAsync(required, snapshot.Actor, token).ConfigureAwait(false)) throw Denied();
        CheckScope(snapshot, token);
        // 角色和机构也可能在模型/操作权限等待中撤销；最新关系紧邻最后资源查询，不沿用等待前的列表。
        var relationships = await access.ParametersAsync(snapshot.Actor, token).ConfigureAwait(false); CheckScope(snapshot, token);
        foreach (var pair in relationships) p[pair.Key] = pair.Value;
        var rows = await query.QueryAsync<AiKnowledgeIndexStatusRecord>(AiKnowledgeRetrievalSql.SourcesFor(database.Value.Provider, snapshot.IsTenant), p, token).ConfigureAwait(false);
        CheckScope(snapshot, token);
        if (rows.Count is < 1 or > 8 || rows.Select(r => r.DocumentId).Distinct().Count() != rows.Count
            || snapshot.KnowledgeBaseIds.Any(id => !rows.Any(r => r.KnowledgeBaseId == id))) throw Invalid();
        foreach (var row in rows)
        {
            if (!snapshot.KnowledgeBaseIds.Contains(row.KnowledgeBaseId) || row.DocumentId == Guid.Empty || row.VersionId == Guid.Empty
                || row.OwnerUserId == Guid.Empty || row.DocumentVersion < 1 || row.KnowledgeBaseVersion < 1
                || row.IsDeleted != 0 || row.CleanupPending != 0 || row.IsCurrent != 1 || row.StateKey != "completed"
                || row.TaskId is not { } task || task == Guid.Empty || row.ActiveTaskId != task || row.ActiveLeaseToken is not { } lease || lease == Guid.Empty
                || row.ModelConfigId != snapshot.Model.Id || row.ModelConfigVersion != snapshot.Model.Version
                || row.ProviderKey != snapshot.Model.ProviderKey || row.ModelId != snapshot.Model.ModelId
                || row.ContentType is not ("text/plain" or "text/markdown" or "application/pdf" or Domain.AiKnowledgeFilePolicy.DocxContentType)) throw Invalid();
        }
        return (Array.AsReadOnly(rows.OrderBy(r => r.DocumentId.ToString("N"), StringComparer.Ordinal).ToArray()), p);
    }
    private async Task ProveAsync(IReadOnlyList<AiKnowledgeIndexStatusRecord> rows, Dictionary<string, object?> parameters, CancellationToken token)
    {
        foreach (var row in rows)
        {
            Check(token);
            var p = new Dictionary<string, object?>(parameters) { ["KnowledgeBaseId"] = row.KnowledgeBaseId,
                ["DocumentId"] = row.DocumentId, ["VersionId"] = row.VersionId };
            var ready = row.ContentType == "application/pdf" ? await pdfActivation.IsReadyForReadAsync(row, p, token).ConfigureAwait(false)
                : row.ContentType == Domain.AiKnowledgeFilePolicy.DocxContentType
                    ? docxActivation is not null && await docxActivation.IsReadyForReadAsync(row, p, token).ConfigureAwait(false)
                    : await activation.IsReadyForReadAsync(row, p, token).ConfigureAwait(false);
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
            AiKnowledgeIndexDocxVerifiedManifest? manifest = null;
            if (source.ContentType == Domain.AiKnowledgeFilePolicy.DocxContentType)
            {
                var p = new Dictionary<string, object?>(parameters) { ["DocumentId"] = source.DocumentId, ["VersionId"] = source.VersionId, ["TaskId"] = source.TaskId };
                var captured = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexTextManifestRecord>(
                    AiKnowledgeRetrievalSql.DocxManifestFor(database.Value.Provider, snapshot.IsTenant), p, token).ConfigureAwait(false);
                CheckScope(snapshot, token);
                if (captured is null || captured.Id != source.TaskId || captured.FormatKey != AiKnowledgeIndexDocxManifestCodec.FormatKey
                    || captured.PayloadHash != source.ActiveManifestHash || captured.Payload is null
                    || captured.Payload.Length is < 8 or > AiKnowledgeIndexDocxManifestCodec.MaxPayloadBytes) throw Invalid();
                manifestBytes += captured.Payload.Length; if (manifestBytes > AiKnowledgeIndexDocxManifestCodec.MaxPayloadBytes) throw Invalid();
                manifest = AiKnowledgeIndexDocxVerifiedManifest.Capture(captured.Payload, token); var identity = manifest.VectorIdentity;
                if (manifest.PayloadHash != source.ActiveManifestHash || manifest.Plan.Summary.PlanHash != source.ActivePlanHash
                    || manifest.Plan.Batches.Count != rows.Length || identity.KnowledgeBaseId != source.KnowledgeBaseId
                    || identity.KnowledgeBaseVersion != source.KnowledgeBaseVersion || identity.TenantId != snapshot.TenantId
                    || identity.ModelConfigId != model.Id || identity.ModelConfigVersion != model.Version
                    || identity.ProviderKey != model.ProviderKey || identity.ModelId != model.ModelId
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
                if (source.ContentType == "application/pdf")
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
                    if (row.FormatKey != AiKnowledgeIndexDocxVectorCodec.FormatKey) throw Invalid();
                    var docx = AiKnowledgeIndexDocxVectorCodec.ReadBatch(manifest, ordinal, row.Payload, token).Payload;
                    foreach (var item in docx.Chunks)
                    {
                        var c = item.Source; var b = item.Block;
                        Add(new(source, c.Sequence, null, c.Offset, c.Length, c.TokenCount, c.Text, item.Vector)
                        { DocxLocation = new(b.Sequence, b.ParagraphNumber, b.TableNumber, b.RowNumber, b.CellNumber) });
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
    private static void BindCapturedReceipts(IReadOnlyList<AiKnowledgeIndexStatusRecord> sources,
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
    internal static bool SameModel(AiModelConfigRecord expected, AiModelConfigRecord? actual) => actual is not null && actual.IsEnabled
        && actual.Id == expected.Id && actual.TenantId == expected.TenantId && actual.Version == expected.Version
        && actual.ProviderKey == expected.ProviderKey && actual.ModelId == expected.ModelId
        && actual.EndpointBaseUrl == expected.EndpointBaseUrl && actual.OrganizationId == expected.OrganizationId && actual.ApiKeyProtected == expected.ApiKeyProtected;
    private AiKnowledgeRetrievalSnapshot Initial(IReadOnlyList<Guid> knowledgeBaseIds, Guid actor, AiModelConfigRecord model,
        CancellationToken token, AiModelConfigRecord? generationModel)
    {
        Check(token); ArgumentNullException.ThrowIfNull(knowledgeBaseIds); ArgumentNullException.ThrowIfNull(model);
        var scope = AiKnowledgeScope.IsTenant(tenant); var tenantId = tenant.Id; var ids = knowledgeBaseIds.ToArray();
        if (actor == Guid.Empty || tenantId == Guid.Empty || scope != tenantId.HasValue) throw Denied();
        if (ids.Length is < 1 or > 8 || ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length
            || model.Id == Guid.Empty || model.Version < 1 || !model.IsEnabled || model.ProviderKey != AiProviderKeys.OpenAiCompatible
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
