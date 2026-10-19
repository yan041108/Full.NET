using ZiggyCreatures.Caching.Fusion.Serialization;

namespace Full.NET.Caching.Fusion.Serialization;

/// <summary>在 SDK 原序列化器外限制解析输入；保持写入格式和类型元数据归属，不按类型猜测条目业务预算。</summary>
internal sealed class BoundedFusionCacheSerializer : IFusionCacheSerializer
{
    private readonly IFusionCacheSerializer _inner;
    private readonly int _maximumBytes;

    /// <summary>创建同步和异步共用的解析前输入保护。</summary>
    /// <param name="inner">JIT 或源生成原序列化器。</param>
    /// <param name="maximumBytes">全局输入字节上限。</param>
    public BoundedFusionCacheSerializer(IFusionCacheSerializer inner, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (maximumBytes is < 1 or > CacheOptions.MaximumDeserializationBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        _inner = inner;
        _maximumBytes = maximumBytes;
    }

    /// <inheritdoc />
    public byte[] Serialize<T>(T? data) => _inner.Serialize(data);

    /// <inheritdoc />
    public ValueTask<byte[]> SerializeAsync<T>(T? data, CancellationToken token = default) =>
        _inner.SerializeAsync(data, token);

    /// <inheritdoc />
    public T? Deserialize<T>(byte[] data)
    {
        CheckInput(data);
        return _inner.Deserialize<T>(data);
    }

    /// <inheritdoc />
    public ValueTask<T?> DeserializeAsync<T>(byte[] data, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        CheckInput(data);
        return _inner.DeserializeAsync<T>(data, token);
    }

    private void CheckInput(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length <= _maximumBytes) return;
        CacheReliabilityTelemetry.RecordDeserializationRejection();
        throw new CacheDeserializationLimitException(_maximumBytes);
    }
}
