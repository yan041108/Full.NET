using System.Text;
using System.Text.Json;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Serialization;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>草稿的不可变原始来源；独立依据、完整文档叶和有界父 FAQ 图均使用闭合协议，未知业务来源拒绝。</summary>
internal sealed record AiKnowledgeFaqProvenance(int Version, string Origin, string? Basis, IReadOnlyList<AiKnowledgeAnswerDependency> Dependencies)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public AiKnowledgeFaqGraph? Graph { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public AiKnowledgeFaqFeedbackBinding? Feedback { get; init; }
    /// <summary>v4保留全部直接和祖先业务证明；类型不能降级为独立依据或无业务来源的旧协议。</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<Full.NET.Modules.Calendar.Contracts.PersonalScheduleKnowledgeProof>? PersonalSchedules { get; init; }
    internal bool RequiresFeedbackPermission => Feedback is not null || Graph?.Nodes.Any(node => node.Source.FeedbackDerived) == true;
    /// <summary>静态候选 SQL 先过滤私人来源，完整解码再核对推导值，避免一条不可读记录使普通候选整体失败。</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool FeedbackPermission => RequiresFeedbackPermission;
}
internal static class AiKnowledgeFaqProvenanceCodec
{
    internal static string Encode(AiKnowledgeFaqProvenance value)
    {
        Validate(value); var json = JsonSerializer.Serialize(value, AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqProvenance);
        if (new UTF8Encoding(false, true).GetByteCount(json) > 16384) throw Invalid(); return json;
    }
    internal static AiKnowledgeFaqProvenance Decode(AiKnowledgeFaqDraftRecord row)
    {
        try
        {
            if (row.SourceJson.Length > 16384 || new UTF8Encoding(false, true).GetByteCount(row.SourceJson) > 16384
                || AiKnowledgeFaqDraftPolicy.Hash(row.SourceJson) != row.SourceHash) throw Invalid();
            using var json = JsonDocument.Parse(row.SourceJson, new JsonDocumentOptions { MaxDepth = 16 });
            var fields = new List<string> { "version", "origin", "basis", "dependencies" };
            if (json.RootElement.TryGetProperty("graph", out _)) fields.Add("graph");
            if (json.RootElement.TryGetProperty("personalSchedules", out var schedules))
            {
                fields.Add("personalSchedules");
                foreach (var item in schedules.EnumerateArray())
                    AiKnowledgeFaqDraftPolicy.Fields(item, "kind", "purpose", "id", "tenantId", "ownerUserId", "version", "projectionHash");
            }
            if (json.RootElement.TryGetProperty("feedbackPermission", out var permission))
            { if (permission.ValueKind != JsonValueKind.True) throw Invalid(); fields.Add("feedbackPermission"); }
            if (json.RootElement.TryGetProperty("feedback", out var feedback))
            {
                fields.Add("feedback");
                AiKnowledgeFaqDraftPolicy.Fields(feedback, "feedbackId", "feedbackVersion", "reviewId", "reviewVersion", "messageId", "sessionId", "ownerUserId", "tenantId", "lineageHash", "lineageSqlServerHash", "answerHash", "answerSqlServerHash");
            }
            AiKnowledgeFaqDraftPolicy.Fields(json.RootElement, fields.ToArray());
            RejectDuplicates(json.RootElement);
            foreach (var dependency in json.RootElement.GetProperty("dependencies").EnumerateArray())
                AiKnowledgeFaqDraftPolicy.Fields(dependency, "knowledgeBaseId", "documentId", "versionId", "documentVersion", "knowledgeBaseVersion", "fileId", "contentHash", "taskId", "receiptHash", "manifestHash", "planHash", "publicationId");
            var value = JsonSerializer.Deserialize(row.SourceJson, AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqProvenance) ?? throw Invalid(); Validate(value);
            if (value.RequiresFeedbackPermission != json.RootElement.TryGetProperty("feedbackPermission", out _)) throw Invalid();
            if (value.Origin != row.OriginKey || value.Basis != row.Basis) throw Invalid();
            if (value.Feedback is { } binding && binding.TenantId != row.TenantId) throw Invalid();
            if (value.PersonalSchedules?.Any(item => item.TenantId != row.TenantId || row.CreatedById != Guid.Empty && item.OwnerUserId != row.CreatedById) == true) throw Invalid();
            AiKnowledgeFaqGraphs.Validate(value.Graph, value.Dependencies, row.Id, row.ReservedDocumentId);
            return value with { Dependencies = Array.AsReadOnly(value.Dependencies.ToArray()), PersonalSchedules = AiKnowledgeFaqBusinessSources.Freeze(value.PersonalSchedules) };
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or EncoderFallbackException) { throw Invalid(); }
    }
    internal static AiKnowledgeAnswerDependency Dependency(AiKnowledgeIndexStatusRecord row) => new(row.KnowledgeBaseId, row.DocumentId, row.VersionId,
        row.DocumentVersion, row.KnowledgeBaseVersion, AiKnowledgeFileSource.RequireFileId(row), row.ContentHash, row.TaskId ?? Guid.Empty,
        row.ActiveReceiptHash ?? "", row.ActiveManifestHash ?? "", row.ActivePlanHash ?? "", row.PublicationId);
    private static void Validate(AiKnowledgeFaqProvenance value)
    {
        if (value is null || value.Dependencies is null || value.Origin is not ("independent" or "document_derived" or "faq_derived" or "feedback_derived")) throw Invalid();
        var business = value.PersonalSchedules is not null;
        if ((value.Version == 4) != business || business && value.Origin == "independent") throw Invalid();
        if (value.Origin == "feedback_derived")
        { if (value.Version != (business ? 4 : 3) || value.Feedback is null || value.Graph is null && value.Dependencies.Count == 0) throw Invalid(); value.Feedback.Validate(); }
        else if (value.Feedback is not null || (value.Origin == "faq_derived" ? value.Version != (business ? 4 : 2) || value.Graph is null : value.Version != (business ? 4 : 1) || value.Graph is not null)) throw Invalid();
        if (value.Origin == "independent")
        { if (value.Basis is null || AiKnowledgeFaqDraftPolicy.Basis(value.Basis) != value.Basis || value.Dependencies.Count != 0) throw Invalid(); return; }
        if (value.Basis is not null || value.Dependencies.Count > 8 || value.Origin == "document_derived" && value.Dependencies.Count == 0 || value.Dependencies.Any(item => item is null)
            || value.Dependencies.Select(item => item.DocumentId).Distinct().Count() != value.Dependencies.Count) throw Invalid();
        foreach (var item in value.Dependencies)
            if (item is null || item.KnowledgeBaseId == Guid.Empty || item.DocumentId == Guid.Empty || item.VersionId == Guid.Empty || item.FileId is null || item.FileId == Guid.Empty || item.Faq is not null
                || item.TaskId == Guid.Empty || item.PublicationId is null || item.PublicationId == Guid.Empty || item.DocumentVersion < 1 || item.KnowledgeBaseVersion < 1
                || new[] { item.ContentHash, item.ReceiptHash, item.ManifestHash, item.PlanHash }.Any(hash => hash is not { Length: 64 } || !hash.All(Uri.IsHexDigit))) throw Invalid();
        AiKnowledgeFaqGraphs.Validate(value.Graph, value.Dependencies);
        AiKnowledgeFaqBusinessSources.Validate(value.PersonalSchedules, value.Dependencies.Count, value.Graph);
    }
    internal static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject()) { if (!names.Add(property.Name)) throw Invalid(); RejectDuplicates(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) RejectDuplicates(item);
    }
    private static InvalidDataException Invalid() => new("FAQ 原始来源元数据无效或不受当前协议支持。");
}
