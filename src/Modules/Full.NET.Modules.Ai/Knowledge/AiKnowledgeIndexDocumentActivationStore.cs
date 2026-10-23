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

/// <summary>多格式文档内部活动代次的原子提交；外部授权不进入 AI 本地事务。</summary>
internal sealed class AiKnowledgeIndexDocumentActivationStore(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    IDataTransactionState transactions, ICurrentTenant tenant, IClock clock, IIdGenerator ids, AiKnowledgeIndexTaskStore tasks,
    IBackgroundSessionAuthorization authorization, IOptions<DatabaseOptions> database)
{
    private readonly ICommandTransaction proofTransactions = AiKnowledgeIndexPdfAuthorityBoundary.TrackTransactions(transaction);
    internal const int MaxReceiptBytes = 1048576;
    /// <summary>当前读取者由外层独立授权；原写入会话离线不影响已完成内容。</summary>
    internal Task<bool> IsReadyForReadAsync(AiKnowledgeIndexStatusRecord expected, Dictionary<string, object?> parameters, CancellationToken token)
        => ReadGenerationAsync(expected, parameters, null, token);

    /// <summary>完整证明与旧载荷删除共享 AI 锁事务；拒绝或取消必须回滚。</summary>
    internal Task<bool> WithReadyGenerationAsync(AiKnowledgeIndexStatusRecord expected, Dictionary<string, object?> parameters,
        Func<CancellationToken, Task<bool>> cleanup, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        return ReadGenerationAsync(expected, parameters, cleanup, token);
    }

    private async Task<bool> ReadGenerationAsync(AiKnowledgeIndexStatusRecord expected, Dictionary<string, object?> parameters,
        Func<CancellationToken, Task<bool>>? cleanup, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); NoTransaction();
        if (!AiKnowledgeDocumentFormats.IsDocument(expected.ContentType) || expected.IsCurrent != 1 || expected.IsDeleted != 0
            || expected.CleanupPending != 0 || expected.StateKey != "completed" || expected.TaskId is not { } taskId || taskId == Guid.Empty
            || expected.ActiveTaskId != taskId || expected.ActiveLeaseToken is not { } lease || lease == Guid.Empty) return false;
        var captured = new Dictionary<string, object?>(parameters, StringComparer.Ordinal);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token); bounded.CancelAfter(TimeSpan.FromSeconds(25));
        // 不能用可放弃迟到 IO 的边界包装整段证明，调用方必须等待本地事务结束。
        try
        {
            return await VerifyActiveAsync(new(expected.KnowledgeBaseId, expected.DocumentId, expected.VersionId, expected.DocumentVersion,
                expected.KnowledgeBaseVersion, expected.OwnerUserId), taskId, lease, bounded.Token, expected, captured, cleanup).ConfigureAwait(false);
        }
        catch (ActivationRejectedException) { return false; }
    }

    private static readonly string[] Permissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
        AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.IndexEnqueue];

    /// <summary>原租约重试仍须当前授权与完整持久回执，不缓存成功结果。</summary>
    internal async Task<bool> IsActiveAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token); bounded.CancelAfter(TimeSpan.FromSeconds(25));
        return await VerifyActiveAsync(binding, taskId, leaseToken, bounded.Token).ConfigureAwait(false);
    }

    private async Task<bool> VerifyActiveAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token,
        AiKnowledgeIndexStatusRecord? expected = null, Dictionary<string, object?>? readParameters = null,
        Func<CancellationToken, Task<bool>>? cleanup = null)
    {
        NoTransaction(); var p = tasks.HeldParameters(binding, taskId, leaseToken, token);
        var pointer = await ReadAuthorityAsync(ct => query.QuerySingleOrDefaultAsync<AiKnowledgeIndexActivationRecord>(Choose(AiKnowledgeIndexActivationSql.PointerHost,
            AiKnowledgeIndexActivationSql.PointerTenant), p, ct), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); NoTransaction();
        if (pointer is null || pointer.Id == Guid.Empty || pointer.IndexTaskId != taskId || pointer.DocumentId != binding.DocumentId
            || pointer.VersionId != binding.VersionId || pointer.LeaseToken != leaseToken || (pointer.ReceiptJson.Length > MaxReceiptBytes || Encoding.UTF8.GetByteCount(pointer.ReceiptJson) > MaxReceiptBytes)
            || Hash(pointer.ReceiptJson) != pointer.ReceiptHash) return false;
        AiKnowledgeIndexVectorReceiptRecord[]? receipts;
        try { receipts = JsonSerializer.Deserialize(pointer.ReceiptJson, AiKnowledgeIndexActivationJsonContext.Default.AiKnowledgeIndexVectorReceiptRecordArray); }
        catch (JsonException) { return false; }
        if (!ValidReceipts(receipts)) return false;
        var row = await ReadAuthorityAsync(ct => query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(Choose(AiKnowledgeIndexActivationSql.SessionHost,
            AiKnowledgeIndexActivationSql.SessionTenant), p, ct), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); NoTransaction();
        if (row is null || row.Id == Guid.Empty || row.IndexTaskId != taskId) return false;
        var session = Snapshot(row);
        var manifestRow = await ReadAuthorityAsync(ct => ReadManifestAsync(p, ct), token).ConfigureAwait(false);
        if (manifestRow is null || manifestRow.PayloadHash != pointer.ManifestHash) return false;
        var verifiedManifest = CaptureManifest(manifestRow, token);
        var manifest = verifiedManifest?.Plan;
        if (manifest is null || manifest.Summary.PlanHash != pointer.PlanHash || !ValidManifest(binding, manifest)
            || receipts!.Length != manifest.Batches.Count
            || (expected is null && !await AuthorizedAsync(binding, taskId, leaseToken, session, token).ConfigureAwait(false))) return false;
        Fill(p, session, manifest, receipts);
        p["Completed"] = 1;
        var proven = await proofTransactions.ExecuteAsync<AiKnowledgeIndexDispatchRecord?>(async cancellation =>
        {
            var source = await ValidateLockedAsync(binding, p, session, new(manifest, pointer.ManifestHash), receipts, null, cancellation).ConfigureAwait(false);
            if (source is null) return null;
            var current = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexActivationRecord>(Choose(AiKnowledgeIndexActivationSql.PointerHost,
                AiKnowledgeIndexActivationSql.PointerTenant), p, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (current != pointer) return null;
            if (expected is not null)
            {
                // 只查询 AI 可见性；角色/机构关系与当前权限由外层在证明之后重新读取。
                var visible = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexStatusRecord>(
                    AiKnowledgeIndexStatusSql.ForActive(database.Value.Provider, tasks.IsTenant), readParameters, cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                if (visible != expected || expected.ActiveManifestHash != pointer.ManifestHash || expected.ActivePlanHash != pointer.PlanHash
                    || expected.ActiveReceiptHash != pointer.ReceiptHash) return null;
                if (cleanup is not null)
                {
                    // 回调可能已经写入；false 不能提交部分删除。
                    if (!await cleanup(cancellation).ConfigureAwait(false)) throw new ActivationRejectedException();
                    cancellation.ThrowIfCancellationRequested();
                }
            }
            return source;
        }, token).ConfigureAwait(false);
        if (expected is not null) return proven is not null;
        // 数据库等待后原会话可能被撤销；读到活动指针不免除当前授权。
        if (proven is null || !await AuthorizedAsync(binding, taskId, leaseToken, session, token).ConfigureAwait(false)) return false;
        // 最后权限等待后再锁读 AI 来源、目的地、原绑定和指针；不重复解码整份计划。
        // 向量/结算证明对应前一个完整锁事务；这不是 Identity 与 AI 的联合原子快照。
        return await proofTransactions.ExecuteAsync(async cancellation =>
        {
            cancellation.ThrowIfCancellationRequested(); p["Now"] = clock.UtcNow;
            var currentSource = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Choose(
                AiKnowledgeIndexDocumentActivationSql.AuthoritySqlServerHost, AiKnowledgeIndexDocumentActivationSql.AuthoritySqlServerTenant,
                AiKnowledgeIndexDocumentActivationSql.AuthorityMySqlHost, AiKnowledgeIndexDocumentActivationSql.AuthorityMySqlTenant), p, cancellation).ConfigureAwait(false);
            if (currentSource is null || !SameSource(proven, currentSource)) return false;
            var currentSession = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(Choose(
                AiKnowledgeIndexActivationSql.SessionHost, AiKnowledgeIndexActivationSql.SessionTenant), p, cancellation).ConfigureAwait(false);
            var currentPointer = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexActivationRecord>(Choose(
                AiKnowledgeIndexActivationSql.PointerHost, AiKnowledgeIndexActivationSql.PointerTenant), p, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            return currentSession is not null && Snapshot(currentSession) == session && currentPointer == pointer;
        }, token).ConfigureAwait(false);
    }

    /// <summary>可失败入口；事务实现须再次复核实际载荷及当前来源。</summary>
    internal async Task<bool> ActivateAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken,
        AiKnowledgeIndexDocumentGenerationProof proof, CancellationToken token)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token); bounded.CancelAfter(TimeSpan.FromSeconds(25));
        token = bounded.Token;
        NoTransaction(); var p = tasks.HeldParameters(binding, taskId, leaseToken, token);
        ArgumentNullException.ThrowIfNull(proof); var manifest = proof.Manifest; ArgumentNullException.ThrowIfNull(manifest);
        var receipts = proof.Receipts.ToArray();
        if (proof.Source.TaskId != taskId || proof.Source.LeaseToken != leaseToken || proof.Source.DocumentId != binding.DocumentId
            || proof.Source.VersionId != binding.VersionId || !ValidReceipts(receipts) || !ValidManifest(binding, manifest.Manifest)
            || receipts.Length != manifest.Manifest.Batches.Count || !await AuthorizedAsync(binding, taskId, leaseToken, proof.Session, token).ConfigureAwait(false)) return false;
        Fill(p, proof.Session, manifest.Manifest, receipts); p["Completed"] = 0;
        p["Id"] = ids.NewId(); p["ManifestHash"] = manifest.PayloadHash;
        try
        {
            _ = await proofTransactions.ExecuteAsync(async cancellation =>
            {
                var source = await ValidateLockedAsync(binding, p, proof.Session, manifest, receipts, proof.Source, cancellation).ConfigureAwait(false);
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

    private async Task<AiKnowledgeIndexDispatchRecord?> ValidateLockedAsync(AiKnowledgeIndexTaskBinding binding, Dictionary<string, object?> p,
        SessionBindingSnapshot session, AiKnowledgeIndexDocumentManifestSnapshot expectedManifest, AiKnowledgeIndexVectorReceiptRecord[] expected,
        AiKnowledgeIndexDispatchRecord? expectedSource, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); p["Now"] = clock.UtcNow;
        var source = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Choose(AiKnowledgeIndexDocumentActivationSql.AuthoritySqlServerHost,
            AiKnowledgeIndexDocumentActivationSql.AuthoritySqlServerTenant, AiKnowledgeIndexDocumentActivationSql.AuthorityMySqlHost, AiKnowledgeIndexDocumentActivationSql.AuthorityMySqlTenant), p, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (source is null || source.TaskId != (Guid)p["TaskId"]! || source.DocumentId != binding.DocumentId || source.VersionId != binding.VersionId
            || source.ModelConfigId != expectedManifest.Manifest.Source.ModelConfigId || source.ModelConfigVersion != expectedManifest.Manifest.Source.ModelConfigVersion
            || !string.Equals(source.ContentHash, expectedManifest.Manifest.Source.Document.ContentHash, StringComparison.OrdinalIgnoreCase) || source.ContentType != (string)p["ContentType"]!
            || source.ParsingPolicyKey != (string)p["ParsingPolicyKey"]!
            || source.DataClassification != expectedManifest.Manifest.Source.DataClassification
            || AiKnowledgeDocumentModelChunker.Destination(source.ModelEndpointBaseUrl, source.ModelOrganizationId) != expectedManifest.Manifest.Source.DestinationHash
            || source.SizeBytes != expectedManifest.Manifest.Source.Document.SizeBytes || source.DataClassification is not ("public" or "internal" or "restricted")
            || source.ModelProviderKey != expectedManifest.Manifest.Source.ProviderKey || source.ModelId != expectedManifest.Manifest.Source.ModelId
            || (expectedSource is not null && (!SameSource(expectedSource, source) || source.LeaseExpiresAtUtc <= clock.UtcNow))) return null;
        // 根查询已经锁住原会话及清单；这些有界重读不调用跨模块 Port。
        var currentSession = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(Choose(AiKnowledgeIndexActivationSql.SessionHost,
            AiKnowledgeIndexActivationSql.SessionTenant), p, token).ConfigureAwait(false);
        var manifestRow = await ReadManifestAsync(p, token).ConfigureAwait(false);
        if (currentSession is null || Snapshot(currentSession) != session || manifestRow is null || manifestRow.PayloadHash != expectedManifest.PayloadHash) return null;
        if (manifestRow.Id != (Guid)p["TaskId"]! || manifestRow.Payload.Length is < 8 or > AiKnowledgeIndexDocumentManifestCodec.MaxPayloadBytes) return null;
        manifestRow = manifestRow with { Payload = manifestRow.Payload.ToArray() };
        var verified = CaptureManifest(manifestRow, token);
        if (verified is null || !AiKnowledgeIndexDocumentVerifiedManifest.SamePlan(verified.Plan, expectedManifest.Manifest)) return null;
        var batches = await query.QueryAsync<AiKnowledgeIndexBatchRecord>(Choose(AiKnowledgeIndexActivationSql.BatchesSqlServerHost,
            AiKnowledgeIndexActivationSql.BatchesSqlServerTenant, AiKnowledgeIndexActivationSql.BatchesMySqlHost, AiKnowledgeIndexActivationSql.BatchesMySqlTenant), p, token).ConfigureAwait(false);
        if (batches.Count != expected.Length) return null;
        var receipts = await query.QueryAsync<AiKnowledgeIndexVectorReceiptRecord>(Choose(AiKnowledgeIndexDocumentActivationSql.ReceiptsSqlServerHost,
            AiKnowledgeIndexDocumentActivationSql.ReceiptsSqlServerTenant, AiKnowledgeIndexDocumentActivationSql.ReceiptsMySqlHost, AiKnowledgeIndexDocumentActivationSql.ReceiptsMySqlTenant), p, token).ConfigureAwait(false);
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
                AiKnowledgeIndexDocumentActivationSql.PayloadSqlServerHost, AiKnowledgeIndexDocumentActivationSql.PayloadSqlServerTenant,
                AiKnowledgeIndexDocumentActivationSql.PayloadMySqlHost, AiKnowledgeIndexDocumentActivationSql.PayloadMySqlTenant), p, token).ConfigureAwait(false);
            if (row is null || row.IndexTaskId != (Guid)p["TaskId"]! || row.BatchOrdinal != ordinal
                || row.ModelConfigId != model.Id || row.ModelConfigVersion != model.Version
                || row.Payload.Length is < 8 or > AiKnowledgeIndexDocumentVectorCodec.MaxPayloadBytes) return null;
            row = row with { Payload = row.Payload.ToArray() };
            if (row.PayloadHash != Convert.ToHexString(SHA256.HashData(row.Payload)) || Receipt(row) != expected[ordinal]) return null;
            AiKnowledgeIndexDocumentVectorSnapshot payload;
            try { payload = AiKnowledgeIndexDocumentVectorCodec.ReadBatch(verified, ordinal, row.Payload, token); }
            catch (InvalidDataException) { return null; }
            if (payload.Payload.Dimensions != row.Dimensions || payload.Payload.Chunks.Count != row.InputCount
                || row.RequestHash != AiOperationBudgetStore.Fingerprint(AiEmbeddingBudgetRequest.Create(row.Id, model,
                    payload.Payload.Chunks.Select(chunk => chunk.Source.Text).ToArray()))) return null;
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

    private Task<AiKnowledgeIndexTextManifestRecord?> ReadManifestAsync(Dictionary<string, object?> p, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<AiKnowledgeIndexTextManifestRecord>(Choose(AiKnowledgeIndexDocumentActivationSql.ManifestHost, AiKnowledgeIndexDocumentActivationSql.ManifestTenant), p, token);
    private static AiKnowledgeIndexDocumentVerifiedManifest? CaptureManifest(AiKnowledgeIndexTextManifestRecord row, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (row.FormatKey != AiKnowledgeIndexDocumentManifestCodec.FormatKey || row.Payload.Length is < 8 or > AiKnowledgeIndexDocumentManifestCodec.MaxPayloadBytes
            ) return null;
        try
        {
            var verified = AiKnowledgeIndexDocumentVerifiedManifest.Capture(row.Payload, token);
            return verified.PayloadHash == row.PayloadHash ? verified : null;
        }
        catch (InvalidDataException) { return null; }
    }
    private bool ValidManifest(AiKnowledgeIndexTaskBinding binding, AiKnowledgeDocumentPlan manifest) =>
        manifest.Source.KnowledgeBaseId == binding.KnowledgeBaseId && manifest.Source.KnowledgeBaseVersion == binding.KnowledgeBaseVersion
        && manifest.Source.TenantId == tenant.Id && manifest.Source.ProviderKey == "openai_compatible"
        && manifest.Source.ModelId is "text-embedding-3-small" or "text-embedding-3-large";
    private async Task<bool> AuthorizedAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, SessionBindingSnapshot session, CancellationToken token)
    {
        if (session.UserId != binding.OwnerUserId || session.TenantId != tenant.Id || session.SessionId == Guid.Empty
            || session.SessionKind is not (SessionBindingKinds.Refresh or SessionBindingKinds.OidcApplication)) return false;
        NoTransaction();
        var frozen = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(Choose(AiKnowledgeIndexActivationSql.SessionHost,
            AiKnowledgeIndexActivationSql.SessionTenant), tasks.HeldParameters(binding, taskId, leaseToken, token), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); NoTransaction();
        if (frozen is null || frozen.Generation < 0 || frozen.IndexTaskId != taskId || Snapshot(frozen) != session) return false;
        // 读取历史活动代次另走读取者授权；写入或恢复重建必须继续保有原重建权限。
        string[] required = frozen.Generation > 0 ? [.. Permissions, AiKnowledgeDocumentPermissions.IndexRead, AiKnowledgeDocumentPermissions.IndexRebuild] : Permissions;
        {
            token.ThrowIfCancellationRequested(); NoTransaction();
            var actor = await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(ct => authorization.AuthorizeAllAsync(session, required, ct), token).ConfigureAwait(false);
            if (actor is null || actor.UserId != session.UserId || actor.TenantId != session.TenantId || actor.SessionId != session.SessionId) return false;
        }
        token.ThrowIfCancellationRequested(); NoTransaction(); return session.TenantId == tenant.Id;
    }
    private static bool ValidReceipts(AiKnowledgeIndexVectorReceiptRecord[]? rows) => rows is { Length: >= 1 and <= 2048 }
        && rows.Select(row => row?.Id).Distinct().Count() == rows.Length
        && rows.Select((row, ordinal) => row is not null && row.Id != Guid.Empty && row.BatchOrdinal == ordinal && row.InputCount is >= 1 and <= 16
            && Digest(row.RequestHash) && Digest(row.PayloadHash) && row.ProviderKey == "openai_compatible"
            && row.ModelId is "text-embedding-3-small" or "text-embedding-3-large" && row.Dimensions == (row.ModelId == "text-embedding-3-small" ? 1536 : 3072)
            && row.FormatKey == AiKnowledgeIndexDocumentVectorCodec.FormatKey && row.DistanceKey == "cosine").All(valid => valid);
    private static bool Digest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static SessionBindingSnapshot Snapshot(AiKnowledgeIndexSessionRecord row) => new(row.ActorUserId, row.TenantId, row.SessionId,
        row.SecurityStamp, row.ActorScope, row.EffectiveScope, row.SessionKind);
    private static void Fill(Dictionary<string, object?> p, SessionBindingSnapshot session, AiKnowledgeDocumentPlan manifest, AiKnowledgeIndexVectorReceiptRecord[] receipts)
    {
        p["SessionId"] = session.SessionId; p["SessionKind"] = session.SessionKind; p["SecurityStamp"] = session.SecurityStamp;
        p["ActorScope"] = session.ActorScope; p["EffectiveScope"] = session.EffectiveScope;
        p["ModelConfigId"] = manifest.Source.ModelConfigId; p["ModelConfigVersion"] = manifest.Source.ModelConfigVersion;
        p["ProviderKey"] = manifest.Source.ProviderKey; p["ModelId"] = manifest.Source.ModelId;
        p["SizeBytes"] = manifest.Source.Document.SizeBytes; p["ContentHash"] = manifest.Source.Document.ContentHash;
        p["ContentType"] = AiKnowledgeDocumentFormats.ContentType(manifest.Source.Document);
        p["ParsingPolicyKey"] = AiKnowledgeDocumentFormats.PolicyKey(manifest.Source.Document);
        p["ScopeKey"] = session.TenantId?.ToString("N") ?? "host"; p["PlanHash"] = manifest.Summary.PlanHash;
        var json = JsonSerializer.Serialize(receipts, AiKnowledgeIndexActivationJsonContext.Default.AiKnowledgeIndexVectorReceiptRecordArray);
        if (Encoding.UTF8.GetByteCount(json) > MaxReceiptBytes) throw new InvalidDataException("Knowledge generation receipts exceed the fixed limit.");
        p["ReceiptJson"] = json; p["ReceiptHash"] = Hash(json);
    }
    private SqlStatement Choose(SqlStatement host, SqlStatement scoped) => tasks.IsTenant ? scoped : host;
    private SqlStatement Choose(SqlStatement serverHost, SqlStatement serverTenant, SqlStatement mysqlHost, SqlStatement mysqlTenant) =>
        database.Value.Provider == DatabaseProvider.MySql ? Choose(mysqlHost, mysqlTenant) : Choose(serverHost, serverTenant);
    // 仅事务外纯读取可以放弃迟到结果；所有事务及命令必须由原调用者等待。
    private async Task<T> ReadAuthorityAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
    { token.ThrowIfCancellationRequested(); NoTransaction(); var result = await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(action, token).ConfigureAwait(false); token.ThrowIfCancellationRequested(); NoTransaction(); return result; }
    private void NoTransaction()
    { if (transactions.HasTransaction) throw new InvalidOperationException("Generation activation cannot join an ambient transaction."); }
    private sealed class ActivationRejectedException : Exception;
}
