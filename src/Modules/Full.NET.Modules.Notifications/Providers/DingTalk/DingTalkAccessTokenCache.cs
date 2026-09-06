using Full.NET.Abstractions.Time;

namespace Full.NET.Modules.Notifications.Providers.DingTalk;

/// <summary>按 appKey 缓存钉钉 access_token，并在过期前 60 秒主动续期。</summary>
internal sealed class DingTalkAccessTokenCache(IDingTalkTransport transport, IClock clock)
{
    private static readonly TimeSpan RenewalSkew = TimeSpan.FromSeconds(60);
    private readonly object _sync = new();
    private readonly Dictionary<string, CachedToken> _tokens = new(StringComparer.Ordinal);

    /// <summary>返回可用 token；缓存失效时通过 transport 续期。</summary>
    /// <param name="appKey">应用 AppKey，同时作为缓存键。</param>
    /// <param name="appSecret">应用 AppSecret。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async ValueTask<string> GetOrRefreshAsync(
        string appKey,
        string appSecret,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        lock (_sync)
        {
            if (_tokens.TryGetValue(appKey, out var cached)
                && cached.ExpiresAtUtc > now.Add(RenewalSkew))
            {
                return cached.Token;
            }
        }

        var issued = await transport.GetAccessTokenAsync(appKey, appSecret, cancellationToken)
            .ConfigureAwait(false);
        lock (_sync)
        {
            _tokens[appKey] = new CachedToken(issued.Token, issued.ExpiresAtUtc);
        }

        return issued.Token;
    }

    private sealed record CachedToken(string Token, DateTimeOffset ExpiresAtUtc);
}
