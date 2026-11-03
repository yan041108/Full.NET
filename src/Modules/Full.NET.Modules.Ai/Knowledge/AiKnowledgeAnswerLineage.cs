using Full.NET.Modules.Ai.Persistence;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Modules.Calendar.Contracts;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>仅保存来源身份和完成证明；不得保存正文、向量、模型目的地或凭据。</summary>
internal sealed record AiKnowledgeAnswerDependency(Guid KnowledgeBaseId, Guid DocumentId, Guid VersionId,
    int DocumentVersion, int KnowledgeBaseVersion, Guid? FileId, string ContentHash,
    Guid TaskId, string ReceiptHash, string ManifestHash, string PlanHash, Guid? PublicationId = null)
{
    /// <summary>FAQ 修订及全部生成依赖；普通文件禁止携带，未知来源不能清空此节点。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiKnowledgeFaqAnswerSource? Faq { get; init; }
}

/// <summary>已证明反馈与完整来源的联合查询参数；只来源于服务器冻结元数据。</summary>
internal sealed record AiKnowledgeAnswerFeedbackReviewBinding(Guid FeedbackId, Guid MessageId, AiKnowledgeAnswerDependency Dependency);

/// <summary>服务器核对的引用位置；模型输出只能选择引用标识。</summary>
internal sealed record AiKnowledgeAnswerReference(Guid Id, Guid DocumentId, Guid VersionId,
    int Sequence, int? PageNumber, int? Offset, int? Length)
{
    /// <summary>v4 人工引用明确保存全部来源；旧协议没有该字段，不能回填或猜测。</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Full.NET.Modules.Ai.Contracts.AiKnowledgeCorrectionCitationResponse? Correction { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Full.NET.Modules.Ai.Contracts.AiKnowledgeDocxLocationResponse? DocxLocation { get; init; }
    /// <summary>服务器核对的统一来源位置；旧答案未保存此字段时省略。</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Full.NET.Modules.Ai.Contracts.AiKnowledgeSourceLocationResponse? SourceLocation { get; init; }
}

/// <summary>版本化完整依赖绑定实际答案消息及可信所有者范围，不能按展示引用缩减。</summary>
internal sealed record AiKnowledgeAnswerLineage(int Version, Guid MessageId, Guid SessionId, Guid ActorUserId,
    Guid? TenantId, IReadOnlyList<AiKnowledgeAnswerDependency> Dependencies, IReadOnlyList<AiKnowledgeAnswerReference> References)
{
    /// <summary>v8 保存所有实际日程输入，包括模型未引用项；旧协议不写空字段。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<PersonalScheduleKnowledgeProof>? PersonalSchedules { get; init; }
    /// <summary>独立业务引用不携带正文，也不复用文档 UUID。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AiPersonalScheduleAnswerReference>? PersonalScheduleReferences { get; init; }
}

/// <summary>只保存服务器引用身份与准确日程身份，版本和摘要从完整依赖读取。</summary>
internal sealed record AiPersonalScheduleAnswerReference(Guid Id, Guid ScheduleId);

