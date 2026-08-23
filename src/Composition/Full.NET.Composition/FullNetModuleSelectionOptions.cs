namespace Full.NET.Composition;

/// <summary>
/// 运行时模块裁剪配置；Composition 编译闭包仍包含全部官方模块实现，仅控制注册到 DI 的子集。
/// </summary>
public sealed class FullNetModuleSelectionOptions
{
    /// <summary>
    /// 运行时模块裁剪配置节的稳定路径 <c>FullNet:Modules</c>。
    /// </summary>
    public const string SectionName = "FullNet:Modules";

    /// <summary>
    /// 预设名称：<see cref="Presets.Full"/>（默认）、<see cref="Presets.Minimal"/>、
    /// <see cref="Presets.Platform"/> 或 <see cref="Presets.Content"/>。
    /// </summary>
    public string Preset { get; init; } = Presets.Full;

    /// <summary>
    /// 显式启用的模块稳定键；非空时覆盖 <see cref="Preset"/>。
    /// </summary>
    public string[]? Enabled { get; init; }

    /// <summary>
    /// 预设模块集合稳定名称；<see cref="Full"/> 为默认，其余预设按底座能力逐级叠加。
    /// </summary>
    public static class Presets
    {
        /// <summary>
        /// 默认预设：注册全部官方模块，与 <see cref="FullNetModuleCatalog"/> 编译闭包一致。
        /// </summary>
        public const string Full = "Full";

        /// <summary>
        /// 快速底座：Identity + Tenancy + Settings + Organization。
        /// </summary>
        public const string Minimal = "Minimal";

        /// <summary>
        /// 平台底座：Minimal + Auditing + Notifications + Jobs + Messaging。
        /// </summary>
        public const string Platform = "Platform";

        /// <summary>
        /// 内容底座：Platform + Files + Document。
        /// </summary>
        public const string Content = "Content";
    }
}
