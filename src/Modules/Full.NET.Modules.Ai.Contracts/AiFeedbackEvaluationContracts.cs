using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>人工改写的评估样例；服务端不从生产问答复制正文，也不声称自动匿名化。</summary>
/// <param name="LanguageTag">规范语言标签，仅支持 zh-CN 或 en-US。</param>
/// <param name="Question">人工脱敏问题，最多 2048 个字符。</param>
/// <param name="ExpectedAnswer">人工脱敏的参考答案，最多 4096 个字符。</param>
/// <param name="Evidence">人工脱敏的完整评估依据，最多 8192 个字符。</param>
/// <param name="ShouldRefuse">该样例是否预期拒答。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiFeedbackEvaluationSample(string LanguageTag, string Question, string ExpectedAnswer, string Evidence, bool ShouldRefuse);

/// <summary>依据当前已批准反馈准备短期候选；版本不匹配时必须刷新，不能覆盖。</summary>
/// <param name="FeedbackVersion">当前反馈版本。</param>
/// <param name="ReviewVersion">当前已批准审核版本。</param>
/// <param name="Sample">仅接收人工脱敏样例。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PrepareAiFeedbackEvaluationRequest(int FeedbackVersion, int ReviewVersion, AiFeedbackEvaluationSample Sample);

/// <summary>候选凭据只在原交互会话内十五分钟有效，不能作为来源或评估授权。</summary>
/// <param name="CandidateToken">加密凭据；客户端不得解析、持久化或写入日志。</param>
/// <param name="SampleHash">规范样例的 SHA256。</param>
/// <param name="ExpiresAtUtc">原候选到期时间。</param>
public sealed record AiFeedbackEvaluationCandidate(string CandidateToken, string SampleHash, DateTimeOffset ExpiresAtUtc);

/// <summary>显式批准人工脱敏样例进入离线评估候选集；本操作不调用模型。</summary>
/// <param name="CandidateToken">准备阶段的原始短期凭据。</param>
/// <param name="ConfirmManualRedaction">确认已人工移除个人信息、凭据和生产机密。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdmitAiFeedbackEvaluationRequest(string CandidateToken, bool ConfirmManualRedaction);

/// <summary>获当前完整来源资格的评估输入；它不构成已通过质量评估或生产策略激活证据。</summary>
/// <param name="FormatVersion">固定格式版本。</param>
/// <param name="CaseKey">同候选的稳定摘要身份，不包含原消息或主体标识。</param>
/// <param name="SampleHash">规范样例摘要。</param>
/// <param name="Sample">已人工脱敏的评估输入。</param>
/// <param name="AdmittedAtUtc">本次重新核对来源并交付的时间。</param>
public sealed record AiFeedbackEvaluationAdmission(int FormatVersion, string CaseKey, string SampleHash,
    AiFeedbackEvaluationSample Sample, DateTimeOffset AdmittedAtUtc);

/// <summary>评估候选准备与准入分别授权，标签批准不隐含评估或外部模型调用资格。</summary>
public static class AiFeedbackEvaluationPermissions
{
    /// <summary>为已批准的反馈准备人工脱敏候选。</summary>
    public const string Prepare = "ai.knowledge_answer_feedback.evaluation_prepare";
    /// <summary>重新核对来源后显式准入并交付候选。</summary>
    public const string Admit = "ai.knowledge_answer_feedback.evaluation_admit";
}
