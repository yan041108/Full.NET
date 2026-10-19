using System.Text.Json.Serialization.Metadata;
using Full.NET.Caching.Fusion.Serialization;
using Full.NET.Modules.Settings.Contracts;

namespace Full.NET.Modules.Settings.Serialization;

/// <summary>
/// 只向缓存基础设施贡献真实 Grid 载荷的源生成 JSON 元数据，避免基础设施反向认识业务契约。
/// </summary>
internal sealed class SettingsCacheJsonTypeInfoContributor : ICacheJsonTypeInfoContributor
{
    public JsonTypeInfo? GetTypeInfo(Type type) =>
        type == typeof(GridPreferenceResponse)
            ? SettingsJsonSerializerContext.Default.GridPreferenceResponse
            : SettingsCacheJsonSerializerContext.Default.GetTypeInfo(type);
}
