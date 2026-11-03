using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>有界导出选择；整次请求不截断，不返回部分来源正文。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExportAiKnowledgeFaqs(IReadOnlyList<Guid> FaqIds);
/// <summary>导出保留原始权威身份和全部来源；这些字段不是导入授权，目标作用域仍须重新核证。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiKnowledgeFaqTransferItem(Guid FaqId, Guid KnowledgeBaseId, Guid RevisionId, int HeadVersion,
    string Origin, string? Basis, string SourceHash, string ContentHash, AiKnowledgeFaqContent Content,
    IReadOnlyList<AiKnowledgeSearchSource> Sources)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiKnowledgeFaqSourceGraph? Graph { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiKnowledgeFaqFeedbackSource? Feedback { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AiPersonalScheduleSourceSelection>? PersonalSchedules { get; init; }
}
/// <summary>当前包只接受本实例当前可读的权威修订；未知实例或缺少原始记录时拒绝，不降级为独立内容。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiKnowledgeFaqTransferPackage(string Format, IReadOnlyList<AiKnowledgeFaqTransferItem> Items);
/// <summary>预览不写入；提交时绑定整个包摘要，并逐条重新读取当前来源。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewAiKnowledgeFaqImport(AiKnowledgeFaqTransferPackage Package);
/// <summary>每条草稿使用独立幂等键和本地事务；重试必须保留原键，不自动重试未确认请求。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ImportAiKnowledgeFaqs(AiKnowledgeFaqTransferPackage Package, string ExpectedDigest, IReadOnlyList<Guid> RequestKeys);
/// <summary>不回显不合格条目的正文；状态为 ready、source_unavailable、classification_conflict、conflict、created 或 replayed。</summary>
public sealed record AiKnowledgeFaqImportItemResult(int Index, string Status, Guid? FaqId, Guid? SavedRevisionId);
/// <summary>逐条原子结果，不宣称整包原子；失败项不能计入已创建，未执行项不能计入成功。</summary>
public sealed record AiKnowledgeFaqImportReport(string Digest, IReadOnlyList<AiKnowledgeFaqImportItemResult> Items);
