using System.Security.Cryptography;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Budgets;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>当前原会话授权下的单批恢复结果，不证明整个代次齐全。</summary>
internal sealed record AiKnowledgeIndexPdfStoredBatch(Guid OperationId, AiKnowledgeIndexPdfVectorSnapshot Snapshot);

/// <summary>分页清单及全量回执的瞬时证明；后续激活仍须事务内复核，不能缓存授权。</summary>
internal sealed record AiKnowledgeIndexPdfGenerationProof(AiKnowledgeIndexDispatchRecord Source, SessionBindingSnapshot Session,
    AiKnowledgeIndexPdfManifestSnapshot Manifest, IReadOnlyList<AiKnowledgeIndexVectorReceiptRecord> Receipts);

/// <summary>PDF 批次绑定完整清单和结算操作，唯一竞争只复用原载荷。</summary>
internal sealed class AiKnowledgeIndexPdfVectorStore(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    IDataTransactionState transactions, ICurrentTenant tenant, IClock clock, AiKnowledgeIndexTaskStore tasks,
    AiKnowledgeIndexSessionService sessions, IBackgroundSessionAuthorization authorization, IOptions<DatabaseOptions> database)
{
    /// <summary>只消费数据库完整清单，逐批验载荷并复核前后回执；不累计整代次向量或改变状态。</summary>
    internal async Task<AiKnowledgeIndexPdfGenerationProof?> ReadWholePlanProofAsync(AiKnowledgeIndexTaskBinding binding,
        Guid taskId, Guid leaseToken, CancellationToken token)
    {
        NoTransaction(); _ = tasks.HeldParameters(binding, taskId, leaseToken, token);
        var state = await CurrentAsync(binding, taskId, leaseToken, null, token).ConfigureAwait(false);
        if (state is null) return null;
        // CurrentAsync 已冻结原字节并验证完整清单；调用方不能缩减期望集合。
        var manifest = AiKnowledgeIndexPdfManifestCodec.Decode(state.Manifest.Payload, token);
        var before = await ReadReceiptsAsync(binding, state, manifest.Batches.Count, token).ConfigureAwait(false);
        if (before is null || before.Count != manifest.Batches.Count) return null;
        var operations = new HashSet<Guid>();
        for (var ordinal = 0; ordinal < before.Count; ordinal++)
        {
            token.ThrowIfCancellationRequested(); var receipt = before[ordinal];
            if (receipt.BatchOrdinal != ordinal || receipt.Id == Guid.Empty || !operations.Add(receipt.Id)) return null;
            // 每批携带同一清单/来源/会话约束，分页、正文及请求指纹由单批读边界完整验证。
            var batch = await ReadAsync(binding, taskId, leaseToken, ordinal, state, token).ConfigureAwait(false);
            if (batch is null || receipt != WholeReceipt(batch, state.Model)) return null;
        }
        // 最后 SQL 同时复算每批实际原字节 SHA 并检查账本，不能只比较表中声明的摘要。
        var after = await ReadReceiptsAsync(binding, state, manifest.Batches.Count, token).ConfigureAwait(false);
        return after is not null && before.SequenceEqual(after)
            ? new(state.Source, state.Session, new(manifest, state.Manifest.PayloadHash), after) : null;
    }

    private async Task<IReadOnlyList<AiKnowledgeIndexVectorReceiptRecord>?> ReadReceiptsAsync(AiKnowledgeIndexTaskBinding binding,
        State expected, int count, CancellationToken token)
    {
        var state = await CurrentAsync(binding, expected.Source.TaskId, expected.Source.LeaseToken, expected, token).ConfigureAwait(false);
        if (state is null) return null;
        var p = Parameters(binding, state, 0, token); p["ExpectedBatchCount"] = count;
        var rows = await query.QueryAsync<AiKnowledgeIndexVectorReceiptRecord>(ReceiptStatement, p, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); NoTransaction();
        if (rows.Count != count || rows.Count is < 1 or > 2048) return null;
        var frozen = Array.AsReadOnly(rows.ToArray());
        // 回执查询也会等待；末次授权与完整清单复核不能复用等待之前的结果。
        return await CurrentAsync(binding, expected.Source.TaskId, expected.Source.LeaseToken, expected, token).ConfigureAwait(false) is not null
            ? frozen : null;
    }

    private static AiKnowledgeIndexVectorReceiptRecord WholeReceipt(AiKnowledgeIndexPdfStoredBatch batch, AiModelConfigRecord model) => new()
    {
        Id = batch.OperationId, BatchOrdinal = batch.Snapshot.Payload.Ordinal,
        RequestHash = Fingerprint(batch.OperationId, model, batch.Snapshot.Payload), InputCount = batch.Snapshot.Payload.Chunks.Count,
        PayloadHash = batch.Snapshot.PayloadHash, ProviderKey = model.ProviderKey, ModelId = model.ModelId,
        Dimensions = batch.Snapshot.Payload.Dimensions, FormatKey = AiKnowledgeIndexPdfVectorCodec.FormatKey, DistanceKey = "cosine"
    };

    private static readonly string[] Permissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
        AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.IndexEnqueue];

    /// <summary>先冻结输入，再复核来源；短事务只写一批，取消或到期必须回滚。</summary>
    internal async Task<bool> PersistAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, Guid operationId,
        AiKnowledgeIndexPdfPlan plan, int ordinal, IReadOnlyList<ReadOnlyMemory<float>> vectors, CancellationToken token)
    {
        NoTransaction(); _ = tasks.HeldParameters(binding, taskId, leaseToken, token); ArgumentNullException.ThrowIfNull(plan);
        if (operationId == Guid.Empty || ordinal < 0 || ordinal >= plan.Batches.Count) return false;
        var bytes = AiKnowledgeIndexPdfVectorCodec.Encode(plan, ordinal, vectors, token);
        var state = await CurrentAsync(binding, taskId, leaseToken, null, token).ConfigureAwait(false);
        if (state is null) return false;
        AiKnowledgeIndexPdfVectorSnapshot frozen;
        try { frozen = AiKnowledgeIndexPdfVectorCodec.ReadBatch(state.Manifest.Payload, ordinal, bytes, token); }
        catch (InvalidDataException) { return false; }
        var p = Parameters(binding, state, ordinal, token);
        p["OperationId"] = operationId; p["InputCount"] = frozen.Payload.Chunks.Count;
        p["RequestHash"] = Fingerprint(operationId, state.Model, frozen.Payload);
        p["ProviderKey"] = state.Model.ProviderKey; p["ModelId"] = state.Model.ModelId;
        p["Dimensions"] = frozen.Payload.Dimensions; p["FormatKey"] = AiKnowledgeIndexPdfVectorCodec.FormatKey;
        p["DistanceKey"] = "cosine"; p["Payload"] = bytes; p["PayloadHash"] = frozen.PayloadHash;
        try
        {
            if (!await transaction.ExecuteAsync(async cancellation =>
            {
                cancellation.ThrowIfCancellationRequested(); p["Now"] = clock.UtcNow;
                if (state.Source.LeaseExpiresAtUtc <= clock.UtcNow) return false;
                var count = await command.ExecuteAsync(WriteStatement, p, cancellation).ConfigureAwait(false);
                // 回调返回 false 仍会提交，因此已写入后的取消与到期必须抛出。
                cancellation.ThrowIfCancellationRequested();
                if (count == 1 && state.Source.LeaseExpiresAtUtc <= clock.UtcNow) throw new LeaseExpiredDuringWriteException();
                return count is 0 or 1 ? count == 1 : throw new InvalidOperationException("Unexpected PDF vector write count.");
            }, token).ConfigureAwait(false)) return false;
        }
        catch (LeaseExpiredDuringWriteException) { return false; }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            // 唯一竞争在事务回滚后重读；不能覆盖或把其他数据库故障转换为成功。
        }
        var stored = await ReadAsync(binding, taskId, leaseToken, ordinal, state, token).ConfigureAwait(false);
        return stored is not null && stored.OperationId == operationId && stored.Snapshot.PayloadHash == frozen.PayloadHash;
    }

    /// <summary>恢复原字节验证的单批；不会重新派发、结算或改变 ready。</summary>
    internal async Task<AiKnowledgeIndexPdfStoredBatch?> ReadSnapshotAsync(AiKnowledgeIndexTaskBinding binding,
        Guid taskId, Guid leaseToken, int ordinal, CancellationToken token)
    {
        NoTransaction(); _ = tasks.HeldParameters(binding, taskId, leaseToken, token);
        if (ordinal is < 0 or >= 2048) throw new ArgumentOutOfRangeException(nameof(ordinal));
        return await ReadAsync(binding, taskId, leaseToken, ordinal, null, token).ConfigureAwait(false);
    }

    /// <summary>只报告当前授权下的完整单批，不提供可缓存的激活资格。</summary>
    internal async Task<bool> IsStoredAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken,
        int ordinal, CancellationToken token) => await ReadSnapshotAsync(binding, taskId, leaseToken, ordinal, token).ConfigureAwait(false) is not null;

    private async Task<AiKnowledgeIndexPdfStoredBatch?> ReadAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId,
        Guid leaseToken, int ordinal, State? expected, CancellationToken token)
    {
        var state = await CurrentAsync(binding, taskId, leaseToken, expected, token).ConfigureAwait(false);
        if (state is null) return null;
        var row = Capture(await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexVectorRecord>(ReadStatement,
            Parameters(binding, state, ordinal, token), token).ConfigureAwait(false), state, ordinal, token);
        if (row is null) return null;
        AiKnowledgeIndexPdfVectorSnapshot snapshot;
        try { snapshot = AiKnowledgeIndexPdfVectorCodec.ReadBatch(state.Manifest.Payload, ordinal, row.Payload, token); }
        catch (InvalidDataException) { return null; }
        if (snapshot.Payload.Dimensions != row.Dimensions || snapshot.Payload.Chunks.Count != row.InputCount
            || row.RequestHash != Fingerprint(row.Id, state.Model, snapshot.Payload)) return null;
        var final = await CurrentAsync(binding, taskId, leaseToken, state, token).ConfigureAwait(false);
        if (final is null || row.RequestHash != Fingerprint(row.Id, final.Model, snapshot.Payload)) return null;
        // unknown 用量可继续结算并改变结果；最后一次向量 SQL 必须再次检查账本与原清单。
        var last = Capture(await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexVectorRecord>(ReadStatement,
            Parameters(binding, final, ordinal, token), token).ConfigureAwait(false), final, ordinal, token);
        if (last is null || Receipt(row) != Receipt(last)) return null;
        // 最后向量 SQL 也会等待，返回前仍须复核原会话授权；跨模块授权不加入 AI 事务。
        // 账本/清单的证明只对应最后向量 SQL 判定点，不能作为后续 ready 的授权快照。
        var authorized = await AuthorityAsync(binding, taskId, leaseToken, final.Source, final.Session, token).ConfigureAwait(false);
        return authorized is not null && row.RequestHash == Fingerprint(row.Id, authorized.Model, snapshot.Payload) ? new(row.Id, snapshot) : null;
    }

    private static AiKnowledgeIndexVectorRecord Receipt(AiKnowledgeIndexVectorRecord row) => row with { Payload = Array.Empty<byte>() };

    private AiKnowledgeIndexVectorRecord? Capture(AiKnowledgeIndexVectorRecord? row, State state, int ordinal, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); NoTransaction();
        if (clock.UtcNow >= state.Source.LeaseExpiresAtUtc || row is null || row.Id == Guid.Empty
            || row.IndexTaskId != state.Source.TaskId || row.BatchOrdinal != ordinal
            || row.ModelConfigId != state.Model.Id || row.ModelConfigVersion != state.Model.Version
            || row.ProviderKey != state.Model.ProviderKey || row.ModelId != state.Model.ModelId || row.Dimensions != Dimensions(state.Model)
            || row.FormatKey != AiKnowledgeIndexPdfVectorCodec.FormatKey || row.DistanceKey != "cosine"
            || row.Payload.Length is < 8 or > AiKnowledgeIndexPdfVectorCodec.MaxPayloadBytes) return null;
        row = row with { Payload = row.Payload.ToArray() };
        return row.PayloadHash == Convert.ToHexString(SHA256.HashData(row.Payload)) ? row : null;
    }

    private async Task<State?> CurrentAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken,
        State? expected, CancellationToken token)
    {
        var authority = await AuthorityAsync(binding, taskId, leaseToken, expected?.Source, expected?.Session, token).ConfigureAwait(false);
        if (authority is null || (expected is not null && !SameModel(expected, authority.Model))) return null;
        var p = AuthorityParameters(binding, authority.Source, authority.Session, token);
        var row = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexTextManifestRecord>(tasks.IsTenant
            ? AiKnowledgeIndexTextManifestSql.FindTenant : AiKnowledgeIndexTextManifestSql.FindHost, p, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); NoTransaction();
        if (row is null || row.Id != taskId || row.FormatKey != AiKnowledgeIndexPdfManifestCodec.FormatKey
            || row.Payload.Length is < 8 or > AiKnowledgeIndexPdfManifestCodec.MaxPayloadBytes) return null;
        row = row with { Payload = row.Payload.ToArray() };
        if (row.PayloadHash != Convert.ToHexString(SHA256.HashData(row.Payload))
            || (expected is not null && row.PayloadHash != expected.Manifest.PayloadHash)) return null;
        AiKnowledgeIndexPdfManifest manifest;
        try { manifest = AiKnowledgeIndexPdfManifestCodec.Decode(row.Payload, token); }
        catch (InvalidDataException) { return null; }
        if (!Matches(manifest.Identity, binding, authority)) return null;
        var final = await AuthorityAsync(binding, taskId, leaseToken, authority.Source, authority.Session, token).ConfigureAwait(false);
        return final is not null && Matches(manifest.Identity, binding, final)
            && (expected is null || SameModel(expected, final.Model)) ? new(final.Source, final.Session, final.Model, row) : null;
    }

    // 同主键/版本不能掩盖目的地或调用选项变化，证明全过程冻结批准模型的完整请求绑定。
    private static bool SameModel(State expected, AiModelConfigRecord current) =>
        AiEmbeddingBudgetRequest.Create(expected.Source.TaskId, expected.Model, ["binding-check"])
        == AiEmbeddingBudgetRequest.Create(expected.Source.TaskId, current, ["binding-check"]);

    private async Task<Authority?> AuthorityAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken,
        AiKnowledgeIndexDispatchRecord? expectedSource, SessionBindingSnapshot? expectedSession, CancellationToken token)
    {
        NoTransaction(); var source = await tasks.ReadDispatchAsync(binding, taskId, leaseToken, token).ConfigureAwait(false);
        if (source is null || (expectedSource is not null && !AiKnowledgeIndexEmbeddingService.SameSource(expectedSource, source))) return null;
        var session = await sessions.ReadHeldAsync(binding, taskId, leaseToken, token).ConfigureAwait(false);
        if (session is null || (expectedSession is not null && expectedSession != session)) return null;
        var model = await query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(tasks.IsTenant ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat,
            AiSqlParameters.Create(("ModelConfigId", source.ModelConfigId)), token).ConfigureAwait(false);
        if (model is null || !model.IsEnabled || model.Id != source.ModelConfigId || model.Version != source.ModelConfigVersion
            || model.TenantId == Guid.Empty || (model.TenantId.HasValue && model.TenantId != tenant.Id) || Dimensions(model) == 0) return null;
        if (await sessions.ReadHeldAsync(binding, taskId, leaseToken, token).ConfigureAwait(false) != session) return null;
        foreach (var permission in Permissions)
        {
            token.ThrowIfCancellationRequested(); NoTransaction();
            var actor = await authorization.AuthorizeAsync(session, permission, token).ConfigureAwait(false);
            if (actor is null || actor.UserId != session.UserId || actor.TenantId != session.TenantId || actor.SessionId != session.SessionId) return null;
        }
        token.ThrowIfCancellationRequested(); NoTransaction();
        var final = await tasks.ReadDispatchAsync(binding, taskId, leaseToken, token).ConfigureAwait(false);
        return final is not null && final.LeaseExpiresAtUtc > clock.UtcNow && AiKnowledgeIndexEmbeddingService.SameSource(source, final) ? new(final, session, model) : null;
    }

    private bool Matches(AiKnowledgeIndexPdfPlanIdentity identity, AiKnowledgeIndexTaskBinding binding, Authority state) =>
        identity.KnowledgeBaseId == binding.KnowledgeBaseId && identity.KnowledgeBaseVersion == state.Source.KnowledgeBaseVersion
        && identity.TenantId == tenant.Id && identity.ModelConfigId == state.Model.Id && identity.ModelConfigVersion == state.Model.Version
        && identity.ProviderKey == state.Model.ProviderKey && identity.ModelId == state.Model.ModelId
        && string.Equals(identity.ContentHash, state.Source.ContentHash, StringComparison.OrdinalIgnoreCase)
        && state.Source.ContentType == "application/pdf";

    private Dictionary<string, object?> AuthorityParameters(AiKnowledgeIndexTaskBinding binding,
        AiKnowledgeIndexDispatchRecord source, SessionBindingSnapshot session, CancellationToken token)
    {
        var p = tasks.HeldParameters(binding, source.TaskId, source.LeaseToken, token);
        p["SessionId"] = session.SessionId; p["SessionKind"] = session.SessionKind; p["SecurityStamp"] = session.SecurityStamp;
        p["ActorScope"] = session.ActorScope; p["EffectiveScope"] = session.EffectiveScope;
        p["FileId"] = source.FileId; p["ContentHash"] = source.ContentHash; p["ContentType"] = source.ContentType; p["SizeBytes"] = source.SizeBytes;
        return p;
    }
    private Dictionary<string, object?> Parameters(AiKnowledgeIndexTaskBinding binding, State state, int ordinal, CancellationToken token)
    {
        var p = AuthorityParameters(binding, state.Source, state.Session, token);
        p["BatchOrdinal"] = ordinal; p["ScopeKey"] = tenant.Id is { } id ? id.ToString("N") : "host";
        p["PdfManifestHash"] = state.Manifest.PayloadHash; return p;
    }
    private static int Dimensions(AiModelConfigRecord model) => model.ProviderKey != "openai_compatible" ? 0
        : model.ModelId switch { "text-embedding-3-small" => 1536, "text-embedding-3-large" => 3072, _ => 0 };
    private static string Fingerprint(Guid operation, AiModelConfigRecord model, AiKnowledgeIndexPdfVectorPayload payload) =>
        AiOperationBudgetStore.Fingerprint(AiEmbeddingBudgetRequest.Create(operation, model, payload.Chunks.Select(chunk => chunk.Source.EmbeddingText).ToArray()));
    private void NoTransaction()
    { if (transactions.HasTransaction) throw new InvalidOperationException("PDF vector persistence cannot join an ambient transaction."); }
    private SqlStatement WriteStatement => database.Value.Provider == DatabaseProvider.MySql
        ? (tasks.IsTenant ? AiKnowledgeIndexVectorSql.InsertPdfTenant : AiKnowledgeIndexVectorSql.InsertPdfHost)
        : (tasks.IsTenant ? AiKnowledgeIndexVectorSql.InsertPdfSqlServerTenant : AiKnowledgeIndexVectorSql.InsertPdfSqlServerHost);
    private SqlStatement ReadStatement => database.Value.Provider == DatabaseProvider.MySql
        ? (tasks.IsTenant ? AiKnowledgeIndexVectorSql.FindPdfTenant : AiKnowledgeIndexVectorSql.FindPdfHost)
        : (tasks.IsTenant ? AiKnowledgeIndexVectorSql.FindPdfSqlServerTenant : AiKnowledgeIndexVectorSql.FindPdfSqlServerHost);
    private SqlStatement ReceiptStatement => database.Value.Provider == DatabaseProvider.MySql
        ? (tasks.IsTenant ? AiKnowledgeIndexVectorSql.ReceiptsPdfTenant : AiKnowledgeIndexVectorSql.ReceiptsPdfHost)
        : (tasks.IsTenant ? AiKnowledgeIndexVectorSql.ReceiptsPdfSqlServerTenant : AiKnowledgeIndexVectorSql.ReceiptsPdfSqlServerHost);
    private sealed record Authority(AiKnowledgeIndexDispatchRecord Source, SessionBindingSnapshot Session, AiModelConfigRecord Model);
    private sealed record State(AiKnowledgeIndexDispatchRecord Source, SessionBindingSnapshot Session, AiModelConfigRecord Model,
        AiKnowledgeIndexTextManifestRecord Manifest);
    private sealed class LeaseExpiredDuringWriteException : Exception;
}
