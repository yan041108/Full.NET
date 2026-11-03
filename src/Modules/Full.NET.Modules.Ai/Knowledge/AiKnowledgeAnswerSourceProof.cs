using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>历史消费者需要真实完成证明；测试可模拟不可用，不能把指针摘要当成派生载荷完整性。</summary>
internal interface IAiKnowledgeAnswerSourceProof
{
    /// <summary>在事务外验证实际清单、回执及载荷；损坏或缺失返回 false。</summary>
    Task<bool> VerifyAsync(AiKnowledgeIndexStatusRecord source, Dictionary<string, object?> parameters, CancellationToken token);
    /// <summary>只返回已经匹配来源证明的原引用对象；默认实现不能接受尚未支持的人工引用。</summary>
    Task<IReadOnlyList<AiKnowledgeAnswerReference>> MatchReferencesAsync(AiKnowledgeIndexStatusRecord source, Dictionary<string, object?> parameters,
        IReadOnlyList<AiKnowledgeAnswerReference> references, CancellationToken token) => Task.FromResult<IReadOnlyList<AiKnowledgeAnswerReference>>(
            source.CorrectionRevisionId.HasValue ? [] : references.Where(r => r.Correction is null).ToArray());
}

/// <summary>按冻结任务身份选择自动或人工完成证明，不依赖原索引写入人的在线会话。</summary>
internal sealed class AiKnowledgeAnswerSourceProof(AiKnowledgeIndexActivationStore text, AiKnowledgeIndexPdfActivationStore pdf,
    AiKnowledgeIndexDocumentActivationStore? docx = null, AiKnowledgeCorrectionActivationStore? correction = null) : IAiKnowledgeAnswerSourceProof
{
    /// <inheritdoc/>
    public Task<bool> VerifyAsync(AiKnowledgeIndexStatusRecord source, Dictionary<string, object?> parameters, CancellationToken token)
        => ProveAsync(source, parameters, token);

    /// <inheritdoc/>
    public Task<IReadOnlyList<AiKnowledgeAnswerReference>> MatchReferencesAsync(AiKnowledgeIndexStatusRecord source, Dictionary<string, object?> parameters,
        IReadOnlyList<AiKnowledgeAnswerReference> references, CancellationToken token) => source.CorrectionRevisionId.HasValue
            ? correction is not null ? correction.MatchReferencesAsync(source, parameters, references, token) : Task.FromResult<IReadOnlyList<AiKnowledgeAnswerReference>>([])
            : Task.FromResult<IReadOnlyList<AiKnowledgeAnswerReference>>(references.Where(r => r.Correction is null).ToArray());

    private async Task<bool> ProveAsync(AiKnowledgeIndexStatusRecord source, Dictionary<string, object?> parameters, CancellationToken token)
    {
        if (source.CorrectionRevisionId.HasValue)
            return correction is not null && await correction.IsReadyForReadAsync(source, parameters, token).ConfigureAwait(false);
        if (AiKnowledgeDocumentFormats.IsDocument(source.ContentType) && docx is not null && await docx.IsReadyForReadAsync(source, parameters, token).ConfigureAwait(false)) return true;
        return await (source.ContentType == "application/pdf" ? pdf.IsReadyForReadAsync(source, parameters, token)
            : source.ContentType == Domain.AiKnowledgeFilePolicy.DocxContentType && docx is not null ? docx.IsReadyForReadAsync(source, parameters, token)
            : source.ContentType is "text/plain" or "text/markdown" ? text.IsReadyForReadAsync(source, parameters, token) : Task.FromResult(false)).ConfigureAwait(false);
    }
}
