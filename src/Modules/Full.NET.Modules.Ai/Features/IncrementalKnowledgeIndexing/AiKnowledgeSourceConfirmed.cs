using MemoryPack;

namespace Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;

/// <summary>不可变文件版本与 Files 引用均已确认；只触发增量意图，不携带凭据、正文或执行授权。</summary>
/// <remarks>持久化 v1 字段顺序不可调整；后续字段只能追加，并保持旧事件可消费。</remarks>
/// <param name="KnowledgeBaseId">AI 自有知识库。</param>
/// <param name="DocumentId">AI 自有文档。</param>
/// <param name="VersionId">完成确认的不可变来源版本。</param>
/// <param name="TenantId">由生产者可信上下文取得的范围；Host 为 null。</param>
[MemoryPackable]
internal sealed partial record AiKnowledgeSourceConfirmed(Guid KnowledgeBaseId, Guid DocumentId, Guid VersionId, Guid? TenantId)
{
    internal const string EventType = "fullnet.ai.knowledge_source.confirmed";
    internal const string TopicCode = "ai.knowledge-source-confirmed.v1";
}
