namespace Full.NET.Modules.Ai.Contracts;

/// <summary>本人运行的对账快照；未知用量保留原预留，不提供模型正文、凭据或外部响应。</summary>
/// <param name="RunId">原运行标识。</param><param name="Version">提交时必须匹配的版本。</param>
/// <param name="StatusKey">当前运行状态。</param><param name="UsageStatus">原账本计量状态。</param>
/// <param name="ReservedTokens">仍占用的预留量。</param><param name="QuotaMonthKey">原预留月份。</param>
/// <param name="InputTokens">已记录输入量；未知为空。</param><param name="OutputTokens">已记录输出量；未知为空。</param>
/// <param name="CachedInputTokens">已记录缓存输入量。</param><param name="CanReconcile">当前状态是否可人工关闭；不替代授权。</param>
public sealed record AiAgentReconciliationResponse(Guid RunId, long Version, string StatusKey, string UsageStatus,
    long ReservedTokens, string QuotaMonthKey, long? InputTokens, long? OutputTokens, long? CachedInputTokens, bool CanReconcile);

/// <summary>显式授权人员依据外部证据关闭未知执行；永不重新派发，不伪造成功正文。</summary>
/// <param name="ClientRequestId">本次人工处理的幂等标识。</param><param name="ExpectedVersion">读取到的运行版本。</param>
/// <param name="EvidenceDigest">人工核证材料的 SHA-256；只保存摘要，不接收正文或文件地址。</param>
/// <param name="InputTokens">核证的整次运行输入量；输入输出同时为空时继续保留未知预留。</param>
/// <param name="OutputTokens">核证的整次运行输出量。</param><param name="CachedInputTokens">核证缓存输入量；不得超过输入量。</param>
public sealed record ReconcileAiAgentRunRequest(Guid ClientRequestId, long ExpectedVersion, string EvidenceDigest,
    long? InputTokens = null, long? OutputTokens = null, long? CachedInputTokens = null);
