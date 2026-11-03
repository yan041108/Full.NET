using System.Security.Cryptography;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Budgets;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>当前原会话下的成功结算单批；不表示完整代次或发布完成。</summary>
internal sealed record AiKnowledgeCorrectionStoredBatch(Guid OperationId, AiKnowledgeCorrectionVectorBatch Snapshot);
/// <summary>事务前逐批验收的瞬时事实；激活必须重新比较实际字节摘要及完整账本集合。</summary>
internal sealed record AiKnowledgeCorrectionGenerationProof(AiKnowledgeCorrectionIndexPreparation Preparation,
    IReadOnlyList<AiKnowledgeIndexVectorReceiptRecord> Receipts);

/// <summary>人工向量只消费原完整清单和成功结算凭据，恢复原字节不再次调用模型。</summary>
internal sealed class AiKnowledgeCorrectionVectorStore(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    IDataTransactionState transactions, ICurrentTenant tenant, IClock clock, IOptions<DatabaseOptions> database,
    AiKnowledgeIndexTaskStore tasks, AiKnowledgeCorrectionIndexService planning)
{
    /// <summary>从持久完整清单决定期望集合，逐批验证并在末次读取重新核对全部原字节及成功账本。</summary>
    internal async Task<AiKnowledgeCorrectionGenerationProof?> ReadWholePlanProofAsync(AiKnowledgeIndexTaskBinding binding,
        Guid taskId, Guid leaseToken, CancellationToken token)
    {
        Check(token); var prepared = await planning.ReadAsync(binding, taskId, leaseToken, token).ConfigureAwait(false);
        if (prepared is null) return null;
        var before = await Receipts(prepared, token).ConfigureAwait(false); if (before is null) return null;
        var ids = new HashSet<Guid>();
        for (var ordinal = 0; ordinal < before.Count; ordinal++)
        {
            token.ThrowIfCancellationRequested(); var expected = before[ordinal];
            if (expected.BatchOrdinal != ordinal || expected.Id == Guid.Empty || !ids.Add(expected.Id)) return null;
            var batch = await ReadAsync(binding, taskId, leaseToken, ordinal, token).ConfigureAwait(false);
            if (batch is null || batch.OperationId != expected.Id || batch.Snapshot.PayloadHash != expected.PayloadHash
                || expected.InputCount != batch.Snapshot.Chunks.Count || expected.FormatKey != AiKnowledgeCorrectionVectorCodec.FormatKey
                || expected.ProviderKey != prepared.Plan.Model.ProviderKey || expected.ModelId != prepared.Plan.Model.ModelId
                || expected.Dimensions != batch.Snapshot.Dimensions || expected.DistanceKey != "cosine") return null;
        }
        var current = await planning.ReadAsync(binding, taskId, leaseToken, token).ConfigureAwait(false);
        if (!Same(prepared, current)) return null;
        var after = await Receipts(current!, token).ConfigureAwait(false);
        return after is not null && before.SequenceEqual(after) ? new(current!, after) : null;

        async Task<IReadOnlyList<AiKnowledgeIndexVectorReceiptRecord>?> Receipts(AiKnowledgeCorrectionIndexPreparation expected, CancellationToken ct)
        {
            var p = Parameters(binding, expected, 0, ct); p["ExpectedBatchCount"] = expected.Plan.Batches.Count;
            p["ModelConfigId"] = expected.Plan.Model.ModelConfigId;
            var rows = await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(inner => query.QueryAsync<AiKnowledgeIndexVectorReceiptRecord>(
                AiKnowledgeCorrectionVectorSql.Receipts(database.Value.Provider, tasks.IsTenant), p, inner), ct).ConfigureAwait(false);
            Check(ct);
            return rows.Count == expected.Plan.Batches.Count && rows.Count is >= 1 and <= 2048 ? Array.AsReadOnly(rows.ToArray()) : null;
        }
    }

    internal async Task<bool> PersistAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken,
        AiKnowledgeCorrectionEmbeddingBatch result, CancellationToken token)
    {
        Check(token); var ordinal = result.Ordinal; var expected = result.Preparation;
        if (result.OperationId == Guid.Empty || ordinal < 0 || ordinal >= expected.Plan.Batches.Count) return false;
        var bytes = AiKnowledgeCorrectionVectorCodec.Encode(expected.Plan, ordinal, result.Vectors, token);
        try
        {
            var current = await planning.ReadAsync(binding, taskId, leaseToken, token).ConfigureAwait(false);
            if (!Same(expected, current)) return false;
            var model = await planning.ReadCurrentModelAsync(binding, taskId, leaseToken, current!, token).ConfigureAwait(false); if (model is null) return false;
            var p = Parameters(binding, current!, ordinal, token); p["OperationId"] = result.OperationId;
            p["RequestHash"] = AiOperationBudgetStore.Fingerprint(AiKnowledgeCorrectionBudgetRequest.Create(result.OperationId, model, current!.Plan, ordinal));
            p["InputCount"] = current.Plan.Batches[ordinal].Chunks.Count; p["ProviderKey"] = model.ProviderKey; p["ModelId"] = model.ModelId;
            p["Dimensions"] = AiKnowledgeCorrectionVectorCodec.Dimensions(current.Plan); p["FormatKey"] = AiKnowledgeCorrectionVectorCodec.FormatKey;
            p["DistanceKey"] = "cosine"; p["Payload"] = bytes; var hash = Convert.ToHexString(SHA256.HashData(bytes)); p["PayloadHash"] = hash;
            try
            {
                var inserted = await transaction.ExecuteAsync(async ct =>
                {
                    // 保存修订和向量落库沿同一根锁；锁内只复核 AI 自有来源、清单和账本。
                    p["Now"] = clock.UtcNow;
                    if (current.Source.LeaseExpiresAtUtc <= clock.UtcNow
                        || await command.ExecuteAsync(tasks.IsTenant ? AiKnowledgeIndexCorrectionSql.LockTenant : AiKnowledgeIndexCorrectionSql.LockHost, p, ct).ConfigureAwait(false) != 1) return false;
                    p["Now"] = clock.UtcNow;
                    var count = await command.ExecuteAsync(AiKnowledgeCorrectionVectorSql.Write(database.Value.Provider, tasks.IsTenant), p, ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    if (count == 1 && current.Source.LeaseExpiresAtUtc <= clock.UtcNow) throw new ExpiredDuringWriteException();
                    return count is 0 or 1 ? count == 1 : throw new InvalidOperationException("Unexpected correction vector write count.");
                }, token).ConfigureAwait(false);
                if (!inserted) return false;
            }
            catch (ExpiredDuringWriteException) { return false; }
            catch (DataCommandException error) when (error.Kind == DataCommandFailureKind.UniqueConstraint)
            { /* 竞争事务回滚后只读取原载荷，不覆盖已经保存的向量。 */ }
            var stored = await ReadAsync(binding, taskId, leaseToken, ordinal, token).ConfigureAwait(false);
            return stored is not null && stored.OperationId == result.OperationId && stored.Snapshot.PayloadHash == hash;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    /// <summary>读取前后分别核对清单、原会话、Files 和成功账本；未知操作可恢复已有向量，但不得免费重发。</summary>
    internal Task<AiKnowledgeCorrectionStoredBatch?> ReadAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, int ordinal, CancellationToken token) =>
        AiKnowledgeTelemetry.RunAsync("knowledge_correction_restore", () => ReadCoreAsync(binding, taskId, leaseToken, ordinal, token), token);

    private async Task<AiKnowledgeCorrectionStoredBatch?> ReadCoreAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, int ordinal, CancellationToken token)
    {
        Check(token); if (ordinal is < 0 or >= 2048) throw new ArgumentOutOfRangeException(nameof(ordinal));
        // 计划与模型之间没有外部等待，共用同次完整证明；不把内存计划当成授权缓存。
        var proven = await planning.ReadWithCurrentModelAsync(binding, taskId, leaseToken, token).ConfigureAwait(false);
        if (proven is null || ordinal >= proven.Preparation.Plan.Batches.Count) return null;
        var prepared = proven.Preparation;
        var model = proven.Model;
        var row = await ReadRow(prepared, model, token).ConfigureAwait(false); if (row is null) return null;
        AiKnowledgeCorrectionVectorBatch snapshot;
        try { snapshot = AiKnowledgeCorrectionVectorCodec.ReadBatch(prepared.Plan, ordinal, row.Payload, token); }
        catch (InvalidDataException) { return null; }
        if (snapshot.PayloadHash != row.PayloadHash) return null;
        var finalProof = await planning.ReadWithCurrentModelAsync(binding, taskId, leaseToken, token).ConfigureAwait(false);
        var final = finalProof?.Preparation;
        if (!Same(prepared, final)) return null;
        var finalModel = finalProof!.Model;
        var last = await ReadRow(final!, finalModel, token).ConfigureAwait(false);
        if (last is null || (row with { Payload = Array.Empty<byte>() }) != (last with { Payload = Array.Empty<byte>() })) return null;
        // 最后 SQL 后仍复核撤权；账本及实际字节证明仅对应该次数据库判定点。
        return await planning.ReadCurrentModelAsync(binding, taskId, leaseToken, final!, token).ConfigureAwait(false) is not null
            ? new(row.Id, snapshot) : null;

        async Task<AiKnowledgeIndexVectorRecord?> ReadRow(AiKnowledgeCorrectionIndexPreparation state, AiModelConfigRecord currentModel, CancellationToken ct)
        {
            var value = await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(inner => query.QuerySingleOrDefaultAsync<AiKnowledgeIndexVectorRecord>(
                AiKnowledgeCorrectionVectorSql.Read(database.Value.Provider, tasks.IsTenant), Parameters(binding, state, ordinal, inner), inner), ct).ConfigureAwait(false);
            Check(ct);
            if (value is null || value.Id == Guid.Empty || value.IndexTaskId != taskId || value.BatchOrdinal != ordinal
                || value.ModelConfigId != currentModel.Id || value.ModelConfigVersion != currentModel.Version || value.ProviderKey != currentModel.ProviderKey
                || value.ModelId != currentModel.ModelId || value.FormatKey != AiKnowledgeCorrectionVectorCodec.FormatKey || value.DistanceKey != "cosine"
                || value.Dimensions != AiKnowledgeCorrectionVectorCodec.Dimensions(state.Plan) || value.InputCount != state.Plan.Batches[ordinal].Chunks.Count
                || value.Payload is not { Length: >= 8 and <= AiKnowledgeCorrectionVectorCodec.MaxPayloadBytes }
                || value.RequestHash != AiOperationBudgetStore.Fingerprint(AiKnowledgeCorrectionBudgetRequest.Create(value.Id, currentModel, state.Plan, ordinal))) return null;
            var captured = value with { Payload = value.Payload.ToArray() };
            return Convert.ToHexString(SHA256.HashData(captured.Payload)) == captured.PayloadHash ? captured : null;
        }
    }

    private Dictionary<string, object?> Parameters(AiKnowledgeIndexTaskBinding binding, AiKnowledgeCorrectionIndexPreparation prepared, int ordinal, CancellationToken token)
    {
        var source = prepared.Source; var session = prepared.Session; var p = tasks.HeldParameters(binding, source.TaskId, source.LeaseToken, token);
        p["BatchOrdinal"] = ordinal; p["ScopeKey"] = tenant.Id is { } id ? id.ToString("N") : "host"; p["DocumentManifestHash"] = prepared.ManifestHash;
        p["SessionId"] = session.SessionId; p["SessionKind"] = session.SessionKind; p["SecurityStamp"] = session.SecurityStamp;
        p["ActorScope"] = session.ActorScope; p["EffectiveScope"] = session.EffectiveScope;
        p["FileId"] = source.FileId; p["ContentHash"] = source.ContentHash; p["ContentType"] = source.ContentType;
        p["SizeBytes"] = source.SizeBytes; p["ParsingPolicyKey"] = source.ParsingPolicyKey; return p;
    }
    private static bool Same(AiKnowledgeCorrectionIndexPreparation expected, AiKnowledgeCorrectionIndexPreparation? current) => current is not null
        && current.Session == expected.Session && current.ManifestHash == expected.ManifestHash && current.Plan.PlanHash == expected.Plan.PlanHash
        && AiKnowledgeIndexEmbeddingService.SameSource(expected.Source, current.Source);
    private void Check(CancellationToken token)
    { token.ThrowIfCancellationRequested(); if (transactions.HasTransaction) throw new InvalidOperationException("Correction vectors cannot join an ambient transaction."); }
    private sealed class ExpiredDuringWriteException : Exception;
}
