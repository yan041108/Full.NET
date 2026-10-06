using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;
using Full.NET.Modules.Ai.Budgets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>PDF 内部活动代次的原子提交；外部授权不进入 AI 本地事务。</summary>
internal sealed class AiKnowledgeIndexPdfActivationStore(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    IDataTransactionState transactions, ICurrentTenant tenant, IClock clock, IIdGenerator ids, AiKnowledgeIndexTaskStore tasks,
    IBackgroundSessionAuthorization authorization, IOptions<DatabaseOptions> database)
{
    internal const int MaxReceiptBytes = 1048576;
    private static readonly string[] Permissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
        AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.IndexEnqueue];

    /// <summary>原租约重试仍须当前授权与完整持久回执，不缓存成功结果。</summary>
    internal async Task<bool> IsActiveAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
        {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token); bounded.CancelAfter(TimeSpan.FromSeconds(25));
        return await VerifyActiveAsync(binding, taskId, leaseToken, bounded.Token).ConfigureAwait(false);
    }

    private async Task<bool> VerifyActiveAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
    {
        NoTransaction(); var p = tasks.HeldParameters(binding, taskId, leaseToken, token);
        var pointer = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexActivationRecord>(Choose(AiKnowledgeIndexActivationSql.PointerHost,
            AiKnowledgeIndexActivationSql.PointerTenant), p, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); NoTransaction();
        if (pointer is null || pointer.Id == Guid.Empty || pointer.IndexTaskId != taskId || pointer.DocumentId != binding.DocumentId
            || pointer.VersionId != binding.VersionId || pointer.LeaseToken != leaseToken || pointer.ReceiptJson.Length > MaxReceiptBytes
            || Hash(pointer.ReceiptJson) != pointer.ReceiptHash) return false;
        AiKnowledgeIndexVectorReceiptRecord[]? receipts;
        try { receipts = JsonSerializer.Deserialize(pointer.ReceiptJson, AiKnowledgeIndexActivationJsonContext.Default.AiKnowledgeIndexVectorReceiptRecordArray); }
        catch (JsonException) { return false; }
        if (!ValidReceipts(receipts)) return false;
        var row = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(Choose(AiKnowledgeIndexActivationSql.SessionHost,
            AiKnowledgeIndexActivationSql.SessionTenant), p, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); NoTransaction();
        if (row is null || row.Id == Guid.Empty || row.IndexTaskId != taskId) return false;
        var session = Snapshot(row);
        var manifestRow = await ReadManifest(p, token).ConfigureAwait(false);
        if (manifestRow is null || manifestRow.PayloadHash != pointer.ManifestHash) return false;
        var manifest = DecodeManifest(manifestRow, token);
        if (manifest is null || manifest.Summary.PlanHash != pointer.PlanHash || !ValidManifest(binding, manifest)
            || receipts!.Length != manifest.Batches.Count
            || !await Authorized(binding, session, token).ConfigureAwait(false)) return false;
        Fill(p, session, manifest, receipts);
        p["Completed"] = 1;
        var valid = await transaction.ExecuteAsync(async cancellation =>
        {
            var source = await ValidateLocked(binding, p, session, new(manifest, pointer.ManifestHash), receipts, null, cancellation).ConfigureAwait(false);
            if (source is null) return false;
            var current = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexActivationRecord>(Choose(AiKnowledgeIndexActivationSql.PointerHost,
                AiKnowledgeIndexActivationSql.PointerTenant), p, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (current != pointer) return false;
            return true;
        }, token).ConfigureAwait(false);
        // 数据库等待后原会话可能被撤销；读到活动指针不免除当前授权。
        return valid && await Authorized(binding, session, token).ConfigureAwait(false);
    }

    /// <summary>可失败入口；事务实现须再次复核实际载荷及当前来源。</summary>
    internal async Task<bool> ActivateAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken,
        AiKnowledgeIndexPdfGenerationProof proof, CancellationToken token)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token); bounded.CancelAfter(TimeSpan.FromSeconds(25));
        token = bounded.Token;
        NoTransaction(); var p = tasks.HeldParameters(binding, taskId, leaseToken, token);
        ArgumentNullException.ThrowIfNull(proof); var manifest = proof.Manifest; ArgumentNullException.ThrowIfNull(manifest);
        var receipts = proof.Receipts.ToArray();
        if (proof.Source.TaskId != taskId || proof.Source.LeaseToken != leaseToken || proof.Source.DocumentId != binding.DocumentId
            || proof.Source.VersionId != binding.VersionId || !ValidReceipts(receipts) || !ValidManifest(binding, manifest.Manifest)
            || receipts.Length != manifest.Manifest.Batches.Count || !await Authorized(binding, proof.Session, token).ConfigureAwait(false)) return false;
        Fill(p, proof.Session, manifest.Manifest, receipts); p["Completed"] = 0;
        p["Id"] = ids.NewId(); p["ManifestHash"] = manifest.PayloadHash;
        try
        {
            _ = await transaction.ExecuteAsync(async cancellation =>
            {
                var source = await ValidateLocked(binding, p, proof.Session, manifest, receipts, proof.Source, cancellation).ConfigureAwait(false);
                if (source is null) return false;
                var previous = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexActivationRecord>(Choose(AiKnowledgeIndexActivationSql.ExistingPointerHost,
                    AiKnowledgeIndexActivationSql.ExistingPointerTenant), p, cancellation).ConfigureAwait(false);
                p["Now"] = clock.UtcNow;
                if (source.LeaseExpiresAtUtc <= clock.UtcNow) return false;
                if (await command.ExecuteAsync(Choose(AiKnowledgeIndexActivationSql.CompleteHost, AiKnowledgeIndexActivationSql.CompleteTenant), p, cancellation).ConfigureAwait(false) != 1)
                    return false;
                // 第一次写入后任何拒绝都必须抛出，不能以 false 提交孤立完成态。
                var statement = previous is null ? Choose(AiKnowledgeIndexActivationSql.InsertHost, AiKnowledgeIndexActivationSql.InsertTenant)
                    : Choose(AiKnowledgeIndexActivationSql.UpdateHost, AiKnowledgeIndexActivationSql.UpdateTenant);
                var count = await command.ExecuteAsync(statement, p, cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                if (count != 1 || source.LeaseExpiresAtUtc <= clock.UtcNow) throw new ActivationRejectedException();
                return true;
            }, token).ConfigureAwait(false);
            // 竞争者可能已经完成；幂等恢复走完成态专用复核，不复用 running 授权。
            return await IsActiveAsync(binding, taskId, leaseToken, token).ConfigureAwait(false);
        }
        catch (ActivationRejectedException) { return false; }
    }

    private async Task<AiKnowledgeIndexDispatchRecord?> ValidateLocked(AiKnowledgeIndexTaskBinding binding, Dictionary<string, object?> p,
        SessionBindingSnapshot session, AiKnowledgeIndexPdfManifestSnapshot expectedManifest, AiKnowledgeIndexVectorReceiptRecord[] expected,
        AiKnowledgeIndexDispatchRecord? expectedSource, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); p["Now"] = clock.UtcNow;
        var source = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Choose(AiKnowledgeIndexActivationSql.AuthoritySqlServerHost,
            AiKnowledgeIndexActivationSql.AuthoritySqlServerTenant, AiKnowledgeIndexActivationSql.AuthorityMySqlHost, AiKnowledgeIndexActivationSql.AuthorityMySqlTenant), p, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (source is null || source.TaskId != (Guid)p["TaskId"]! || source.DocumentId != binding.DocumentId || source.VersionId != binding.VersionId
            || source.ModelConfigId != expectedManifest.Manifest.Identity.ModelConfigId || source.ModelConfigVersion != expectedManifest.Manifest.Identity.ModelConfigVersion
            || source.ContentHash != expectedManifest.Manifest.Identity.ContentHash || source.ContentType != (string)p["ContentType"]!
            || source.ModelProviderKey != expectedManifest.Manifest.Identity.ProviderKey || source.ModelId != expectedManifest.Manifest.Identity.ModelId
            || (expectedSource is not null && (!SameSource(expectedSource, source) || source.LeaseExpiresAtUtc <= clock.UtcNow))) return null;
        // 根查询已经锁住原会话及清单；这些有界重读不调用跨模块 Port。
        var currentSession = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(Choose(AiKnowledgeIndexActivationSql.SessionHost,
            AiKnowledgeIndexActivationSql.SessionTenant), p, token).ConfigureAwait(false);
        var manifestRow = await ReadManifest(p, token).ConfigureAwait(false);
        if (currentSession is null || Snapshot(currentSession) != session || manifestRow is null || manifestRow.PayloadHash != expectedManifest.PayloadHash) return null;
        if (manifestRow.Id != (Guid)p["TaskId"]! || manifestRow.Payload.Length is < 8 or > AiKnowledgeIndexPdfManifestCodec.MaxPayloadBytes) return null;
        manifestRow = manifestRow with { Payload = manifestRow.Payload.ToArray() };
        var decoded = DecodeManifest(manifestRow, token);
        if (decoded is null || decoded.Identity != expectedManifest.Manifest.Identity || decoded.Summary != expectedManifest.Manifest.Summary
            || !decoded.Batches.SequenceEqual(expectedManifest.Manifest.Batches)) return null;
        var batches = await query.QueryAsync<AiKnowledgeIndexBatchRecord>(Choose(AiKnowledgeIndexActivationSql.BatchesSqlServerHost,
            AiKnowledgeIndexActivationSql.BatchesSqlServerTenant, AiKnowledgeIndexActivationSql.BatchesMySqlHost, AiKnowledgeIndexActivationSql.BatchesMySqlTenant), p, token).ConfigureAwait(false);
        if (batches.Count != expected.Length) return null;
        var receipts = await query.QueryAsync<AiKnowledgeIndexVectorReceiptRecord>(Choose(AiKnowledgeIndexPdfActivationSql.ReceiptsSqlServerHost,
            AiKnowledgeIndexPdfActivationSql.ReceiptsSqlServerTenant, AiKnowledgeIndexPdfActivationSql.ReceiptsMySqlHost, AiKnowledgeIndexPdfActivationSql.ReceiptsMySqlTenant), p, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!expected.SequenceEqual(receipts)) return null;
        // 逐批捕获原载荷，不累计整代次向量；当前模型目的地参与每批请求指纹复算。
        var model = new AiModelConfigRecord { Id = source.ModelConfigId, Version = source.ModelConfigVersion,
            ProviderKey = source.ModelProviderKey, ModelId = source.ModelId, EndpointBaseUrl = source.ModelEndpointBaseUrl,
            OrganizationId = source.ModelOrganizationId };
        for (var ordinal = 0; ordinal < expected.Length; ordinal++)
        {
            token.ThrowIfCancellationRequested(); p["BatchOrdinal"] = ordinal;
            var row = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexVectorRecord>(Choose(
                AiKnowledgeIndexPdfActivationSql.PayloadSqlServerHost, AiKnowledgeIndexPdfActivationSql.PayloadSqlServerTenant,
                AiKnowledgeIndexPdfActivationSql.PayloadMySqlHost, AiKnowledgeIndexPdfActivationSql.PayloadMySqlTenant), p, token).ConfigureAwait(false);
            if (row is null || row.IndexTaskId != (Guid)p["TaskId"]! || row.BatchOrdinal != ordinal
                || row.ModelConfigId != model.Id || row.ModelConfigVersion != model.Version
                || row.Payload.Length is < 8 or > AiKnowledgeIndexPdfVectorCodec.MaxPayloadBytes) return null;
            row = row with { Payload = row.Payload.ToArray() };
            if (row.PayloadHash != Convert.ToHexString(SHA256.HashData(row.Payload)) || Receipt(row) != expected[ordinal]) return null;
            AiKnowledgeIndexPdfVectorSnapshot payload;
            try { payload = AiKnowledgeIndexPdfVectorCodec.ReadBatch(manifestRow.Payload, ordinal, row.Payload, token); }
            catch (InvalidDataException) { return null; }
            if (payload.Payload.Dimensions != row.Dimensions || payload.Payload.Chunks.Count != row.InputCount
                || row.RequestHash != AiOperationBudgetStore.Fingerprint(AiEmbeddingBudgetRequest.Create(row.Id, model,
                    payload.Payload.Chunks.Select(chunk => chunk.Source.EmbeddingText).ToArray()))) return null;
        }
        token.ThrowIfCancellationRequested();
        return expectedSource is null || source.LeaseExpiresAtUtc > clock.UtcNow ? source : null;
    }

    private static AiKnowledgeIndexVectorReceiptRecord Receipt(AiKnowledgeIndexVectorRecord row) => new()
    { Id = row.Id, BatchOrdinal = row.BatchOrdinal, RequestHash = row.RequestHash, InputCount = row.InputCount,
        PayloadHash = row.PayloadHash, ProviderKey = row.ProviderKey, ModelId = row.ModelId, Dimensions = row.Dimensions,
        FormatKey = row.FormatKey, DistanceKey = row.DistanceKey };
    private static bool SameSource(AiKnowledgeIndexDispatchRecord expected, AiKnowledgeIndexDispatchRecord current) =>
        AiKnowledgeIndexEmbeddingService.SameSource(expected, current) && expected.DataClassification == current.DataClassification
        && expected.ModelProviderKey == current.ModelProviderKey && expected.ModelId == current.ModelId
        && expected.ModelEndpointBaseUrl == current.ModelEndpointBaseUrl && expected.ModelOrganizationId == current.ModelOrganizationId;

    private Task<AiKnowledgeIndexTextManifestRecord?> ReadManifest(Dictionary<string, object?> p, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<AiKnowledgeIndexTextManifestRecord>(Choose(AiKnowledgeIndexActivationSql.ManifestHost, AiKnowledgeIndexActivationSql.ManifestTenant), p, token);
    private static AiKnowledgeIndexPdfManifest? DecodeManifest(AiKnowledgeIndexTextManifestRecord row, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (row.FormatKey != AiKnowledgeIndexPdfManifestCodec.FormatKey || row.Payload.Length is < 8 or > AiKnowledgeIndexPdfManifestCodec.MaxPayloadBytes
            || row.PayloadHash != Convert.ToHexString(SHA256.HashData(row.Payload))) return null;
        try { return AiKnowledgeIndexPdfManifestCodec.Decode(row.Payload, token); }
        catch (InvalidDataException) { return null; }
    }
    private bool ValidManifest(AiKnowledgeIndexTaskBinding binding, AiKnowledgeIndexPdfManifest manifest) =>
        manifest.Identity.KnowledgeBaseId == binding.KnowledgeBaseId && manifest.Identity.KnowledgeBaseVersion == binding.KnowledgeBaseVersion
        && manifest.Identity.TenantId == tenant.Id && manifest.Identity.ProviderKey == "openai_compatible"
        && manifest.Identity.ModelId is "text-embedding-3-small" or "text-embedding-3-large";
    private async Task<bool> Authorized(AiKnowledgeIndexTaskBinding binding, SessionBindingSnapshot session, CancellationToken token)
    {
        if (session.UserId != binding.OwnerUserId || session.TenantId != tenant.Id || session.SessionId == Guid.Empty
            || session.SessionKind is not (SessionBindingKinds.Refresh or SessionBindingKinds.OidcApplication)) return false;
        foreach (var permission in Permissions)
        {
            token.ThrowIfCancellationRequested(); NoTransaction();
            var actor = await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(ct => authorization.AuthorizeAsync(session, permission, ct), token).ConfigureAwait(false);
            if (actor is null || actor.UserId != session.UserId || actor.TenantId != session.TenantId || actor.SessionId != session.SessionId) return false;
        }
        token.ThrowIfCancellationRequested(); NoTransaction(); return session.TenantId == tenant.Id;
    }
    private static bool ValidReceipts(AiKnowledgeIndexVectorReceiptRecord[]? rows) => rows is { Length: >= 1 and <= 2048 }
        && rows.Select((row, ordinal) => row is not null && row.Id != Guid.Empty && row.BatchOrdinal == ordinal && row.InputCount is >= 1 and <= 16
            && Digest(row.RequestHash) && Digest(row.PayloadHash) && row.ProviderKey == "openai_compatible"
            && row.ModelId is "text-embedding-3-small" or "text-embedding-3-large" && row.Dimensions == (row.ModelId == "text-embedding-3-small" ? 1536 : 3072)
            && row.FormatKey == AiKnowledgeIndexPdfVectorCodec.FormatKey && row.DistanceKey == "cosine").All(valid => valid);
    private static bool Digest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static SessionBindingSnapshot Snapshot(AiKnowledgeIndexSessionRecord row) => new(row.ActorUserId, row.TenantId, row.SessionId,
        row.SecurityStamp, row.ActorScope, row.EffectiveScope, row.SessionKind);
    private static void Fill(Dictionary<string, object?> p, SessionBindingSnapshot session, AiKnowledgeIndexPdfManifest manifest, AiKnowledgeIndexVectorReceiptRecord[] receipts)
    {
        p["SessionId"] = session.SessionId; p["SessionKind"] = session.SessionKind; p["SecurityStamp"] = session.SecurityStamp;
        p["ActorScope"] = session.ActorScope; p["EffectiveScope"] = session.EffectiveScope;
        p["ModelConfigId"] = manifest.Identity.ModelConfigId; p["ModelConfigVersion"] = manifest.Identity.ModelConfigVersion;
        p["ProviderKey"] = manifest.Identity.ProviderKey; p["ModelId"] = manifest.Identity.ModelId;
        p["ContentHash"] = manifest.Identity.ContentHash; p["ContentType"] = "application/pdf";
        p["ScopeKey"] = session.TenantId?.ToString("N") ?? "host"; p["PlanHash"] = manifest.Summary.PlanHash;
        var json = JsonSerializer.Serialize(receipts, AiKnowledgeIndexActivationJsonContext.Default.AiKnowledgeIndexVectorReceiptRecordArray);
        if (Encoding.UTF8.GetByteCount(json) > MaxReceiptBytes) throw new InvalidDataException("Knowledge generation receipts exceed the fixed limit.");
        p["ReceiptJson"] = json; p["ReceiptHash"] = Hash(json);
    }
    private SqlStatement Choose(SqlStatement host, SqlStatement scoped) => tasks.IsTenant ? scoped : host;
    private SqlStatement Choose(SqlStatement serverHost, SqlStatement serverTenant, SqlStatement mysqlHost, SqlStatement mysqlTenant) =>
        database.Value.Provider == DatabaseProvider.MySql ? Choose(mysqlHost, mysqlTenant) : Choose(serverHost, serverTenant);
    private void NoTransaction()
    { if (transactions.HasTransaction) throw new InvalidOperationException("Generation activation cannot join an ambient transaction."); }
    private sealed class ActivationRejectedException : Exception;
}
