using MemoryPack;

namespace Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;

/// <summary>文档元数据已提交；只通知来源意图对账，不携带文本、会话或收费授权。</summary>
/// <remarks>持久化 v1 字段顺序固定；元数据版本不代表文件来源版本。</remarks>
/// <param name="KnowledgeBaseId">AI 自有知识库。</param>
/// <param name="DocumentId">AI 自有文档。</param>
/// <param name="MetadataVersion">成功更新后读取的文档版本。</param>
/// <param name="TenantId">生产者可信租户范围，Host 为 null。</param>
[MemoryPackable]
internal sealed partial record AiKnowledgeDocumentMetadataUpdated(Guid KnowledgeBaseId, Guid DocumentId, int MetadataVersion, Guid? TenantId)
{
    internal const string EventType = "fullnet.ai.knowledge_document.metadata_updated";
    internal const string TopicCode = "ai.knowledge-document-metadata-updated.v1";
}
