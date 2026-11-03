using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Calendar.Contracts;
using Full.NET.Modules.Calendar.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Calendar.Features.Completion;

/// <summary>原会话核证在事务外；本地根锁、pending CAS和原操作回执原子提交。</summary>
internal sealed class PersonalScheduleCompletion(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    IDataTransactionState transactions, ICurrentTenant tenant, IBackgroundSessionAuthorization authorization,
    IOptions<DatabaseOptions> database, IIdGenerator ids, IClock clock) : IPersonalScheduleCompletion
{
    public async Task<bool> CanCompleteAsync(Guid scheduleId, int expectedVersion, SessionBindingSnapshot original, CancellationToken cancellationToken = default)
    {
        using var phase = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); phase.CancelAfter(TimeSpan.FromSeconds(25)); cancellationToken = phase.Token;
        Outside();
        if (scheduleId == Guid.Empty || expectedVersion is < 1 or int.MaxValue || !Valid(original)
            || !await AuthorizedAsync(original, cancellationToken).ConfigureAwait(false)) return false;
        var version = await query.QuerySingleOrDefaultAsync<int>(PersonalScheduleCompletionSql.Read,
            Parameters(original, ("ScheduleId", scheduleId)), cancellationToken).ConfigureAwait(false);
        if (version != expectedVersion || !await AuthorizedAsync(original, cancellationToken).ConfigureAwait(false)) return false;
        // 授权等待可能改变目标；只读资格仍绑定最后准确版本，真正写入还需本地CAS。
        return version == await query.QuerySingleOrDefaultAsync<int>(PersonalScheduleCompletionSql.Read,
            Parameters(original, ("ScheduleId", scheduleId)), cancellationToken).ConfigureAwait(false) && Valid(original);
    }

    public async Task<PersonalScheduleCompletionReceipt?> FindReceiptAsync(PersonalScheduleCompletionRequest request, CancellationToken cancellationToken = default)
    {
        using var phase = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); phase.CancelAfter(TimeSpan.FromSeconds(25)); cancellationToken = phase.Token;
        Outside(); Validate(request);
        var found = await query.QuerySingleOrDefaultAsync<PersonalScheduleCompletionRecord>(PersonalScheduleCompletionSql.Find,
            Parameters(request.Original, ("OperationId", request.OperationId)), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Valid(request.Original)) throw new InvalidOperationException("日程回执范围已改变。");
        return Match(found, request);
    }

    public async Task<PersonalScheduleCompletionReceipt> ReceiveAsync(PersonalScheduleCompletionRequest request, CancellationToken cancellationToken = default)
    {
        using var phase = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); phase.CancelAfter(TimeSpan.FromSeconds(25)); cancellationToken = phase.Token;
        Outside(); Validate(request);
        var receipt = await FindReceiptAsync(request, cancellationToken).ConfigureAwait(false);
        if (receipt is not null) return receipt;
        // 已存在的根等待先在短事务内排空，再在事务外核证原会话；不持有Calendar锁调用Identity。
        await transaction.ExecuteAsync(ct => query.QuerySingleOrDefaultAsync<int>(database.Value.Provider == DatabaseProvider.SqlServer
            ? PersonalScheduleCompletionSql.LockSqlServer : PersonalScheduleCompletionSql.LockMySql,
            Parameters(request.Original, ("ScheduleId", request.ScheduleId)), ct), cancellationToken).ConfigureAwait(false);
        // 原事实可对账不等于旧会话能执行；权限失败保存明确拒绝，不借新授权重写旧意图。
        var authorized = await AuthorizedAsync(request.Original, cancellationToken).ConfigureAwait(false);
        var reason = !authorized ? "authorization_unavailable" : request.ExpiresAtUtc <= clock.UtcNow ? "intent_expired" : null;
        var parameters = Parameters(request.Original, ("OperationId", request.OperationId), ("ScheduleId", request.ScheduleId));
        var result = await transaction.ExecuteAsync(async token =>
        {
            var current = await query.QuerySingleOrDefaultAsync<int>(database.Value.Provider == DatabaseProvider.SqlServer
                ? PersonalScheduleCompletionSql.LockSqlServer : PersonalScheduleCompletionSql.LockMySql, parameters, token).ConfigureAwait(false);
            Validate(request);
            var existing = await query.QuerySingleOrDefaultAsync<PersonalScheduleCompletionRecord>(PersonalScheduleCompletionSql.Find, parameters, token).ConfigureAwait(false);
            Validate(request);
            if (Match(existing, request) is { } replay) return replay;
            var now = clock.UtcNow.ToUniversalTime(); now = now.AddTicks(-(now.Ticks % 10));
            var rejection = reason ?? (request.ExpiresAtUtc <= now ? "intent_expired" : current != request.ExpectedVersion ? "target_changed" : null);
            parameters["ExpectedVersion"] = request.ExpectedVersion; parameters["ActualVersion"] = request.ExpectedVersion + 1; parameters["Now"] = now;
            if (rejection is null && await command.ExecuteAsync(PersonalScheduleCompletionSql.Complete, parameters, token).ConfigureAwait(false) != 1)
                rejection = "target_changed";
            var completed = rejection is null;
            parameters["Id"] = ids.NewId(); parameters["RunId"] = request.RunId; parameters["RequestDigest"] = request.Digest();
            parameters["Outcome"] = completed ? "completed" : "rejected"; parameters["Reason"] = rejection;
            parameters["ActualVersion"] = completed ? request.ExpectedVersion + 1 : null;
            parameters["CompletedAtUtc"] = completed ? now : null;
            if (await command.ExecuteAsync(PersonalScheduleCompletionSql.Insert, parameters, token).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("日程原操作回执未写入。");
            token.ThrowIfCancellationRequested();
            if (!Valid(request.Original)) throw new InvalidOperationException("日程完成范围已改变。");
            return new PersonalScheduleCompletionReceipt(request.OperationId, request.Digest(), completed ? "completed" : "rejected",
                rejection, completed ? request.ExpectedVersion + 1 : null, completed ? now : null);
        }, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested(); Validate(request); return result;
    }

    private async Task<bool> AuthorizedAsync(SessionBindingSnapshot original, CancellationToken token)
    {
        Outside(); token.ThrowIfCancellationRequested(); if (!Valid(original)) return false;
        var actor = await authorization.AuthorizeAllAsync(original,
            [CalendarPermissions.Read, CalendarPermissions.SetStatus, PersonalScheduleCompletionKeys.Permission], token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return Valid(original) && actor?.UserId == original.UserId && actor.SessionId == original.SessionId && actor.TenantId == original.TenantId;
    }
    private bool Valid(SessionBindingSnapshot original) => original is not null && original.UserId != Guid.Empty && original.SessionId != Guid.Empty
        && original.TenantId != Guid.Empty && original.TenantId == tenant.Id
        && (original.TenantId is null ? tenant.IsHost : tenant.IsAvailable && !tenant.IsHost)
        && !string.IsNullOrWhiteSpace(original.SecurityStamp) && original.SessionKind is SessionBindingKinds.Refresh or SessionBindingKinds.OidcApplication;
    private void Validate(PersonalScheduleCompletionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Valid(request.Original) || request.RunId == Guid.Empty || request.OperationId == Guid.Empty || request.ScheduleId == Guid.Empty
            || request.ExpectedVersion is < 1 or int.MaxValue || request.ApproverId == Guid.Empty || request.ApproverId == request.Original.UserId
            || request.ExpiresAtUtc.Offset != TimeSpan.Zero) throw new InvalidOperationException("日程原操作身份、独立批准或范围无效。");
    }
    private static PersonalScheduleCompletionReceipt? Match(PersonalScheduleCompletionRecord? row, PersonalScheduleCompletionRequest request)
    {
        if (row is null) return null;
        if (row.RequestDigest != request.Digest()) throw new InvalidOperationException("同一日程操作不能更换原请求参数。");
        var receipt = new PersonalScheduleCompletionReceipt(row.OperationId, row.RequestDigest, row.Outcome, row.Reason, row.ActualVersion, row.CompletedAtUtc);
        return receipt.IsFor(request) ? receipt : throw new InvalidOperationException("原操作回执组合无效。");
    }
    private Dictionary<string, object?> Parameters(SessionBindingSnapshot original, params (string Name, object? Value)[] values)
    {
        var parameters = CalendarSqlParameters.Create(values); parameters["OwnerUserId"] = original.UserId;
        parameters["ScopeTenantId"] = original.TenantId; parameters["ScopeKey"] = original.TenantId?.ToString("N") ?? "host"; return parameters;
    }
    private void Outside() { if (transactions.HasTransaction) throw new InvalidOperationException("日程完成Contract不能加入调用方事务。"); }
}
