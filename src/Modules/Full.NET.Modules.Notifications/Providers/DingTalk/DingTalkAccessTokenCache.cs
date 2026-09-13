using Full.NET.Abstractions.Time;

namespace Full.NET.Modules.Notifications.Providers.DingTalk;

/// <summary>按 appKey 缓存钉钉 access_token，并在过期前 60 秒主动续期。</summary>
internal sealed class DingTalkAccessTokenCache(IDingTalkTransport transport, IClock clock)
{
    private static readonly TimeSpan RenewalSkew = TimeSpan.FromSeconds(60);
    // 保留既有进程内 Provider 缓存；容量封顶避免历史配置永久累积。
    private const int MaximumEntries = 1024;
    private DateTimeOffset _nextCleanupUtc;
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
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var now = clock.UtcNow;
            RemoveExpired(now);
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
            var now = clock.UtcNow;
            RemoveExpired(now);
            if (issued.ExpiresAtUtc <= now.Add(RenewalSkew)) return issued.Token;
            if (!_tokens.ContainsKey(appKey) && _tokens.Count >= MaximumEntries)
            {
                // 最早失效的条目优先退出；淘汰只增加回源，不延长凭据有效期。
                _tokens.Remove(_tokens.MinBy(pair => pair.Value.ExpiresAtUtc).Key);
            }
            _tokens[appKey] = new CachedToken(issued.Token, issued.ExpiresAtUtc);
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
