namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>内部文本批次消费者：完整持久批次恢复不再收费，未持久操作也不能免费重发。</summary>
internal sealed class AiKnowledgeIndexPersistenceService(AiKnowledgeIndexEmbeddingService execution, AiKnowledgeIndexVectorStore vectors,
    AiKnowledgeIndexTextManifestStore manifests, AiKnowledgeIndexActivationStore? activations = null, AiKnowledgeIndexSessionService? sessions = null)
{
    /// <summary>按完整清单执行全部文本批次；续租不替代原会话和来源授权。</summary>
    internal async Task<bool> ExecuteTextGenerationAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
    {
        if (sessions is null || activations is null) throw new InvalidOperationException("Generation session and activation stores are required.");
        token.ThrowIfCancellationRequested();
        using var dispatch = CancellationTokenSource.CreateLinkedTokenSource(token);
        // 总时限阻止新派发；已计费操作仍使用原执行器的独立结算边界。
        dispatch.CancelAfter(TimeSpan.FromMinutes(15));
        Full.NET.Modules.Identity.Contracts.SessionBindingSnapshot? original;
        using (var phase = Phase(dispatch.Token))
        {
            // 已完成任务没有运行租约，只能通过原完成令牌和最新授权恢复结果。
            if (await activations.IsActiveAsync(binding, taskId, leaseToken, phase.Token).ConfigureAwait(false)) return true;
            original = await sessions.ReadHeldAsync(binding, taskId, leaseToken, phase.Token).ConfigureAwait(false);
            if (original is null) return false;
        }
        AiKnowledgeIndexTextManifestSnapshot? frozen;
        var next = 0;
        using (var phase = Phase(dispatch.Token))
        {
            if (!await sessions.RenewHeldAsync(binding, taskId, leaseToken, original, phase.Token).ConfigureAwait(false)) return false;
            frozen = await manifests.ReadSnapshotAsync(binding, taskId, leaseToken, phase.Token).ConfigureAwait(false);
            if (frozen is null)
            {
                // 首批先完整冻结清单再预留预算；缺失或损坏清单不能越过原消费者的校验。
                if (!await ExecuteTextBatchAsync(binding, taskId, leaseToken, 0, phase.Token).ConfigureAwait(false)) return false;
                frozen = await manifests.ReadSnapshotAsync(binding, taskId, leaseToken, phase.Token).ConfigureAwait(false);
                if (frozen is null) return false;
                next = 1;
            }
            phase.Token.ThrowIfCancellationRequested();
        }
        for (; next < frozen.Manifest.Batches.Count; next++)
        {
            using var phase = Phase(dispatch.Token);
            if (!await sessions.RenewHeldAsync(binding, taskId, leaseToken, original, phase.Token).ConfigureAwait(false)) return false;
            var current = await manifests.ReadSnapshotAsync(binding, taskId, leaseToken, phase.Token).ConfigureAwait(false);
            if (!SameManifest(frozen, current)
                || !await ExecuteTextBatchAsync(binding, taskId, leaseToken, next, phase.Token).ConfigureAwait(false)) return false;
            phase.Token.ThrowIfCancellationRequested();
        }
        using (var phase = Phase(dispatch.Token))
        {
            if (!await sessions.RenewHeldAsync(binding, taskId, leaseToken, original, phase.Token).ConfigureAwait(false)) return false;
            var current = await manifests.ReadSnapshotAsync(binding, taskId, leaseToken, phase.Token).ConfigureAwait(false);
            return SameManifest(frozen, current)
                && await ActivateTextGenerationAsync(binding, taskId, leaseToken, phase.Token).ConfigureAwait(false);
        }
    }

    private static CancellationTokenSource Phase(CancellationToken token)
    {
        var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
        bounded.CancelAfter(TimeSpan.FromSeconds(25)); return bounded;
    }

    private static bool SameManifest(AiKnowledgeIndexTextManifestSnapshot frozen, AiKnowledgeIndexTextManifestSnapshot? current)
        => current is not null && current.PayloadHash == frozen.PayloadHash && current.Manifest.Identity == frozen.Manifest.Identity
            && current.Manifest.Summary == frozen.Manifest.Summary && current.Manifest.Batches.SequenceEqual(frozen.Manifest.Batches);

    /// <summary>内部完成态和活动指针一起提交；不派发模型，不能据此升级公开 ready 能力。</summary>
    internal async Task<bool> ActivateTextGenerationAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
    {
        if (activations is null) throw new InvalidOperationException("Generation activation store is required.");
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token); bounded.CancelAfter(TimeSpan.FromSeconds(25));
        if (await activations.IsActiveAsync(binding, taskId, leaseToken, bounded.Token).ConfigureAwait(false)) return true;
        var snapshot = await manifests.ReadSnapshotAsync(binding, taskId, leaseToken, bounded.Token).ConfigureAwait(false);
        if (snapshot is null) return false;
        var proof = await vectors.ReadWholePlanProofAsync(binding, taskId, leaseToken, snapshot.Manifest, bounded.Token).ConfigureAwait(false);
        if (proof is null) return false;
        var current = await manifests.ReadSnapshotAsync(binding, taskId, leaseToken, bounded.Token).ConfigureAwait(false);
        bounded.Token.ThrowIfCancellationRequested();
        if (current is null || current.PayloadHash != snapshot.PayloadHash || current.Manifest.Identity != snapshot.Manifest.Identity
            || current.Manifest.Summary != snapshot.Manifest.Summary || !current.Manifest.Batches.SequenceEqual(snapshot.Manifest.Batches)) return false;
        return await activations.ActivateAsync(binding, taskId, leaseToken, proof, snapshot, bounded.Token).ConfigureAwait(false);
    }

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
