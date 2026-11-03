using System.Security.Cryptography;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>由已完成交互授权和 Files 复核的服务调用；本存储器不授予读取、编辑或模型派发资格。</summary>
internal sealed class AiKnowledgeCorrectionStore(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    IDataTransactionState state, ICurrentTenant tenant, IClock clock, IIdGenerator ids, IOptions<DatabaseOptions> database)
{
    internal sealed record Binding(Guid KnowledgeBaseId, Guid DocumentId, Guid VersionId, Guid OwnerUserId, int DocumentVersion);
    private bool Scoped => AiKnowledgeScope.IsTenant(tenant);

    internal Task<AiKnowledgeCorrectionSourceRecord?> ReadSourceAsync(Binding binding, Guid sourceId, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<AiKnowledgeCorrectionSourceRecord>(AiKnowledgeCorrectionSql.SourceFor(database.Value.Provider, Scoped),
            Parameters(binding, ("SourceId", sourceId)), token);
    internal Task<AiKnowledgeCorrectionRevisionRecord?> ReadRevisionAsync(Binding binding, Guid revisionId, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<AiKnowledgeCorrectionRevisionRecord>(AiKnowledgeCorrectionSql.RevisionFor(database.Value.Provider, Scoped),
            Parameters(binding, ("RevisionId", revisionId)), token);
    internal Task<AiKnowledgeCorrectionHeadRecord?> ReadHeadAsync(Binding binding, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<AiKnowledgeCorrectionHeadRecord>(Scoped ? AiKnowledgeCorrectionSql.HeadTenant : AiKnowledgeCorrectionSql.HeadHost, Parameters(binding), token);

    internal Task<AiKnowledgeCorrectionSourceRecord?> ReadRequestedSourceAsync(Binding binding, Guid requestKey, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<AiKnowledgeCorrectionSourceRecord>(AiKnowledgeCorrectionSql.SourceFor(database.Value.Provider, Scoped, true),
            Parameters(binding, ("RequestKey", requestKey)), token);
    internal Task<AiKnowledgeCorrectionRevisionRecord?> ReadRequestedRevisionAsync(Binding binding, Guid requestKey, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<AiKnowledgeCorrectionRevisionRecord>(AiKnowledgeCorrectionSql.RevisionFor(database.Value.Provider, Scoped, true),
            Parameters(binding, ("RequestKey", requestKey)), token);
    internal Task<IReadOnlyList<AiKnowledgeCorrectionRevisionSummaryRecord>> ReadHistoryAsync(Binding binding, long beforeHeadVersion, CancellationToken token) =>
        query.QueryAsync<AiKnowledgeCorrectionRevisionSummaryRecord>(AiKnowledgeCorrectionSql.HistoryFor(database.Value.Provider, Scoped),
            Parameters(binding, ("BeforeHeadVersion", beforeHeadVersion)), token);

    /// <summary>当前头前后必须一致；读取中发生保存时拒绝混合快照，由调用者显式重读。</summary>
    internal async Task<AiKnowledgeCorrectionSnapshot?> ReadCurrentAsync(Binding binding, AiKnowledgeParsedFormat format,
        string fileHash, int size, AiKnowledgeParsePolicy policy, CancellationToken token)
    {
        NoTransaction(); var scoped = Scoped; var tenantId = tenant.Id;
        try
        {
            var head = await ReadHeadAsync(binding, token).ConfigureAwait(false);
            if (head is null || head.Id != binding.VersionId || head.Version < 0 || (head.RevisionId.HasValue ? head.Version < 1 : head.Version != 0)) return null;
            var source = await ReadSourceAsync(binding, head.SourceId, token).ConfigureAwait(false);
            var revision = head.RevisionId is { } id ? await ReadRevisionAsync(binding, id, token).ConfigureAwait(false) : null;
            if (source is null || head.RevisionId.HasValue && revision is null) return null;
            var snapshot = AiKnowledgeCorrectionSnapshot.Verify(source, revision, format, fileHash, size, policy, token);
            if (snapshot?.Identity.HeadVersion != head.Version || snapshot.Identity.SourceId != head.SourceId
                || snapshot.Identity.RevisionId != head.RevisionId || !Same(scoped, tenantId)) return null;
            var final = await ReadHeadAsync(binding, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); NoTransaction();
            return final == head && Same(scoped, tenantId) ? snapshot : null;
        }
        catch (DataCommandException error) when (error.Kind == DataCommandFailureKind.Deadlock)
        {
            // 只读头与并发保存的锁可能成环；没有写入的读取失败等同快照不稳定，由调用者重新授权并核对 CAS。
            // 不包围保存、幂等回放或提交后复核，避免把已提交结果误报为未提交的版本竞争。
            token.ThrowIfCancellationRequested(); NoTransaction();
            return null;
        }
    }

    /// <summary>只读取指定冻结身份，绝不以较新人工头或原始自动结果替代丢失/损坏修订。</summary>
    internal async Task<AiKnowledgeCorrectionSnapshot?> ReadFrozenAsync(Binding binding, AiKnowledgeCorrectionIdentity identity,
        AiKnowledgeParsedFormat format, string fileHash, int size, AiKnowledgeParsePolicy policy, CancellationToken token)
    {
        NoTransaction(); var scoped = Scoped; var tenantId = tenant.Id;
        var source = await ReadSourceAsync(binding, identity.SourceId, token).ConfigureAwait(false);
        var revision = identity.RevisionId is { } id ? await ReadRevisionAsync(binding, id, token).ConfigureAwait(false) : null;
        if (source is null || identity.RevisionId.HasValue && revision is null) return null;
        var snapshot = AiKnowledgeCorrectionSnapshot.Verify(source, revision, format, fileHash, size, policy, token);
        token.ThrowIfCancellationRequested(); NoTransaction();
        return snapshot?.Identity == identity && Same(scoped, tenantId) ? snapshot : null;
    }

    /// <summary>首份解析初始化工作台；重解析只另存快照，不覆盖已有人工修订头。</summary>
    internal async Task<Result<AiKnowledgeCorrectionSourceRecord>> CreateSourceAsync(Binding binding, Guid requestKey, string requestHash,
        AiKnowledgeParsedDocument original, string parserRuntimeId, CancellationToken token, AiKnowledgeCorrectionPreparationLease? preparation = null)
    {
        NoTransaction(); Validate(binding, requestKey, requestHash);
        var originalBytes = AiKnowledgeParsedWireCodec.Encode(original, token);
        var initial = AiKnowledgeCorrectionWireCodec.Encode(AiKnowledgeCorrectionDocument.Create(original, ids.NewId, token), parserRuntimeId, token);
        var source = new AiKnowledgeCorrectionSourceRecord(ids.NewId(), binding.VersionId, requestKey, requestHash,
            original.ContentHash, parserRuntimeId, AiKnowledgeCorrectionWireCodec.SourceHash(original, parserRuntimeId, token), originalBytes, initial,
            Hash(initial), binding.OwnerUserId, Now());
        var scoped = Scoped; var tenantId = tenant.Id;
        var p = Parameters(binding, ("SourceId", source.Id), ("RequestKey", requestKey), ("RequestHash", requestHash),
            ("FileHash", source.FileHash), ("ParserRuntimeId", parserRuntimeId), ("SourceHash", source.SourceHash),
            ("OriginalPayload", originalBytes), ("InitialPayload", initial), ("InitialPayloadHash", source.InitialPayloadHash), ("Now", source.CreatedAtUtc));
        if (preparation is not null)
        {
            if (preparation.Id != requestKey || preparation.Token == Guid.Empty || preparation.ParserRuntimeId != parserRuntimeId) return Conflict<AiKnowledgeCorrectionSourceRecord>();
            p["PreparationId"] = preparation.Id; p["PreparationLease"] = preparation.Token; p["PreparationHash"] = preparation.RequestHash;
        }
        return await transaction.ExecuteResultAsync(async ct =>
        {
            if (!await Lock(binding, p, scoped, tenantId, ct).ConfigureAwait(false)) return Conflict<AiKnowledgeCorrectionSourceRecord>();
            p["LeaseNow"] = Now();
            if (preparation is not null && await query.QuerySingleOrDefaultAsync<int>(scoped ? AiKnowledgeCorrectionPreparationSql.HeldTenant : AiKnowledgeCorrectionPreparationSql.HeldHost, p, ct).ConfigureAwait(false) != 1)
                return Conflict<AiKnowledgeCorrectionSourceRecord>();
            var existing = await query.QuerySingleOrDefaultAsync<AiKnowledgeCorrectionSourceRecord>(AiKnowledgeCorrectionSql.SourceFor(database.Value.Provider, scoped, true), p, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                if (existing.RequestHash != requestHash || existing.ActorUserId != binding.OwnerUserId || existing.ParserRuntimeId != parserRuntimeId) return Conflict<AiKnowledgeCorrectionSourceRecord>();
                p["SourceId"] = existing.Id;
            }
            else
            {
                if (await command.ExecuteAsync(scoped ? AiKnowledgeCorrectionSql.InsertSourceTenant : AiKnowledgeCorrectionSql.InsertSourceHost, p, ct).ConfigureAwait(false) != 1)
                    return Conflict<AiKnowledgeCorrectionSourceRecord>();
                _ = await command.ExecuteAsync(scoped ? AiKnowledgeCorrectionSql.InitializeTenant : AiKnowledgeCorrectionSql.InitializeHost, p, ct).ConfigureAwait(false);
            }
            p["LeaseNow"] = Now();
            if (preparation is not null && await command.ExecuteAsync(scoped ? AiKnowledgeCorrectionPreparationSql.CompleteTenant : AiKnowledgeCorrectionPreparationSql.CompleteHost, p, ct).ConfigureAwait(false) != 1)
                return Conflict<AiKnowledgeCorrectionSourceRecord>();
            ct.ThrowIfCancellationRequested();
            return Same(scoped, tenantId) ? Result<AiKnowledgeCorrectionSourceRecord>.Success(existing ?? source) : Conflict<AiKnowledgeCorrectionSourceRecord>();
        }, token).ConfigureAwait(false);
    }

    /// <summary>校对或恢复一并写入不可变修订、推进修订头和文档版本，任一失败回滚整个本地事务。</summary>
    internal async Task<Result<AiKnowledgeCorrectionRevisionRecord>> SaveAsync(Binding binding, Guid sourceId, int headVersion,
        Guid requestKey, string requestHash, string reason, AiKnowledgeCorrectionDocument document, string parserRuntimeId, CancellationToken token)
    {
        NoTransaction(); Validate(binding, requestKey, requestHash);
        if (sourceId == Guid.Empty || headVersion < 0 || headVersion == int.MaxValue || reason is not { Length: > 0 and <= 1000 }
            || !AiKnowledgeParsedDocument.ValidText(reason, token) || string.IsNullOrWhiteSpace(reason)) return Conflict<AiKnowledgeCorrectionRevisionRecord>();
        var bytes = AiKnowledgeCorrectionWireCodec.Encode(document, parserRuntimeId, token);
        var sourceHash = AiKnowledgeCorrectionWireCodec.SourceHash(document.Source, parserRuntimeId, token);
        var revision = new AiKnowledgeCorrectionRevisionRecord(ids.NewId(), binding.VersionId, sourceId, null, requestKey, requestHash,
            bytes, Hash(bytes), reason, binding.OwnerUserId, Now(), binding.DocumentVersion + 1, headVersion + 1);
        var scoped = Scoped; var tenantId = tenant.Id;
        var p = Parameters(binding, ("SourceId", sourceId), ("SourceHash", sourceHash), ("RevisionId", revision.Id),
            ("RequestKey", requestKey), ("RequestHash", requestHash), ("Payload", bytes), ("PayloadHash", revision.PayloadHash),
            ("ReasonText", reason), ("HeadVersion", headVersion), ("Now", revision.CreatedAtUtc));
        // 有界但可能较大的载荷校验留在根锁外；事务内再核对同一不可变来源及清理状态。
        var verifiedSource = await ReadSourceAsync(binding, sourceId, token).ConfigureAwait(false);
        if (!Same(scoped, tenantId) || verifiedSource is null || verifiedSource.SourceHash != sourceHash
            || verifiedSource.ParserRuntimeId != parserRuntimeId
            || AiKnowledgeCorrectionSnapshot.Verify(verifiedSource, null, document.Source.Format, document.Source.ContentHash,
                document.Source.SizeBytes, document.Source.Policy, token) is null) return Conflict<AiKnowledgeCorrectionRevisionRecord>();
        return await transaction.ExecuteResultAsync(async ct =>
        {
            if (!await Lock(binding, p, scoped, tenantId, ct).ConfigureAwait(false)) return Conflict<AiKnowledgeCorrectionRevisionRecord>();
            var existing = await query.QuerySingleOrDefaultAsync<AiKnowledgeCorrectionRevisionRecord>(AiKnowledgeCorrectionSql.RevisionFor(database.Value.Provider, scoped, true), p, ct).ConfigureAwait(false);
            if (existing is not null) return existing.RequestHash == requestHash && existing.ActorUserId == binding.OwnerUserId
                ? Result<AiKnowledgeCorrectionRevisionRecord>.Success(existing) : Conflict<AiKnowledgeCorrectionRevisionRecord>();
            var source = await query.QuerySingleOrDefaultAsync<AiKnowledgeCorrectionSourceRecord>(AiKnowledgeCorrectionSql.SourceFor(database.Value.Provider, scoped), p, ct).ConfigureAwait(false);
            if (source is null || source.SourceHash != sourceHash || source.FileHash != document.Source.ContentHash || source.ParserRuntimeId != parserRuntimeId
                || source.InitialPayloadHash != verifiedSource.InitialPayloadHash)
                return Conflict<AiKnowledgeCorrectionRevisionRecord>();
            var head = await query.QuerySingleOrDefaultAsync<AiKnowledgeCorrectionHeadRecord>(scoped ? AiKnowledgeCorrectionSql.HeadTenant : AiKnowledgeCorrectionSql.HeadHost, p, ct).ConfigureAwait(false);
            if (head is null || head.Version != headVersion) return Conflict<AiKnowledgeCorrectionRevisionRecord>();
            if (await command.ExecuteAsync(scoped ? AiKnowledgeCorrectionSql.InsertRevisionTenant : AiKnowledgeCorrectionSql.InsertRevisionHost, p, ct).ConfigureAwait(false) != 1
                || await command.ExecuteAsync(scoped ? AiKnowledgeCorrectionSql.AdvanceTenant : AiKnowledgeCorrectionSql.AdvanceHost, p, ct).ConfigureAwait(false) != 1
                || await command.ExecuteAsync(scoped ? AiKnowledgeCorrectionSql.TouchTenant : AiKnowledgeCorrectionSql.TouchHost, p, ct).ConfigureAwait(false) != 1)
                return Conflict<AiKnowledgeCorrectionRevisionRecord>();
            ct.ThrowIfCancellationRequested();
            return Same(scoped, tenantId) ? Result<AiKnowledgeCorrectionRevisionRecord>.Success(revision with { ParentId = head.RevisionId }) : Conflict<AiKnowledgeCorrectionRevisionRecord>();
        }, token).ConfigureAwait(false);
    }

    private async Task<bool> Lock(Binding binding, Dictionary<string, object?> p, bool scoped, Guid? tenantId, CancellationToken token) =>
        Same(scoped, tenantId) && await query.QuerySingleOrDefaultAsync<Guid>(AiKnowledgePublicationSql.LockFor(database.Value.Provider, scoped), p, token).ConfigureAwait(false) == binding.DocumentId
            && Same(scoped, tenantId);
    private bool Same(bool scoped, Guid? tenantId) => Scoped == scoped && tenant.Id == tenantId && (scoped ? tenant.IsAvailable : tenant.IsHost);
    private void NoTransaction() { if (state.HasTransaction) throw new InvalidOperationException("Correction writes require an independent local transaction."); }
    private static void Validate(Binding binding, Guid key, string hash)
    {
        if (binding.KnowledgeBaseId == Guid.Empty || binding.DocumentId == Guid.Empty || binding.VersionId == Guid.Empty || binding.OwnerUserId == Guid.Empty
            || binding.DocumentVersion is < 1 or int.MaxValue || key == Guid.Empty || hash is not { Length: 64 } || hash.Any(c => !char.IsAsciiHexDigit(c)))
            throw new ArgumentException("Invalid correction operation binding.");
    }
    private static Dictionary<string, object?> Parameters(Binding binding, params (string Name, object? Value)[] fields)
    {
        var p = new Dictionary<string, object?> { ["KnowledgeBaseId"] = binding.KnowledgeBaseId, ["DocumentId"] = binding.DocumentId,
            ["VersionId"] = binding.VersionId, ["OwnerUserId"] = binding.OwnerUserId, ["DocumentVersion"] = binding.DocumentVersion };
        foreach (var (name, value) in fields) p.Add(name, value); return p;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    // 双库统一到 MySQL 微秒精度，同一操作的首次响应与持久重放保持相同时间。
    private DateTimeOffset Now() { var value = clock.UtcNow.ToUniversalTime(); return value.AddTicks(-(value.Ticks % 10)); }
    private static Result<T> Conflict<T>() => Result<T>.Failure(new Error("ai.knowledge.correction_conflict", "The knowledge correction changed or is unavailable.", ErrorType.Conflict));
}
