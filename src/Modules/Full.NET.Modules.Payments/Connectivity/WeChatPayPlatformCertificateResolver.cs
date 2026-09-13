using Full.NET.Abstractions.Time;
using Full.NET.Modules.Payments.Persistence;

namespace Full.NET.Modules.Payments.Connectivity;

/// <summary>缓存微信支付平台证书公钥，减少重复拉取。</summary>
internal sealed class WeChatPayPlatformCertificateResolver(WeChatNativePayClient weChatClient, IClock clock)
    : IWeChatPayPlatformCertificateResolver
{
    // 固定条目预算和绝对有效期限制历史商户、轮换证书的驻留；失效后重新回源。
    private const int MaximumEntries = 1024;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);
    private readonly object _sync = new();
    private readonly Dictionary<string, CachedKey> _cache = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _nextCleanupUtc;

    /// <inheritdoc />
    public async Task<string?> ResolvePublicKeyPemAsync(
        PaymentMerchantConfigRecord merchantConfig,
        string platformSerialNo,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{merchantConfig.Id:N}:{platformSerialNo}";
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            RemoveExpired(clock.UtcNow);
            if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAtUtc > clock.UtcNow)
                return cached.Pem;
        }

        var publicKeyPem = await weChatClient.FetchPlatformPublicKeyPemAsync(
                merchantConfig,
                platformSerialNo,
                cancellationToken)
            .ConfigureAwait(false);
        if (publicKeyPem is null)
        {
            return null;
        }

        lock (_sync)
        {
            var now = clock.UtcNow;
            RemoveExpired(now);
            if (!_cache.ContainsKey(cacheKey) && _cache.Count >= MaximumEntries)
                _cache.Remove(_cache.MinBy(pair => pair.Value.ExpiresAtUtc).Key);
            _cache[cacheKey] = new CachedKey(publicKeyPem, now.Add(CacheDuration));
        }
        return publicKeyPem;
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        if (now < _nextCleanupUtc) return;
        _nextCleanupUtc = now.AddMinutes(1);
        foreach (var key in _cache.Where(pair => pair.Value.ExpiresAtUtc <= now)
                     .Select(pair => pair.Key).ToArray())
            _cache.Remove(key);
    }

    private sealed record CachedKey(string Pem, DateTimeOffset ExpiresAtUtc);
}
