namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>租户级设置常量；用于在多模块间稳定引用固定设置记录。</summary>
/// <remarks>
/// 设置标识为固定 GUID，发布后不可更改；新增设置常量只能追加到本类末尾，避免破坏既有配置写入与读取。
/// </remarks>
public static class TenancySettingsConstants
{
    /// <summary>租户设置的固定标识；用于在所有租户中定位同一份设置记录，不可更改。</summary>
    public static readonly Guid SettingsId =
        Guid.Parse("00000000-0000-4000-8000-000000000010");
}