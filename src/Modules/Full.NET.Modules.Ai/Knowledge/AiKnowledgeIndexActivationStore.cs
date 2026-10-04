using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>内部活动代次的原子提交；外部授权不进入 AI 本地事务。</summary>
internal sealed class AiKnowledgeIndexActivationStore(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    IDataTransactionState transactions, ICurrentTenant tenant, IClock clock, IIdGenerator ids, AiKnowledgeIndexTaskStore tasks,
    IBackgroundSessionAuthorization authorization, IOptions<DatabaseOptions> database)
{
    internal const int MaxReceiptBytes = 1048576;
    private static readonly string[] Permissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
        AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.Upload];

    /// <summary>原租约重试仍须当前授权与完整持久回执，不缓存成功结果。</summary>
    internal async Task<bool> IsActiveAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
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
            || receipts!.Length != manifest.Batches.Count || !await Authorized(binding, session, token).ConfigureAwait(false)) return false;
        Fill(p, session, manifest, receipts);
        p["Completed"] = 1;
        return await transaction.ExecuteAsync(async cancellation =>
        {
            var source = await ValidateLocked(binding, p, session, new(manifest, pointer.ManifestHash), receipts, null, cancellation).ConfigureAwait(false);
            if (source is null) return false;
            var current = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexActivationRecord>(Choose(AiKnowledgeIndexActivationSql.PointerHost,
                AiKnowledgeIndexActivationSql.PointerTenant), p, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            return current == pointer;
        }, token).ConfigureAwait(false);
    }

    /// <summary>可失败入口；事务实现须再次复核实际载荷及当前来源。</summary>
    internal async Task<bool> ActivateAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken,
        AiKnowledgeIndexGenerationProof proof, AiKnowledgeIndexTextManifestSnapshot manifest, CancellationToken token)
    {
        NoTransaction(); var p = tasks.HeldParameters(binding, taskId, leaseToken, token);
        ArgumentNullException.ThrowIfNull(proof); ArgumentNullException.ThrowIfNull(manifest);
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
        SessionBindingSnapshot session, AiKnowledgeIndexTextManifestSnapshot expectedManifest, AiKnowledgeIndexVectorReceiptRecord[] expected,
        AiKnowledgeIndexDispatchRecord? expectedSource, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); p["Now"] = clock.UtcNow;
        var source = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Choose(AiKnowledgeIndexActivationSql.AuthoritySqlServerHost,
            AiKnowledgeIndexActivationSql.AuthoritySqlServerTenant, AiKnowledgeIndexActivationSql.AuthorityMySqlHost, AiKnowledgeIndexActivationSql.AuthorityMySqlTenant), p, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (source is null || source.TaskId != (Guid)p["TaskId"]! || source.DocumentId != binding.DocumentId || source.VersionId != binding.VersionId
            || source.ModelConfigId != expectedManifest.Manifest.Identity.ModelConfigId || source.ModelConfigVersion != expectedManifest.Manifest.Identity.ModelConfigVersion
            || source.ContentHash != expectedManifest.Manifest.Identity.ContentHash || source.ContentType != (string)p["ContentType"]!
            || (expectedSource is not null && (!AiKnowledgeIndexEmbeddingService.SameSource(expectedSource, source) || source.LeaseExpiresAtUtc <= clock.UtcNow))) return null;
        // 根查询已经锁住原会话及清单；这些有界重读不调用跨模块 Port。
        var currentSession = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(Choose(AiKnowledgeIndexActivationSql.SessionHost,
            AiKnowledgeIndexActivationSql.SessionTenant), p, token).ConfigureAwait(false);
        var manifestRow = await ReadManifest(p, token).ConfigureAwait(false);
        if (currentSession is null || Snapshot(currentSession) != session || manifestRow is null || manifestRow.PayloadHash != expectedManifest.PayloadHash) return null;
        var decoded = DecodeManifest(manifestRow, token);
        if (decoded is null || decoded.Identity != expectedManifest.Manifest.Identity || decoded.Summary != expectedManifest.Manifest.Summary
            || !decoded.Batches.SequenceEqual(expectedManifest.Manifest.Batches)) return null;
        var batches = await query.QueryAsync<AiKnowledgeIndexBatchRecord>(Choose(AiKnowledgeIndexActivationSql.BatchesSqlServerHost,
            AiKnowledgeIndexActivationSql.BatchesSqlServerTenant, AiKnowledgeIndexActivationSql.BatchesMySqlHost, AiKnowledgeIndexActivationSql.BatchesMySqlTenant), p, token).ConfigureAwait(false);
        if (batches.Count != expected.Length) return null;
        var receipts = await query.QueryAsync<AiKnowledgeIndexVectorReceiptRecord>(Choose(AiKnowledgeIndexActivationSql.ReceiptsSqlServerHost,
            AiKnowledgeIndexActivationSql.ReceiptsSqlServerTenant, AiKnowledgeIndexActivationSql.ReceiptsMySqlHost, AiKnowledgeIndexActivationSql.ReceiptsMySqlTenant), p, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return expected.SequenceEqual(receipts) && (expectedSource is null || source.LeaseExpiresAtUtc > clock.UtcNow) ? source : null;
    }

    private Task<AiKnowledgeIndexTextManifestRecord?> ReadManifest(Dictionary<string, object?> p, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<AiKnowledgeIndexTextManifestRecord>(Choose(AiKnowledgeIndexActivationSql.ManifestHost, AiKnowledgeIndexActivationSql.ManifestTenant), p, token);
    private static AiKnowledgeIndexTextManifest? DecodeManifest(AiKnowledgeIndexTextManifestRecord row, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (row.FormatKey != AiKnowledgeIndexTextManifestCodec.FormatKey || row.Payload.Length > AiKnowledgeIndexTextManifestCodec.MaxPayloadBytes
            || row.PayloadHash != Convert.ToHexString(SHA256.HashData(row.Payload))) return null;
        try { return AiKnowledgeIndexTextManifestCodec.Decode(row.Payload, token); }
        catch (InvalidDataException) { return null; }
    }
    private bool ValidManifest(AiKnowledgeIndexTaskBinding binding, AiKnowledgeIndexTextManifest manifest) =>
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
            var actor = await authorization.AuthorizeAsync(session, permission, token).ConfigureAwait(false);
            if (actor is null || actor.UserId != session.UserId || actor.TenantId != session.TenantId || actor.SessionId != session.SessionId) return false;
        }
        token.ThrowIfCancellationRequested(); NoTransaction(); return session.TenantId == tenant.Id;
    }
    private static bool ValidReceipts(AiKnowledgeIndexVectorReceiptRecord[]? rows) => rows is { Length: >= 1 and <= 2048 }
        && rows.Select((row, ordinal) => row is not null && row.Id != Guid.Empty && row.BatchOrdinal == ordinal && row.InputCount is >= 1 and <= 16
            && Digest(row.RequestHash) && Digest(row.PayloadHash) && row.ProviderKey == "openai_compatible"
            && row.ModelId is "text-embedding-3-small" or "text-embedding-3-large" && row.Dimensions == (row.ModelId == "text-embedding-3-small" ? 1536 : 3072)
            && row.FormatKey == AiKnowledgeIndexVectorCodec.FormatKey && row.DistanceKey == "cosine").All(valid => valid);
    private static bool Digest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static SessionBindingSnapshot Snapshot(AiKnowledgeIndexSessionRecord row) => new(row.ActorUserId, row.TenantId, row.SessionId,
        row.SecurityStamp, row.ActorScope, row.EffectiveScope, row.SessionKind);
    private static void Fill(Dictionary<string, object?> p, SessionBindingSnapshot session, AiKnowledgeIndexTextManifest manifest, AiKnowledgeIndexVectorReceiptRecord[] receipts)
    {
        p["SessionId"] = session.SessionId; p["SessionKind"] = session.SessionKind; p["SecurityStamp"] = session.SecurityStamp;
        p["ActorScope"] = session.ActorScope; p["EffectiveScope"] = session.EffectiveScope;
        p["ModelConfigId"] = manifest.Identity.ModelConfigId; p["ModelConfigVersion"] = manifest.Identity.ModelConfigVersion;
        p["ProviderKey"] = manifest.Identity.ProviderKey; p["ModelId"] = manifest.Identity.ModelId;
        p["ContentHash"] = manifest.Identity.ContentHash; p["ContentType"] = manifest.Identity.Format == AiKnowledgeTextFormat.PlainText ? "text/plain" : "text/markdown";
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
