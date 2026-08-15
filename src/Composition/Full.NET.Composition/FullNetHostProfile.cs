namespace Full.NET.Composition;

/// <summary>
/// 定义 Full.NET 官方宿主可选择的显式模块装配范围。
/// </summary>
/// <remarks>
/// 组合根作为模块唯一装配点，按 Profile 角色选择性注入模块能力：
/// <list type="bullet">
/// <item><see cref="Api"/>: 完整模块装配，注入 AddServices 并映射 Endpoint/中间件</item>
/// <item><see cref="Worker"/>: 仅装配后台能力，调用 AddBackgroundServices 避免 HTTP 依赖</item>
/// <item><see cref="Migrator"/>: 仅装配迁移能力，调用 AddMigrationServices 不暴露 Endpoint</item>
/// </list>
/// 新增角色须同步在 <see cref="FullNetModuleCatalog.OfficialHostProfiles"/> 中登记，禁止宿主自行跳过 Composition 直接引用模块实现。
/// </remarks>
public enum FullNetHostProfile
{
    /// <summary>
    /// API 宿主角色：承载完整 HTTP 模块与 Endpoint。
    /// 装配 AddServices + 映射 MapEndpoints + 注入四阶段中间件管道。
    /// </summary>
    Api,

    /// <summary>
    /// Worker 宿主角色：只承载后台消费者最小依赖。
    /// 仅装配 AddBackgroundServices，避免引入认证、HTTP 绑定和完整模块依赖图。
    /// </summary>
    Worker,

    /// <summary>
    /// Migrator 宿主角色：承载数据库迁移、初始化领域服务且不映射 Endpoint。
    /// 仅装配 AddMigrationServices，通过 DbUp 脚本 + SeedOrchestrator 完成初始化。
    /// </summary>
    Migrator,

    /// <summary>
    /// Test 宿主角色：供架构测试与集成测试使用的最小化装配。
    /// 按需启用模块子集以隔离测试依赖，不启动真实宿主进程。
    /// </summary>
    Test,
}
