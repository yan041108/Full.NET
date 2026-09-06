namespace Full.NET.Modules.Notifications.Providers.WeChatMiniProgram;

/// <summary>微信小程序开放平台 HTTP 边界；实现不得记录 OpenId、正文或 Secret。</summary>
internal interface IWeChatMiniProgramTransport
{
    /// <summary>获取小程序全局 access_token。</summary>
    ValueTask<WeChatMiniProgramAccessToken> GetAccessTokenAsync(
        string appId,
        string appSecret,
        CancellationToken cancellationToken);

    /// <summary>使用 js_code 交换 session，返回 OpenId 与可选 UnionId。</summary>
    ValueTask<WeChatMiniProgramSession> ExchangeJsCodeAsync(
        string appId,
        string appSecret,
        string jsCode,
        CancellationToken cancellationToken);

    /// <summary>发送订阅消息并返回微信 msgid。</summary>
    ValueTask<string> SendSubscribeMessageAsync(
        string accessToken,
        WeChatMiniProgramSubscribeSendCommand command,
        CancellationToken cancellationToken);
}

/// <summary>小程序 access_token 及其绝对过期时间。</summary>
internal sealed record WeChatMiniProgramAccessToken(string Token, DateTimeOffset ExpiresAtUtc);

/// <summary>js_code 交换得到的会话信息。</summary>
internal sealed record WeChatMiniProgramSession(string OpenId, string? UnionId);

/// <summary>一次订阅消息发送所需的闭合参数。</summary>
internal sealed record WeChatMiniProgramSubscribeSendCommand(
    string ToUserOpenId,
    string TemplateId,
    string? Page,
    string DataJson,
    string IdempotencyKey);

/// <summary>微信传输失败分类。</summary>
internal enum WeChatMiniProgramTransportFailureKind
{
    Permanent,
    Transient,
    RateLimited,
}

/// <summary>分类后的小程序传输异常。</summary>
internal sealed class WeChatMiniProgramTransportException(
    WeChatMiniProgramTransportFailureKind failureKind,
    string message) : Exception(message)
{
    public WeChatMiniProgramTransportFailureKind FailureKind { get; } = failureKind;
}
