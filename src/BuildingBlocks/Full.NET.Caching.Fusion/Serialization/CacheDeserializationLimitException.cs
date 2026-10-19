namespace Full.NET.Caching.Fusion.Serialization;

/// <summary>表示 L2 输入超过解析前的全局字节上限；调用方可明确识别此拒绝，不能将其当成合法空值。</summary>
public sealed class CacheDeserializationLimitException : InvalidOperationException
{
    /// <summary>创建不包含缓存键、载荷内容或凭据的输入拒绝异常。</summary>
    /// <param name="maximumBytes">当前全局解析字节上限。</param>
    public CacheDeserializationLimitException(int maximumBytes)
        : base("Cache serialized input exceeds the configured deserialization limit.")
    {
        MaximumBytes = maximumBytes;
    }

    /// <summary>触发拒绝时的配置上限，不包含实际载荷内容。</summary>
    public int MaximumBytes { get; }
}
