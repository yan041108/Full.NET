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

/// <summary>DOCX 原字节摘要不能代替权威授权，完整清单不证明向量齐全。</summary>
internal sealed record AiKnowledgeIndexDocxManifestSnapshot(AiKnowledgeIndexDocxPlan Manifest, string PayloadHash);

/// <summary>DOCX 专属任务清单；独立验证完整原文及逻辑坐标，不改变文本和 PDF 入口。</summary>
internal sealed class AiKnowledgeIndexDocxManifestStore(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    IDataTransactionState transactions, ICurrentTenant tenant, IClock clock, AiKnowledgeIndexTaskStore tasks,
    AiKnowledgeIndexSessionService sessions, IBackgroundSessionAuthorization authorization, IOptions<DatabaseOptions>? database = null)
{
    private static readonly string[] Permissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
        AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.IndexEnqueue];

    /// <summary>一个任务一次完整写入；唯一竞争只复用同载荷，绝不 UPDATE 或覆盖原清单。</summary>
    internal async Task<bool> PersistAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, AiKnowledgeIndexDocxPlan plan, CancellationToken token)
    {
        NoTransaction(); _ = tasks.HeldParameters(binding, taskId, leaseToken, token);
        var bytes = AiKnowledgeIndexDocxManifestCodec.Encode(plan, token); var frozen = AiKnowledgeIndexDocxManifestCodec.Decode(bytes, token);
        var state = await CurrentAsync(binding, taskId, leaseToken, null, null, token).ConfigureAwait(false);
        if (state is null || !Matches(frozen.Source, binding, state)) return false;
        var parameters = Parameters(binding, state, token); var hash = Convert.ToHexString(SHA256.HashData(bytes));
        parameters["FormatKey"] = AiKnowledgeIndexDocxManifestCodec.FormatKey; parameters["PayloadHash"] = hash; parameters["Payload"] = bytes;
        try
        {
            if (!await transaction.ExecuteAsync(async cancellation =>
            {
                cancellation.ThrowIfCancellationRequested(); parameters["Now"] = clock.UtcNow;
                if (state.Source.LeaseExpiresAtUtc <= clock.UtcNow) return false;
                var statement = database?.Value.Provider == DatabaseProvider.MySql
                    ? (tasks.IsTenant ? AiKnowledgeIndexDocxManifestSql.InsertTenant : AiKnowledgeIndexDocxManifestSql.InsertHost)
                    : (tasks.IsTenant ? AiKnowledgeIndexDocxManifestSql.InsertSqlServerTenant : AiKnowledgeIndexDocxManifestSql.InsertSqlServerHost);
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
    internal async Task<AiKnowledgeIndexDocxPlan?> ReadAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
        => (await ReadSnapshotAsync(binding, taskId, leaseToken, token).ConfigureAwait(false))?.Manifest;

    /// <summary>读取有当前原会话授权的原始清单摘要；用于完整代次提交前后比对。</summary>
    internal async Task<AiKnowledgeIndexDocxManifestSnapshot?> ReadSnapshotAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
    {
        NoTransaction(); _ = tasks.HeldParameters(binding, taskId, leaseToken, token);
        var row = await ReadRowAsync(binding, taskId, leaseToken, null, null, token).ConfigureAwait(false);
        return row is not null ? new(row.Value.Manifest, row.Value.Row.PayloadHash) : null;
    }

    private async Task<(AiKnowledgeIndexTextManifestRecord Row, AiKnowledgeIndexDocxPlan Manifest)?> ReadRowAsync(
        AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, AiKnowledgeIndexDispatchRecord? expectedSource,
        SessionBindingSnapshot? expectedSession, CancellationToken token)
    {
        var state = await CurrentAsync(binding, taskId, leaseToken, expectedSource, expectedSession, token).ConfigureAwait(false);
        if (state is null) return null;
        var row = await ReadAuthorityAsync(ct => query.QuerySingleOrDefaultAsync<AiKnowledgeIndexTextManifestRecord>(tasks.IsTenant ? AiKnowledgeIndexDocxManifestSql.FindTenant : AiKnowledgeIndexDocxManifestSql.FindHost,
            Parameters(binding, state, token), ct), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); NoTransaction();
        if (row is null || row.Id != taskId || row.FormatKey != AiKnowledgeIndexDocxManifestCodec.FormatKey
            || row.Payload.Length is < 8 or > AiKnowledgeIndexDocxManifestCodec.MaxPayloadBytes) return null;
        // 执行器持有的数组先有界捕获；解码与摘要必须消费同一次读取的原字节。
        row = row with { Payload = row.Payload.ToArray() };
        if (row.PayloadHash != Convert.ToHexString(SHA256.HashData(row.Payload))) return null;
        AiKnowledgeIndexDocxPlan manifest;
        try { manifest = AiKnowledgeIndexDocxManifestCodec.Decode(row.Payload, token); }
        catch (InvalidDataException) { return null; }
        if (!Matches(manifest.Source, binding, state)) return null;
        var final = await CurrentAsync(binding, taskId, leaseToken, state.Source, state.Session, token).ConfigureAwait(false);
        return final is not null && Matches(manifest.Source, binding, final) ? (row, manifest) : null;
    }

    private async Task<State?> CurrentAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken,
        AiKnowledgeIndexDispatchRecord? expectedSource, SessionBindingSnapshot? expectedSession, CancellationToken token)
    {
        NoTransaction(); var source = await ReadAuthorityAsync(ct => tasks.ReadDispatchAsync(binding, taskId, leaseToken, ct), token).ConfigureAwait(false);
        if (source is null || (expectedSource is not null && !SameAuthoritySource(expectedSource, source))) return null;
        var session = await ReadAuthorityAsync(ct => sessions.ReadHeldAsync(binding, taskId, leaseToken, ct), token).ConfigureAwait(false);
        if (session is null || (expectedSession is not null && expectedSession != session)) return null;
        var model = await ReadAuthorityAsync(ct => query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(tasks.IsTenant ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat,
            AiSqlParameters.Create(("ModelConfigId", source.ModelConfigId)), ct), token).ConfigureAwait(false);
        if (model is null || !model.IsEnabled || model.Id != source.ModelConfigId || model.Version != source.ModelConfigVersion
            || model.TenantId == Guid.Empty || (model.TenantId.HasValue && model.TenantId != tenant.Id)
            || model.ProviderKey != "openai_compatible" || model.ModelId is not ("text-embedding-3-small" or "text-embedding-3-large")) return null;
        if (await ReadAuthorityAsync(ct => sessions.ReadHeldAsync(binding, taskId, leaseToken, ct), token).ConfigureAwait(false) != session) return null;
        foreach (var permission in Permissions)
        {
            token.ThrowIfCancellationRequested(); NoTransaction();
            var actor = await ReadAuthorityAsync(ct => authorization.AuthorizeAsync(session, permission, ct), token).ConfigureAwait(false);
            if (actor is null || actor.UserId != session.UserId || actor.TenantId != session.TenantId || actor.SessionId != session.SessionId) return null;
        }
        token.ThrowIfCancellationRequested(); NoTransaction();
        var final = await ReadAuthorityAsync(ct => tasks.ReadDispatchAsync(binding, taskId, leaseToken, ct), token).ConfigureAwait(false);
        return final is not null && SameAuthoritySource(source, final)
            && model.ProviderKey == final.ModelProviderKey && model.ModelId == final.ModelId
            && model.EndpointBaseUrl == final.ModelEndpointBaseUrl && model.OrganizationId == final.ModelOrganizationId
            ? new(final, session, model) : null;
    }
    // 最后权限或来源等待之后仍核对完整投影，版本号相同不能掩盖目的地和分类漂移。
    private static bool SameAuthoritySource(AiKnowledgeIndexDispatchRecord expected, AiKnowledgeIndexDispatchRecord current) =>
        AiKnowledgeIndexEmbeddingService.SameSource(expected, current)
        && expected.DataClassification == current.DataClassification && expected.ModelProviderKey == current.ModelProviderKey
        && expected.ModelId == current.ModelId && expected.ModelEndpointBaseUrl == current.ModelEndpointBaseUrl
        && expected.ModelOrganizationId == current.ModelOrganizationId;

    private bool Matches(AiKnowledgeDocxModelChunks identity, AiKnowledgeIndexTaskBinding binding, State state) =>
        identity.KnowledgeBaseId == binding.KnowledgeBaseId && identity.KnowledgeBaseVersion == state.Source.KnowledgeBaseVersion
        && identity.TenantId == tenant.Id && identity.ModelConfigId == state.Model.Id && identity.ModelConfigVersion == state.Model.Version
        && identity.ProviderKey == state.Model.ProviderKey && identity.ModelId == state.Model.ModelId
        // 上传保留大写十六进制，计划采用小写规范编码；摘要比较不改变权威来源快照。
        && string.Equals(identity.Document.ContentHash, state.Source.ContentHash, StringComparison.OrdinalIgnoreCase)
        && identity.Document.SizeBytes == state.Source.SizeBytes
        && state.Source.ContentType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private Dictionary<string, object?> Parameters(AiKnowledgeIndexTaskBinding binding, State state, CancellationToken token)
    {
        var p = tasks.HeldParameters(binding, state.Source.TaskId, state.Source.LeaseToken, token);
        p["SessionId"] = state.Session.SessionId; p["SessionKind"] = state.Session.SessionKind; p["SecurityStamp"] = state.Session.SecurityStamp;
        p["ActorScope"] = state.Session.ActorScope; p["EffectiveScope"] = state.Session.EffectiveScope;
        p["FileId"] = state.Source.FileId; p["ContentHash"] = state.Source.ContentHash; p["ContentType"] = state.Source.ContentType; p["SizeBytes"] = state.Source.SizeBytes;
        return p;
    }
    // 只隔离事务外读取；命令与写事务必须由原调用者等待，不能以超时放弃后仍在后台提交。
    private async Task<T> ReadAuthorityAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); NoTransaction();
        var result = await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(action, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); NoTransaction(); return result;
    }

    private void NoTransaction()
    { if (transactions.HasTransaction) throw new InvalidOperationException("Manifest persistence cannot join an ambient transaction."); }
    private sealed record State(AiKnowledgeIndexDispatchRecord Source, SessionBindingSnapshot Session, AiModelConfigRecord Model);
    private sealed class LeaseExpiredDuringWriteException : Exception;
}
