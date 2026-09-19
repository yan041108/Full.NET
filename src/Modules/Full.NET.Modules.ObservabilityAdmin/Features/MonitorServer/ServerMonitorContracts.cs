namespace Full.NET.Modules.ObservabilityAdmin.Features.MonitorServer;

/// <summary>表示实例目录中的一条只读登记项。</summary>
/// <param name="InstanceKey">实例稳定标识，用于运行时查询路由。</param>
/// <param name="DisplayName">面向运维展示的实例名称。</param>
/// <param name="HostRole">宿主角色，例如 Api 或 Worker。</param>
/// <param name="IsCurrent">是否对应当前正在处理请求的进程。</param>
/// <param name="RuntimeQueryability">运行时指标是否可在本进程本地采集。</param>
public sealed record ServerInstanceCatalogEntry(
    string InstanceKey,
    string DisplayName,
    string HostRole,
    bool IsCurrent,
    string RuntimeQueryability);

/// <summary>表示一条运行时指标及其可用性状态。</summary>
/// <param name="Key">稳定机器码键，供客户端按语义渲染。</param>
/// <param name="Label">面向运维展示的中文友好标签。</param>
/// <param name="LongValue">整型指标值；不可用时为 <see langword="null"/>。</param>
/// <param name="DoubleValue">浮点指标值；不可用时为 <see langword="null"/>。</param>
/// <param name="Unit">单位说明，例如 bytes、percent、count。</param>
/// <param name="Availability">可用性状态，见 <see cref="ServerRuntimeMetricAvailability"/>。</param>
/// <param name="UnavailableReason">指标不可用时的简短原因，禁止包含敏感配置。</param>
public sealed record ServerRuntimeMetric(
    string Key,
    string Label,
    long? LongValue,
    double? DoubleValue,
    string? Unit,
    string Availability,
    string? UnavailableReason);

/// <summary>表示某一实例在采集时刻的运行时快照。</summary>
/// <param name="InstanceKey">实例稳定标识。</param>
/// <param name="DisplayName">面向运维展示的实例名称。</param>
/// <param name="HostRole">宿主角色。</param>
/// <param name="MachineName">机器名，仅用于实例定位，不包含连接串或环境变量。</param>
/// <param name="ProcessId">当前进程标识。</param>
/// <param name="FrameworkDescription">.NET 运行时描述。</param>
/// <param name="ApplicationVersion">应用版本号。</param>
/// <param name="OperatingSystemDescription">操作系统描述。</param>
/// <param name="ProcessArchitecture">进程架构。</param>
/// <param name="ProcessStartedAtUtc">进程启动 UTC 时间。</param>
/// <param name="CapturedAtUtc">快照采集 UTC 时间。</param>
/// <param name="UptimeSeconds">自进程启动起的运行秒数。</param>
/// <param name="Metrics">内存、CPU 等运行时指标集合。</param>
public sealed record ServerRuntimeSnapshot(
    string InstanceKey,
    string DisplayName,
    string HostRole,
    string MachineName,
    int ProcessId,
    string FrameworkDescription,
    string ApplicationVersion,
    string OperatingSystemDescription,
    string ProcessArchitecture,
    DateTimeOffset ProcessStartedAtUtc,
    DateTimeOffset CapturedAtUtc,
    long UptimeSeconds,
    IReadOnlyList<ServerRuntimeMetric> Metrics);

/// <summary>运行时指标可用性状态常量。</summary>
public static class ServerRuntimeMetricAvailability
{
    /// <summary>指标在当前实例可正常采集，客户端可渲染对应数值。</summary>
    public const string Available = "available";

    /// <summary>指标因暂时故障无法采集；客户端应展示不可用占位而非 0。</summary>
    public const string Unavailable = "unavailable";

    /// <summary>当前平台或宿主角色不支持该指标；客户端应隐藏而非展示为不可用。</summary>
    public const string NotSupportedOnPlatform = "not_supported_on_platform";
}

/// <summary>实例运行时查询能力常量。</summary>
public static class ServerInstanceRuntimeQueryability
{
    /// <summary>当前进程可本地采集运行时指标；监控端点直连本进程。</summary>
    public const string Local = "local";

    /// <summary>仅能在实例目录中展示，当前进程不提供运行时指标查询。</summary>
    public const string CatalogOnly = "catalog_only";
}
