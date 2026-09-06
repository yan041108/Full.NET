namespace Full.NET.Modules.Notifications.Providers.WeCom;

/// <summary>企业微信应用消息 HTTP 边界；实现不得记录 userId、正文或 Secret。</summary>
internal interface IWeComTransport
{
    /// <summary>获取 access_token；失败时抛出分类后的 <see cref="WeComTransportException"/>。</summary>
    /// <param name="corpId">企业 ID。</param>
    /// <param name="corpSecret">应用 Secret。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask<WeComAccessToken> GetAccessTokenAsync(
        string corpId,
        string corpSecret,
        CancellationToken cancellationToken);

    /// <summary>发送文本应用消息并返回 msgid；失败时抛出分类后的异常。</summary>
    /// <param name="accessToken">当前有效的 access_token。</param>
    /// <param name="command">闭合发送命令。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask<string> SendTextAsync(
        string accessToken,
        WeComSendTextCommand command,
        CancellationToken cancellationToken);
}

/// <summary>企业微信 access_token 及其绝对过期时间。</summary>
internal sealed record WeComAccessToken(string Token, DateTimeOffset ExpiresAtUtc);

/// <summary>一次文本应用消息发送所需的闭合参数。</summary>
internal sealed record WeComSendTextCommand(
    string ToUserId,
    int AgentId,
    string Content,
    string IdempotencyKey);

/// <summary>企业微信传输失败分类；Adapter 映射为 Worker 重试语义。</summary>
internal enum WeComTransportFailureKind
{
    Permanent,
    Transient,
    RateLimited,
}

/// <summary>分类后的企业微信传输异常。</summary>
internal sealed class WeComTransportException(
    WeComTransportFailureKind failureKind,
    string message) : Exception(message)
{
    public WeComTransportFailureKind FailureKind { get; } = failureKind;
}
