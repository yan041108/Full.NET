namespace Full.NET.Modules.Ai.Contracts;

/// <summary>会话内独立知识问答；生成模型固定为会话绑定，不接受客户端来源证明。</summary>
/// <param name="KnowledgeBaseIds">本次完整读取的知识库集合，最多八个。</param>
/// <param name="EmbeddingModelConfigId">显式选择的 Embedding 模型配置。</param>
/// <param name="Question">最多 4096 字符的问题。</param>
/// <param name="SearchScope">从搜索进入时绑定文档范围、精确索引模型版本和全部发布身份；不是客户端授权证明。</param>
/// <param name="PersonalSchedules">显式加入的本人日程期望身份，全部内容及资格由Calendar再次核证。</param>
public sealed record AskAiKnowledgeRequest(IReadOnlyList<Guid> KnowledgeBaseIds, Guid EmbeddingModelConfigId, string Question,
    AiKnowledgeSearchAnswerScope? SearchScope = null, IReadOnlyList<AiPersonalScheduleSourceSelection>? PersonalSchedules = null);

/// <summary>搜索到收费问答的冻结选择；服务器在收费前和每次交付复核时重新证明全部来源。</summary>
/// <param name="DocumentIds">搜索使用的文档筛选；空数组表示所选知识库内全部文档。</param>
/// <param name="EmbeddingModelVersion">搜索时批准的索引模型版本。</param>
/// <param name="Sources">搜索响应的完整来源集合，不能仅传命中或最终引用的来源。</param>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record AiKnowledgeSearchAnswerScope(IReadOnlyList<Guid> DocumentIds, int EmbeddingModelVersion,
    IReadOnlyList<AiKnowledgeSearchSource> Sources);

/// <summary>完成落库、清理及当前资格复核后交付的答案。</summary>
/// <param name="Message">服务器确认的消息与引用位置。</param>
/// <param name="Refused">证据不足时明确拒答，正文与引用均为空。</param>
public sealed record AskAiKnowledgeResponse(AiChatMessageResponse Message, bool Refused);
