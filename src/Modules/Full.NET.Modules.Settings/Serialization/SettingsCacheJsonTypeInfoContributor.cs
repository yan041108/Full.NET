using System.Text.Json.Serialization.Metadata;
using Full.NET.Caching.Fusion.Serialization;

namespace Full.NET.Modules.Settings.Serialization;

/// <summary>
/// 向缓存基础设施贡献 Settings 所有载荷的源生成 JSON 元数据，避免基础设施反向认识业务契约。
/// </summary>
internal sealed class SettingsCacheJsonTypeInfoContributor : ICacheJsonTypeInfoContributor
{
    public JsonTypeInfo? GetTypeInfo(Type type) =>
        SettingsJsonSerializerContext.Default.GetTypeInfo(type);
}
