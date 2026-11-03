using System.Security.Cryptography;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>完整人工计划及其原会话；没有模型调用、费用结算、向量齐全或激活证明。</summary>
internal sealed record AiKnowledgeCorrectionIndexPreparation(AiKnowledgeIndexDispatchRecord Source,
    SessionBindingSnapshot Session, AiKnowledgeCorrectionIndexPlan Plan, string ManifestHash);

/// <summary>将实际持租约任务的冻结修订接到完整计划和不可替换清单；恢复不打开原文件、不重新解析或收费。</summary>
internal sealed class AiKnowledgeCorrectionIndexService(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    IDataTransactionState transactions, ICurrentTenant tenant, IClock clock, IOptions<DatabaseOptions> database,
    AiKnowledgeIndexTaskStore tasks, AiKnowledgeIndexSessionService sessions, IBackgroundSessionAuthorization authorization,
    AiKnowledgeCorrectionAuthority authority, AiKnowledgeCorrectionStore corrections)
{
    private static readonly string[] Permissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
        AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.IndexEnqueue];

    /// <summary>先完成当前会话、Files、批准模型和冻结载荷验收，才写一份全量清单；没有部分计划。</summary>
    internal async Task<AiKnowledgeCorrectionIndexPreparation?> PrepareAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
    {
        Check(token); using var deadline = Deadline(token); var bounded = deadline.Token;
        var state = await CurrentAsync(binding, taskId, leaseToken, bounded).ConfigureAwait(false);
        if (state is null) return null;
        var snapshot = await FrozenAsync(state, bounded).ConfigureAwait(false);
        if (snapshot is null || !Same(state, await CurrentAsync(binding, taskId, leaseToken, bounded).ConfigureAwait(false))) return null;
        var plan = AiKnowledgeCorrectionIndexPlan.Create(snapshot, state.ModelBinding, bounded);
        if (plan is null) return null;
        var bytes = AiKnowledgeCorrectionManifestCodec.Encode(plan, bounded);
        try
        {
            if (!Same(state, await CurrentAsync(binding, taskId, leaseToken, bounded).ConfigureAwait(false))) return null;
            var p = Parameters(binding, state, bounded); p["FormatKey"] = AiKnowledgeCorrectionManifestCodec.FormatKey;
            p["PayloadHash"] = Convert.ToHexString(SHA256.HashData(bytes)); p["Payload"] = bytes;
            try
            {
                var inserted = await transaction.ExecuteAsync(async ct =>
                {
                    // 与校对保存和重建沿同一文档根锁，锁内不执行 Identity、Files、解析或词表操作。
                    p["Now"] = clock.UtcNow;
                    if (state.Source.LeaseExpiresAtUtc <= clock.UtcNow
                        || await command.ExecuteAsync(tasks.IsTenant ? AiKnowledgeIndexCorrectionSql.LockTenant : AiKnowledgeIndexCorrectionSql.LockHost, p, ct).ConfigureAwait(false) != 1) return false;
                    p["Now"] = clock.UtcNow;
                    var count = await command.ExecuteAsync(tasks.IsTenant ? AiKnowledgeCorrectionManifestSql.InsertTenant : AiKnowledgeCorrectionManifestSql.InsertHost, p, ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    if (count == 1 && state.Source.LeaseExpiresAtUtc <= clock.UtcNow) throw new ExpiredDuringWriteException();
                    return count is 0 or 1 ? count == 1 : throw new InvalidOperationException("Unexpected correction manifest write count.");
                }, bounded).ConfigureAwait(false);
                if (!inserted) return null;
            }
            catch (ExpiredDuringWriteException) { return null; }
            catch (DataCommandException error) when (error.Kind == DataCommandFailureKind.UniqueConstraint)
            { /* 唯一竞争回滚后只重读同一完整载荷，不能 UPDATE 胜者或借机采用新修订。 */ }
            var restored = await ReadCoreAsync(binding, taskId, leaseToken, bounded).ConfigureAwait(false);
            return restored?.Plan.PlanHash == plan.PlanHash && restored.Session == state.Session ? restored : null;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    /// <summary>恢复完整人工清单；丢失、损坏、撤权、较新修订或目的地漂移均拒绝，不重解析或回退原文。</summary>
    internal async Task<AiKnowledgeCorrectionIndexPreparation?> ReadAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
    { Check(token); using var deadline = Deadline(token); return await ReadCoreAsync(binding, taskId, leaseToken, deadline.Token).ConfigureAwait(false); }

    /// <summary>同次完整清单与末次权威复核返回计划和模型；仅供本次读取前后比较，不跨调用缓存或授予模型派发资格。</summary>
    internal async Task<CurrentPlan?> ReadWithCurrentModelAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
    {
        Check(token);
        using var deadline = Deadline(token);
        return await ReadProvenCoreAsync(binding, taskId, leaseToken, deadline.Token).ConfigureAwait(false);
    }

    /// <summary>后续模型派发前仍须核证此计划；返回配置不是持久外发授权。</summary>
    internal async Task<AiModelConfigRecord?> ReadCurrentModelAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken,
        AiKnowledgeCorrectionIndexPreparation expected, CancellationToken token)
    {
        Check(token); ArgumentNullException.ThrowIfNull(expected); using var deadline = Deadline(token);
        // 内存中的有效计划不能代替已落库清单；载荷丢失或损坏后不得继续取得收费模型。
        var proven = await ReadProvenCoreAsync(binding, taskId, leaseToken, deadline.Token).ConfigureAwait(false);
        if (proven is null) return null;
        var manifest = proven.Preparation;
        if (manifest.Session != expected.Session || manifest.Plan.PlanHash != expected.Plan.PlanHash || manifest.ManifestHash != expected.ManifestHash
            || manifest.Plan.Model != expected.Plan.Model || manifest.Plan.Snapshot.ParserRuntimeId != expected.Plan.Snapshot.ParserRuntimeId
            || !AiKnowledgeIndexEmbeddingService.SameSource(manifest.Source, expected.Source)) return null;
        // 本次完整载荷证明已经以末次 CurrentAsync 结束；直接交付同次模型，不另起重复链或采用 expected 中的旧模型。
        // 后续派发仍执行原有独立复核；本快照不跨调用缓存，也不放宽阶段期限。
        return proven.Model;
    }

    private async Task<AiKnowledgeCorrectionIndexPreparation?> ReadCoreAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token) =>
        (await ReadProvenCoreAsync(binding, taskId, leaseToken, token).ConfigureAwait(false))?.Preparation;

    private Task<CurrentPlan?> ReadProvenCoreAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token) =>
        AiKnowledgeTelemetry.RunAsync("knowledge_correction_proof", () => ReadProvenPlanAsync(binding, taskId, leaseToken, token), token);

    private async Task<CurrentPlan?> ReadProvenPlanAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
    {
        var state = await CurrentAsync(binding, taskId, leaseToken, token).ConfigureAwait(false); if (state is null) return null;
        var row = await Bound(ct => query.QuerySingleOrDefaultAsync<AiKnowledgeIndexTextManifestRecord>(AiKnowledgeCorrectionManifestSql.Find(database.Value.Provider, tasks.IsTenant),
            Parameters(binding, state, ct), ct), token).ConfigureAwait(false);
        if (row is null || row.Id != taskId || row.FormatKey != AiKnowledgeCorrectionManifestCodec.FormatKey
            || row.Payload is not { Length: >= 8 and <= AiKnowledgeCorrectionManifestCodec.MaxPayloadBytes }) return null;
        var owned = row.Payload.ToArray();
        try
        {
            if (Convert.ToHexString(SHA256.HashData(owned)) != row.PayloadHash) return null;
            var version = state.Context.Version; AiKnowledgeCorrectionIndexPlan plan;
            try { plan = AiKnowledgeCorrectionManifestCodec.Decode(owned, AiKnowledgeDocumentFormats.Format(version.ContentType)!.Value,
                version.ContentHash, checked((int)version.SizeBytes), AiKnowledgeDocumentFormats.Policy(version.ContentType, version.ParsingPolicyKey)!, token); }
            catch (InvalidDataException) { return null; }
            if (plan.Model != state.ModelBinding || plan.Snapshot.Identity != AiKnowledgeIndexCorrectionInput.Identity(state.Source)) return null;
            var frozen = await FrozenAsync(state, token).ConfigureAwait(false);
            if (frozen is null || frozen.ParserRuntimeId != plan.Snapshot.ParserRuntimeId) return null;
            return Same(state, await CurrentAsync(binding, taskId, leaseToken, token).ConfigureAwait(false))
                ? new(new(state.Source, state.Session, plan, row.PayloadHash), state.Model) : null;
        }
        finally { CryptographicOperations.ZeroMemory(owned); }
    }

    private async Task<AiKnowledgeCorrectionSnapshot?> FrozenAsync(State state, CancellationToken token)
    {
        var v = state.Context.Version;
        return await Bound(ct => corrections.ReadFrozenAsync(state.Context.Binding, AiKnowledgeIndexCorrectionInput.Identity(state.Source)!,
            AiKnowledgeDocumentFormats.Format(v.ContentType)!.Value, v.ContentHash, checked((int)v.SizeBytes),
            AiKnowledgeDocumentFormats.Policy(v.ContentType, v.ParsingPolicyKey)!, ct), token).ConfigureAwait(false);
    }

    private Task<State?> CurrentAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token) =>
        AiKnowledgeTelemetry.RunAsync("knowledge_correction_current", () => ReadCurrentStateAsync(binding, taskId, leaseToken, token), token);

    private async Task<State?> ReadCurrentStateAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
    {
        Check(token); _ = tasks.HeldParameters(binding, taskId, leaseToken, token);
        var source = await Bound(ct => tasks.ReadDispatchAsync(binding, taskId, leaseToken, ct), token).ConfigureAwait(false);
        if (source is null || AiKnowledgeIndexCorrectionInput.Identity(source) is null) return null;
        var session = await Bound(ct => sessions.ReadHeldAsync(binding, taskId, leaseToken, ct), token).ConfigureAwait(false);
        if (session is null || session.UserId != binding.OwnerUserId || session.TenantId != tenant.Id) return null;
        var actor = await Bound(ct => authorization.AuthorizeAllAsync(session, Permissions, ct), token).ConfigureAwait(false);
        if (actor?.UserId != session.UserId || actor.TenantId != session.TenantId || actor.SessionId != session.SessionId) return null;
        var context = await AiKnowledgeTelemetry.RunAsync("knowledge_correction_authority",
            () => Bound(ct => authority.ReadAsync(binding.KnowledgeBaseId, binding.DocumentId, binding.VersionId, session,
                AiKnowledgeCorrectionPermissions.Read, ct, binding.DocumentVersion), token), token).ConfigureAwait(false);
        if (context is null) return null;
        var p = new Dictionary<string, object?> { ["Id"] = binding.KnowledgeBaseId, ["OwnerUserId"] = binding.OwnerUserId, ["ModelConfigId"] = source.ModelConfigId };
        var knowledge = await Bound(ct => query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(tasks.IsTenant ? AiKnowledgeSql.FindTenant : AiKnowledgeSql.FindHost, p, ct), token).ConfigureAwait(false);
        var model = await Bound(ct => query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(tasks.IsTenant ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat, p, ct), token).ConfigureAwait(false);
        if (knowledge is null || model is null || knowledge.Id != binding.KnowledgeBaseId || knowledge.OwnerUserId != binding.OwnerUserId
            || knowledge.Version != binding.KnowledgeBaseVersion || knowledge.TenantId != tenant.Id || model.TenantId == Guid.Empty
            || model.TenantId.HasValue && model.TenantId != tenant.Id || model.Id != source.ModelConfigId || model.Version != source.ModelConfigVersion
            || AiKnowledgeCorrectionModelBinding.Capture(knowledge, model) is not { Valid: true } modelBinding) return null;
        if (await Bound(ct => sessions.ReadHeldAsync(binding, taskId, leaseToken, ct), token).ConfigureAwait(false) != session
            || !await Bound(ct => authority.StillCurrentAsync(context, session, AiKnowledgeCorrectionPermissions.Read, ct), token).ConfigureAwait(false)) return null;
        var final = await Bound(ct => tasks.ReadDispatchAsync(binding, taskId, leaseToken, ct), token).ConfigureAwait(false);
        if (final is null || !AiKnowledgeIndexEmbeddingService.SameSource(source, final) || final.DataClassification != modelBinding.DataClassification
            || final.ModelProviderKey != model.ProviderKey || final.ModelId != model.ModelId || final.ModelEndpointBaseUrl != model.EndpointBaseUrl
            || final.ModelOrganizationId != model.OrganizationId) return null;
        return new(final, session, context, model, modelBinding);
    }

    private Dictionary<string, object?> Parameters(AiKnowledgeIndexTaskBinding binding, State state, CancellationToken token)
    {
        var p = tasks.HeldParameters(binding, state.Source.TaskId, state.Source.LeaseToken, token); var id = AiKnowledgeIndexCorrectionInput.Identity(state.Source)!;
        p["SessionId"] = state.Session.SessionId; p["SessionKind"] = state.Session.SessionKind; p["SecurityStamp"] = state.Session.SecurityStamp;
        p["ActorScope"] = state.Session.ActorScope; p["EffectiveScope"] = state.Session.EffectiveScope;
        p["CorrectionSourceId"] = id.SourceId; p["CorrectionRevisionId"] = id.RevisionId; p["CorrectionHeadVersion"] = id.HeadVersion;
        p["CorrectionSourceHash"] = id.SourceHash; p["CorrectionPayloadHash"] = id.PayloadHash; return p;
    }
    private static bool Same(State expected, State? current) => current is not null && current.Session == expected.Session
        && current.Context.Binding == expected.Context.Binding && current.ModelBinding == expected.ModelBinding
        && AiKnowledgeIndexEmbeddingService.SameSource(expected.Source, current.Source);
    private async Task<T> Bound<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
    { Check(token); var value = await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(action, token).ConfigureAwait(false); Check(token); return value; }
    private void Check(CancellationToken token)
    { token.ThrowIfCancellationRequested(); if (transactions.HasTransaction) throw new InvalidOperationException("Correction indexing requires a transaction-free boundary."); }
    private static CancellationTokenSource Deadline(CancellationToken token)
    { var source = CancellationTokenSource.CreateLinkedTokenSource(token); source.CancelAfter(TimeSpan.FromSeconds(25)); return source; }
    private sealed record State(AiKnowledgeIndexDispatchRecord Source, SessionBindingSnapshot Session,
        AiKnowledgeCorrectionContext Context, AiModelConfigRecord Model, AiKnowledgeCorrectionModelBinding ModelBinding);
    /// <summary>只在一次完整清单证明内携带模型，不进入持久 DTO、日志或长期授权缓存。</summary>
    internal sealed record CurrentPlan(AiKnowledgeCorrectionIndexPreparation Preparation, AiModelConfigRecord Model);
    private sealed class ExpiredDuringWriteException : Exception;
}
