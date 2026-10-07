namespace Full.NET.Modules.Ai.Contracts;

/// <summary>会话内独立知识问答；生成模型固定为会话绑定，不接受客户端来源证明。</summary>
/// <param name="KnowledgeBaseIds">本次完整读取的知识库集合，最多八个。</param>
/// <param name="EmbeddingModelConfigId">显式选择的 Embedding 模型配置。</param>
/// <param name="Question">最多 4096 字符的问题。</param>
public sealed record AskAiKnowledgeRequest(IReadOnlyList<Guid> KnowledgeBaseIds, Guid EmbeddingModelConfigId, string Question);

/// <summary>完成落库、清理及当前资格复核后交付的答案。</summary>
/// <param name="Message">服务器确认的消息与引用位置。</param>
/// <param name="Refused">证据不足时明确拒答，正文与引用均为空。</param>
public sealed record AskAiKnowledgeResponse(AiChatMessageResponse Message, bool Refused);
