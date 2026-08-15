namespace Full.NET.Data.Abstractions;

/// <summary>
/// 数据库连接与行为的强类型配置选项，从 IConfiguration 的 "Database" 节绑定。
/// </summary>
/// <remarks>
/// <para>
/// 该类承载与 Provider 无关的通用配置，连接池大小、最小连接数等 ADO.NET 驱动参数
/// 通过 ConnectionString 关键字直接传递，不在此处重复定义，避免与驱动语义偏差。
/// </para>
/// <para>
/// Startup 校验阶段应保证：ConnectionString 非空、Provider 指定了合法值、
/// MySqlGuidStorageMode 在迁移结束后锁定为 Binary16、CommandTimeoutSeconds 为正整数。
/// </para>
/// </remarks>
public sealed class DatabaseOptions
{
    /// <summary>
    /// IConfiguration 绑定节名称，约定值为 "Database"。
    /// </summary>
    public const string SectionName = "Database";

    /// <summary>
    /// 获取或设置当前应用使用的关系型数据库 Provider。
    /// </summary>
    /// <remarks>
    /// 枚举默认值 0 对应 <see cref="DatabaseProvider.SqlServer"/>，被视为有意选择。
    /// 若希望强制显式配置，请在 Startup 中对默认值抛出配置异常。
    /// </remarks>
    public DatabaseProvider Provider { get; set; }

    /// <summary>
    /// 获取或设置逻辑连接名称，用于多租户分库场景下区分不同用途的连接工厂。
    /// </summary>
    /// <remarks>
    /// 默认值 "fullnet" 指向主业务库。当引入只读副本、Outbox 专属实例、分析库时，
    /// 通过该名称路由到不同的 ConnectionString 注册，无需引入额外配置节。
    /// </remarks>
    public string ConnectionName { get; set; } = "fullnet";

    /// <summary>
    /// 获取或设置 ADO.NET 兼容的连接字符串。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 连接池大小（Max Pool Size）、最小连接数（Min Pool Size）、连接生命周期
    /// （Connection Lifetime）等性能参数通过此字符串内的关键字直接控制，示例：
    /// "Server=.;Database=FullNet;Max Pool Size=200;Min Pool Size=10;Connection Lifetime=300;"。
    /// </para>
    /// <para>
    /// 安全要求：生产环境必须从 Secrets Manager / Key Vault 注入，禁止提交到 appsettings.json。
    /// </para>
    /// </remarks>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 MySQL UUID 的物理存储模式；迁移完成前默认保持旧 CHAR(36) 映射。
    /// </summary>
    public MySqlGuidStorageMode MySqlGuidStorageMode { get; set; } =
        global::Full.NET.Data.Abstractions.MySqlGuidStorageMode.LegacyChar36;

    /// <summary>
    /// 获取或设置单个数据库命令的执行超时秒数，默认 30 秒。
    /// </summary>
    /// <remarks>
    /// 该值对应 IDbCommand.CommandTimeout。对于报表类长查询，应在 Repository
    /// 层为特定语句局部覆盖，而不是全局增大默认值导致慢查询阻塞连接池。
    /// 取值必须为正整数，0 表示无限等待（不推荐用于生产）。
    /// </remarks>
    public int CommandTimeoutSeconds { get; set; } = 30;
}
