using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace Full.NET.Caching.Fusion;

/// <summary>FusionCache 专属的有界载荷存储；不限制宿主其他 IMemoryCache 消费者。</summary>
/// <remarks>计量单位为条目权重，不是字节。SDK 标签标记的零权重必须保留，不能因容量淘汰而遗失失效信息。</remarks>
internal sealed class FusionCacheMemoryStore(int capacity) : IMemoryCache
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = capacity });

    /// <inheritdoc />
    public bool TryGetValue(object key, out object? value) => _cache.TryGetValue(key, out value);
    /// <inheritdoc />
    public void Remove(object key) => _cache.Remove(key);
    /// <inheritdoc />
    public ICacheEntry CreateEntry(object key) => new SizedEntry(_cache.CreateEntry(key));
    /// <inheritdoc />
    public void Dispose() => _cache.Dispose();

    // 显式 Fusion 选项和 L2 元数据可能不带 Size；在最终写入边界补齐，避免容量配置导致业务请求异常。
    private sealed class SizedEntry(ICacheEntry inner) : ICacheEntry
    {
        public object Key => inner.Key;
        public object? Value { get => inner.Value; set => inner.Value = value; }
        public DateTimeOffset? AbsoluteExpiration { get => inner.AbsoluteExpiration; set => inner.AbsoluteExpiration = value; }
        public TimeSpan? AbsoluteExpirationRelativeToNow { get => inner.AbsoluteExpirationRelativeToNow; set => inner.AbsoluteExpirationRelativeToNow = value; }
        public TimeSpan? SlidingExpiration { get => inner.SlidingExpiration; set => inner.SlidingExpiration = value; }
        public IList<IChangeToken> ExpirationTokens => inner.ExpirationTokens;
        public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks => inner.PostEvictionCallbacks;
        public CacheItemPriority Priority { get => inner.Priority; set => inner.Priority = value; }
        public long? Size { get => inner.Size; set => inner.Size = value; }
        public void Dispose()
        {
            inner.Size ??= 1;
            inner.Dispose();
        }
    }
}
