using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Calendar.Contracts;
using Full.NET.Modules.Calendar.Persistence;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Calendar.Features.Knowledge;

/// <summary>当前会话、准确本人字段和完整批次的交集；拒绝跨模块事务和旧授权缓存。</summary>
internal sealed class PersonalScheduleKnowledgeSource(IQueryExecutor query, IDataTransactionState transactions,
    ICurrentTenant tenant, ICurrentSessionAuthorization? authorization = null, IBackgroundSessionAuthorization? background = null) : IPersonalScheduleKnowledgeSource
{
    public Task<IReadOnlyList<PersonalScheduleKnowledgeSnapshot>?> CaptureAsync(
        IReadOnlyCollection<Guid> scheduleIds, CancellationToken cancellationToken = default) => CaptureCoreAsync(scheduleIds, null, cancellationToken);

    private async Task<IReadOnlyList<PersonalScheduleKnowledgeSnapshot>?> CaptureCoreAsync(
        IReadOnlyCollection<Guid> scheduleIds, SessionBindingSnapshot? original, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scheduleIds);
        if (scheduleIds.Count is < 1 or > PersonalScheduleKnowledgeSourceKeys.MaxSources
            || scheduleIds.Contains(Guid.Empty) || scheduleIds.Distinct().Count() != scheduleIds.Count) return null;
        // 调用者可变集合必须先冻结，授权等待期间不可改变实际输入。
        var ids = scheduleIds.ToArray(); var scope = CalendarScope.Resolve(tenant);
        var actor = await AuthorizeAsync(scope, cancellationToken, original).ConfigureAwait(false);
        if (actor is null) return null;
        IReadOnlyList<PersonalScheduleKnowledgeSnapshot>? captured = null;
        for (var pass = 0; pass < 2; pass++)
        {
            RequireOutsideTransaction();
            if (!SameScope(scope)) return null;
            var rows = await query.QueryAsync<PersonalScheduleRecord>(
                scope.TenantId is null ? PersonalScheduleKnowledgeSql.ReadHost : PersonalScheduleKnowledgeSql.ReadTenant,
                CalendarSqlParameters.Create(("Ids", ids), ("OwnerUserId", actor.UserId)), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!SameScope(scope) || rows.Count != ids.Length || rows.Select(row => row.Id).Distinct().Count() != ids.Length
                || rows.Any(row => !ids.Contains(row.Id) || row.TenantId != scope.TenantId || row.OwnerUserId != actor.UserId)) return null;
            PersonalScheduleKnowledgeSnapshot[] current;
            try { current = ids.Select(id => PersonalScheduleKnowledgeProjection.Capture(rows.Single(row => row.Id == id))).ToArray(); }
            catch (Exception error) when (error is InvalidDataException or System.Text.EncoderFallbackException) { return null; }
            if (captured is not null && current.Where((snapshot, index) => snapshot.Proof != captured[index].Proof).Any()) return null;
            captured = Array.AsReadOnly(current);
            // 数据库等待中撤销权限或原会话必须拒绝；再一轮读取阻止授权等待中的版本变化。
            if (await AuthorizeAsync(scope, cancellationToken, original).ConfigureAwait(false) != actor) return null;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return captured;
    }

    public async Task<bool> VerifyAsync(IReadOnlyCollection<PersonalScheduleKnowledgeProof> proofs,
        CancellationToken cancellationToken = default) => await VerifyCoreAsync(proofs, null, cancellationToken).ConfigureAwait(false);

    public async Task<bool> VerifyBackgroundAsync(SessionBindingSnapshot original,
        IReadOnlyCollection<PersonalScheduleKnowledgeProof> proofs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(proofs);
        // 后台资格只能来自原会话，不能回退当前 HTTP 用户，也不因原用户仍启用就忽略失效会话。
        if (original.UserId == Guid.Empty || original.SessionId == Guid.Empty || original.TenantId == Guid.Empty
            || string.IsNullOrEmpty(original.SecurityStamp) || original.SessionKind is not (SessionBindingKinds.Refresh or SessionBindingKinds.OidcApplication)
            || proofs.Any(value => value is null || value.OwnerUserId != original.UserId || value.TenantId != original.TenantId)) return false;
        return await VerifyCoreAsync(proofs, original, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> VerifyCoreAsync(IReadOnlyCollection<PersonalScheduleKnowledgeProof> proofs,
        SessionBindingSnapshot? original, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proofs);
        if (proofs.Count is < 1 or > PersonalScheduleKnowledgeSourceKeys.MaxSources) return false;
        var frozen = proofs.ToArray();
        if (frozen.Any(proof => proof is null || !PersonalScheduleKnowledgeProjection.IsValid(proof))
            || frozen.Select(proof => proof.Id).Distinct().Count() != frozen.Length) return false;
        var captured = await CaptureCoreAsync(frozen.Select(proof => proof.Id).ToArray(), original, cancellationToken).ConfigureAwait(false);
        return captured is not null && captured.Select(value => value.Proof).SequenceEqual(frozen);
    }

    private async Task<AuthorizedSessionActor?> AuthorizeAsync(CalendarScope scope, CancellationToken token, SessionBindingSnapshot? original = null)
    {
        RequireOutsideTransaction(); token.ThrowIfCancellationRequested();
        if (!SameScope(scope)) return null;
        if (original is null && authorization is null) return null;
        if (original is not null && (background is null || original.TenantId != scope.TenantId)) return null;
        var actor = original is null ? await authorization!.AuthorizeAllAsync(
            [CalendarPermissions.Read, PersonalScheduleKnowledgePermissions.Read], token).ConfigureAwait(false)
            : await background!.AuthorizeAllAsync(original, [CalendarPermissions.Read, PersonalScheduleKnowledgePermissions.Read], token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return SameScope(scope) && actor is { UserId: var user, SessionId: var session }
            && user != Guid.Empty && session != Guid.Empty && actor.TenantId == scope.TenantId
            && (original is null || actor.UserId == original.UserId && actor.SessionId == original.SessionId) ? actor : null;
    }

    private bool SameScope(CalendarScope scope) => scope.TenantId is { } id
        ? !tenant.IsHost && tenant.IsAvailable && tenant.Id == id
        : tenant.IsHost && tenant.Id is null;

    private void RequireOutsideTransaction()
    {
        if (transactions.HasTransaction) throw new InvalidOperationException("个人日程来源核证不能加入调用方本地事务。");
    }
}
