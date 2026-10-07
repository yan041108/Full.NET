using Full.NET.Modules.Ai.Persistence;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>仅保存来源身份和完成证明；不得保存正文、向量、模型目的地或凭据。</summary>
internal sealed record AiKnowledgeAnswerDependency(Guid KnowledgeBaseId, Guid DocumentId, Guid VersionId,
    int DocumentVersion, int KnowledgeBaseVersion, Guid FileId, string ContentHash,
    Guid TaskId, string ReceiptHash, string ManifestHash, string PlanHash);

/// <summary>服务器核对的引用位置；模型输出只能选择引用标识。</summary>
internal sealed record AiKnowledgeAnswerReference(Guid Id, Guid DocumentId, Guid VersionId,
    int Sequence, int? PageNumber, int Offset, int Length);

/// <summary>版本化完整依赖绑定实际答案消息及可信所有者范围，不能按展示引用缩减。</summary>
internal sealed record AiKnowledgeAnswerLineage(int Version, Guid MessageId, Guid SessionId, Guid ActorUserId,
    Guid? TenantId, IReadOnlyList<AiKnowledgeAnswerDependency> Dependencies, IReadOnlyList<AiKnowledgeAnswerReference> References);

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
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 5 });
            var root = document.RootElement;
            Fields(root, "version", "messageId", "sessionId", "actorUserId", "tenantId", "dependencies", "references");
            var dependencies = root.GetProperty("dependencies"); var references = root.GetProperty("references");
            if (dependencies.ValueKind != JsonValueKind.Array || dependencies.GetArrayLength() is < 1 or > 8
                || references.ValueKind != JsonValueKind.Array || references.GetArrayLength() > 8) throw Invalid();
            foreach (var dependency in dependencies.EnumerateArray())
                Fields(dependency, "knowledgeBaseId", "documentId", "versionId", "documentVersion", "knowledgeBaseVersion", "fileId",
                    "contentHash", "taskId", "receiptHash", "manifestHash", "planHash");
            foreach (var reference in references.EnumerateArray())
                Fields(reference, "id", "documentId", "versionId", "sequence", "pageNumber", "offset", "length");
            var lineage = JsonSerializer.Deserialize(json, AiKnowledgeAnswerLineageJsonContext.Default.AiKnowledgeAnswerLineage) ?? throw Invalid();
            Validate(lineage);
            if (lineage.MessageId != messageId || lineage.SessionId != sessionId || lineage.ActorUserId != actor || lineage.TenantId != tenantId) throw Invalid();
            return lineage with { Dependencies = Array.AsReadOnly(lineage.Dependencies.ToArray()), References = Array.AsReadOnly(lineage.References.ToArray()) };
        }
        catch (JsonException) { throw Invalid(); }
        catch (DecoderFallbackException) { throw Invalid(); }
        catch (EncoderFallbackException) { throw Invalid(); }
    }
    /// <summary>只投影全部召回依赖与已接受引用；临时快照中的凭据、向量与来源正文不进入存储。</summary>
    internal static AiKnowledgeAnswerLineage Create(AiKnowledgeExecutedAnswer answer, Guid messageId, Guid sessionId)
    {
        var snapshot = answer.Prepared.Retrieval;
        var dependencies = snapshot.Sources.Select(row => new AiKnowledgeAnswerDependency(row.KnowledgeBaseId, row.DocumentId,
            row.VersionId, row.DocumentVersion, row.KnowledgeBaseVersion, row.FileId, row.ContentHash, row.TaskId ?? Guid.Empty,
            row.ActiveReceiptHash ?? "", row.ActiveManifestHash ?? "", row.ActivePlanHash ?? "")).ToArray();
        if (dependencies.Where((dependency, i) => !Matches(dependency, snapshot.Sources[i])).Any()) throw Invalid();
        var references = answer.Answer.Citations.Select(citation => new AiKnowledgeAnswerReference(citation.Id,
            citation.Chunk.Source.DocumentId, citation.Chunk.Source.VersionId, citation.Chunk.Sequence,
            citation.Chunk.PageNumber, citation.Chunk.Offset, citation.Chunk.Length)).ToArray();
        var lineage = new AiKnowledgeAnswerLineage(1, messageId, sessionId, snapshot.Actor, snapshot.TenantId,
            Array.AsReadOnly(dependencies), Array.AsReadOnly(references));
        Validate(lineage); return lineage;
    }
    internal static bool Matches(AiKnowledgeAnswerDependency dependency, AiKnowledgeIndexStatusRecord row) =>
        row.KnowledgeBaseId == dependency.KnowledgeBaseId && row.DocumentId == dependency.DocumentId && row.VersionId == dependency.VersionId
        && row.DocumentVersion == dependency.DocumentVersion && row.KnowledgeBaseVersion == dependency.KnowledgeBaseVersion
        && row.FileId == dependency.FileId && row.ContentHash == dependency.ContentHash && row.TaskId == dependency.TaskId
        && row.ActiveTaskId == dependency.TaskId && row.ActiveLeaseToken is { } lease && lease != Guid.Empty
        && row.ActiveReceiptHash == dependency.ReceiptHash && row.ActiveManifestHash == dependency.ManifestHash && row.ActivePlanHash == dependency.PlanHash
        && row.IsDeleted == 0 && row.CleanupPending == 0 && row.IsCurrent == 1 && row.StateKey == "completed" && row.HasDerivedData == 1;
    private static void Validate(AiKnowledgeAnswerLineage lineage)
    {
        if (lineage.Version != 1 || lineage.MessageId == Guid.Empty || lineage.SessionId == Guid.Empty || lineage.ActorUserId == Guid.Empty
            || lineage.TenantId == Guid.Empty || lineage.Dependencies is null || lineage.References is null
            || lineage.Dependencies.Count is < 1 or > 8 || lineage.References.Count > 8) throw Invalid();
        var documents = new HashSet<Guid>(); var citations = new HashSet<Guid>();
        foreach (var source in lineage.Dependencies)
        {
            if (source is null || source.KnowledgeBaseId == Guid.Empty || source.DocumentId == Guid.Empty || !documents.Add(source.DocumentId)
                || source.VersionId == Guid.Empty || source.FileId == Guid.Empty || source.TaskId == Guid.Empty
                || source.DocumentVersion < 1 || source.KnowledgeBaseVersion < 1 || !Hash(source.ContentHash)
                || !Hash(source.ReceiptHash) || !Hash(source.ManifestHash) || !Hash(source.PlanHash)) throw Invalid();
        }
        foreach (var reference in lineage.References)
            if (reference is null || reference.Id == Guid.Empty || !citations.Add(reference.Id) || reference.Sequence < 0
                || reference.PageNumber is < 1 || reference.Offset < 0 || reference.Length is < 1 or > 8000
                || (long)reference.Offset + reference.Length > int.MaxValue
                || !lineage.Dependencies.Any(source => source.DocumentId == reference.DocumentId && source.VersionId == reference.VersionId)) throw Invalid();
    }
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
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
[JsonSerializable(typeof(AiKnowledgeAnswerLineage))]
[JsonSerializable(typeof(AiKnowledgeAnswerDependency[]))]
internal partial class AiKnowledgeAnswerLineageJsonContext : JsonSerializerContext;
