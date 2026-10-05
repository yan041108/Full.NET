using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using System.Security.Cryptography;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>冻结完整预期集合；本地事务不调用其他模块，恢复必须复核原会话和有效来源。</summary>
internal sealed class AiKnowledgeIndexTextManifestStore(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    IDataTransactionState transactions, ICurrentTenant tenant, IClock clock, AiKnowledgeIndexTaskStore tasks,
    AiKnowledgeIndexSessionService sessions, IBackgroundSessionAuthorization authorization, IOptions<DatabaseOptions>? database = null)
{
    private static readonly string[] Permissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
        AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.IndexEnqueue];

    /// <summary>一个任务一次完整写入；唯一竞争只复用同载荷，绝不 UPDATE 或覆盖原清单。</summary>
    internal async Task<bool> PersistAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, AiKnowledgeIndexTextPlan plan, CancellationToken token)
    {
        NoTransaction(); _ = tasks.HeldParameters(binding, taskId, leaseToken, token);
        var bytes = AiKnowledgeIndexTextManifestCodec.Encode(plan, token); var frozen = AiKnowledgeIndexTextManifestCodec.Decode(bytes, token);
        var state = await CurrentAsync(binding, taskId, leaseToken, null, null, token).ConfigureAwait(false);
        if (state is null || !Matches(frozen.Identity, binding, state)) return false;
        var parameters = Parameters(binding, state, token); var hash = Convert.ToHexString(SHA256.HashData(bytes));
        parameters["FormatKey"] = AiKnowledgeIndexTextManifestCodec.FormatKey; parameters["PayloadHash"] = hash; parameters["Payload"] = bytes;
        try
        {
            if (!await transaction.ExecuteAsync(async cancellation =>
            {
                cancellation.ThrowIfCancellationRequested(); parameters["Now"] = clock.UtcNow;
                if (state.Source.LeaseExpiresAtUtc <= clock.UtcNow) return false;
                var statement = database?.Value.Provider == DatabaseProvider.MySql
                    ? (tasks.IsTenant ? AiKnowledgeIndexTextManifestSql.InsertTenant : AiKnowledgeIndexTextManifestSql.InsertHost)
                    : (tasks.IsTenant ? AiKnowledgeIndexTextManifestSql.InsertSqlServerTenant : AiKnowledgeIndexTextManifestSql.InsertSqlServerHost);
                var count = await command.ExecuteAsync(statement,
                    parameters, cancellation).ConfigureAwait(false);
                // 在回调内观察等待后的取消和租约失效；仅返回 false 不会回滚已插入行。
                cancellation.ThrowIfCancellationRequested();
                if (count == 1 && state.Source.LeaseExpiresAtUtc <= clock.UtcNow) throw new LeaseExpiredDuringWriteException();
                return count is 0 or 1 ? count == 1 : throw new InvalidOperationException("Unexpected manifest write count.");
            }, token).ConfigureAwait(false)) return false;
        }
        catch (LeaseExpiredDuringWriteException) { return false; }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            // 事务回滚后才重读竞争胜者；其他故障传播，不能假装幂等成功。
        }
        var stored = await ReadRowAsync(binding, taskId, leaseToken, state.Source, state.Session, token).ConfigureAwait(false);
        return stored is not null && stored.Value.Row.PayloadHash == hash;
    }

    /// <summary>只读取仍具当前原会话授权的完整清单；清单存在不证明向量齐全。</summary>
    internal async Task<AiKnowledgeIndexTextManifest?> ReadAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
        => (await ReadSnapshotAsync(binding, taskId, leaseToken, token).ConfigureAwait(false))?.Manifest;

    /// <summary>读取有当前原会话授权的原始清单摘要；用于完整代次提交前后比对。</summary>
    internal async Task<AiKnowledgeIndexTextManifestSnapshot?> ReadSnapshotAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
    {
        NoTransaction(); _ = tasks.HeldParameters(binding, taskId, leaseToken, token);
        var row = await ReadRowAsync(binding, taskId, leaseToken, null, null, token).ConfigureAwait(false);
        return row is not null ? new(row.Value.Manifest, row.Value.Row.PayloadHash) : null;
    }

    private async Task<(AiKnowledgeIndexTextManifestRecord Row, AiKnowledgeIndexTextManifest Manifest)?> ReadRowAsync(
        AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, AiKnowledgeIndexDispatchRecord? expectedSource,
        SessionBindingSnapshot? expectedSession, CancellationToken token)
    {
        var state = await CurrentAsync(binding, taskId, leaseToken, expectedSource, expectedSession, token).ConfigureAwait(false);
        if (state is null) return null;
        var row = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexTextManifestRecord>(tasks.IsTenant ? AiKnowledgeIndexTextManifestSql.FindTenant : AiKnowledgeIndexTextManifestSql.FindHost,
            Parameters(binding, state, token), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); NoTransaction();
        if (row is null || row.Id != taskId || row.FormatKey != AiKnowledgeIndexTextManifestCodec.FormatKey
            || row.Payload.Length > AiKnowledgeIndexTextManifestCodec.MaxPayloadBytes || row.PayloadHash != Convert.ToHexString(SHA256.HashData(row.Payload))) return null;
        AiKnowledgeIndexTextManifest manifest;
        try { manifest = AiKnowledgeIndexTextManifestCodec.Decode(row.Payload, token); }
        catch (InvalidDataException) { return null; }
        if (!Matches(manifest.Identity, binding, state)) return null;
        var final = await CurrentAsync(binding, taskId, leaseToken, state.Source, state.Session, token).ConfigureAwait(false);
        return final is not null && Matches(manifest.Identity, binding, final) ? (row, manifest) : null;
    }

    private async Task<State?> CurrentAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken,
        AiKnowledgeIndexDispatchRecord? expectedSource, SessionBindingSnapshot? expectedSession, CancellationToken token)
    {
        NoTransaction(); var source = await tasks.ReadDispatchAsync(binding, taskId, leaseToken, token).ConfigureAwait(false);
        if (source is null || (expectedSource is not null && !AiKnowledgeIndexEmbeddingService.SameSource(expectedSource, source))) return null;
        var session = await sessions.ReadHeldAsync(binding, taskId, leaseToken, token).ConfigureAwait(false);
        if (session is null || (expectedSession is not null && expectedSession != session)) return null;
        var model = await query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(tasks.IsTenant ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat,
            AiSqlParameters.Create(("ModelConfigId", source.ModelConfigId)), token).ConfigureAwait(false);
        if (model is null || !model.IsEnabled || model.Id != source.ModelConfigId || model.Version != source.ModelConfigVersion
            || model.TenantId == Guid.Empty || (model.TenantId.HasValue && model.TenantId != tenant.Id)
            || model.ProviderKey != "openai_compatible" || model.ModelId is not ("text-embedding-3-small" or "text-embedding-3-large")) return null;
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
    private bool Matches(AiKnowledgeIndexTextPlanIdentity identity, AiKnowledgeIndexTaskBinding binding, State state) =>
        identity.KnowledgeBaseId == binding.KnowledgeBaseId && identity.KnowledgeBaseVersion == state.Source.KnowledgeBaseVersion
        && identity.TenantId == tenant.Id && identity.ModelConfigId == state.Model.Id && identity.ModelConfigVersion == state.Model.Version
        && identity.ProviderKey == state.Model.ProviderKey && identity.ModelId == state.Model.ModelId && identity.ContentHash == state.Source.ContentHash
        && (identity.Format == AiKnowledgeTextFormat.PlainText ? state.Source.ContentType == "text/plain" : state.Source.ContentType == "text/markdown");
    private Dictionary<string, object?> Parameters(AiKnowledgeIndexTaskBinding binding, State state, CancellationToken token)
    {
        var p = tasks.HeldParameters(binding, state.Source.TaskId, state.Source.LeaseToken, token);
        p["SessionId"] = state.Session.SessionId; p["SessionKind"] = state.Session.SessionKind; p["SecurityStamp"] = state.Session.SecurityStamp;
        p["ActorScope"] = state.Session.ActorScope; p["EffectiveScope"] = state.Session.EffectiveScope;
        p["FileId"] = state.Source.FileId; p["ContentHash"] = state.Source.ContentHash; p["ContentType"] = state.Source.ContentType; p["SizeBytes"] = state.Source.SizeBytes;
        return p;
    }
    private void NoTransaction()
    { if (transactions.HasTransaction) throw new InvalidOperationException("Manifest persistence cannot join an ambient transaction."); }
    private sealed record State(AiKnowledgeIndexDispatchRecord Source, SessionBindingSnapshot Session, AiModelConfigRecord Model);
    private sealed class LeaseExpiredDuringWriteException : Exception;
}
