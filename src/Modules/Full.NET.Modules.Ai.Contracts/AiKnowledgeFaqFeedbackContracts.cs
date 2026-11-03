using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>公开的准确反馈资格摘要；不包含原用户问题、原所属人或会话身份。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiKnowledgeFaqFeedbackSource(Guid FeedbackId, int FeedbackVersion, Guid ReviewId, int ReviewVersion,
    Guid MessageId, string LineageHash, string AnswerHash);
/// <summary>预览只接受准确反馈身份，正文和全部依赖由服务器重新读取。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewAiKnowledgeFaqFeedback(Guid FeedbackId);
/// <summary>供人工编写标准问题的字段；不接受客户端答案或原用户问题。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiKnowledgeFaqFeedbackDraftInput(string Question, IReadOnlyList<string> Aliases, string Category,
    DateTimeOffset? ValidFromUtc, DateTimeOffset? ExpiresAtUtc);
/// <summary>显式预览绑定来源及目标分类版本；生成初始草稿不调用模型。</summary>
public sealed record AiKnowledgeFaqFeedbackPreview(string Digest, string SourceHash, AiKnowledgeFaqFeedbackSource Feedback, string Answer,
    IReadOnlyList<AiKnowledgeSearchSource> Sources, AiKnowledgeFaqSourceGraph? Graph)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AiPersonalScheduleSourceSelection>? PersonalSchedules { get; init; }
}
/// <summary>提交预览摘要和人工问题，初始答案只能复用服务器当前核证的原回答。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAiKnowledgeFaqFeedbackDraft(Guid RequestKey, Guid FeedbackId, string Digest, AiKnowledgeFaqFeedbackDraftInput Content);
