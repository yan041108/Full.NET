namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>人工输入的付费批次、完整候选与持久恢复；发布和普通引用仍须独立完成。</summary>
internal sealed class AiKnowledgeCorrectionPersistenceService(AiKnowledgeCorrectionEmbeddingService execution, AiKnowledgeCorrectionVectorStore vectors,
    AiKnowledgeCorrectionIndexService planning, AiKnowledgeIndexSessionService sessions, AiKnowledgeCorrectionActivationStore activation)
{
    /// <summary>完整持久清单决定所有批次；每步按原会话续租，恢复不重派已付费载荷，全部齐全才原子完成。</summary>
    internal async Task<bool> ExecuteGenerationAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); using var total = CancellationTokenSource.CreateLinkedTokenSource(token); total.CancelAfter(TimeSpan.FromMinutes(15));
        if (await activation.IsActiveAsync(binding, taskId, leaseToken, total.Token).ConfigureAwait(false)) return true;
        var original = await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(ct => sessions.ReadHeldAsync(binding, taskId, leaseToken, ct), total.Token).ConfigureAwait(false);
        if (original is null) return false;
        var frozen = await planning.ReadAsync(binding, taskId, leaseToken, total.Token).ConfigureAwait(false)
            ?? await planning.PrepareAsync(binding, taskId, leaseToken, total.Token).ConfigureAwait(false);
        if (frozen is null || frozen.Session != original) return false;
        for (var ordinal = 0; ordinal < frozen.Plan.Batches.Count; ordinal++)
        {
            using var phase = CancellationTokenSource.CreateLinkedTokenSource(total.Token); phase.CancelAfter(TimeSpan.FromSeconds(25));
            if (!await sessions.RenewHeldWithBoundedReadsAsync(binding, taskId, leaseToken, original, phase.Token).ConfigureAwait(false)) return false;
            var current = await planning.ReadAsync(binding, taskId, leaseToken, phase.Token).ConfigureAwait(false);
            if (current is null || current.ManifestHash != frozen.ManifestHash || current.Plan.PlanHash != frozen.Plan.PlanHash || current.Session != original
                || !await ExecuteBatchAsync(binding, taskId, leaseToken, ordinal, phase.Token).ConfigureAwait(false)) return false;
        }
        using var final = CancellationTokenSource.CreateLinkedTokenSource(total.Token); final.CancelAfter(TimeSpan.FromSeconds(25));
        if (!await sessions.RenewHeldWithBoundedReadsAsync(binding, taskId, leaseToken, original, final.Token).ConfigureAwait(false)) return false;
        return await activation.ActivateAsync(binding, taskId, leaseToken, planning, vectors, final.Token).ConfigureAwait(false);
    }

    /// <summary>优先恢复成功结算的原字节；缺失向量且已有操作时不再次收费，也不把批次成功当成代次完成。</summary>
    internal Task<bool> ExecuteBatchAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, int ordinal, CancellationToken token) =>
        AiKnowledgeTelemetry.RunAsync("knowledge_correction_batch", () => ExecuteBatchCoreAsync(binding, taskId, leaseToken, ordinal, token), token);

    private async Task<bool> ExecuteBatchCoreAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, int ordinal, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(25));
        if (await vectors.ReadAsync(binding, taskId, leaseToken, ordinal, deadline.Token).ConfigureAwait(false) is not null) return true;
        // 固定阶段只观测原期限内的累计成本，不生成身份、不重置预算，也不吞掉取消。
        var result = await AiKnowledgeTelemetry.RunAsync("knowledge_correction_embedding",
            () => execution.ExecuteAsync(binding, taskId, leaseToken, ordinal, deadline.Token), deadline.Token).ConfigureAwait(false);
        if (result is null || !await AiKnowledgeTelemetry.RunAsync("knowledge_correction_persist",
            () => vectors.PersistAsync(binding, taskId, leaseToken, result, deadline.Token), deadline.Token).ConfigureAwait(false)) return false;
        var stored = await vectors.ReadAsync(binding, taskId, leaseToken, ordinal, deadline.Token).ConfigureAwait(false);
        return stored?.OperationId == result.OperationId && stored.Snapshot.PlanHash == result.Preparation.Plan.PlanHash;
    }
}
