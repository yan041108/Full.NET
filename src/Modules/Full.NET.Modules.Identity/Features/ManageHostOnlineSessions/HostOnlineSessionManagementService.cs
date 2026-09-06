using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.ManageHostOnlineSessions;

/// <summary>Host 在线会话强制下线；撤销整个刷新令牌族以阻断后续轮换，并写入审计与实时通知。</summary>
internal sealed class HostOnlineSessionManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock,
    IIdGenerator idGenerator,
    IdentitySessionRealtimeDelivery realtimeDelivery)
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
        var record = await queryExecutor.QuerySingleOrDefaultAsync<OnlineSessionRevokeRow>(
                OnlineSessionSql.FindActiveHostSessionById,
                IdentitySqlParameters.Create(
                    ("SessionId", sessionId),
                    ("NowUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        var snapshot = Map(record);
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.RevokeRefreshFamily,
                IdentitySqlParameters.Create(
                    ("FamilyId", record.FamilyId),
                    ("RevokedAtUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows < 1)
        {
            return NotFound();
        }

        await WriteAuditAsync(
                actorUserId,
                record.UserId,
                sessionId,
                RevokeAuditEventType,
                $"session:{sessionId:D}",
                ipAddress,
                userAgent,
                cancellationToken)
            .ConfigureAwait(false);
        await realtimeDelivery.PublishSessionsRevokedAsync(
                record.UserId,
                [sessionId],
                cancellationToken)
            .ConfigureAwait(false);
        return Result<HostOnlineSessionResponse>.Success(snapshot);
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

        var sessionIds = (await queryExecutor.QueryAsync<Guid>(
                    OnlineSessionSql.ListActiveHostSessionIdsByUserExcept,
                    IdentitySqlParameters.Create(
                        ("UserId", userId),
                        ("ExceptSessionId", Guid.Empty),
                        ("NowUtc", clock.UtcNow)),
                    cancellationToken)
                .ConfigureAwait(false))
            .ToArray();
        if (sessionIds.Length == 0)
        {
            return Result<RevokeAllHostUserSessionsResponse>.Success(
                new RevokeAllHostUserSessionsResponse(
                    user.Id,
                    user.Username,
                    user.DisplayName,
                    0));
        }

        var revokedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.RevokeAllUserSessions,
                IdentitySqlParameters.Create(
                    ("UserId", userId),
                    ("RevokedAtUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
        if (revokedRows < 1)
        {
            return Result<RevokeAllHostUserSessionsResponse>.Success(
                new RevokeAllHostUserSessionsResponse(
                    user.Id,
                    user.Username,
                    user.DisplayName,
                    0));
        }

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
