using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageTenantMembers;
using Full.NET.Modules.Identity.Features.TenantSeatOperations;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.MyTenantRegistrations;

/// <summary>本人只能继续原注册工作项；租户权威及席位调用保持在 Identity 本地事务之外。</summary>
internal sealed class MyTenantRegistrationService(IQueryExecutor queries, ICommandExecutor commands,
    ICommandTransaction transaction, ICurrentTenantContextWriter tenant, IIdentityActiveTenantDirectory activeTenants,
    TenantSeatOperationProcessor seats, IClock clock, IOptions<DatabaseOptions> database)
{
    internal Task<Result<IReadOnlyList<SelfTenantRegistrationResponse>>> ListAsync(Guid userId, CancellationToken token) =>
        IdentityHostExecutionScope.RunAsync(tenant, async () =>
        {
            var rows = await queries.QueryAsync<SelfTenantRegistrationRow>(database.Value.Provider == DatabaseProvider.SqlServer
                ? SelfTenantRegistrationSql.ListSqlServer : SelfTenantRegistrationSql.ListMySql,
                IdentitySqlParameters.Create(("UserId", userId)), token).ConfigureAwait(false);
            return Result<IReadOnlyList<SelfTenantRegistrationResponse>>.Success(rows.Select(row => row.ToResponse()).ToArray());
        });

    internal Task<Result<SelfTenantRegistrationResponse>> ContinueAsync(Guid userId, Guid id, int version, CancellationToken token) =>
        IdentityHostExecutionScope.RunAsync(tenant, () => ContinueHostAsync(userId, id, version, token));

    private async Task<Result<SelfTenantRegistrationResponse>> ContinueHostAsync(Guid userId, Guid id, int version, CancellationToken token)
    {
        if (version <= 0) return Failure(ValidationErrorCodes.Failed, ErrorType.Validation);
        var op = await FindAsync(userId, id, token).ConfigureAwait(false);
        if (op is null) return Failure(IdentityErrorCodes.RegistrationMembershipNotFound, ErrorType.NotFound);
        if (op.Version != version) return Failure(IdentityErrorCodes.TenantMemberVersionConflict, ErrorType.Conflict);
        if (op.Phase == TenantSeatPhases.Activated) return await ReadResponseAsync(userId, id, token).ConfigureAwait(false);
        if (ManuallyChanged(op)) return Failure(IdentityErrorCodes.RegistrationMembershipRetryRejected, ErrorType.Conflict);
        if (!await activeTenants.IsActiveTenantAsync(op.TenantId, token).ConfigureAwait(false))
            return Failure(IdentityErrorCodes.RegistrationWayTenantInactive, ErrorType.Conflict);
        var way = await queries.QuerySingleOrDefaultAsync<RegistrationWayRecord>(RegistrationWaySql.FindById,
            IdentitySqlParameters.Create(("WayId", op.RegistrationWayId)), token).ConfigureAwait(false);
        if (way is null || !way.IsEnabled || way.TenantId != op.TenantId)
            return Failure(IdentityErrorCodes.RegistrationWayNotFound, ErrorType.Conflict);

        if (op.Phase == TenantSeatPhases.Cancelled)
        {
            var reset = await transaction.ExecuteResultAsync(async local =>
            {
                var current = await FindAsync(userId, id, local).ConfigureAwait(false);
                if (current is null || current.Version != version || current.Phase != TenantSeatPhases.Cancelled || current.LeaseId is not null)
                    return Result<bool>.Failure(new Error(IdentityErrorCodes.TenantMemberVersionConflict, "The registration intent changed.", ErrorType.Conflict));
                if (ManuallyChanged(current)) return RetryRejected();
                var member = await queries.QuerySingleOrDefaultAsync<TenantMemberRecord>(SelfTenantRegistrationSql.FindMember,
                    IdentitySqlParameters.Create(("MemberId", current.MemberId), ("TenantId", current.TenantId), ("UserId", userId)), local).ConfigureAwait(false);
                // 只复原本操作自己补偿为 Removed 的普通成员；管理员的版本、角色或状态变化均阻止旧意图复活。
                if (member is null || member.Version != current.MemberVersion + 1
                    || member.Status != TenantMemberStatuses.Removed || member.MemberRole != TenantMemberRoles.Member) return RetryRejected();
                if (await commands.ExecuteAsync(SelfTenantRegistrationSql.RetryMember, IdentitySqlParameters.Create(
                    ("MemberId", member.Id), ("TenantId", current.TenantId), ("UserId", userId),
                    ("MemberVersion", member.Version), ("Now", clock.UtcNow)), local).ConfigureAwait(false) != 1) return RetryRejected();
                if (await commands.ExecuteAsync(TenantSeatOperationSql.Retry, IdentitySqlParameters.Create(
                    ("Id", id), ("TenantId", current.TenantId), ("MemberVersion", member.Version + 1),
                    ("Version", version), ("Now", clock.UtcNow)), local).ConfigureAwait(false) != 1)
                    return Result<bool>.Failure(new Error(IdentityErrorCodes.TenantMemberVersionConflict, "The registration intent changed.", ErrorType.Conflict));
                return Result<bool>.Success(true);
            }, token).ConfigureAwait(false);
            if (!reset.IsSuccess) return Result<SelfTenantRegistrationResponse>.Failure(reset.Error!);
        }
        // API 单独运行时也可由用户显式推进；失败阶段和错误码由持久工作项返回，不能假报已加入。
        await seats.ProcessAsync(id, token).ConfigureAwait(false);
        return await ReadResponseAsync(userId, id, token).ConfigureAwait(false);
    }

    private Task<TenantSeatOperation?> FindAsync(Guid userId, Guid id, CancellationToken token) =>
        queries.QuerySingleOrDefaultAsync<TenantSeatOperation>(SelfTenantRegistrationSql.Find,
            IdentitySqlParameters.Create(("UserId", userId), ("Id", id)), token);

    private async Task<Result<SelfTenantRegistrationResponse>> ReadResponseAsync(Guid userId, Guid id, CancellationToken token)
    {
        var row = await queries.QuerySingleOrDefaultAsync<SelfTenantRegistrationRow>(SelfTenantRegistrationSql.Response,
            IdentitySqlParameters.Create(("UserId", userId), ("Id", id)), token).ConfigureAwait(false);
        return row is null ? Failure(IdentityErrorCodes.RegistrationMembershipNotFound, ErrorType.NotFound)
            : Result<SelfTenantRegistrationResponse>.Success(row.ToResponse());
    }

    private static bool ManuallyChanged(TenantSeatOperation op) =>
        op.ErrorCode is "identity.seat_operation.member_removed" or "identity.seat_operation.member_changed";
    private static Result<bool> RetryRejected() => Result<bool>.Failure(new Error(
        IdentityErrorCodes.RegistrationMembershipRetryRejected, "The original member was changed by an administrator.", ErrorType.Conflict));
    private static Result<SelfTenantRegistrationResponse> Failure(string code, ErrorType type) => Result<SelfTenantRegistrationResponse>.Failure(
        new Error(code, "The registration membership cannot be continued in its current state.", type));
}
