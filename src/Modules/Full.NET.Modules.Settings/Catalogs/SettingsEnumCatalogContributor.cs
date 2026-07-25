using Full.NET.Modules.Settings.Contracts;

namespace Full.NET.Modules.Settings.Catalogs;

/// <summary>Settings 模块内置枚举/常量目录（配置值类型）。</summary>
internal sealed class SettingsEnumCatalogContributor : IEnumCatalogContributor
{
    public IReadOnlyCollection<EnumCatalogDefinition> Catalogs { get; } =
    [
        new EnumCatalogDefinition(
            "settings.config_value_kind",
            "配置值类型",
            "系统配置项 ValueKind 稳定机器码。",
            ConfigValueKinds.All
                .Select((kind, index) => new EnumCatalogMemberDefinition(
                    kind,
                    kind,
                    index))
                .ToArray()),
    ];
}
