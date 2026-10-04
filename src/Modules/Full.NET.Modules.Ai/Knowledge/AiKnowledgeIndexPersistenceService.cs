namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>内部文本批次消费者：完整持久批次恢复不再收费，未持久操作也不能免费重发。</summary>
internal sealed class AiKnowledgeIndexPersistenceService(AiKnowledgeIndexEmbeddingService execution, AiKnowledgeIndexVectorStore vectors)
{
    /// <summary>成功只代表一个当前批次已持久化，不能据此宣告整个代次或文档 ready。</summary>
    internal async Task<bool> ExecuteTextBatchAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, int ordinal, CancellationToken token)
    {
        if (await vectors.IsStoredAsync(binding, taskId, leaseToken, ordinal, token).ConfigureAwait(false)) return true;
        var batch = await execution.ExecuteTextBatchAsync(binding, taskId, leaseToken, ordinal, token).ConfigureAwait(false);
        return batch is not null && await vectors.PersistAsync(binding, taskId, leaseToken, batch, token).ConfigureAwait(false);
    }
}
