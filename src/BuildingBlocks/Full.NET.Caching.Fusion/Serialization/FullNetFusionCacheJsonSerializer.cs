using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ZiggyCreatures.Caching.Fusion.Serialization;

namespace Full.NET.Caching.Fusion.Serialization;

/// <summary>
/// FusionCache L2 的 AOT 安全 JSON 序列化器；仅支持
/// <see cref="FusionCacheJsonSerializerContext"/> 显式登记的类型。
/// </summary>
internal sealed class FullNetFusionCacheJsonSerializer : IFusionCacheSerializer
{
    public byte[] Serialize<T>(T? data) => SerializeCore(data);

    public ValueTask<byte[]> SerializeAsync<T>(
        T? data,
        CancellationToken token = default) =>
        new(SerializeCore(data));

    public T? Deserialize<T>(byte[] data) => DeserializeCore<T>(data);

    public ValueTask<T?> DeserializeAsync<T>(
        byte[] data,
        CancellationToken token = default) =>
        new(DeserializeCore<T>(data));

    private static byte[] SerializeCore<T>(T? data)
    {
        if (data is null)
        {
            return [];
        }

        var typeInfo = ResolveTypeInfo(typeof(T));
        return JsonSerializer.SerializeToUtf8Bytes(data, typeInfo);
    }

    private static T? DeserializeCore<T>(byte[] data)
    {
        if (data.Length == 0)
        {
            return default;
        }

        var typeInfo = ResolveTypeInfo(typeof(T));
        return JsonSerializer.Deserialize(data, typeInfo) is T typed ? typed : default;
    }

    private static JsonTypeInfo ResolveTypeInfo(Type type)
    {
        var typeInfo = FusionCacheJsonSerializerContext.Default.GetTypeInfo(type);
        if (typeInfo is null)
        {
            throw new NotSupportedException(
                $"FusionCache L2 类型 {type.FullName} 未登记在 FusionCacheJsonSerializerContext；"
                + "新增 HybridCache/IFusionCache 载荷时必须扩展源生成上下文。");
        }

        return typeInfo;
    }
}
