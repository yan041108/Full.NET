namespace Full.NET.Composition;

/// <summary>
/// 运行时模块裁剪配置；Composition 编译闭包仍包含全部官方模块实现，仅控制注册到 DI 的子集。
/// </summary>
public sealed class FullNetModuleSelectionOptions
{
    public const string SectionName = "FullNet:Modules";

    /// <summary>
    /// 预设名称：<see cref="Presets.Full"/>（默认）或 <see cref="Presets.Minimal"/>。
    /// </summary>
    public string Preset { get; init; } = Presets.Full;

    /// <summary>
    /// 显式启用的模块稳定键；非空时覆盖 <see cref="Preset"/>。
    /// </summary>
    public string[]? Enabled { get; init; }

    public static class Presets
    {
        public const string Full = "Full";

        /// <summary>
        /// 快速底座：Identity + Tenancy + Settings + Organization。
        /// </summary>
        public const string Minimal = "Minimal";
    }
}
