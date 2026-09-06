using Full.NET.Abstractions.Time;

namespace Full.NET.Modules.Notifications.Providers.WeCom;

/// <summary>按 corpId 缓存企业微信 access_token，并在过期前 60 秒主动续期。</summary>
internal sealed class WeComAccessTokenCache(IWeComTransport transport, IClock clock)
{
    private static readonly TimeSpan RenewalSkew = TimeSpan.FromSeconds(60);
    private readonly object _sync = new();
    private readonly Dictionary<string, CachedToken> _tokens = new(StringComparer.Ordinal);

    /// <summary>返回可用 token；缓存失效时通过 transport 续期。</summary>
    /// <param name="corpId">企业 ID，同时作为缓存键。</param>
    /// <param name="corpSecret">应用 Secret。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async ValueTask<string> GetOrRefreshAsync(
        string corpId,
        string corpSecret,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        lock (_sync)
        {
            if (_tokens.TryGetValue(corpId, out var cached)
                && cached.ExpiresAtUtc > now.Add(RenewalSkew))
            {
                return cached.Token;
            }
        }

        var issued = await transport.GetAccessTokenAsync(corpId, corpSecret, cancellationToken)
            .ConfigureAwait(false);
        lock (_sync)
        {
            _tokens[corpId] = new CachedToken(issued.Token, issued.ExpiresAtUtc);
        }

        return issued.Token;
    }

    private sealed record CachedToken(string Token, DateTimeOffset ExpiresAtUtc);
}
