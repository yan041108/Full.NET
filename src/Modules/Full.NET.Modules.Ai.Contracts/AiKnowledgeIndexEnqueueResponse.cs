namespace Full.NET.Modules.Ai.Contracts;

/// <summary>已绑定原会话的索引任务标识；接受请求不证明任务状态或索引可用。</summary>
/// <param name="TaskId">本次来源及批准快照的稳定任务标识；同一原会话重放复用。</param>
/// <param name="VersionId">请求的不可变文档版本。</param>
public sealed record AiKnowledgeIndexEnqueueResponse(Guid TaskId, Guid VersionId);
