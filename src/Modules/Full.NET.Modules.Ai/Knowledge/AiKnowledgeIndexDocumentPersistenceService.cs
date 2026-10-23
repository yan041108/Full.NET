namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>消费已结算多格式文档单批；完整原载荷恢复不能重新计费。</summary>
internal sealed class AiKnowledgeIndexDocumentPersistenceService(AiKnowledgeIndexDocumentEmbeddingService execution,
    AiKnowledgeIndexDocumentVectorStore vectors, AiKnowledgeIndexDocumentManifestStore? manifests = null,
    AiKnowledgeIndexSessionService? sessions = null, AiKnowledgeIndexDocumentActivationStore? activations = null,
    Microsoft.Extensions.Options.IOptions<AiKnowledgeDocumentRuntimeOptions>? documentOptions = null)
{
    // 恢复完整持久化代次只需要权威存储；阶段期限来自部署资源档，不为读回执创建解析器或模型客户端。
    private int PhaseSeconds => documentOptions?.Value.Enabled == true ? 150 : 25;
    private int ExecutionBudgetMilliseconds => documentOptions?.Value.Enabled == true ? documentOptions.Value.TimeoutMs + 25000 : 25000;
    /// <summary>按完整清单执行全部多格式文档批次；续租不替代原会话和来源授权。</summary>
    internal async Task<bool> ExecuteGenerationAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
    {
        if (sessions is null || activations is null || manifests is null) throw new InvalidOperationException("Generation session and activation stores are required.");
        token.ThrowIfCancellationRequested();
        using var dispatch = CancellationTokenSource.CreateLinkedTokenSource(token);
        // 总时限阻止新派发；已计费操作仍使用原执行器的独立结算边界。
        dispatch.CancelAfter(TimeSpan.FromMinutes(15));
        Full.NET.Modules.Identity.Contracts.SessionBindingSnapshot? original;
        using (var phase = Phase(dispatch.Token))
        {
            // 已完成任务没有运行租约，只能通过原完成令牌和最新授权恢复结果。
            if (await activations.IsActiveAsync(binding, taskId, leaseToken, phase.Token).ConfigureAwait(false)) return true;
            original = await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(ct => sessions.ReadHeldAsync(binding, taskId, leaseToken, ct), phase.Token).ConfigureAwait(false);
            if (original is null) return false;
        }
        AiKnowledgeIndexDocumentManifestSnapshot? frozen;
        var next = 0;
        using (var phase = Phase(dispatch.Token))
        {
            if (!await sessions.RenewHeldWithBoundedReadsAsync(binding, taskId, leaseToken, original, phase.Token, PhaseSeconds).ConfigureAwait(false)) return false;
            frozen = await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(ct => manifests.ReadSnapshotAsync(binding, taskId, leaseToken, ct), phase.Token).ConfigureAwait(false);
            if (frozen is null)
            {
                // 首批先完整冻结清单再预留预算；缺失或损坏清单不能越过原消费者的校验。
                if (!await ExecuteBatchAsync(binding, taskId, leaseToken, 0, phase.Token).ConfigureAwait(false)) return false;
                frozen = await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(ct => manifests.ReadSnapshotAsync(binding, taskId, leaseToken, ct), phase.Token).ConfigureAwait(false);
                if (frozen is null) return false;
                next = 1;
            }
            phase.Token.ThrowIfCancellationRequested();
        }
        for (; next < frozen.Manifest.Batches.Count; next++)
        {
            using var phase = Phase(dispatch.Token);
            if (!await sessions.RenewHeldWithBoundedReadsAsync(binding, taskId, leaseToken, original, phase.Token, PhaseSeconds).ConfigureAwait(false)) return false;
            var current = await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(ct => manifests.ReadSnapshotAsync(binding, taskId, leaseToken, ct), phase.Token).ConfigureAwait(false);
            if (!SameManifest(frozen, current)
                || !await ExecuteBatchAsync(binding, taskId, leaseToken, next, phase.Token).ConfigureAwait(false)) return false;
            phase.Token.ThrowIfCancellationRequested();
        }
        using (var phase = Phase(dispatch.Token))
        {
            if (!await sessions.RenewHeldWithBoundedReadsAsync(binding, taskId, leaseToken, original, phase.Token, PhaseSeconds).ConfigureAwait(false)) return false;
            var current = await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(ct => manifests.ReadSnapshotAsync(binding, taskId, leaseToken, ct), phase.Token).ConfigureAwait(false);
            return SameManifest(frozen, current)
                && await ActivateGenerationAsync(binding, taskId, leaseToken, phase.Token).ConfigureAwait(false);
        }
    }

    private CancellationTokenSource Phase(CancellationToken token)
    {
        var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
        bounded.CancelAfter(ExecutionBudgetMilliseconds); return bounded;
    }

    private static bool SameManifest(AiKnowledgeIndexDocumentManifestSnapshot frozen, AiKnowledgeIndexDocumentManifestSnapshot? current)
        => current is not null && current.PayloadHash == frozen.PayloadHash
            && AiKnowledgeIndexDocumentVerifiedManifest.SamePlan(current.Manifest, frozen.Manifest);

    /// <summary>内部完成态和活动指针一起提交；不派发模型，不能据此升级公开 ready 能力。</summary>
    internal async Task<bool> ActivateGenerationAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
    {
        if (activations is null || manifests is null) throw new InvalidOperationException("Generation activation store is required.");
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token); bounded.CancelAfter(ExecutionBudgetMilliseconds);
        if (await activations.IsActiveAsync(binding, taskId, leaseToken, bounded.Token).ConfigureAwait(false)) return true;
        var snapshot = await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(ct => manifests.ReadSnapshotAsync(binding, taskId, leaseToken, ct), bounded.Token).ConfigureAwait(false);
        if (snapshot is null) return false;
        var proof = await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(ct => vectors.ReadWholePlanProofAsync(binding, taskId, leaseToken, ct), bounded.Token).ConfigureAwait(false);
        if (proof is null || !SameManifest(snapshot, proof.Manifest)) return false;
        var current = await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(ct => manifests.ReadSnapshotAsync(binding, taskId, leaseToken, ct), bounded.Token).ConfigureAwait(false);
        bounded.Token.ThrowIfCancellationRequested();
        if (!SameManifest(snapshot, current)) return false;
        return await activations.ActivateAsync(binding, taskId, leaseToken, proof, bounded.Token).ConfigureAwait(false);
    }

    /// <summary>成功仅证明当前单批落库，不升级整个文档或代次状态。</summary>
    internal async Task<bool> ExecuteBatchAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken,
        int ordinal, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
        bounded.CancelAfter(ExecutionBudgetMilliseconds);
        if (await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(ct => vectors.ReadSnapshotAsync(binding, taskId, leaseToken, ordinal, ct), bounded.Token).ConfigureAwait(false) is not null) return true;
        var batch = await execution.ExecuteAsync(binding, taskId, leaseToken, ordinal, bounded.Token).ConfigureAwait(false);
        if (batch is null || !await vectors.PersistAsync(binding, taskId, leaseToken, batch.OperationId,
                batch.Preparation.Plan, ordinal, batch.Vectors, bounded.Token).ConfigureAwait(false)) return false;
        var current = await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(ct => vectors.ReadSnapshotAsync(binding, taskId, leaseToken, ordinal, ct), bounded.Token).ConfigureAwait(false);
        bounded.Token.ThrowIfCancellationRequested();
        return current is not null && current.OperationId == batch.OperationId
            && current.Snapshot.Payload.PlanHash == batch.Preparation.Plan.Summary.PlanHash;
    }
}
