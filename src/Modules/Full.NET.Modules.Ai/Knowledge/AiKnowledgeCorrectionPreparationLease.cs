namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>原解析落库必须仍持有此准备租约；来源与任务完成在同一文档根锁事务提交。</summary>
internal sealed record AiKnowledgeCorrectionPreparationLease(Guid Id, Guid Token, string RequestHash, string ParserRuntimeId);