/// <summary>有界依赖编解码；未知版本、重复字段和缺失依赖均失败关闭。</summary>
internal static class AiKnowledgeAnswerLineageCodec
{
    internal const int MaxBytes = 16384;
    internal static string Encode(AiKnowledgeAnswerLineage lineage)
    {
        Validate(lineage);
        var json = JsonSerializer.Serialize(lineage, AiKnowledgeAnswerLineageJsonContext.Default.AiKnowledgeAnswerLineage);
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes) throw Invalid();
        return json;
    }
    internal static AiKnowledgeAnswerLineage Decode(string? json, Guid messageId, Guid sessionId, Guid actor, Guid? tenantId)
    {
        try
        {
            if (string.IsNullOrEmpty(json) || json.Length > MaxBytes || new UTF8Encoding(false, true).GetByteCount(json) > MaxBytes) throw Invalid();
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 20 });
            AiKnowledgeFaqProvenanceCodec.RejectDuplicates(document.RootElement);
            var root = document.RootElement;
            if (root.GetProperty("version").ValueKind != JsonValueKind.Number || !root.GetProperty("version").TryGetInt32(out var protocol)) throw Invalid();
            string[] rootFields = ["version", "messageId", "sessionId", "actorUserId", "tenantId", "dependencies", "references"];
            Fields(root, protocol == 8 ? [.. rootFields, "personalSchedules", "personalScheduleReferences"] : rootFields);
            if (protocol == 8)
            {
                var schedules = root.GetProperty("personalSchedules"); var scheduleReferences = root.GetProperty("personalScheduleReferences");
                if (schedules.ValueKind != JsonValueKind.Array || schedules.GetArrayLength() is < 1 or > 7
                    || scheduleReferences.ValueKind != JsonValueKind.Array || scheduleReferences.GetArrayLength() > 7) throw Invalid();
                foreach (var source in schedules.EnumerateArray()) Fields(source, "kind", "purpose", "id", "tenantId", "ownerUserId", "version", "projectionHash");
                foreach (var reference in scheduleReferences.EnumerateArray()) Fields(reference, "id", "scheduleId");
            }
            var dependencies = root.GetProperty("dependencies"); var references = root.GetProperty("references");
            if (dependencies.ValueKind != JsonValueKind.Array || dependencies.GetArrayLength() is < 1 or > 8
                || references.ValueKind != JsonValueKind.Array || references.GetArrayLength() > 8) throw Invalid();
            foreach (var dependency in dependencies.EnumerateArray()) DependencyFields(dependency, protocol is 5 or 6 or 7 or 8, protocol >= 6, protocol is 7 or 8, protocol == 8);
            foreach (var reference in references.EnumerateArray())
            {
                if (reference.ValueKind != JsonValueKind.Object) throw Invalid();
                if (reference.TryGetProperty("correction", out var correction))
                {
                    if (!root.GetProperty("version").TryGetInt32(out var sourceVersion) || sourceVersion is not (4 or 5 or 6 or 7 or 8)) throw Invalid();
                    Fields(reference, "id", "documentId", "versionId", "sequence", "pageNumber", "offset", "length", "correction");
                    Fields(correction, "revisionId", "textLength", "spans");
                    var spans = correction.GetProperty("spans");
                    if (spans.ValueKind != JsonValueKind.Array || spans.GetArrayLength() is < 1 or > 32) throw Invalid();
                    foreach (var span in spans.EnumerateArray())
                    {
                        Fields(span, "contentKind", "textOffset", "textLength", "sourceLocation", "sourceOffset", "sourceLength");
                        if (span.GetProperty("sourceLocation") is { ValueKind: not JsonValueKind.Null } anchor) SourceFields(anchor);
                    }
                }
                else if (reference.TryGetProperty("sourceLocation", out var sourceLocation))
                {
                    if (!root.GetProperty("version").TryGetInt32(out var sourceVersion) || sourceVersion is not (3 or 4 or 5 or 6 or 7 or 8)) throw Invalid();
                    Fields(reference, "id", "documentId", "versionId", "sequence", "pageNumber", "offset", "length", "sourceLocation");
                    SourceFields(sourceLocation);
                }
                else if (reference.TryGetProperty("docxLocation", out var location))
                {
                    var versionElement = root.GetProperty("version");
                    if (versionElement.ValueKind != JsonValueKind.Number || !versionElement.TryGetInt32(out var version) || version is not (2 or 5 or 6 or 7 or 8)) throw Invalid();
                    Fields(reference, "id", "documentId", "versionId", "sequence", "pageNumber", "offset", "length", "docxLocation");
                    Fields(location, "blockSequence", "paragraphNumber", "tableNumber", "rowNumber", "cellNumber");
                }
                else Fields(reference, "id", "documentId", "versionId", "sequence", "pageNumber", "offset", "length");
            }
            var lineage = JsonSerializer.Deserialize(json, AiKnowledgeAnswerLineageJsonContext.Default.AiKnowledgeAnswerLineage) ?? throw Invalid();
            Validate(lineage);
            if (lineage.MessageId != messageId || lineage.SessionId != sessionId || lineage.ActorUserId != actor || lineage.TenantId != tenantId) throw Invalid();
            return lineage with { Dependencies = Array.AsReadOnly(lineage.Dependencies.Select(value => value with
                { Faq = value.Faq is { } faq ? faq with { Dependencies = Array.AsReadOnly(faq.Dependencies.ToArray()) } : null }).ToArray()),
                References = Array.AsReadOnly(lineage.References.Select(r => r with { Correction = AiKnowledgeCorrectionCitations.Capture(r.Correction) }).ToArray()),
                PersonalSchedules = lineage.PersonalSchedules is { } scheduleProofs ? Array.AsReadOnly(scheduleProofs.ToArray()) : null,
                PersonalScheduleReferences = lineage.PersonalScheduleReferences is { } scheduleRefs ? Array.AsReadOnly(scheduleRefs.ToArray()) : null };
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { throw Invalid(); }
        catch (DecoderFallbackException) { throw Invalid(); }
        catch (EncoderFallbackException) { throw Invalid(); }
    }
    /// <summary>只投影全部召回依赖与已接受引用；临时快照中的凭据、向量与来源正文不进入存储。</summary>
    internal static AiKnowledgeAnswerLineage Create(AiKnowledgeExecutedAnswer answer, Guid messageId, Guid sessionId)
        => Build(answer.Prepared.Retrieval, answer.Answer.Citations, messageId, sessionId, answer.Answer.PersonalScheduleCitations);

    /// <summary>生成收费前验证全部候选引用都可持久化，不能在模型返回后才发现来源元数据超过容量。</summary>
    internal static void EnsureFits(AiKnowledgePreparedAnswer prepared)
    {
        // 引用身份只作为等长占位，不创建消息、会话或费用操作。
        if (prepared.Context.Citations.Count == 0) throw Invalid(); var placeholder = prepared.Context.Citations[0].Id;
        _ = Encode(Build(prepared.Retrieval, prepared.Context.Citations, placeholder, placeholder, prepared.Context.PersonalScheduleCitations));
    }

    private static AiKnowledgeAnswerLineage Build(AiKnowledgeRetrievalSnapshot snapshot, IReadOnlyList<AiKnowledgeCitation> citations, Guid messageId, Guid sessionId,
        IReadOnlyList<AiPersonalScheduleCitation> scheduleCitations)
    {
        var dependencies = snapshot.Sources.Select(row => new AiKnowledgeAnswerDependency(row.KnowledgeBaseId, row.DocumentId,
            row.VersionId, row.DocumentVersion, row.KnowledgeBaseVersion, row.FileId, row.ContentHash, row.TaskId ?? Guid.Empty,
            row.ActiveReceiptHash ?? "", row.ActiveManifestHash ?? "", row.ActivePlanHash ?? "", row.PublicationId)
            { Faq = row.FaqRevisionId.HasValue ? snapshot.FaqSources.GetValueOrDefault(row.DocumentId) ?? throw Invalid() : null }).ToArray();
        if (dependencies.Where((dependency, i) => !Matches(dependency, snapshot.Sources[i])).Any()) throw Invalid();
        var references = citations.Select(citation => new AiKnowledgeAnswerReference(citation.Id,
            citation.Chunk.Source.DocumentId, citation.Chunk.Source.VersionId, citation.Chunk.Sequence,
            citation.Chunk.PageNumber, citation.Chunk.Correction is null ? citation.Chunk.Offset : null, citation.Chunk.Correction is null ? citation.Chunk.Length : null)
            { DocxLocation = citation.Chunk.DocxLocation, SourceLocation = citation.Chunk.SourceLocation, Correction = AiKnowledgeCorrectionCitations.Capture(citation.Chunk.Correction) }).ToArray();
        // 人工引用写 v4，不能把修订内区间伪装成原文；原自动引用及完整依赖仍按其原有语义保存。
        var scheduleProofs = AiKnowledgeFaqBusinessSources.For(snapshot);
        var lineage = new AiKnowledgeAnswerLineage(scheduleProofs.Count > 0 ? 8 : dependencies.Any(value => value.Faq?.RequiresFeedbackPermission == true) ? 7 : dependencies.Any(value => value.Faq?.Graph is not null) ? 6 : dependencies.Any(value => value.Faq is not null) ? 5 : references.Any(r => r.Correction is not null) ? 4 : 3, messageId, sessionId, snapshot.Actor, snapshot.TenantId,
            Array.AsReadOnly(dependencies), Array.AsReadOnly(references))
        {
            PersonalSchedules = scheduleProofs.Count > 0 ? scheduleProofs : null,
            PersonalScheduleReferences = scheduleProofs.Count > 0 ? Array.AsReadOnly(scheduleCitations.Select(value =>
            {
                if (!snapshot.PersonalSchedules.Any(source => source.Proof == value.Proof)) throw Invalid();
                return new AiPersonalScheduleAnswerReference(value.Id, value.Proof.Id);
            }).ToArray()) : null
        };
        if (snapshot.PersonalSchedules.Count == 0 && scheduleCitations.Count > 0) throw Invalid();
        Validate(lineage); return lineage;
    }
    internal static bool Matches(AiKnowledgeAnswerDependency dependency, AiKnowledgeIndexStatusRecord row) =>
        row.PublicationId == dependency.PublicationId && row.KnowledgeBaseId == dependency.KnowledgeBaseId && row.DocumentId == dependency.DocumentId && row.VersionId == dependency.VersionId
        && row.DocumentVersion == dependency.DocumentVersion && row.KnowledgeBaseVersion == dependency.KnowledgeBaseVersion
        && row.FileId == dependency.FileId && row.FaqRevisionId == dependency.Faq?.RevisionId && row.ContentHash == dependency.ContentHash && row.TaskId == dependency.TaskId
        && (dependency.Faq is null || row.ModelConfigId == dependency.Faq.ModelConfigId && row.ModelConfigVersion == dependency.Faq.ModelConfigVersion)
        && row.ActiveTaskId == dependency.TaskId && row.ActiveLeaseToken is { } lease && lease != Guid.Empty
        && row.ActiveReceiptHash == dependency.ReceiptHash && row.ActiveManifestHash == dependency.ManifestHash && row.ActivePlanHash == dependency.PlanHash
        && row.IsDeleted == 0 && row.CleanupPending == 0 && row.IsCurrent == 1 && row.StateKey == "completed" && row.HasDerivedData == 1;
    private static void Validate(AiKnowledgeAnswerLineage lineage)
    {
        if (lineage.Version is not (1 or 2 or 3 or 4 or 5 or 6 or 7 or 8) || lineage.MessageId == Guid.Empty || lineage.SessionId == Guid.Empty || lineage.ActorUserId == Guid.Empty
            || lineage.TenantId == Guid.Empty || lineage.Dependencies is null || lineage.References is null
            || lineage.Dependencies.Count is < 1 or > 8 || lineage.References.Count > 8) throw Invalid();
        // 旧版本保持精确字段；新版允许拒答或未展示 DOCX 引用，完整来源证明仍由外层逐项验证。
        if (lineage.Version is not (2 or 5 or 6 or 7 or 8) && lineage.References.Any(reference => reference?.DocxLocation is not null)
            || lineage.Version < 3 && lineage.References.Any(reference => reference?.SourceLocation is not null)) throw Invalid();
        if (lineage.Version == 6 && !lineage.Dependencies.Any(value => value?.Faq?.Graph is not null) || lineage.Version < 6 && lineage.Dependencies.Any(value => value?.Faq?.Graph is not null)) throw Invalid();
        if (lineage.Version != 8 && (lineage.Version == 7) != lineage.Dependencies.Any(value => value?.Faq?.RequiresFeedbackPermission == true)) throw Invalid();
        if (lineage.Version == 8)
        {
            if (lineage.PersonalSchedules is not { Count: >= 1 and <= 7 } schedules || lineage.PersonalScheduleReferences is null
                || schedules.Any(value => !AiPersonalScheduleAnswerSources.Valid(value, lineage.ActorUserId, lineage.TenantId))
                || schedules.Select(value => value.Id).Distinct().Count() != schedules.Count
                || lineage.Dependencies.Count + schedules.Count > 8
                || lineage.PersonalScheduleReferences.Count + lineage.References.Count > 8) throw Invalid();
        }
        else if (lineage.PersonalSchedules is not null || lineage.PersonalScheduleReferences is not null) throw Invalid();
        var documents = new HashSet<Guid>(); var citations = new HashSet<Guid>();
        foreach (var source in lineage.Dependencies)
        {
            if (source is null || source.KnowledgeBaseId == Guid.Empty || source.DocumentId == Guid.Empty || !documents.Add(source.DocumentId)
                || source.VersionId == Guid.Empty || source.TaskId == Guid.Empty
                || source.DocumentVersion < 1 || source.KnowledgeBaseVersion < 1 || !Hash(source.ContentHash, allowLowercase: true)
                || !Hash(source.ReceiptHash) || !Hash(source.ManifestHash) || !Hash(source.PlanHash, lineage.Version >= 2)) throw Invalid();
            if (source.Faq is { } faq)
            {
                if (lineage.Version is not (5 or 6 or 7 or 8) || source.FileId is not null || faq.FaqId == Guid.Empty || faq.RevisionId == Guid.Empty
                    || !Hash(faq.SourceHash) || !Hash(faq.ContentHash) || faq.ModelConfigId == Guid.Empty || faq.ModelConfigVersion < 1
                    || faq.Dependencies is null || faq.Dependencies.Count > 8 || faq.Dependencies.Any(value => value is null || value.Faq is not null)
                    || source.PublicationId is null || source.PublicationId == Guid.Empty) throw Invalid();
                // 节点的文档叶独立核证；父关系只来自有界平面图，禁止再嵌套 FAQ 依赖或省略私人用途标记。
                if (faq.Dependencies.Count > 0)
                    Validate(new(3, lineage.MessageId, lineage.SessionId, lineage.ActorUserId, lineage.TenantId, faq.Dependencies, []));
                AiKnowledgeFaqGraphs.Validate(faq.Graph, faq.Dependencies, faq.FaqId, source.DocumentId);
                AiKnowledgeFaqBusinessSources.Validate(faq.PersonalSchedules, faq.Dependencies.Count, faq.Graph);
                if (faq.PersonalSchedules is not null && (lineage.Version != 8
                    || faq.PersonalSchedules.Any(value => lineage.PersonalSchedules?.Contains(value) != true))) throw Invalid();
            }
            else if (source.FileId is null || source.FileId == Guid.Empty) throw Invalid();
        }
        if (lineage.Dependencies.Where(value => value.Faq is not null).SelectMany(value => value.Faq!.Dependencies)
            .Select(value => value.DocumentId).Distinct().Count() > 8) throw Invalid();
        var ancestors = lineage.Dependencies.Where(value => value.Faq?.Graph is not null).SelectMany(value => value.Faq!.Graph!.Nodes).GroupBy(node => node.Status.DocumentId).ToArray();
        var proofLeaves = lineage.Dependencies.Where(value => value.Faq is not null).SelectMany(value => value.Faq!.Dependencies).Select(value => value.DocumentId).Distinct().Count();
        if (ancestors.Length > AiKnowledgeFaqGraphs.MaximumNodes || ancestors.Length + proofLeaves > AiKnowledgeFaqGraphs.MaximumDependencies
            || ancestors.Any(group => group.Skip(1).Any(node => node.Status != group.First().Status
                || !AiKnowledgeFaqPublishedSourceReader.Same(node.Source, group.First().Source) || !node.ParentDocumentIds.SequenceEqual(group.First().ParentDocumentIds)
                || node.Publication != group.First().Publication))) throw Invalid();
        foreach (var reference in lineage.References)
        {
            if (reference is null || reference.Id == Guid.Empty || !citations.Add(reference.Id) || reference.Sequence < 0
                || !lineage.Dependencies.Any(source => source.DocumentId == reference.DocumentId && source.VersionId == reference.VersionId)) throw Invalid();
            if (lineage.Dependencies.Single(value => value.DocumentId == reference.DocumentId).Faq is not null && reference.Correction is not null) throw Invalid();
            if (reference.Correction is { } correction)
            {
                if (lineage.Version is not (4 or 5 or 6 or 7 or 8) || reference.PageNumber is not null || reference.Offset is not null || reference.Length is not null
                    || reference.SourceLocation is not null || reference.DocxLocation is not null || !AiKnowledgeCorrectionCitations.Valid(correction)) throw Invalid();
                continue;
            }
            if (lineage.Dependencies.Single(value => value.DocumentId == reference.DocumentId).Faq is not null
                && (reference.PageNumber is not null || reference.SourceLocation is not null || reference.DocxLocation is not null)) throw Invalid();
            if (reference.Offset is not { } offset || reference.Length is not { } length
                || !AiKnowledgeSourceLocations.Valid(reference.SourceLocation)
                || reference.SourceLocation is not null && (reference.DocxLocation is not null || reference.PageNumber != reference.SourceLocation.PageNumber)
                || !ValidLocation(reference.DocxLocation, reference.PageNumber) || reference.PageNumber is < 1 || offset < 0 || length is < 1 or > 8000
                || (long)offset + length > int.MaxValue) throw Invalid();
        }
        foreach (var reference in lineage.PersonalScheduleReferences ?? [])
            if (reference is null || reference.Id == Guid.Empty || !citations.Add(reference.Id)
                || !lineage.PersonalSchedules!.Any(value => value.Id == reference.ScheduleId)) throw Invalid();
        if (lineage.Version == 8) AiPersonalScheduleAnswerSources.RequireBudget(
            AiPersonalScheduleAnswerSources.CountKnowledgeDependencies(lineage), lineage.PersonalSchedules!.Count);
    }
    private static void DependencyFields(JsonElement value, bool allowFaq, bool allowGraph = false, bool allowFeedback = false, bool allowBusiness = false)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        string[] fields = ["knowledgeBaseId", "documentId", "versionId", "documentVersion", "knowledgeBaseVersion", "fileId", "contentHash", "taskId", "receiptHash", "manifestHash", "planHash", "publicationId"];
        if (!value.TryGetProperty("faq", out var faq)) { Fields(value, fields); return; }
        if (!allowFaq) throw Invalid();
        Fields(value, [.. fields, "faq"]);
        if (faq.TryGetProperty("graph", out _) && !allowGraph) throw Invalid();
        var faqFields = new List<string> { "faqId", "revisionId", "sourceHash", "contentHash", "modelConfigId", "modelConfigVersion", "dependencies" };
        if (faq.TryGetProperty("graph", out _)) faqFields.Add("graph");
        if (faq.TryGetProperty("personalSchedules", out var schedules))
        {
            if (!allowBusiness || schedules.ValueKind != JsonValueKind.Array || schedules.GetArrayLength() is < 1 or > 7) throw Invalid();
            faqFields.Add("personalSchedules");
            foreach (var item in schedules.EnumerateArray()) Fields(item, "kind", "purpose", "id", "tenantId", "ownerUserId", "version", "projectionHash");
        }
        if (faq.TryGetProperty("feedbackDerived", out var feedback)) { if (!allowFeedback || feedback.ValueKind != JsonValueKind.True) throw Invalid(); faqFields.Add("feedbackDerived"); }
        Fields(faq, faqFields.ToArray());
        if (faq.GetProperty("dependencies").ValueKind != JsonValueKind.Array || faq.GetProperty("dependencies").GetArrayLength() > 8) throw Invalid();
        foreach (var dependency in faq.GetProperty("dependencies").EnumerateArray()) DependencyFields(dependency, false);
    }
    internal static bool ValidLocation(Full.NET.Modules.Ai.Contracts.AiKnowledgeDocxLocationResponse? location, int? page) => location is null
        || page is null && location.BlockSequence is >= 0 and < 8192 && location.ParagraphNumber is >= 1 and <= 8192
        && (location.TableNumber is null && location.RowNumber is null && location.CellNumber is null
            || location.TableNumber is >= 1 and <= 128 && location.RowNumber is >= 1 and <= 4096 && location.CellNumber is >= 1 and <= 64);
    private static void SourceFields(JsonElement value, bool parent = false)
    {
        Fields(value, "blockSequence", "kind", "pageNumber", "paragraphNumber", "tableNumber", "rowNumber", "cellNumber", "grid", "slide", "htmlPath", "image");
        if (value.GetProperty("grid") is { ValueKind: not JsonValueKind.Null } grid)
            Fields(grid, "sheetNumber", "sheetName", "row", "column");
        if (value.GetProperty("slide") is { ValueKind: not JsonValueKind.Null } slide)
            Fields(slide, "slideNumber", "shapeNumber", "rowNumber", "columnNumber", "rowSpan", "columnSpan");
        if (value.GetProperty("image") is { ValueKind: not JsonValueKind.Null } image)
        {
            if (parent) throw Invalid();
            Fields(image, "frameNumber", "imageWidth", "imageHeight", "x", "y", "width", "height", "confidence", "tableNumber", "rowNumber", "columnNumber", "parent");
            if (image.GetProperty("parent") is { ValueKind: not JsonValueKind.Null } anchor) SourceFields(anchor, true);
        }
    }
    private static bool Hash(string? value, bool allowLowercase = false) => value is { Length: 64 }
        && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F' || allowLowercase && c is >= 'a' and <= 'f');
    private static void Fields(JsonElement element, params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in element.EnumerateObject()) if (!expected.Contains(field.Name, StringComparer.Ordinal) || !names.Add(field.Name)) throw Invalid();
        if (names.Count != expected.Length) throw Invalid();
    }
    private static InvalidDataException Invalid() => new("知识答案来源依赖无效或不可用。");
}

/// <summary>持久化元数据只使用显式源生成类型，禁止依赖反射序列化。</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, GenerationMode = JsonSourceGenerationMode.Metadata,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(Full.NET.Modules.Ai.Contracts.AiKnowledgeSourceLocationResponse))]
[JsonSerializable(typeof(Full.NET.Modules.Ai.Contracts.AiKnowledgeDocxLocationResponse))]
[JsonSerializable(typeof(Full.NET.Modules.Ai.Contracts.AiKnowledgeCorrectionCitationResponse))]
[JsonSerializable(typeof(AiKnowledgeAnswerLineage))]
[JsonSerializable(typeof(AiKnowledgeAnswerDependency[]))]
[JsonSerializable(typeof(AiKnowledgeAnswerFeedbackReviewBinding[]))]
internal partial class AiKnowledgeAnswerLineageJsonContext : JsonSerializerContext;
