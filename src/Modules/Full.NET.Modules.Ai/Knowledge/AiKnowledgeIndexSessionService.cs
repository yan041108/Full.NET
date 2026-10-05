using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>任务与原会话原子冻结，恢复时不能由新的登录接管。</summary>
internal sealed class AiKnowledgeIndexSessionService(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    IDataTransactionState transactions, ICurrentTenant tenant, IClock clock, IIdGenerator ids, AiKnowledgeIndexTaskStore tasks,
    IBackgroundSessionAuthorization authorization)
{
    private static readonly string[] Permissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
        AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.Upload];

    /// <summary>只复用完全相同的冻结会话；无绑定的旧任务及其他登录均拒绝接管。</summary>
    internal async Task<Guid?> EnqueueAsync(AiKnowledgeIndexTaskBinding binding, SessionBindingSnapshot session, CancellationToken token)
    {
        NoTransaction(); ArgumentNullException.ThrowIfNull(session);
        var parameters = tasks.TaskParameters(binding, ids.NewId(), token);
        if (!Valid(binding, session) || !await AuthorizedAsync(binding, session, token).ConfigureAwait(false)) return null;
        parameters["SessionRecordId"] = ids.NewId();
        parameters["SessionId"] = session.SessionId; parameters["SessionKind"] = session.SessionKind;
        parameters["SecurityStamp"] = session.SecurityStamp; parameters["ActorScope"] = session.ActorScope;
        parameters["EffectiveScope"] = session.EffectiveScope; parameters["Now"] = clock.UtcNow;
        try
        {
            var created = await transaction.ExecuteAsync(async cancellation =>
            {
                // Identity 已在事务外授权；事务内只写 AI 自有任务及绑定。
                if (!Valid(binding, session)) return false;
                if (await command.ExecuteAsync(tasks.IsTenant ? AiKnowledgeIndexTaskSql.EnqueueTenant : AiKnowledgeIndexTaskSql.EnqueueHost, parameters, cancellation).ConfigureAwait(false) != 1)
                    return false;
                // 第一笔已写入，第二笔失败必须抛异常触发回滚，不能返回业务拒绝并提交孤立任务。
                if (await command.ExecuteAsync(tasks.IsTenant ? AiKnowledgeIndexSessionSql.InsertTenant : AiKnowledgeIndexSessionSql.InsertHost, parameters, cancellation).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("Knowledge index session binding was not created.");
                return true;
            }, token).ConfigureAwait(false);
            if (!created) return null;
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            // 唯一冲突由事务边界回滚；不补写或更新已有任务的原会话。
        }
        NoTransaction();
        if (!await AuthorizedAsync(binding, session, token).ConfigureAwait(false)) return null;
        var row = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(tasks.IsTenant ? AiKnowledgeIndexSessionSql.FindBindingTenant : AiKnowledgeIndexSessionSql.FindBindingHost, parameters, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); NoTransaction();
        return row is not null && row.Id != Guid.Empty && row.IndexTaskId != Guid.Empty && Snapshot(row) == session && Valid(binding, session) ? row.IndexTaskId : null;
    }

    /// <summary>读取当前租约的冻结身份；SQL 等待期间到期也拒绝，授权仍由 Identity Port 判定。</summary>
    internal async Task<SessionBindingSnapshot?> ReadHeldAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
    {
        NoTransaction();
        var row = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(tasks.IsTenant ? AiKnowledgeIndexSessionSql.HeldTenant : AiKnowledgeIndexSessionSql.HeldHost,
            tasks.HeldParameters(binding, taskId, leaseToken, token), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); NoTransaction();
        if (row is null || row.Id == Guid.Empty || row.IndexTaskId != taskId || row.LeaseExpiresAtUtc is not { } expiry || expiry <= clock.UtcNow) return null;
        var session = Snapshot(row); return Valid(binding, session) ? session : null;
    }

    /// <summary>整代次消费者的阶段门禁；续租只绑定原会话，授权不进入事务。</summary>
    internal async Task<bool> RenewHeldAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, SessionBindingSnapshot original, CancellationToken token)
    {
        NoTransaction(); ArgumentNullException.ThrowIfNull(original);
        if (await ReadHeldAsync(binding, taskId, leaseToken, token).ConfigureAwait(false) != original
            || !await AuthorizedAsync(binding, original, token).ConfigureAwait(false)) return false;
        var source = await tasks.ReadDispatchAsync(binding, taskId, leaseToken, token).ConfigureAwait(false);
        if (source is null) return false;
        // 每个派发阶段最多 25 秒；充足租约不重复写入，临界租约必须先成功续租。
        if (source.LeaseExpiresAtUtc <= clock.UtcNow.AddSeconds(25)
            && !await tasks.RenewAsync(binding, taskId, leaseToken, token).ConfigureAwait(false)) return false;
        token.ThrowIfCancellationRequested(); NoTransaction();
        if (await ReadHeldAsync(binding, taskId, leaseToken, token).ConfigureAwait(false) != original
            || !await AuthorizedAsync(binding, original, token).ConfigureAwait(false)) return false;
        var current = await tasks.ReadDispatchAsync(binding, taskId, leaseToken, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); NoTransaction();
        // 等待期间会话、来源和剩余时间都可能变化；续租成功不是下一阶段的永久授权。
        return current is not null && AiKnowledgeIndexEmbeddingService.SameSource(source, current)
            && current.LeaseExpiresAtUtc > clock.UtcNow.AddSeconds(25);
    }

    private async Task<bool> AuthorizedAsync(AiKnowledgeIndexTaskBinding binding, SessionBindingSnapshot session, CancellationToken token)
    {
        foreach (var permission in Permissions)
        {
            token.ThrowIfCancellationRequested(); NoTransaction(); if (!Valid(binding, session)) return false;
            var actor = await authorization.AuthorizeAsync(session, permission, token).ConfigureAwait(false);
            if (actor is null || actor.UserId != session.UserId || actor.TenantId != session.TenantId || actor.SessionId != session.SessionId) return false;
        }
        token.ThrowIfCancellationRequested(); NoTransaction(); return Valid(binding, session);
    }

    private bool Valid(AiKnowledgeIndexTaskBinding binding, SessionBindingSnapshot session) => session.UserId == binding.OwnerUserId
        && session.TenantId == tenant.Id && session.SessionId != Guid.Empty
        && session.SessionKind is SessionBindingKinds.Refresh or SessionBindingKinds.OidcApplication
        && Text(session.SecurityStamp, 128) && Text(session.ActorScope, 64) && Text(session.EffectiveScope, 64);
    private static bool Text(string value, int limit) => !string.IsNullOrWhiteSpace(value) && value.Length <= limit;
    private static SessionBindingSnapshot Snapshot(AiKnowledgeIndexSessionRecord row) => new(row.ActorUserId, row.TenantId, row.SessionId,
        row.SecurityStamp, row.ActorScope, row.EffectiveScope, row.SessionKind);
    private void NoTransaction()
    {
        if (transactions.HasTransaction) throw new InvalidOperationException("Knowledge index session binding cannot join an ambient transaction.");
    }
}
