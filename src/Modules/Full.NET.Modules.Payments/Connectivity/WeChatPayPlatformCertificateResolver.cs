using System.Collections.Concurrent;
using Full.NET.Modules.Payments.Persistence;

namespace Full.NET.Modules.Payments.Connectivity;

/// <summary>缓存微信支付平台证书公钥，减少重复拉取。</summary>
internal sealed class WeChatPayPlatformCertificateResolver(WeChatNativePayClient weChatClient)
    : IWeChatPayPlatformCertificateResolver
{
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<string?> ResolvePublicKeyPemAsync(
        PaymentMerchantConfigRecord merchantConfig,
        string platformSerialNo,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{merchantConfig.Id:N}:{platformSerialNo}";
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
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

        _cache[cacheKey] = publicKeyPem;
        return publicKeyPem;
    }
}
