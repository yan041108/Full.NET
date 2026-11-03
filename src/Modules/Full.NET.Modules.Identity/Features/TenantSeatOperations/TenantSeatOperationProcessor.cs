using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Features.AcceptTenantInvitation;
using Full.NET.Modules.Identity.Features.ManageTenantMembers;
using Full.NET.Modules.Identity.Features.ManageTenantMembers.Persistence;
using Full.NET.Messaging.Abstractions;

namespace Full.NET.Modules.Identity.Features.TenantSeatOperations;

/// <summary>跨模块调用只在事务外执行；成员保持 Pending，直到原席位确认和 Identity 激活事务均已提交。</summary>
internal sealed class TenantSeatOperationProcessor(IQueryExecutor queries, ICommandExecutor commands,
    ICommandTransaction transaction, ICurrentTenantContextWriter tenant, IActiveTenantContextResolver tenants,
    ITenantMemberSeatQuotaPort seats, IClock clock, ITenantInvitationGrantorAuthority grantors, IOutboxWriter outbox)
{
    /// <summary>与移除成员同事务登记取消；不能先返还席位再提交成员禁用。</summary>
    internal Task CancelMemberAsync(Guid tenantId, Guid memberId, DateTimeOffset now, CancellationToken token) =>
        IdentityHostExecutionScope.RunAsync(tenant, async () =>
        {
            await commands.ExecuteAsync(TenantSeatOperationSql.CancelMember, IdentitySqlParameters.Create(
                ("TenantId", tenantId), ("MemberId", memberId), ("Now", now)), token).ConfigureAwait(false);
        });

    internal Task<TenantSeatOperation?> FindRequestAsync(Guid tenantId, string key, CancellationToken token) =>
        IdentityHostExecutionScope.RunAsync(tenant, () => queries.QuerySingleOrDefaultAsync<TenantSeatOperation>(
            TenantSeatOperationSql.FindRequest, IdentitySqlParameters.Create(("TenantId", tenantId), ("RequestKey", key)), token));

    /// <summary>由成员/邀请的本地事务登记；事务回滚时不得单独留下工作项。</summary>
    internal Task EnlistAsync(TenantSeatOperation operation, CancellationToken token) => IdentityHostExecutionScope.RunAsync(tenant, async () =>
    {
        if (await commands.ExecuteAsync(TenantSeatOperationSql.Insert, IdentitySqlParameters.Create(
            ("Id", operation.Id), ("RequestKey", operation.RequestKey), ("TenantId", operation.TenantId),
            ("UserId", operation.UserId), ("MemberId", operation.MemberId), ("InvitationId", operation.InvitationId),
            ("PreviousRole", operation.PreviousRole), ("PreviousStatus", operation.PreviousStatus),
            ("MemberVersion", operation.MemberVersion), ("InvitationVersion", operation.InvitationVersion),
            ("Phase", operation.Phase), ("RequestHash", operation.RequestHash),
            ("CreatedAtUtc", operation.CreatedAtUtc), ("DueAtUtc", operation.DueAtUtc)), token).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Seat activation intent was not persisted.");
    });

    internal Task<Result<bool>> ProcessAsync(Guid id, CancellationToken token) => IdentityHostExecutionScope.RunAsync(tenant,
        () => ProcessHostAsync(id, token));

    private async Task<Result<bool>> ProcessHostAsync(Guid id, CancellationToken token)
    {
        var op = await queries.QuerySingleOrDefaultAsync<TenantSeatOperation>(TenantSeatOperationSql.Find,
            IdentitySqlParameters.Create(("Id", id)), token).ConfigureAwait(false);
        if (op is null) return Failure("identity.seat_operation.not_found", ErrorType.NotFound);
        if (op.Phase == TenantSeatPhases.Activated) return Result<bool>.Success(true);
        if (op.Phase == TenantSeatPhases.Cancelled) return Failure(op.ErrorCode ?? "identity.seat_operation.cancelled", ErrorType.Conflict);
        var lease = Guid.CreateVersion7();
        if (await commands.ExecuteAsync(TenantSeatOperationSql.Acquire,
            IdentitySqlParameters.Create(("Id", id), ("LeaseId", lease), ("Now", clock.UtcNow),
                ("Until", clock.UtcNow.AddSeconds(30))), token).ConfigureAwait(false) != 1)
            return Result<bool>.Success(false);
        try
        {
            // 租约可能在首次读取后被另一执行器使用过；取得租约后必须重新读取实际阶段。
            op = await queries.QuerySingleOrDefaultAsync<TenantSeatOperation>(TenantSeatOperationSql.Find,
                IdentitySqlParameters.Create(("Id", id)), token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Seat operation disappeared.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            var ct = deadline.Token;
            if (op.Phase != TenantSeatPhases.Cancelling
                && await tenants.ResolveActiveByIdAsync(op.TenantId, ct).ConfigureAwait(false) is null)
                op = await SaveAsync(op, lease, TenantSeatPhases.Cancelling, "identity.seat_operation.tenant_inactive", ct).ConfigureAwait(false);
            var key = id.ToString("D") + ":" + op.Attempt.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (op.Phase == TenantSeatPhases.Requested)
            {
                var reserved = await seats.TryReserveAsync(op.TenantId, key, ct).ConfigureAwait(false);
                op = reserved.IsSuccess && reserved.Value
                    ? await SaveAsync(op, lease, TenantSeatPhases.Reserved, null, ct).ConfigureAwait(false)
                    : await SaveAsync(op, lease, TenantSeatPhases.Cancelling, reserved.Error?.Code ?? "identity.seat_operation.quota_rejected", ct).ConfigureAwait(false);
            }
            if (op.Phase == TenantSeatPhases.Reserved)
            {
                var confirmed = await seats.ConfirmAsync(op.TenantId, key, ct).ConfigureAwait(false);
                op = confirmed.IsSuccess && confirmed.Value
                    ? await SaveAsync(op, lease, TenantSeatPhases.Confirmed, null, ct).ConfigureAwait(false)
                    : await SaveAsync(op, lease, TenantSeatPhases.Cancelling, confirmed.Error?.Code ?? "identity.seat_operation.confirm_rejected", ct).ConfigureAwait(false);
            }
            if (op.Phase == TenantSeatPhases.Confirmed)
            {
                var activationError = "identity.seat_operation.member_changed";
                bool activated;
                try
                {
                    activated = await transaction.ExecuteAsync(async local =>
                    {
                        if (op.InvitationId is Guid invitationId)
                        {
                            var invitation = await queries.QuerySingleOrDefaultAsync<TenantInvitationRecord>(TenantMembershipSql.FindInvitationByIdGlobal,
                                IdentitySqlParameters.Create(("InvitationId", invitationId)), local).ConfigureAwait(false);
                            if (invitation is null || invitation.TenantId != op.TenantId
                                || !await grantors.IsAuthorizedAsync(invitation.InvitedByUserId, op.TenantId, local).ConfigureAwait(false))
                            {
                                activationError = "identity.seat_operation.inviter_unauthorized";
                                return false;
                            }
                        }
                        var (valid, way) = await FindRegistrationWayAsync(op, local).ConfigureAwait(false);
                        if (!valid)
                        {
                            activationError = IdentityErrorCodes.RegistrationWayNotFound;
                            return false;
                        }
                        if (await commands.ExecuteAsync(TenantSeatOperationSql.ActivateMember, MemberParameters(op), local).ConfigureAwait(false) != 1)
                            return false;
                        if (way is not null) await ApplyRegistrationWayAsync(op, way, local).ConfigureAwait(false);
                        if (op.InvitationId is not null)
                            await commands.ExecuteAsync(RegistrationInvitationSql.MarkActivated, MemberParameters(op), local).ConfigureAwait(false);
                        await SaveAsync(op, lease, TenantSeatPhases.Activated, null, local).ConfigureAwait(false);
                        return true;
                    }, ct).ConfigureAwait(false);
                }
                catch (RegistrationWayChangedException)
                {
                    // 角色授予与成员激活已经一起回滚；通过原席位补偿返回可识别的配置版本冲突。
                    activated = false;
                    activationError = IdentityErrorCodes.RegistrationWayVersionConflict;
                }
                if (activated) return Result<bool>.Success(true);
                op = await SaveAsync(op, lease, TenantSeatPhases.Cancelling, activationError, ct).ConfigureAwait(false);
            }
            if (op.Phase == TenantSeatPhases.Cancelling)
            {
                // 响应丢失时无法仅凭阶段推断预留是否已被确认；两条幂等终止路径都必须探测。
                var released = await seats.ReleaseAsync(op.TenantId, key, ct).ConfigureAwait(false);
                if (!released.IsSuccess || !released.Value)
                {
                    var cancelled = await seats.CancelConsumptionAsync(op.TenantId, key, ct).ConfigureAwait(false);
                    if ((!cancelled.IsSuccess || !cancelled.Value)
                        && cancelled.Error?.Code != TenantMemberSeatQuotaErrorCodes.OperationNotFound)
                        return Failure(cancelled.Error?.Code ?? "identity.seat_operation.cancel_pending", ErrorType.Conflict);
                }
                await transaction.ExecuteAsync(async local =>
                {
                    await commands.ExecuteAsync(TenantSeatOperationSql.RestoreMember, MemberParameters(op), local).ConfigureAwait(false);
                    if (op.InvitationId is not null && op.ErrorCode is not ("identity.seat_operation.member_removed" or "identity.seat_operation.member_changed"))
                        await commands.ExecuteAsync(TenantSeatOperationSql.RestoreInvitation, MemberParameters(op), local).ConfigureAwait(false);
                    await SaveAsync(op, lease, TenantSeatPhases.Cancelled, op.ErrorCode, local).ConfigureAwait(false);
                    return true;
                }, ct).ConfigureAwait(false);
                return Failure(op.ErrorCode ?? "identity.seat_operation.cancelled", ErrorType.Conflict);
            }
            throw new InvalidOperationException("Unsupported seat operation phase.");
        }
        finally
        {
            await commands.ExecuteAsync(TenantSeatOperationSql.ReleaseLease,
                IdentitySqlParameters.Create(("Id", id), ("LeaseId", lease), ("DueAtUtc", clock.UtcNow.AddSeconds(15))),
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<(bool Valid, RegistrationWayRecord? Way)> FindRegistrationWayAsync(TenantSeatOperation op, CancellationToken token)
    {
        if (op.InvitationId is not Guid invitationId) return (true, null);
        var invitation = await queries.QuerySingleOrDefaultAsync<RegistrationInvitationRecord>(RegistrationInvitationSql.FindById,
            IdentitySqlParameters.Create(("InvitationId", invitationId)), token).ConfigureAwait(false);
        if (invitation is null) return (true, null);
        if (invitation.TenantId != op.TenantId || invitation.Status is not (0 or 1)
            || invitation.BoundUserId is Guid bound && bound != op.UserId) return (false, null);
        var way = await queries.QuerySingleOrDefaultAsync<RegistrationWayRecord>(RegistrationWaySql.FindById,
            IdentitySqlParameters.Create(("WayId", invitation.RegistrationWayId)), token).ConfigureAwait(false);
        if (way is null || !way.IsEnabled || way.TenantId != op.TenantId) return (false, null);
        var role = await queries.QuerySingleOrDefaultAsync<IdentityRoleRecord>(RegistrationWaySql.FindActiveTenantRole,
            IdentitySqlParameters.Create(("RoleId", way.RoleId), ("TenantId", op.TenantId)), token).ConfigureAwait(false);
        return role is { IsSuperAdministrator: false } && role.ScopeKey == $"tenant:{op.TenantId:N}"
            ? (true, way) : (false, null);
    }

    private async Task ApplyRegistrationWayAsync(TenantSeatOperation op, RegistrationWayRecord way, CancellationToken token)
    {
        var parameters = IdentitySqlParameters.Create(("UserId", op.UserId), ("RoleId", way.RoleId),
            ("TenantId", op.TenantId), ("ScopeKey", $"tenant:{op.TenantId:N}"), ("WayId", way.Id), ("WayVersion", way.Version));
        await commands.ExecuteAsync(RegistrationMemberActivationSql.GrantRole, parameters, token).ConfigureAwait(false);
        if (await queries.QuerySingleOrDefaultAsync<int>(RegistrationMemberActivationSql.HasRole, parameters, token).ConfigureAwait(false) != 1)
            throw new RegistrationWayChangedException();
        // 事件租户来自已提交的席位意图；临时绑定只用于 Outbox 信封，发布后立即恢复 Host 编排作用域。
        await IdentityTenantInvitationScope.RunAsync(tenant, new TenantContext(op.TenantId, op.TenantId.ToString("D"), op.TenantId.ToString("D")), async () =>
        {
            await outbox.AddAsync(RegistrationMemberIntegrationEventTypes.Activated, 1,
                new RegistrationMemberActivatedIntegrationEvent(op.Id, op.TenantId, op.MemberId, op.MemberVersion + 1,
                    op.UserId, way.OrganizationUnitId, way.PositionId, clock.UtcNow),
                IntegrationEventMetadata.Create(op.TenantId.ToString("D"), "fullnet.identity"), token).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    private async Task<TenantSeatOperation> SaveAsync(TenantSeatOperation op, Guid lease, string phase, string? error, CancellationToken token)
    {
        if (await commands.ExecuteAsync(TenantSeatOperationSql.Save,
            IdentitySqlParameters.Create(("Id", op.Id), ("LeaseId", lease), ("Phase", phase), ("ExpectedPhase", op.Phase), ("ErrorCode", error),
                ("DueAtUtc", clock.UtcNow.AddSeconds(15))), token).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Seat operation lease was lost.");
        return op with { Phase = phase, ErrorCode = error };
    }

    private object MemberParameters(TenantSeatOperation op) => IdentitySqlParameters.Create(("MemberId", op.MemberId),
        ("TenantId", op.TenantId), ("UserId", op.UserId), ("MemberVersion", op.MemberVersion),
        ("InvitationId", op.InvitationId), ("InvitationVersion", op.InvitationVersion), ("Now", clock.UtcNow),
        ("PreviousRole", op.PreviousRole ?? TenantMemberRoles.Member), ("PreviousStatus", op.PreviousStatus ?? TenantMemberStatuses.Removed));

    private static Result<bool> Failure(string code, ErrorType type) => Result<bool>.Failure(new Error(code,
        "The tenant membership activation has not completed.", type));

    private sealed class RegistrationWayChangedException : Exception { }
}
