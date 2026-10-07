using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>引用始终携带服务器确认的来源位置，不从生成模型返回值推导。</summary>
internal sealed record AiKnowledgeRetrievalChunk(AiKnowledgeIndexStatusRecord Source, int Sequence, int? PageNumber,
    int Offset, int Length, int TokenCount, string Text, ReadOnlyMemory<float> Vector);
/// <summary>固定模型空间下的余弦分数，平分按规范来源标识与块序号排序。</summary>
internal sealed record AiKnowledgeRankedChunk(AiKnowledgeRetrievalChunk Chunk, double Score);
