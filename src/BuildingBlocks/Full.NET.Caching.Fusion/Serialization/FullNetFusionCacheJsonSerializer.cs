using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ZiggyCreatures.Caching.Fusion.Serialization;

namespace Full.NET.Caching.Fusion.Serialization;

/// <summary>
/// FusionCache L2 的 AOT 安全 JSON 序列化器；仅支持载荷所有者通过
/// <see cref="ICacheJsonTypeInfoContributor"/> 显式登记的类型。
/// </summary>
internal sealed class FullNetFusionCacheJsonSerializer(
    IEnumerable<ICacheJsonTypeInfoContributor> contributors) : IFusionCacheSerializer
{
    private readonly ICacheJsonTypeInfoContributor[] _contributors =
        contributors?.ToArray() ?? throw new ArgumentNullException(nameof(contributors));

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

    private byte[] SerializeCore<T>(T? data)
    {
        if (data is null)
        {
            return [];
        }

        var typeInfo = ResolveTypeInfo(typeof(T));
        return JsonSerializer.SerializeToUtf8Bytes(data, typeInfo);
    }

    private T? DeserializeCore<T>(byte[] data)
    {
        if (data.Length == 0)
        {
            return default;
        }

        var typeInfo = ResolveTypeInfo(typeof(T));
        return JsonSerializer.Deserialize(data, typeInfo) is T typed ? typed : default;
    }

    private JsonTypeInfo ResolveTypeInfo(Type type)
    {
        // 标签与清空标记由 SDK 使用 long 包装；业务包装仍由载荷所有者显式贡献。
        if (FusionCacheJsonSerializerContext.Default.GetTypeInfo(type) is { } infrastructureTypeInfo)
            return infrastructureTypeInfo;

        foreach (var contributor in _contributors)
        {
            if (contributor.GetTypeInfo(type) is { } typeInfo)
            {
                return typeInfo;
            }
        }

        throw new NotSupportedException(
            $"FusionCache L2 类型 {type.FullName} 未由 ICacheJsonTypeInfoContributor 登记；"
            + "新增 HybridCache/IFusionCache 载荷时必须由载荷所有者贡献源生成 JSON 元数据。");
    }
}
