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
/// <remarks>
/// 该 record 只是只读规划产物，不进入写盘计划。所有包含 ManualReview 或
/// Blocked 的 <see cref="ModuleIntegrationPlan"/> 必须先由开发者人工确认，
/// 再通过对应的 IntegrationEditor.Apply 显式应用，禁止静默跳过。
/// </remarks>
/// <param name="Area">
/// 影响区域枚举值。用于前端工作台按区域分组展示，并决定后续调用
/// 哪一类 IntegrationEditor 来执行自动应用。
/// </param>
/// <param name="Status">
/// 保守判定结果。Satisfied 表示已对齐无需改动；ChangeRequired 表示
/// 有自动编辑器可安全处理；ManualReview 表示无法推断拓扑，需人工介入；
/// Blocked 表示检测到冲突或依赖缺失，禁止继续。
/// </param>
/// <param name="RelativePath">
/// 仓库相对路径；指向将要被检查或编辑的目标文件。必须已通过
/// <see cref="GenerationArtifactPath"/> 可移植性校验，禁止使用绝对路径。
/// </param>
/// <param name="Instruction">
/// 给开发者的简短中文操作说明；当 Status = ManualReview/Blocked 时
/// 该字段必须包含可执行的修复步骤，不应只给出笼统描述。
/// </param>
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

    /// <summary>
    /// 获取按 Area 排序的只读接入项集合；每项声明影响区域、保守判定状态、相对路径与人工操作说明。
    /// 确定性：排序使用 ModuleIntegrationArea 枚举值的数值序，与文化无关；含重复路径立即 FAIL-closed 抛异常。
    /// </summary>
    public IReadOnlyList<ModuleIntegrationPlanItem> Items { get; }
}
