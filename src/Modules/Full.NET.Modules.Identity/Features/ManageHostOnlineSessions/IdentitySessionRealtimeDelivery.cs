using Full.NET.Modules.Identity.Observability;
using Full.NET.Realtime;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Identity.Features.ManageHostOnlineSessions;

/// <summary>
/// 向受影响用户推送会话撤销实时通知；客户端按 sessionId 比对当前 sid 后清理本地凭据。
/// </summary>
internal sealed class IdentitySessionRealtimeDelivery(
    IRealtimePublisher realtimePublisher,
    ILogger<IdentitySessionRealtimeDelivery> logger)
{
    private const int MaxPublishAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>向指定用户逐条推送已撤销会话标识。</summary>
    /// <param name="userId">会话所属用户。</param>
    /// <param name="sessionIds">已撤销的刷新会话标识集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task PublishSessionsRevokedAsync(
        Guid userId,
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken = default)
    {
        if (sessionIds.Count == 0)
        {
            return;
        }

        foreach (var sessionId in sessionIds)
        {
            await PublishSessionRevokedWithBoundedRetryAsync(
                    userId,
                    sessionId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task PublishSessionRevokedWithBoundedRetryAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxPublishAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await realtimePublisher.PublishToUserAsync(
                        userId,
                        new RealtimeMessage(
                            RealtimeMessageCodes.SessionRevoked,
                            new Dictionary<string, object?>
                            {
                                ["sessionId"] = sessionId,
                            }),
                        cancellationToken)
                    .ConfigureAwait(false);
                IdentitySessionRevokeRealtimeTelemetry.RecordAttempt("success");
                return;
            }
            catch (Exception ex) when (attempt < MaxPublishAttempts)
            {
                IdentitySessionRevokeRealtimeTelemetry.RecordAttempt("retry");
                logger.LogWarning(
                    ex,
                    "OIDC session revoke realtime publish attempt {Attempt}/{MaxAttempts} failed for user {UserId} session {SessionId}.",
                    attempt,
                    MaxPublishAttempts,
                    userId,
                    sessionId);
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                IdentitySessionRevokeRealtimeTelemetry.RecordAttempt("exhausted");
                logger.LogError(
                    ex,
                    "OIDC session revoke realtime publish exhausted {MaxAttempts} attempts for user {UserId} session {SessionId}; authoritative revoke already committed.",
                    MaxPublishAttempts,
                    userId,
                    sessionId);
            }
        }
    }
}
