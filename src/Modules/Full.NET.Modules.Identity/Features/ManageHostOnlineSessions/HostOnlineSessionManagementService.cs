using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.ManageHostOnlineSessions;

/// <summary>Host 在线会话强制下线；撤销整个刷新令牌族以阻断后续轮换，并写入审计与实时通知。</summary>
internal sealed class HostOnlineSessionManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock,
    IIdGenerator idGenerator,
    IdentitySessionRealtimeDelivery realtimeDelivery,
    IdentityOidcSessionService oidcSessionService)
{
    private const string RevokeAuditEventType = "host_online_session.revoked";
    private const string RevokeAllAuditEventType = "host_online_session.revoked_all";

    /// <summary>强制下线指定在线会话。</summary>
    public Task<Result<HostOnlineSessionResponse>> RevokeAsync(
        Guid actorUserId,
        Guid sessionId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => RevokeCoreAsync(
                actorUserId,
                sessionId,
                ipAddress,
                userAgent,
                token),
            cancellationToken);

    /// <summary>撤销指定 Host 用户的全部活跃在线会话。</summary>
    public Task<Result<RevokeAllHostUserSessionsResponse>> RevokeAllByUserAsync(
        Guid actorUserId,
        Guid userId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => RevokeAllByUserCoreAsync(
                actorUserId,
                userId,
                ipAddress,
                userAgent,
                token),
            cancellationToken);

    private async Task<Result<HostOnlineSessionResponse>> RevokeCoreAsync(
        Guid actorUserId,
        Guid sessionId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var refreshRecord = await queryExecutor.QuerySingleOrDefaultAsync<OnlineSessionRevokeRow>(
                OnlineSessionSql.FindActiveHostSessionById,
                IdentitySqlParameters.Create(
                    ("SessionId", sessionId),
                    ("NowUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
        if (refreshRecord is not null)
        {
            var snapshot = Map(refreshRecord);
            var affectedRows = await commandExecutor.ExecuteAsync(
                    IdentitySql.RevokeRefreshFamily,
                    IdentitySqlParameters.Create(
                        ("FamilyId", refreshRecord.FamilyId),
                        ("RevokedAtUtc", clock.UtcNow)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (affectedRows < 1)
            {
                return NotFound();
            }

            await oidcSessionService.RevokeAllApplicationSessionsByUserAsync(
                    refreshRecord.UserId,
                    cancellationToken)
                .ConfigureAwait(false);
            await oidcSessionService.RevokeAllCenterSessionsByUserAsync(
                    refreshRecord.UserId,
                    cancellationToken)
                .ConfigureAwait(false);
            await WriteAuditAsync(
                    actorUserId,
                    refreshRecord.UserId,
                    sessionId,
                    RevokeAuditEventType,
                    $"session:{sessionId:D}",
                    ipAddress,
                    userAgent,
                    cancellationToken)
                .ConfigureAwait(false);
            await realtimeDelivery.PublishSessionsRevokedAsync(
                    refreshRecord.UserId,
                    [sessionId],
                    cancellationToken)
                .ConfigureAwait(false);
            return Result<HostOnlineSessionResponse>.Success(snapshot);
        }

        var oidcRecord = await queryExecutor.QuerySingleOrDefaultAsync<OnlineSessionListRow>(
                IdentityOidcSessionSql.FindActiveHostApplicationSessionById,
                IdentitySqlParameters.Create(
                    ("SessionId", sessionId),
                    ("NowUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
        if (oidcRecord is null)
        {
            return NotFound();
        }

        var revoked = await oidcSessionService.RevokeApplicationSessionAsync(sessionId, cancellationToken)
            .ConfigureAwait(false);
        if (!revoked)
        {
            return NotFound();
        }

        var oidcSnapshot = Map(oidcRecord);
        await WriteAuditAsync(
                actorUserId,
                oidcRecord.UserId,
                sessionId,
                RevokeAuditEventType,
                $"session:{sessionId:D}",
                ipAddress,
                userAgent,
                cancellationToken)
            .ConfigureAwait(false);
        await realtimeDelivery.PublishSessionsRevokedAsync(
                oidcRecord.UserId,
                [sessionId],
                cancellationToken)
            .ConfigureAwait(false);
        return Result<HostOnlineSessionResponse>.Success(oidcSnapshot);
    }

    private async Task<Result<RevokeAllHostUserSessionsResponse>> RevokeAllByUserCoreAsync(
        Guid actorUserId,
        Guid userId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var user = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                IdentitySqlParameters.Create(("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (user is null)
        {
            return UserNotFound();
        }

        var refreshSessionIds = (await queryExecutor.QueryAsync<Guid>(
                    OnlineSessionSql.ListActiveHostSessionIdsByUser,
                    IdentitySqlParameters.Create(
                        ("UserId", userId),
                        ("NowUtc", clock.UtcNow)),
                    cancellationToken)
                .ConfigureAwait(false))
            .ToArray();
        var oidcSessionIds = (await queryExecutor.QueryAsync<Guid>(
                    IdentityOidcSessionSql.ListActiveHostOidcApplicationSessionIdsByUser,
                    IdentitySqlParameters.Create(
                        ("UserId", userId),
                        ("NowUtc", clock.UtcNow)),
                    cancellationToken)
                .ConfigureAwait(false))
            .ToArray();
        var sessionIds = refreshSessionIds.Concat(oidcSessionIds).Distinct().ToArray();
        if (sessionIds.Length == 0)
        {
            return Result<RevokeAllHostUserSessionsResponse>.Success(
                new RevokeAllHostUserSessionsResponse(
                    user.Id,
                    user.Username,
                    user.DisplayName,
                    0));
        }

        if (refreshSessionIds.Length > 0)
        {
            var revokedRows = await commandExecutor.ExecuteAsync(
                    IdentitySql.RevokeAllUserSessions,
                    IdentitySqlParameters.Create(
                        ("UserId", userId),
                        ("RevokedAtUtc", clock.UtcNow)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (revokedRows < 1 && oidcSessionIds.Length == 0)
            {
                return Result<RevokeAllHostUserSessionsResponse>.Success(
                    new RevokeAllHostUserSessionsResponse(
                        user.Id,
                        user.Username,
                        user.DisplayName,
                        0));
            }
        }

        await oidcSessionService.RevokeAllApplicationSessionsByUserAsync(userId, cancellationToken)
            .ConfigureAwait(false);
        await oidcSessionService.RevokeAllCenterSessionsByUserAsync(userId, cancellationToken)
            .ConfigureAwait(false);
        await WriteAuditAsync(
                actorUserId,
                user.Id,
                null,
                RevokeAllAuditEventType,
                $"target:{user.Id:D};sessions:{sessionIds.Length}",
                ipAddress,
                userAgent,
                cancellationToken)
            .ConfigureAwait(false);
        await realtimeDelivery.PublishSessionsRevokedAsync(
                user.Id,
                sessionIds,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<RevokeAllHostUserSessionsResponse>.Success(
            new RevokeAllHostUserSessionsResponse(
                user.Id,
                user.Username,
                user.DisplayName,
                sessionIds.Length));
    }

    private async Task WriteAuditAsync(
        Guid actorUserId,
        Guid targetUserId,
        Guid? sessionId,
        string eventType,
        string resultCode,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var audit = new AuthAuditEvent(
            idGenerator.NewId(),
            actorUserId,
            sessionId,
            string.Empty,
            eventType,
            resultCode,
            true,
            Truncate(ipAddress, 64),
            Truncate(userAgent, 512),
            null,
            clock.UtcNow);
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.InsertAuthAudit,
                audit,
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Identity session revoke audit insert affected {affectedRows} rows instead of one.");
        }
    }

    private static HostOnlineSessionResponse Map(OnlineSessionRevokeRow record) =>
        new(
            record.SessionId,
            record.UserId,
            record.Username,
            record.DisplayName,
            record.ClientId,
            record.ActiveTenantId,
            record.CreatedAtUtc,
            record.ExpiresAtUtc);

    private static HostOnlineSessionResponse Map(OnlineSessionListRow record) =>
        new(
            record.SessionId,
            record.UserId,
            record.Username,
            record.DisplayName,
            record.ClientId,
            record.ActiveTenantId,
            record.CreatedAtUtc,
            record.ExpiresAtUtc);

    private static string? Truncate(string? value, int maxLength) =>
        value is null
            ? null
            : value.Length <= maxLength
                ? value
                : value[..maxLength];

    private static Result<HostOnlineSessionResponse> NotFound() =>
        Result<HostOnlineSessionResponse>.Failure(new Error(
            IdentityErrorCodes.OnlineSessionNotFound,
            "The online session was not found.",
            ErrorType.NotFound));

    private static Result<RevokeAllHostUserSessionsResponse> UserNotFound() =>
        Result<RevokeAllHostUserSessionsResponse>.Failure(new Error(
            IdentityErrorCodes.UserNotFound,
            "The host user was not found.",
            ErrorType.NotFound));
}
