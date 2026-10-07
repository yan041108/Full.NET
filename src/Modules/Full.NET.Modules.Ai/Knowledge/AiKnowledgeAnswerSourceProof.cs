using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>历史消费者需要真实完成证明；测试可模拟不可用，不能把指针摘要当成派生载荷完整性。</summary>
internal interface IAiKnowledgeAnswerSourceProof
{
    /// <summary>在事务外验证实际清单、回执及载荷；损坏或缺失返回 false。</summary>
    Task<bool> VerifyAsync(AiKnowledgeIndexStatusRecord source, Dictionary<string, object?> parameters, CancellationToken token);
}

/// <summary>复用检索已有的 TXT/PDF 完成证明，不依赖原索引写入人的在线会话。</summary>
internal sealed class AiKnowledgeAnswerSourceProof(AiKnowledgeIndexActivationStore text, AiKnowledgeIndexPdfActivationStore pdf) : IAiKnowledgeAnswerSourceProof
{
    /// <inheritdoc/>
    public Task<bool> VerifyAsync(AiKnowledgeIndexStatusRecord source, Dictionary<string, object?> parameters, CancellationToken token)
        => source.ContentType == "application/pdf" ? pdf.IsReadyForReadAsync(source, parameters, token)
            : source.ContentType is "text/plain" or "text/markdown" ? text.IsReadyForReadAsync(source, parameters, token) : Task.FromResult(false);
}
