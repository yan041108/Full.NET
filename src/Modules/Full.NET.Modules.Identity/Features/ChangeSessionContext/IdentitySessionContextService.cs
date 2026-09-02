using System.Security.Claims;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.Modules.Identity.Features.ChangeSessionContext;

internal sealed class IdentitySessionContextService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IPermissionSnapshotReader permissionSnapshotReader,
    PermissionClaimEvaluator permissionClaimEvaluator,
    IAccessTokenIssuer accessTokenIssuer,
    IClock clock,
    IIdGenerator idGenerator) : IIdentitySessionContextService
{
    private const string HostScope = "host";
    private const string SwitchPermission = "tenancy.tenants.switch";

    /// <summary>
    /// 使用当前 Access Token 所代表的会话上下文执行一次乐观并发切换并签发新令牌。
    /// </summary>
    /// <param name="principal">包含会话标识、来源作用域和当前租户上下文的已认证身份。</param>
    /// <param name="tenant">目标租户；传入空值表示返回 Host 上下文。</param>
    /// <param name="cancellationToken">用于取消数据库读写的令牌。</param>
    /// <returns>切换成功时返回新令牌和上下文，状态已变化时返回稳定冲突结果。</returns>
    public async Task<Result<TenantContextTokenResponse>> ChangeAsync(
        ClaimsPrincipal principal,
        VerifiedTenantContext? tenant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (!TryReadIdentity(
                principal,
                out var userId,
                out var sessionId,
                out var expectedActiveTenantId)
            || !string.Equals(
                principal.FindFirstValue(IdentityClaimTypes.ActorScope),
                HostScope,
                StringComparison.Ordinal))
        {
            return Failure(
                IdentityErrorCodes.InvalidActorScope,
                "The current identity cannot switch tenant context.",
                ErrorType.Forbidden);
        }

        if (!permissionClaimEvaluator.HasPermission(principal, SwitchPermission))
        {
            return Failure(
                CommonErrorCodes.PermissionDenied,
                "The current identity does not have the required permission.",
                ErrorType.Forbidden);
        }

        var record = await FindSessionAsync(sessionId, cancellationToken)
            .ConfigureAwait(false);
        if (!IsOwnedActiveHostSession(record, userId, principal))
        {
            return SessionNotActive();
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.UpdateRefreshSessionContext,
                new RefreshSessionContextUpdate(
                    sessionId,
                    userId,
                    tenant?.Id,
                    expectedActiveTenantId,
                    record!.SessionVersion),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            var current = await FindSessionAsync(sessionId, cancellationToken)
                .ConfigureAwait(false);
            if (!IsOwnedActiveHostSession(current, userId, principal))
            {
                return SessionNotActive();
            }

            return Failure(
                IdentityErrorCodes.SessionContextConflict,
                "The session context changed concurrently.",
                ErrorType.Conflict);
        }

        var authorization = await permissionSnapshotReader.ReadAsync(
                record!.UserId,
                record.ScopeKey,
                tenant?.Id ?? record.TenantId,
                cancellationToken)
            .ConfigureAwait(false);
        var audit = new AuthAuditEvent(
            idGenerator.NewId(),
            record.UserId,
            record.SessionId,
            TokenHash.Compute(record.NormalizedUsername),
            "context-switch",
            "identity.session-context-changed",
            true,
            null,
            null,
            tenant?.Id,
            clock.UtcNow);
        var auditRows = await commandExecutor.ExecuteAsync(
                IdentitySql.InsertContextAudit,
                audit,
                cancellationToken)
            .ConfigureAwait(false);
        if (auditRows != 1)
        {
            throw new InvalidOperationException(
                $"Identity context audit insert affected {auditRows} rows instead of one.");
        }

        var issued = accessTokenIssuer.Issue(
            ToUser(record),
            record.SessionId,
            tenant?.Id,
            authorization.Permissions,
            authorization.IsSuperAdministrator);
        var context = tenant is null
            ? new TenantContextDescriptor(null, "host", "Host", HostScope)
            : new TenantContextDescriptor(
                tenant.Id,
                tenant.Identifier,
                tenant.Name,
                $"tenant:{tenant.Id:N}");
        return Result<TenantContextTokenResponse>.Success(
            new TenantContextTokenResponse(
                issued.AccessToken,
                "Bearer",
                issued.ExpiresAtUtc,
                context));
    }

    /// <summary>按会话标识读取刷新会话，用于上下文切换前后的并发校验。</summary>
    /// <param name="sessionId">刷新会话标识。</param>
    /// <param name="cancellationToken">用于取消数据库查询的令牌。</param>
    /// <returns>存在时返回刷新会话记录，否则返回空值。</returns>
    private Task<RefreshSessionRecord?> FindSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var parameters = IdentitySqlParameters.Create(("SessionId", sessionId));
        return queryExecutor.QuerySingleOrDefaultAsync<RefreshSessionRecord>(
            IdentitySql.FindRefreshSessionById,
            parameters,
            cancellationToken);
    }

    private bool IsOwnedActiveHostSession(
        RefreshSessionRecord? record,
        Guid userId,
        ClaimsPrincipal principal)
    {
        return record is not null
            && record.UserId == userId
            && string.Equals(record.ScopeKey, HostScope, StringComparison.Ordinal)
            && string.Equals(
                record.SecurityStamp,
                principal.FindFirstValue(IdentityClaimTypes.SecurityStamp),
                StringComparison.Ordinal)
            && record.IsActive
            && record.ExpiresAtUtc > clock.UtcNow
            && !record.ConsumedAtUtc.HasValue
            && !record.RevokedAtUtc.HasValue;
    }

    /// <summary>读取并验证上下文切换所需的用户、会话和当前租户 Claim。</summary>
    /// <param name="principal">待解析的已认证身份。</param>
    /// <param name="userId">解析得到的用户标识。</param>
    /// <param name="sessionId">解析得到的刷新会话标识。</param>
    /// <param name="activeTenantId">令牌代表的当前租户；Host 上下文为空。</param>
    /// <returns>全部必需 Claim 合法且租户 Claim 为空或为有效 Guid 时返回真。</returns>
    private static bool TryReadIdentity(
        ClaimsPrincipal principal,
        out Guid userId,
        out Guid sessionId,
        out Guid? activeTenantId)
    {
        sessionId = Guid.Empty;
        activeTenantId = null;
        if (!Guid.TryParse(
                principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
                out userId)
            || !Guid.TryParse(
                principal.FindFirstValue(IdentityClaimTypes.SessionId),
                out sessionId))
        {
            return false;
        }

        var tenantClaim = principal.FindFirstValue(IdentityClaimTypes.TenantId);
        if (string.IsNullOrEmpty(tenantClaim))
        {
            return true;
        }

        if (!Guid.TryParse(tenantClaim, out var parsedTenantId))
        {
            return false;
        }

        activeTenantId = parsedTenantId;
        return true;
    }

    private static IdentityUser ToUser(RefreshSessionRecord record) => new(
        record.UserId,
        record.TenantId,
        record.ScopeKey,
        record.Username,
        record.NormalizedUsername,
        record.DisplayName,
        record.PasswordHash,
        record.IsActive,
        record.FailedLoginCount,
        record.LockoutEndUtc,
        record.SecurityStamp,
        record.UserCreatedAtUtc,
        record.UserUpdatedAtUtc,
        record.UserVersion,
        record.PreferredLocale,
        record.ProfileVersion);

    private static Result<TenantContextTokenResponse> SessionNotActive() =>
        Failure(
            IdentityErrorCodes.SessionNotActive,
            "The current session is no longer active.",
            ErrorType.Unauthorized);

    private static Result<TenantContextTokenResponse> Failure(
        string code,
        string message,
        ErrorType type) =>
        Result<TenantContextTokenResponse>.Failure(new Error(
            Code: code,
            Message: message,
            Type: type));
}

/// <summary>刷新会话上下文的乐观并发更新参数。</summary>
/// <param name="SessionId">刷新会话标识。</param>
/// <param name="UserId">会话所属用户标识。</param>
/// <param name="ActiveTenantId">要写入的新活动租户标识。</param>
/// <param name="ExpectedActiveTenantId">发起请求的令牌所代表的原活动租户标识。</param>
/// <param name="Version">读取会话时观察到的并发版本。</param>
internal sealed record RefreshSessionContextUpdate(
    Guid SessionId,
    Guid UserId,
    Guid? ActiveTenantId,
    Guid? ExpectedActiveTenantId,
    int Version);
