using Full.NET.Abstractions.Time;

namespace Full.NET.Modules.Notifications.Providers.WeChatMiniProgram;

/// <summary>按 appId 缓存小程序 access_token，并在过期前 60 秒主动刷新。</summary>
internal sealed class WeChatMiniProgramAccessTokenCache(IWeChatMiniProgramTransport transport, IClock clock)
{
    private const int RenewalSkewSeconds = 60;
    private readonly object _sync = new();
    private string? _appId;
    private string? _token;
    private DateTimeOffset _expiresAtUtc;

    /// <summary>返回当前有效 token；过期或 appId 变化时刷新。</summary>
    public async ValueTask<string> GetOrRefreshAsync(
        string appId,
        string appSecret,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (string.Equals(_appId, appId, StringComparison.Ordinal)
                && _token is { Length: > 0 } cached
                && clock.UtcNow < _expiresAtUtc.AddSeconds(-RenewalSkewSeconds))
            {
                return cached;
            }
        }

        var fresh = await transport.GetAccessTokenAsync(appId, appSecret, cancellationToken)
            .ConfigureAwait(false);
        lock (_sync)
        {
            _appId = appId;
            _token = fresh.Token;
            _expiresAtUtc = fresh.ExpiresAtUtc;
            return fresh.Token;
        }
    }
}
