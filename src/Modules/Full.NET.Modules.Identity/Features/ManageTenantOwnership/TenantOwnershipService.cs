using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageTenantMembers;
using Full.NET.Modules.Identity.Features.ManageTenantMembers.Persistence;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.ManageTenantOwnership;

/// <summary>Owner 职责和审计回执在 Identity 本地原子提交；普通 RBAC、会话和强认证独立校验。</summary>
internal sealed class TenantOwnershipService(IQueryExecutor queries, ICommandExecutor commands, ICommandTransaction transaction,
    IDatabaseTransactionBoundSessionLock locks, ICurrentTenant tenant, IIdentityActiveTenantDirectory activeTenants,
    ICurrentSessionAuthorization sessions, IPermissionSnapshotReader permissions, IStrongReauthenticationProvider reauthentication,
    TenantOwnerAccountGuard accounts, TenantMembershipQueryService members, IHostEnvironment environment,
    IOptions<DatabaseOptions> database, IClock clock, IIdGenerator ids, AccessSessionValidator accessSessions)
{
    internal async Task<Result<TenantOwnershipStateResponse>> ReadAsync(CancellationToken cancellationToken)
    {
        var actor = await sessions.AuthorizeAsync(IdentityTenantMembershipPermissions.Read, cancellationToken).ConfigureAwait(false);
        if (actor?.TenantId is not { } tenantId) return Denied<TenantOwnershipStateResponse>();
        var owners = await ReadOwnersAsync(cancellationToken).ConfigureAwait(false);
        if (owners.Count > 1) return Conflict<TenantOwnershipStateResponse>();
        var owner = owners.SingleOrDefault();
        TenantMemberResponse? response = null;
        if (owner is not null)
        {
            var detail = await members.GetMemberByIdAsync(owner.Id, cancellationToken).ConfigureAwait(false);
            if (!detail.IsSuccess) return Result<TenantOwnershipStateResponse>.Failure(detail.Error!);
            response = detail.Value;
        }
        var grant = await permissions.ReadAsync(actor.UserId, "host", tenantId, cancellationToken).ConfigureAwait(false);
        var canWrite = grant.Permissions.Contains(IdentityTenantMembershipPermissions.TransferOwnership, StringComparer.Ordinal)
            && (!environment.IsProduction() || reauthentication.IsProductionEligible);
        return Result<TenantOwnershipStateResponse>.Success(new(tenantId, response,
            owner is null && canWrite && grant.IsSuperAdministrator,
            owner is not null && owner.UserId == actor.UserId && owner.Status == TenantMemberStatuses.Active && canWrite,
            reauthentication.IsProductionEligible));
    }

    internal async Task<Result<TenantOwnerTransferReceipt>> ReadReceiptAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var actor = await sessions.AuthorizeAsync(IdentityTenantMembershipPermissions.Read, cancellationToken).ConfigureAwait(false);
        if (actor?.TenantId is null) return Denied<TenantOwnerTransferReceipt>();
        var row = await FindReceiptAsync(operationId, cancellationToken).ConfigureAwait(false);
        return row?.ActorUserId == actor.UserId ? Result<TenantOwnerTransferReceipt>.Success(row.ToReceipt()) : NotFound<TenantOwnerTransferReceipt>();
    }

    internal async Task<Result<TenantOwnerTransferReceipt>> TransferAsync(TransferTenantOwnerRequest request,
        ClaimsPrincipal authenticatedPrincipal, CancellationToken cancellationToken)
    {
        // 只接受 Endpoint 认证管线建立的主体；等待期间不沿用可被调用方更换的声明集合。
        var principal = authenticatedPrincipal.Clone();
        if (request.OperationId == Guid.Empty || request.TargetMemberId == Guid.Empty || request.SourceMemberId == Guid.Empty
            || request.CurrentPassword?.Length > 1024 || request.TotpCode?.Length > 32
            || request.TargetVersion is <= 0 or int.MaxValue || request.SourceVersion is <= 0 or int.MaxValue
            || request.SourceMemberId.HasValue != request.SourceVersion.HasValue)
            return Result<TenantOwnerTransferReceipt>.Failure(new Error(ValidationErrorCodes.Failed, "The ownership operation or member versions are invalid.", ErrorType.Validation));
        var actor = await sessions.AuthorizeAsync(IdentityTenantMembershipPermissions.TransferOwnership, cancellationToken).ConfigureAwait(false);
        if (actor?.TenantId is not { } tenantId || tenant.Id != tenantId || tenant.IsHost) return Denied<TenantOwnerTransferReceipt>();
        if (!Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.Subject), out var claimedUser) || claimedUser != actor.UserId)
            return Denied<TenantOwnerTransferReceipt>();
        var digest = Digest(tenantId, actor.UserId, request);
        var existing = await FindReceiptAsync(request.OperationId, cancellationToken).ConfigureAwait(false);
        // 已提交的同一操作只返回原事实，不再次消费 TOTP，也不再修改职责。
        if (existing is not null) return MatchReceipt(existing, actor.UserId, digest);
        if (environment.IsProduction() && !reauthentication.IsProductionEligible)
            return Result<TenantOwnerTransferReceipt>.Failure(new Error(IdentityErrorCodes.TenantOwnershipStrongAuthenticationRequired,
                "Production ownership changes require an eligible strong reauthentication provider.", ErrorType.Forbidden));
        var verified = await reauthentication.VerifyAsync(actor.UserId, request.CurrentPassword ?? string.Empty, request.TotpCode, cancellationToken).ConfigureAwait(false);
        if (!verified.IsSuccess) return Result<TenantOwnerTransferReceipt>.Failure(verified.Error!);
        if (tenant.Id != tenantId || tenant.IsHost) return Denied<TenantOwnerTransferReceipt>();
        // Tenancy 权威读取在本地事务前完成，不让交接事务持有跨模块调用。
        if (!await activeTenants.IsActiveTenantAsync(tenantId, cancellationToken).ConfigureAwait(false)) return Denied<TenantOwnerTransferReceipt>();
        await using var lease = await locks.TryAcquireAsync("identity.tenant.owner:" + tenantId.ToString("N"), cancellationToken).ConfigureAwait(false);
        if (lease is null) return Conflict<TenantOwnerTransferReceipt>();
        return await transaction.ExecuteResultAsync(async token =>
        {
            await lease.EnsureHeldAsync(token).ConfigureAwait(false);
            if (tenant.Id != tenantId || tenant.IsHost) return Denied<TenantOwnerTransferReceipt>();
            var previous = await FindReceiptAsync(request.OperationId, token).ConfigureAwait(false);
            if (previous is not null) return MatchReceipt(previous, actor.UserId, digest);
            var owners = await ReadOwnersAsync(token).ConfigureAwait(false);
            if (owners.Count > 1) return Conflict<TenantOwnerTransferReceipt>();
            var source = owners.SingleOrDefault();
            if (source is null && request.SourceMemberId.HasValue
                || source is not null && (source.Id != request.SourceMemberId || source.Version != request.SourceVersion
                    || source.UserId != actor.UserId || source.Status != TenantMemberStatuses.Active)) return Conflict<TenantOwnerTransferReceipt>();
            var grant = await permissions.ReadAsync(actor.UserId, "host", tenantId, token).ConfigureAwait(false);
            if (!grant.Permissions.Contains(IdentityTenantMembershipPermissions.TransferOwnership, StringComparer.Ordinal)
                || source is null && !grant.IsSuperAdministrator) return Denied<TenantOwnerTransferReceipt>();
            var target = await queries.QuerySingleOrDefaultAsync<TenantMemberListRow>(TenantMembershipSql.FindMemberById,
                IdentitySqlParameters.Create(("MemberId", request.TargetMemberId)), token).ConfigureAwait(false);
            if (target is null) return NotFound<TenantOwnerTransferReceipt>();
            if (target.Version != request.TargetVersion || target.Status != TenantMemberStatuses.Active
                || target.MemberRole is not (TenantMemberRoles.Admin or TenantMemberRoles.Member) || target.Id == source?.Id)
                return Conflict<TenantOwnerTransferReceipt>();
            // 同一请求按稳定顺序锁定两个显式账号；账号管理入口先取相同行锁再判断 Owner 保护。
            foreach (var userId in new[] { actor.UserId, target.UserId }.Distinct().Order())
            {
                if (await accounts.LockAsync(userId, token).ConfigureAwait(false) != userId) return NotFound<TenantOwnerTransferReceipt>();
                var user = await queries.QuerySingleOrDefaultAsync<IdentityUserRecord>(IdentitySql.FindHostUserById,
                    IdentitySqlParameters.Create(("UserId", userId)), token).ConfigureAwait(false);
                if (user is not { IsActive: true } || user.RetiredAtUtc is not null
                    || user.AccountType is not (IdentityAccountTypes.NormalUser or IdentityAccountTypes.SysAdmin or IdentityAccountTypes.SuperAdmin))
                    return Denied<TenantOwnerTransferReceipt>();
            }
            var now = clock.UtcNow;
            if (!await SessionStillValidAsync(principal, token).ConfigureAwait(false)) return Denied<TenantOwnerTransferReceipt>();
            if (source is not null && await ChangeRoleAsync(source.Id, source.Version, TenantMemberRoles.Admin, now, token).ConfigureAwait(false) != 1)
                return Conflict<TenantOwnerTransferReceipt>();
            if (await ChangeRoleAsync(target.Id, target.Version, TenantMemberRoles.Owner, now, token).ConfigureAwait(false) != 1)
                return Conflict<TenantOwnerTransferReceipt>();
            // 回执同时是职责变更的 B0 领域审计；不保存验证凭据，不使用 Outbox 代替本地事实。
            await commands.ExecuteAsync(TenantOwnershipSql.InsertReceipt, IdentitySqlParameters.Create(("Id", ids.NewId()),
                ("OperationId", request.OperationId), ("ActorUserId", actor.UserId), ("SourceMemberId", source?.Id), ("SourceUserId", source?.UserId),
                ("SourcePreviousVersion", source?.Version), ("SourceVersion", source is null ? null : source.Version + 1),
                ("TargetMemberId", target.Id), ("TargetUserId", target.UserId), ("TargetPreviousVersion", target.Version), ("TargetVersion", target.Version + 1),
                ("CreatedAtUtc", now), ("RequestDigest", digest)), token).ConfigureAwait(false);
            var persisted = await FindReceiptAsync(request.OperationId, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Ownership receipt was not persisted.");
            // 领取锁和写入期间可能发生注销/到期；只读 Identity 自有会话事实，失败时回滚职责与回执。
            if (tenant.Id != tenantId || tenant.IsHost || !await SessionStillValidAsync(principal, token).ConfigureAwait(false))
                return Denied<TenantOwnerTransferReceipt>();
            await lease.EnsureHeldAsync(token).ConfigureAwait(false);
            return Result<TenantOwnerTransferReceipt>.Success(persisted.ToReceipt());
        }, cancellationToken).ConfigureAwait(false);
    }

    private Task<IReadOnlyList<TenantMemberRecord>> ReadOwnersAsync(CancellationToken token) =>
        queries.QueryAsync<TenantMemberRecord>(database.Value.Provider == DatabaseProvider.SqlServer
            ? TenantOwnershipSql.OwnersSqlServer : TenantOwnershipSql.OwnersMySql, cancellationToken: token);
    private async Task<bool> SessionStillValidAsync(ClaimsPrincipal principal, CancellationToken token) =>
        long.TryParse(principal.FindFirstValue("exp"), out var expires) && expires > clock.UtcNow.ToUnixTimeSeconds()
        && await accessSessions.IsValidAsync(principal, token).ConfigureAwait(false)
        && expires > clock.UtcNow.ToUnixTimeSeconds();
    private Task<TenantOwnerTransferRow?> FindReceiptAsync(Guid operationId, CancellationToken token) =>
        queries.QuerySingleOrDefaultAsync<TenantOwnerTransferRow>(TenantOwnershipSql.FindReceipt,
            IdentitySqlParameters.Create(("OperationId", operationId)), token);
    private Task<int> ChangeRoleAsync(Guid id, int version, string role, DateTimeOffset now, CancellationToken token) =>
        commands.ExecuteAsync(TenantMembershipSql.UpdateMember, IdentitySqlParameters.Create(("MemberId", id), ("Version", version),
            ("MemberRole", role), ("Status", TenantMemberStatuses.Active), ("UpdatedAtUtc", now)), token);
    private static string Digest(Guid tenantId, Guid actorId, TransferTenantOwnerRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant(
            $"{tenantId:N}|{actorId:N}|{request.SourceMemberId:N}|{request.SourceVersion}|{request.TargetMemberId:N}|{request.TargetVersion}"))));
    private static Result<TenantOwnerTransferReceipt> MatchReceipt(TenantOwnerTransferRow row, Guid actor, string digest) =>
        row.ActorUserId != actor ? NotFound<TenantOwnerTransferReceipt>() : row.RequestDigest != digest
            ? Conflict<TenantOwnerTransferReceipt>() : Result<TenantOwnerTransferReceipt>.Success(row.ToReceipt());
    private static Result<T> Conflict<T>() => Result<T>.Failure(new Error(IdentityErrorCodes.TenantOwnershipConflict, "The ownership or member version changed. Review the current members.", ErrorType.Conflict));
    private static Result<T> Denied<T>() => Result<T>.Failure(new Error(IdentityErrorCodes.TenantOwnershipForbidden, "The current actor cannot perform this ownership operation.", ErrorType.Forbidden));
    private static Result<T> NotFound<T>() => Result<T>.Failure(new Error(IdentityErrorCodes.TenantOwnershipNotFound, "The ownership operation or member was not found.", ErrorType.NotFound));
}
