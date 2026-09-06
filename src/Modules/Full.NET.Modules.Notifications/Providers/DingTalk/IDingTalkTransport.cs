namespace Full.NET.Modules.Notifications.Providers.DingTalk;

/// <summary>钉钉开放平台 HTTP 边界；实现不得记录 userId、卡片参数或 Secret。</summary>
internal interface IDingTalkTransport
{
    /// <summary>获取 access_token；失败时抛出分类后的 <see cref="DingTalkTransportException"/>。</summary>
    /// <param name="appKey">应用 AppKey。</param>
    /// <param name="appSecret">应用 AppSecret。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask<DingTalkAccessToken> GetAccessTokenAsync(
        string appKey,
        string appSecret,
        CancellationToken cancellationToken);

    /// <summary>调用 createAndDeliver 并返回 outTrackId；失败时抛出分类后的异常。</summary>
    /// <param name="accessToken">当前有效的 access_token。</param>
    /// <param name="command">闭合投放命令。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask<string> CreateAndDeliverAsync(
        string accessToken,
        DingTalkCreateAndDeliverCommand command,
        CancellationToken cancellationToken);
}

/// <summary>钉钉 access_token 及其绝对过期时间。</summary>
internal sealed record DingTalkAccessToken(string Token, DateTimeOffset ExpiresAtUtc);

/// <summary>一次互动卡片 createAndDeliver 所需的闭合参数。</summary>
internal sealed record DingTalkCreateAndDeliverCommand(
    string CardTemplateId,
    string OutTrackId,
    string OpenSpaceId,
    string RobotCode,
    string? CallbackRouteKey,
    IReadOnlyDictionary<string, string> CardParamMap);

/// <summary>钉钉传输失败分类；Adapter 映射为 Worker 重试语义。</summary>
internal enum DingTalkTransportFailureKind
{
    Permanent,
    Transient,
    RateLimited,
}

/// <summary>分类后的钉钉传输异常。</summary>
internal sealed class DingTalkTransportException(
    DingTalkTransportFailureKind failureKind,
    string message) : Exception(message)
{
    public DingTalkTransportFailureKind FailureKind { get; } = failureKind;
}
