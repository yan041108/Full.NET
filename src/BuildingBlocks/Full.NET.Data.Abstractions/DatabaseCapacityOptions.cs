namespace Full.NET.Data.Abstractions;

/// <summary>
/// 定义数据库连接池静态预算与单进程准入上限。
/// </summary>
/// <remarks>
/// Provider 连接池上限仍由连接字符串控制；本配置只负责把部署预算与运行时真实值闭环校验，
/// 并在进入连接池前施加更低的应用准入上限。
/// </remarks>
public sealed class DatabaseCapacityOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "DatabaseCapacity";

    /// <summary>获取或设置是否启用运行时预算校验与连接准入。</summary>
    public bool Enabled { get; set; }

    /// <summary>获取或设置当前进程角色。</summary>
    public DatabaseHostRole HostRole { get; set; }

    /// <summary>获取或设置普通数据库会话可同时占用的许可证数量。</summary>
    public int PermitLimit { get; set; } = 1;

    /// <summary>获取或设置等待许可证的最大排队数量；零表示不排队。</summary>
    public int QueueLimit { get; set; }

    /// <summary>获取或设置准入等待超时毫秒数。</summary>
    public int AcquireTimeoutMilliseconds { get; set; } = 250;

    /// <summary>获取或设置当前角色连接字符串必须声明的池上限。</summary>
    public int ExpectedMaxPoolSize { get; set; }

    /// <summary>获取或设置为健康检查保留的连接数量。</summary>
    public int HealthReserve { get; set; }

    /// <summary>获取或设置为 Worker 续租和终态写入保留的连接数量。</summary>
    public int CriticalWorkerReserve { get; set; }

    /// <summary>获取或设置 API 最大副本数。</summary>
    public int ApiMaxReplicas { get; set; }

    /// <summary>获取或设置单个 API 副本的连接池上限。</summary>
    public int ApiMaxPoolSize { get; set; }

    /// <summary>获取或设置 Worker 最大副本数。</summary>
    public int WorkerMaxReplicas { get; set; }

    /// <summary>获取或设置单个 Worker 副本的连接池上限。</summary>
    public int WorkerMaxPoolSize { get; set; }

    /// <summary>获取或设置迁移与运维连接保留量。</summary>
    public int MigrationReserve { get; set; }

    /// <summary>获取或设置数据库允许 Full.NET 使用的总连接预算。</summary>
    public int TotalBudget { get; set; }
}

/// <summary>
/// 定义产生数据库连接池指标的低基数宿主角色。
/// </summary>
public enum DatabaseHostRole
{
    /// <summary>尚未声明宿主角色。</summary>
    Unspecified = 0,

    /// <summary>HTTP API 宿主。</summary>
    Api = 1,

    /// <summary>后台 Worker 宿主。</summary>
    Worker = 2,

    /// <summary>数据库迁移宿主。</summary>
    Migrator = 3,
}
