namespace Full.NET.Modules.Ai.Contracts;

/// <summary>当前授权读取点的索引状态；ready 不构成后续检索的缓存授权。</summary>
/// <param name="VersionId">请求的不可变来源版本。</param>
/// <param name="TaskId">该版本的最新任务，未入队时为空。</param>
/// <param name="State">稳定状态码，不由翻译文本推导。</param>
/// <param name="FailureCode">受控失败码，不包含内部异常或模型输出。</param>
public sealed record AiKnowledgeIndexStatusResponse(Guid VersionId, Guid? TaskId, string State, string? FailureCode);
