using Full.NET.Realtime;

namespace Full.NET.Modules.Identity.Features.ManageHostOnlineSessions;

/// <summary>
/// 向受影响用户推送会话撤销实时通知；客户端按 sessionId 比对当前 sid 后清理本地凭据。
/// </summary>
internal sealed class IdentitySessionRealtimeDelivery(IRealtimePublisher realtimePublisher)
{
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
        }
    }
}
