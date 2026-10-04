using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using System.Security.Cryptography;
using Full.NET.Modules.Ai.Budgets;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>一个批次一次原子写入，恢复不能覆盖原向量或再次派发模型。</summary>
internal sealed class AiKnowledgeIndexVectorStore(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    IDataTransactionState transactions, ICurrentTenant tenant, IClock clock, AiKnowledgeIndexTaskStore tasks,
    AiKnowledgeIndexSessionService sessions, IBackgroundSessionAuthorization authorization, IOptions<DatabaseOptions> database)
{
    private static readonly string[] Permissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
        AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.Upload];

    /// <summary>原子保存一批已结算向量；不同载荷的重复请求拒绝，基础设施故障传播。</summary>
    internal async Task<bool> PersistAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken,
        AiKnowledgeIndexEmbeddingBatch batch, CancellationToken token)
    {
        NoTransaction(); _ = tasks.HeldParameters(binding, taskId, leaseToken, token); ArgumentNullException.ThrowIfNull(batch);
        if (batch.OperationId == Guid.Empty || batch.BatchOrdinal is < 0 or > 4095 || batch.Source.TaskId != taskId
            || batch.Source.LeaseToken != leaseToken || batch.Source.DocumentId != binding.DocumentId) return false;
        // 在任何异步等待前冻结输入；后续只从这份完整载荷重新计算请求指纹。
        var dimensions = batch.Chunks.Count > 0 ? batch.Chunks[0].Vector.Length : 1536;
        var payload = AiKnowledgeIndexVectorCodec.Encode(batch, dimensions, token);
        var frozen = AiKnowledgeIndexVectorCodec.Decode(payload, token);
        var state = await CurrentAsync(binding, taskId, leaseToken, batch.Source, null, token).ConfigureAwait(false);
        if (state is null || Dimensions(state.Model) != dimensions) return false;
        var parameters = Parameters(binding, state, batch.BatchOrdinal, token);
        parameters["OperationId"] = batch.OperationId; parameters["InputCount"] = frozen.Chunks.Count;
        parameters["RequestHash"] = Fingerprint(batch.OperationId, state.Model, frozen);
        parameters["ProviderKey"] = state.Model.ProviderKey; parameters["ModelId"] = state.Model.ModelId;
        parameters["Dimensions"] = dimensions; parameters["FormatKey"] = AiKnowledgeIndexVectorCodec.FormatKey; parameters["DistanceKey"] = "cosine";
        parameters["Payload"] = payload; var hash = Convert.ToHexString(SHA256.HashData(payload)); parameters["PayloadHash"] = hash;
        try
        {
            if (!await transaction.ExecuteAsync(async cancellation =>
            {
                cancellation.ThrowIfCancellationRequested(); parameters["Now"] = clock.UtcNow;
                if (state.Source.LeaseExpiresAtUtc <= clock.UtcNow) return false;
                var count = await command.ExecuteAsync(WriteStatement,
                    parameters, cancellation).ConfigureAwait(false);
                // 命令等待期间也会到期或取消；必须在事务回调内抛出，返回 false 仍会提交。
                cancellation.ThrowIfCancellationRequested();
                if (count == 1 && state.Source.LeaseExpiresAtUtc <= clock.UtcNow) throw new LeaseExpiredDuringWriteException();
                return count is 0 or 1 ? count == 1 : throw new InvalidOperationException("Unexpected vector batch write count.");
            }, token).ConfigureAwait(false)) return false;
        }
        catch (LeaseExpiredDuringWriteException)
        {
            return false;
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            // 事务已回滚才检查原载荷；不 UPDATE，不把其他数据库错误当成幂等成功。
        }
        var row = await ReadAsync(binding, taskId, leaseToken, batch.BatchOrdinal, state.Source, state.Session, token).ConfigureAwait(false);
        return row is not null && row.Id == batch.OperationId && row.PayloadHash == hash;
    }

    /// <summary>恢复只报告已验证的完整当前批次；不授予 ready 或再次调用模型的资格。</summary>
    internal async Task<bool> IsStoredAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken,
        int batchOrdinal, CancellationToken token)
    {
        NoTransaction(); _ = tasks.HeldParameters(binding, taskId, leaseToken, token);
        if (batchOrdinal is < 0 or > 4095) throw new ArgumentOutOfRangeException(nameof(batchOrdinal));
        return await ReadAsync(binding, taskId, leaseToken, batchOrdinal, null, null, token).ConfigureAwait(false) is not null;
    }

    /// <summary>恢复还必须核对完整清单的来源和本批全部原输入；单批成功仍不表示整代次齐全。</summary>
    internal async Task<bool> MatchesPlanAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken,
        AiKnowledgeIndexTextManifest manifest, int ordinal, CancellationToken token)
    {
        NoTransaction(); _ = tasks.HeldParameters(binding, taskId, leaseToken, token); ArgumentNullException.ThrowIfNull(manifest);
        if (ordinal < 0 || ordinal >= manifest.Batches.Count) return false;
        var state = await CurrentAsync(binding, taskId, leaseToken, null, null, token).ConfigureAwait(false); var identity = manifest.Identity;
        if (state is null || !MatchesIdentity(identity, binding, state)) return false;
        var row = await ReadAsync(binding, taskId, leaseToken, ordinal, state.Source, state.Session, token).ConfigureAwait(false);
        return row is not null && MatchesBatch(manifest, ordinal, row, token);
    }

    private static bool MatchesBatch(AiKnowledgeIndexTextManifest manifest, int ordinal, AiKnowledgeIndexVectorRecord row, CancellationToken token)
    {
        var payload = AiKnowledgeIndexVectorCodec.Decode(row.Payload, token); var expected = manifest.Batches[ordinal];
        var chunks = payload.Chunks.Select(chunk => chunk.Source).ToArray();
        if (expected.Ordinal != ordinal || chunks.Length != expected.ChunkCount || chunks[0].Sequence != expected.FirstSequence
            || chunks.Sum(chunk => (long)chunk.TokenCount) != expected.TokenCount) return false;
        token.ThrowIfCancellationRequested();
        return AiKnowledgeIndexTextPlan.ComputeBatchHash(manifest.Identity, ordinal, chunks, token) == expected.SourceHash;
    }

    /// <summary>核对完整期望集合及全部载荷；只证明最终 SQL 读取判定点，不授予可缓存的激活资格。</summary>
    internal async Task<bool> MatchesWholePlanAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken,
        AiKnowledgeIndexTextManifest manifest, CancellationToken token) =>
        await ReadWholePlanProofAsync(binding, taskId, leaseToken, manifest, token).ConfigureAwait(false) is not null;

    /// <summary>冻结已逐批验证的来源、会话及回执；消费者提交前仍须在持锁事务中复核。</summary>
    internal async Task<AiKnowledgeIndexGenerationProof?> ReadWholePlanProofAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken,
        AiKnowledgeIndexTextManifest manifest, CancellationToken token)
    {
        NoTransaction(); _ = tasks.HeldParameters(binding, taskId, leaseToken, token); ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Batches.Count is < 1 or > 2048 || manifest.Summary.BatchCount != manifest.Batches.Count) return null;
        var state = await CurrentAsync(binding, taskId, leaseToken, null, null, token).ConfigureAwait(false);
        if (state is null || !MatchesIdentity(manifest.Identity, binding, state)) return null;
        var before = await ReadReceiptsAsync(binding, state, manifest.Batches.Count, token).ConfigureAwait(false);
        if (before is null || before.Count != manifest.Batches.Count) return null;
        for (var ordinal = 0; ordinal < before.Count; ordinal++)
        {
            token.ThrowIfCancellationRequested();
            if (before[ordinal].BatchOrdinal != ordinal || before[ordinal].Id == Guid.Empty) return null;
            // 每次只物化一批，不累积整份文档的向量；损坏派生数据拒绝，基础设施与取消继续传播。
            try
            {
                var row = await ReadAsync(binding, taskId, leaseToken, ordinal, state.Source, state.Session, token).ConfigureAwait(false);
                if (row is null || Receipt(row) != before[ordinal] || !MatchesBatch(manifest, ordinal, row, token)) return null;
            }
            catch (InvalidDataException) { return null; }
        }
        // 复核已检查批次的载荷与账本指纹，防止检查末批时首批被删除或替换；回执不含正文。
        var after = await ReadReceiptsAsync(binding, state, manifest.Batches.Count, token).ConfigureAwait(false);
        return after is not null && before.SequenceEqual(after) ? new(state.Source, state.Session, after) : null;
    }

    private async Task<IReadOnlyList<AiKnowledgeIndexVectorReceiptRecord>?> ReadReceiptsAsync(AiKnowledgeIndexTaskBinding binding,
        State expected, int count, CancellationToken token)
    {
        var state = await CurrentAsync(binding, expected.Source.TaskId, expected.Source.LeaseToken, expected.Source, expected.Session, token).ConfigureAwait(false);
        if (state is null) return null;
        var p = Parameters(binding, state, 0, token); p["ExpectedBatchCount"] = count;
        var rows = await query.QueryAsync<AiKnowledgeIndexVectorReceiptRecord>(ReceiptStatement, p, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); NoTransaction();
        if (rows.Count > 2048) return null;
        // 冻结执行器返回的集合；最后一次等待也必须复核原会话、来源和有效租约。
        var frozen = rows.ToArray();
        return await CurrentAsync(binding, expected.Source.TaskId, expected.Source.LeaseToken, expected.Source, expected.Session, token).ConfigureAwait(false) is not null
            ? Array.AsReadOnly(frozen) : null;
    }

    private static AiKnowledgeIndexVectorReceiptRecord Receipt(AiKnowledgeIndexVectorRecord row) => new()
    { Id = row.Id, BatchOrdinal = row.BatchOrdinal, RequestHash = row.RequestHash, InputCount = row.InputCount, PayloadHash = row.PayloadHash,
        ProviderKey = row.ProviderKey, ModelId = row.ModelId, Dimensions = row.Dimensions, FormatKey = row.FormatKey, DistanceKey = row.DistanceKey };
    private bool MatchesIdentity(AiKnowledgeIndexTextPlanIdentity identity, AiKnowledgeIndexTaskBinding binding, State state) =>
        identity.KnowledgeBaseId == binding.KnowledgeBaseId && identity.KnowledgeBaseVersion == state.Source.KnowledgeBaseVersion
        && identity.TenantId == tenant.Id && identity.ModelConfigId == state.Model.Id && identity.ModelConfigVersion == state.Model.Version
        && identity.ProviderKey == state.Model.ProviderKey && identity.ModelId == state.Model.ModelId && identity.ContentHash == state.Source.ContentHash
        && (identity.Format == AiKnowledgeTextFormat.PlainText ? state.Source.ContentType == "text/plain" : state.Source.ContentType == "text/markdown");

    private async Task<AiKnowledgeIndexVectorRecord?> ReadAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken,
        int ordinal, AiKnowledgeIndexDispatchRecord? expectedSource, SessionBindingSnapshot? expectedSession, CancellationToken token)
    {
        var state = await CurrentAsync(binding, taskId, leaseToken, expectedSource, expectedSession, token).ConfigureAwait(false);
        if (state is null) return null;
        var row = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexVectorRecord>(ReadStatement,
            Parameters(binding, state, ordinal, token), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); NoTransaction();
        if (row is null || row.Id == Guid.Empty || row.IndexTaskId != taskId || row.BatchOrdinal != ordinal
            || row.ModelConfigId != state.Model.Id || row.ModelConfigVersion != state.Model.Version
            || row.ProviderKey != state.Model.ProviderKey || row.ModelId != state.Model.ModelId || row.Dimensions != Dimensions(state.Model)
            || row.FormatKey != AiKnowledgeIndexVectorCodec.FormatKey || row.DistanceKey != "cosine"
            || row.Payload.Length > AiKnowledgeIndexVectorCodec.MaxPayloadBytes || row.PayloadHash != Convert.ToHexString(SHA256.HashData(row.Payload))) return null;
        var decoded = AiKnowledgeIndexVectorCodec.Decode(row.Payload, token);
        if (decoded.Dimensions != row.Dimensions || decoded.Chunks.Count != row.InputCount || row.RequestHash != Fingerprint(row.Id, state.Model, decoded)) return null;
        var final = await CurrentAsync(binding, taskId, leaseToken, state.Source, state.Session, token).ConfigureAwait(false);
        return final is not null && row.RequestHash == Fingerprint(row.Id, final.Model, decoded) ? row : null;
    }

    private async Task<State?> CurrentAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken,
        AiKnowledgeIndexDispatchRecord? expectedSource, SessionBindingSnapshot? expectedSession, CancellationToken token)
    {
        NoTransaction();
        var source = await tasks.ReadDispatchAsync(binding, taskId, leaseToken, token).ConfigureAwait(false);
        if (source is null || (expectedSource is not null && !AiKnowledgeIndexEmbeddingService.SameSource(expectedSource, source))) return null;
        var session = await sessions.ReadHeldAsync(binding, taskId, leaseToken, token).ConfigureAwait(false);
        if (session is null || (expectedSession is not null && expectedSession != session)) return null;
        var model = await query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(tasks.IsTenant ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat,
            AiSqlParameters.Create(("ModelConfigId", source.ModelConfigId)), token).ConfigureAwait(false);
        if (model is null || !model.IsEnabled || model.Id != source.ModelConfigId || model.Version != source.ModelConfigVersion
            || model.TenantId == Guid.Empty || (model.TenantId.HasValue && model.TenantId != tenant.Id) || Dimensions(model) == 0) return null;
        // 模型/绑定读取都可能等待；最后授权原会话，再以 AI 来源查询结束判定。
        if (await sessions.ReadHeldAsync(binding, taskId, leaseToken, token).ConfigureAwait(false) != session) return null;
        foreach (var permission in Permissions)
        {
            token.ThrowIfCancellationRequested(); NoTransaction();
            var actor = await authorization.AuthorizeAsync(session, permission, token).ConfigureAwait(false);
            if (actor is null || actor.UserId != session.UserId || actor.TenantId != session.TenantId || actor.SessionId != session.SessionId) return null;
        }
        token.ThrowIfCancellationRequested(); NoTransaction();
        var final = await tasks.ReadDispatchAsync(binding, taskId, leaseToken, token).ConfigureAwait(false);
        return final is not null && AiKnowledgeIndexEmbeddingService.SameSource(source, final) ? new(final, session, model) : null;
    }

    private Dictionary<string, object?> Parameters(AiKnowledgeIndexTaskBinding binding, State state, int ordinal, CancellationToken token)
    {
        var p = tasks.HeldParameters(binding, state.Source.TaskId, state.Source.LeaseToken, token);
        p["BatchOrdinal"] = ordinal; p["ScopeKey"] = tenant.Id is { } id ? id.ToString("N") : "host";
        p["SessionId"] = state.Session.SessionId; p["SessionKind"] = state.Session.SessionKind; p["SecurityStamp"] = state.Session.SecurityStamp;
        p["ActorScope"] = state.Session.ActorScope; p["EffectiveScope"] = state.Session.EffectiveScope;
        p["FileId"] = state.Source.FileId; p["ContentHash"] = state.Source.ContentHash; p["ContentType"] = state.Source.ContentType; p["SizeBytes"] = state.Source.SizeBytes;
        return p;
    }
    private static int Dimensions(AiModelConfigRecord model) => model.ProviderKey != "openai_compatible" ? 0
        : model.ModelId switch { "text-embedding-3-small" => 1536, "text-embedding-3-large" => 3072, _ => 0 };
    private static string Fingerprint(Guid operation, AiModelConfigRecord model, AiKnowledgeIndexVectorPayload payload) =>
        AiOperationBudgetStore.Fingerprint(AiEmbeddingBudgetRequest.Create(operation, model, payload.Chunks.Select(chunk => chunk.Source.EmbeddingText).ToArray()));
    private void NoTransaction()
    { if (transactions.HasTransaction) throw new InvalidOperationException("Vector persistence cannot join an ambient transaction."); }
    private SqlStatement WriteStatement => database.Value.Provider == DatabaseProvider.MySql
        ? (tasks.IsTenant ? AiKnowledgeIndexVectorSql.InsertTenant : AiKnowledgeIndexVectorSql.InsertHost)
        : (tasks.IsTenant ? AiKnowledgeIndexVectorSql.InsertSqlServerTenant : AiKnowledgeIndexVectorSql.InsertSqlServerHost);
    private SqlStatement ReadStatement => database.Value.Provider == DatabaseProvider.MySql
        ? (tasks.IsTenant ? AiKnowledgeIndexVectorSql.FindTenant : AiKnowledgeIndexVectorSql.FindHost)
        : (tasks.IsTenant ? AiKnowledgeIndexVectorSql.FindSqlServerTenant : AiKnowledgeIndexVectorSql.FindSqlServerHost);
    private SqlStatement ReceiptStatement => database.Value.Provider == DatabaseProvider.MySql
        ? (tasks.IsTenant ? AiKnowledgeIndexVectorSql.ReceiptsTenant : AiKnowledgeIndexVectorSql.ReceiptsHost)
        : (tasks.IsTenant ? AiKnowledgeIndexVectorSql.ReceiptsSqlServerTenant : AiKnowledgeIndexVectorSql.ReceiptsSqlServerHost);
    private sealed record State(AiKnowledgeIndexDispatchRecord Source, SessionBindingSnapshot Session, AiModelConfigRecord Model);
    private sealed class LeaseExpiredDuringWriteException : Exception;
}
