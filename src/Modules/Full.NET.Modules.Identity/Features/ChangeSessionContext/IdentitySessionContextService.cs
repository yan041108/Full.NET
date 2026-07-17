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
    IAccessTokenIssuer accessTokenIssuer,
    IClock clock,
    IIdGenerator idGenerator) : IIdentitySessionContextService
{
    private const string HostScope = "host";
    private const string SwitchPermission = "tenancy.tenants.switch";

    public async Task<Result<TenantContextTokenResponse>> ChangeAsync(
        ClaimsPrincipal principal,
        VerifiedTenantContext? tenant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (!TryReadIdentity(principal, out var userId, out var sessionId)
            || !string.Equals(
                principal.FindFirstValue(IdentityClaimTypes.ActorScope),
                HostScope,
                StringComparison.Ordinal))
        {
            return Failure(
                "identity.invalid_actor_scope",
                "The current identity cannot switch tenant context.",
                ErrorType.Forbidden);
        }

        var hasPermission = principal
            .FindAll(IdentityClaimTypes.Permission)
            .Any(claim => string.Equals(
                claim.Value,
                SwitchPermission,
                StringComparison.Ordinal));
        if (!hasPermission)
        {
            return Failure(
                "authorization.permission_denied",
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
                "identity.session_context_conflict",
                "The session context changed concurrently.",
                ErrorType.Conflict);
        }

        var permissions = await permissionSnapshotReader.ReadAsync(
                record!.UserId,
                record.ScopeKey,
                record.TenantId,
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
            permissions);
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

    private Task<RefreshSessionRecord?> FindSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<RefreshSessionRecord>(
            IdentitySql.FindRefreshSessionById,
            new { SessionId = sessionId },
            cancellationToken);

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

    private static bool TryReadIdentity(
        ClaimsPrincipal principal,
        out Guid userId,
        out Guid sessionId)
    {
        sessionId = Guid.Empty;
        return Guid.TryParse(
                principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
                out userId)
            && Guid.TryParse(
                principal.FindFirstValue(IdentityClaimTypes.SessionId),
                out sessionId);
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
        record.UserVersion);

    private static Result<TenantContextTokenResponse> SessionNotActive() =>
        Failure(
            "identity.session_not_active",
            "The current session is no longer active.",
            ErrorType.Unauthorized);

    private static Result<TenantContextTokenResponse> Failure(
        string code,
        string message,
        ErrorType type) =>
        Result<TenantContextTokenResponse>.Failure(new Error(code, message, type));
}

internal sealed record RefreshSessionContextUpdate(
    Guid SessionId,
    Guid UserId,
    Guid? ActiveTenantId,
    int Version);
