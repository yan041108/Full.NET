namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>内部文本批次消费者：完整持久批次恢复不再收费，未持久操作也不能免费重发。</summary>
internal sealed class AiKnowledgeIndexPersistenceService(AiKnowledgeIndexEmbeddingService execution, AiKnowledgeIndexVectorStore vectors,
    AiKnowledgeIndexTextManifestStore manifests)
{
    /// <summary>只读取完整代次当前状态，不派发模型、不重建缺失数据，也不写入 ready。</summary>
    /// <remarks>结果不能缓存为激活授权；未来切换仍须在本地事务内原子复核来源与全部派生数据。</remarks>
    internal async Task<bool> IsTextGenerationStoredAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token); bounded.CancelAfter(TimeSpan.FromSeconds(25));
        var manifest = await manifests.ReadAsync(binding, taskId, leaseToken, bounded.Token).ConfigureAwait(false);
        if (manifest is null || !await vectors.MatchesWholePlanAsync(binding, taskId, leaseToken, manifest, bounded.Token).ConfigureAwait(false)) return false;
        var current = await manifests.ReadAsync(binding, taskId, leaseToken, bounded.Token).ConfigureAwait(false);
        bounded.Token.ThrowIfCancellationRequested();
        return current is not null && current.Identity == manifest.Identity && current.Summary == manifest.Summary
            && current.Batches.SequenceEqual(manifest.Batches);
    }

    /// <summary>成功只代表一个当前批次已持久化，不能据此宣告整个代次或文档 ready。</summary>
    internal async Task<bool> ExecuteTextBatchAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, int ordinal, CancellationToken token)
    {
        var current = await manifests.ReadAsync(binding, taskId, leaseToken, token).ConfigureAwait(false);
        if (current is not null && await vectors.MatchesPlanAsync(binding, taskId, leaseToken, current, ordinal, token).ConfigureAwait(false)) return true;
        AiKnowledgeIndexTextManifest? recovered = null;
        var batch = await execution.ExecuteTextBatchAsync(binding, taskId, leaseToken, ordinal, token, async (plan, cancellation) =>
        {
            // 在预留预算前完整冻结；已有清单不能换成新分块或另一来源。
            if (!await manifests.PersistAsync(binding, taskId, leaseToken, plan, cancellation).ConfigureAwait(false)) return false;
            var manifest = AiKnowledgeIndexTextManifestCodec.Decode(AiKnowledgeIndexTextManifestCodec.Encode(plan, cancellation), cancellation);
            if (!await vectors.MatchesPlanAsync(binding, taskId, leaseToken, manifest, ordinal, cancellation).ConfigureAwait(false)) return true;
            // 旧已存批次补齐清单必须重读权威文件并精确匹配，不能借缺失清单再派发模型。
            recovered = manifest; return false;
        }).ConfigureAwait(false);
        if (recovered is null && (batch is null || !await vectors.PersistAsync(binding, taskId, leaseToken, batch, token).ConfigureAwait(false))) return false;
        current = await manifests.ReadAsync(binding, taskId, leaseToken, token).ConfigureAwait(false);
        return current is not null && current.Summary == (recovered?.Summary ?? batch!.Plan)
            && await vectors.MatchesPlanAsync(binding, taskId, leaseToken, current, ordinal, token).ConfigureAwait(false);
    }
}
