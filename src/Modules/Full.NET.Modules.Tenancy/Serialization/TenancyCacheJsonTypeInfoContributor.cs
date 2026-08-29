using System.Text.Json.Serialization.Metadata;
using Full.NET.Caching.Fusion.Serialization;

namespace Full.NET.Modules.Tenancy.Serialization;

/// <summary>
/// 向缓存基础设施贡献租户解析载荷的源生成 JSON 元数据，保持 Native AOT 静态闭包。
/// </summary>
internal sealed class TenancyCacheJsonTypeInfoContributor : ICacheJsonTypeInfoContributor
{
    public JsonTypeInfo? GetTypeInfo(Type type) =>
        TenancyJsonSerializerContext.Default.GetTypeInfo(type);
}
