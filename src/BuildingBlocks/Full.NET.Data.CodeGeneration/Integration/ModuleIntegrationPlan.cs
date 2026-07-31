using System.Collections.ObjectModel;

namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>
/// 标识模块接入计划中的固定影响区域。
/// </summary>
public enum ModuleIntegrationArea
{
    BackendArtifacts = 1,
    ModuleProject = 2,
    ModuleServices = 3,
    ModuleEndpoints = 4,
    CompositionProject = 5,
    CompositionCatalog = 6,
    VueRoute = 7,
    LayuiRoute = 8,
}

/// <summary>
/// 标识只读规划对一个影响区域的保守判定。
/// </summary>
public enum ModuleIntegrationStatus
{
    Satisfied = 1,
    ChangeRequired = 2,
    ManualReview = 3,
    Blocked = 4,
}

/// <summary>
/// 表示一个不会自动应用的模块接入建议。
/// </summary>
public sealed record ModuleIntegrationPlanItem(
    ModuleIntegrationArea Area,
    ModuleIntegrationStatus Status,
    string RelativePath,
    string Instruction);

/// <summary>
/// 保存按固定影响区域排序的模块接入只读计划。
/// </summary>
public sealed class ModuleIntegrationPlan
{
    internal ModuleIntegrationPlan(
        IEnumerable<ModuleIntegrationPlanItem> items)
    {
        Items = new ReadOnlyCollection<ModuleIntegrationPlanItem>(
            items.ToArray());
    }

    public IReadOnlyList<ModuleIntegrationPlanItem> Items { get; }
}
