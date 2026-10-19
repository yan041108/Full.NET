using Full.NET.Abstractions.Time;

namespace Full.NET.Modules.Notifications.Providers.WeChatMiniProgram;

/// <summary>按完整凭据指纹缓存令牌，合并刷新并在宿主关闭时排空在途请求。</summary>
internal sealed class WeChatMiniProgramAccessTokenCache(IWeChatMiniProgramTransport transport, IClock clock) : IAsyncDisposable
{
    private readonly ProviderTokenRefreshCache _cache = new(clock);

    /// <summary>返回有效令牌；调用者取消只终止自己的等待。</summary>
    public ValueTask<string> GetOrRefreshAsync(string identity, string secret, CancellationToken cancellationToken) =>
        _cache.GetAsync(identity, secret, async token =>
        {
            var issued = await transport.GetAccessTokenAsync(identity, secret, token).ConfigureAwait(false);
            return (issued.Token, issued.ExpiresAtUtc);
        }, cancellationToken);

    /// <inheritdoc cref="ProviderTokenRefreshCache.DisposeAsync" />
    public ValueTask DisposeAsync() => _cache.DisposeAsync();
}