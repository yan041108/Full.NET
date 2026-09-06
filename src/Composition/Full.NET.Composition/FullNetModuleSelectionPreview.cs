using Full.NET.Modularity.Modules;
using Microsoft.Extensions.Configuration;

namespace Full.NET.Composition;

/// <summary>
/// 基于 Composition 官方模块闭包分析 <c>FullNet:Modules</c> 配置，不修改运行时注册。
/// </summary>
internal sealed class FullNetModuleSelectionPreview(
    IReadOnlyList<IFullNetModule> allModules) : IFullNetModuleSelectionPreview
{
    /// <inheritdoc />
    public ModuleSelectionAnalysis AnalyzeRuntime(IConfiguration configuration) =>
        FullNetModuleSelection.AnalyzeConfiguration(configuration, allModules);

    /// <inheritdoc />
    public ModuleSelectionAnalysis AnalyzeCandidate(
        string? preset,
        IReadOnlyList<string>? enabled) =>
        FullNetModuleSelection.AnalyzeOptions(
            new FullNetModuleSelectionOptions
            {
                Preset = preset ?? FullNetModuleSelectionOptions.Presets.Full,
                Enabled = enabled?.Count > 0 ? enabled.ToArray() : null,
            },
            allModules);
}
