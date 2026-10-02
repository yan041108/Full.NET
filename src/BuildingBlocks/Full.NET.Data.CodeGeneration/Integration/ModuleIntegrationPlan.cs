using System.Collections.ObjectModel;

namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>
/// 标识模块接入计划中的固定影响区域。
/// </summary>
/// <remarks>
/// 枚举成员数值发布后不可调整；新增成员只能追加到末尾，以保持按 Area 排序的确定性。
/// </remarks>
public enum ModuleIntegrationArea
{
    /// <summary>后端代码生成产物（如实体、DTO、迁移脚本等）的输出目录。</summary>
    BackendArtifacts = 1,
    /// <summary>模块自身的项目文件（.csproj）及程序集级配置。</summary>
    ModuleProject = 2,
    /// <summary>模块的服务层注册与依赖注入配置。</summary>
    ModuleServices = 3,
    /// <summary>模块对外暴露的 HTTP Endpoint 路由与控制器注册。</summary>
    ModuleEndpoints = 4,
    /// <summary>宿主组合层（Composition）的项目文件及程序集引用。</summary>
    CompositionProject = 5,
    /// <summary>组合层的模块目录（Catalog）注册，决定模块是否被加载。</summary>
    CompositionCatalog = 6,
    /// <summary>前端 Vue 路由配置，影响 Vue 工作台的菜单与页面路由。</summary>
    VueRoute = 7,
    /// <summary>前端 Layui 路由配置，影响 Layui 工作台的菜单与页面路由。</summary>
    LayuiRoute = 8,
}

/// <summary>
/// 标识只读规划对一个影响区域的保守判定。
/// </summary>
/// <remarks>
/// 枚举成员数值发布后不可调整；新增成员只能追加到末尾，以保持线格式兼容。
/// </remarks>
public enum ModuleIntegrationStatus
{
    /// <summary>影响区域已与目标状态对齐，无需任何改动；可静默跳过。</summary>
    Satisfied = 1,
    /// <summary>存在可由 IntegrationEditor 自动安全处理的差异，应用后无需人工复核。</summary>
    ChangeRequired = 2,
    /// <summary>规划器无法推断拓扑或存在歧义，必须由开发者人工确认后再应用。</summary>
    ManualReview = 3,
    /// <summary>检测到冲突或依赖缺失，禁止继续应用，必须先修复根因。</summary>
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
