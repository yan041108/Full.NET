using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ZiggyCreatures.Caching.Fusion.Internals.Distributed;

namespace Full.NET.Caching.Fusion.Serialization;

/// <summary>登记 FusionCache 标签和清空操作的内部时间戳，不反向依赖业务模块。</summary>
internal sealed class FusionCacheInfrastructureJsonTypeInfoContributor : ICacheJsonTypeInfoContributor
{
    public JsonTypeInfo? GetTypeInfo(Type type) =>
        FusionCacheInfrastructureJsonSerializerContext.Default.GetTypeInfo(type);
}

// 与默认 JIT 序列化器保持同一线格式，标签失效也必须留在 AOT 静态闭包中。
[JsonSerializable(typeof(FusionCacheDistributedEntry<long>))]
internal partial class FusionCacheInfrastructureJsonSerializerContext : JsonSerializerContext;
