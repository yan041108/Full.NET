using Full.NET.Abstractions.Time;

namespace Full.NET.Modules.Notifications.Providers.WeCom;

/// <summary>按 corpId 缓存企业微信 access_token，并在过期前 60 秒主动续期。</summary>
internal sealed class WeComAccessTokenCache(IWeComTransport transport, IClock clock)
{
    private static readonly TimeSpan RenewalSkew = TimeSpan.FromSeconds(60);
    // 保留既有进程内 Provider 缓存；容量封顶避免历史配置永久累积。
    private const int MaximumEntries = 1024;
    private DateTimeOffset _nextCleanupUtc;
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
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var now = clock.UtcNow;
            RemoveExpired(now);
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
            var now = clock.UtcNow;
            RemoveExpired(now);
            if (issued.ExpiresAtUtc <= now.Add(RenewalSkew)) return issued.Token;
            if (!_tokens.ContainsKey(corpId) && _tokens.Count >= MaximumEntries)
            {
                // 最早失效的条目优先退出；淘汰只增加回源，不延长凭据有效期。
                _tokens.Remove(_tokens.MinBy(pair => pair.Value.ExpiresAtUtc).Key);
            }
            _tokens[corpId] = new CachedToken(issued.Token, issued.ExpiresAtUtc);
        }

        return issued.Token;
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        if (now < _nextCleanupUtc) return;
        _nextCleanupUtc = now.AddMinutes(1);
        foreach (var key in _tokens.Where(pair => pair.Value.ExpiresAtUtc <= now.Add(RenewalSkew))
                     .Select(pair => pair.Key).ToArray())
            _tokens.Remove(key);
    }

    private sealed record CachedToken(string Token, DateTimeOffset ExpiresAtUtc);
}
