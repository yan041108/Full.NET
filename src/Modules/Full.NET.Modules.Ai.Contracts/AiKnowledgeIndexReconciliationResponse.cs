namespace Full.NET.Modules.Ai.Contracts;

/// <summary>所有者当前索引对账结果；恢复提示不执行操作，不替代后续授权或预算。</summary>
/// <param name="VersionId">本次精确来源版本。</param>
/// <param name="TaskId">该来源最新任务，未入队时为空。</param>
/// <param name="State">由当前完整代次证明决定的稳定读取状态。</param>
/// <param name="Reason">稳定故障或等待分类，不含内部异常与收费凭据。</param>
/// <param name="RecoveryAction">none/enqueue/retry/rebuild/wait；retry 仍绑定原会话，rebuild 需要独立的新代次流程。</param>
/// <param name="DocumentVersion">当前文档并发版本。</param>
/// <param name="KnowledgeBaseVersion">当前知识库批准版本。</param>
public sealed record AiKnowledgeIndexReconciliationResponse(Guid VersionId, Guid? TaskId, string State, string Reason,
    string RecoveryAction, int DocumentVersion, int KnowledgeBaseVersion);
