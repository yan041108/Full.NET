namespace Full.NET.Modules.Ai.Contracts;

/// <summary>受控审核决定；反馈与审核双版本均来自服务端当前队列，不接收正文或任意指令。</summary>
/// <param name="DecisionKey">approved 或 rejected，仅批准标签统计资格。</param>
/// <param name="FeedbackVersion">本次审核的反馈版本。</param>
/// <param name="ReviewVersion">当前审核版本，首次审核为零。</param>
public sealed record ReviewAiKnowledgeAnswerFeedbackRequest(string DecisionKey, int FeedbackVersion, int ReviewVersion);

/// <summary>有权评审者可读取的结构化候选；不含消息、会话、主体或来源标识及正文。</summary>
/// <param name="FeedbackId">获当前来源资格的反馈标识，仅供审核命令使用。</param>
/// <param name="RatingKey">固定评价机器码。</param>
/// <param name="ReasonKey">固定原因机器码。</param>
/// <param name="CitationCount">该反馈选择的实际引用数量。</param>
/// <param name="FeedbackVersion">当前反馈并发版本。</param>
/// <param name="ReviewStateKey">当前批准、拒绝或待审核状态；反馈改变使旧状态失效。</param>
/// <param name="ReviewVersion">保留的单调审核版本，首次为零。</param>
public sealed record AiKnowledgeAnswerFeedbackReviewItem(Guid FeedbackId, string RatingKey, string ReasonKey,
    int CitationCount, int FeedbackVersion, string ReviewStateKey, int ReviewVersion);

/// <summary>有界扫描页；不透明游标绑定当前评审者与可信范围，不授予来源资格。</summary>
/// <param name="Items">本页获当前来源资格的标签。</param>
/// <param name="NextCursor">继续扫描的不透明位置，十五分钟有效；空值表示当前扫描结束。</param>
public sealed record AiKnowledgeAnswerFeedbackReviewPage(IReadOnlyList<AiKnowledgeAnswerFeedbackReviewItem> Items, string? NextCursor);

/// <summary>通过小样本抑制的标签统计；没有关联个人或原始回答的标识。</summary>
/// <param name="RatingKey">固定评价机器码。</param>
/// <param name="ReasonKey">固定原因机器码。</param>
/// <param name="CitationCount">选择的实际引用数量。</param>
/// <param name="FeedbackCount">至少五名不同提交者构成的反馈条数；重复提交者不会补足阈值。</param>
public sealed record AiKnowledgeAnswerFeedbackExportCell(string RatingKey, string ReasonKey, int CitationCount, int FeedbackCount);

/// <summary>仅供评估选题的结构化统计，不是可直接训练或输入模型的答案样例。</summary>
/// <remarks>实时精确计数存在差分推断风险，小样本抑制不保证匿名化。</remarks>
/// <param name="PolicyVersion">固定统计规则版本。</param>
/// <param name="IsPartial">始终为真，表示固定最多六十四项的批准窗口，不承诺总体完整性。</param>
/// <param name="Cells">通过当前来源资格与小样本抑制的标签格子。</param>
public sealed record AiKnowledgeAnswerFeedbackExportResponse(string PolicyVersion, bool IsPartial,
    IReadOnlyList<AiKnowledgeAnswerFeedbackExportCell> Cells);

/// <summary>同可信 Host/租户的独立评审权限，不授予其他人的会话读取权。</summary>
public static class AiKnowledgeAnswerFeedbackReviewPermissions
{
    /// <summary>读取获来源资格的结构化审核队列。</summary>
    public const string Read = "ai.knowledge_answer_feedback.review_read";
    /// <summary>审核他人的当前反馈标签。</summary>
    public const string Review = "ai.knowledge_answer_feedback.review";
    /// <summary>导出当前批准标签的有界聚合统计。</summary>
    public const string Export = "ai.knowledge_answer_feedback.export";
}
