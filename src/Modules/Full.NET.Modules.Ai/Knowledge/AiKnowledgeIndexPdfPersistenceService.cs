namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>消费已结算 PDF 单批；完整原载荷恢复不能重新计费。</summary>
internal sealed class AiKnowledgeIndexPdfPersistenceService(AiKnowledgeIndexPdfEmbeddingService execution,
    AiKnowledgeIndexPdfVectorStore vectors)
{
    /// <summary>成功仅证明当前单批落库，不升级整个文档或代次状态。</summary>
    internal async Task<bool> ExecuteBatchAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken,
        int ordinal, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
        bounded.CancelAfter(TimeSpan.FromSeconds(25));
        if (await vectors.ReadSnapshotAsync(binding, taskId, leaseToken, ordinal, bounded.Token).ConfigureAwait(false) is not null) return true;
        var batch = await execution.ExecuteAsync(binding, taskId, leaseToken, ordinal, bounded.Token).ConfigureAwait(false);
        if (batch is null || !await vectors.PersistAsync(binding, taskId, leaseToken, batch.OperationId,
                batch.Preparation.Plan, ordinal, batch.Vectors, bounded.Token).ConfigureAwait(false)) return false;
        var current = await vectors.ReadSnapshotAsync(binding, taskId, leaseToken, ordinal, bounded.Token).ConfigureAwait(false);
        bounded.Token.ThrowIfCancellationRequested();
        return current is not null && current.OperationId == batch.OperationId
            && current.Snapshot.Payload.PlanHash == batch.Preparation.Plan.Summary.PlanHash;
    }
}
