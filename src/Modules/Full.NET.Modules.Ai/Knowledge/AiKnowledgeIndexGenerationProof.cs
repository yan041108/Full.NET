using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>内部已验证回执；仅供随后带数据库复核的激活消费者使用，不是可缓存授权。</summary>
internal sealed record AiKnowledgeIndexGenerationProof(AiKnowledgeIndexDispatchRecord Source, SessionBindingSnapshot Session,
    IReadOnlyList<AiKnowledgeIndexVectorReceiptRecord> Receipts);
