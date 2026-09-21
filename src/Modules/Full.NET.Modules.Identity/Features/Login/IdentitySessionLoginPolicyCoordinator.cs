using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostOnlineSessions;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.Login;

/// <summary>
/// 登录成功后按 <see cref="IdentitySessionLoginPolicy"/> 撤销并列活跃刷新会话。
/// </summary>
internal static class IdentitySessionLoginPolicyCoordinator
{
    public static async Task EnforceAfterSuccessfulLoginAsync(
        IdentitySessionLoginPolicy policy,
        Guid userId,
        Guid sessionId,
        string clientId,
        IQueryExecutor queryExecutor,
        ICommandExecutor commandExecutor,
        IClock clock,
        IdentitySessionRealtimeDelivery sessionRealtimeDelivery,
        CancellationToken cancellationToken)
    {
        if (policy == IdentitySessionLoginPolicy.AllowMultiple)
        {
            return;
        }

        var listSql = policy == IdentitySessionLoginPolicy.SingleSession
            ? OnlineSessionSql.ListActiveHostSessionIdsByUserExcept
            : OnlineSessionSql.ListActiveHostSessionIdsByUserAndClientExcept;
        var revokeSql = policy == IdentitySessionLoginPolicy.SingleSession
            ? IdentitySql.RevokeUserSessionsExcept
            : IdentitySql.RevokeUserSessionsByClientExcept;

        var parameters = policy == IdentitySessionLoginPolicy.SingleSession
            ? IdentitySqlParameters.Create(
                ("UserId", userId),
                ("ExceptSessionId", sessionId),
                ("NowUtc", clock.UtcNow))
            : IdentitySqlParameters.Create(
                ("UserId", userId),
                ("ClientId", clientId),
                ("ExceptSessionId", sessionId),
                ("NowUtc", clock.UtcNow));

        var revokedSessionIds = (await queryExecutor.QueryAsync<Guid>(
                    listSql,
                    parameters,
                    cancellationToken)
                .ConfigureAwait(false))
            .ToArray();
        if (revokedSessionIds.Length == 0)
        {
            return;
        }

        var revokeParameters = policy == IdentitySessionLoginPolicy.SingleSession
            ? IdentitySqlParameters.Create(
                ("UserId", userId),
                ("ExceptSessionId", sessionId),
                ("RevokedAtUtc", clock.UtcNow))
            : IdentitySqlParameters.Create(
                ("UserId", userId),
                ("ClientId", clientId),
                ("ExceptSessionId", sessionId),
                ("RevokedAtUtc", clock.UtcNow));

        var revokedRows = await commandExecutor.ExecuteAsync(
                revokeSql,
                revokeParameters,
                cancellationToken)
            .ConfigureAwait(false);
        if (revokedRows > 0)
        {
            await sessionRealtimeDelivery.PublishSessionsRevokedAsync(
                    userId,
                    revokedSessionIds,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
