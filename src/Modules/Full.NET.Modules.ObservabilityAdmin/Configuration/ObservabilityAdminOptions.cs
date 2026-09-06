namespace Full.NET.Modules.ObservabilityAdmin.Configuration;

/// <summary>
/// 定义 Host 日志控制面的固定根目录与有界读取限制。
/// </summary>
public sealed class ObservabilityAdminOptions
{
    public const string SectionName = "FullNet:ObservabilityAdmin";

    public string LogRootPath { get; init; } = "logs";

    public int MaximumListFiles { get; init; } = 100;

    public int DefaultTailLines { get; init; } = 200;

    public int MaximumTailLines { get; init; } = 5_000;

    public int DefaultTailBytes { get; init; } = 256 * 1024;

    public int MaximumTailBytes { get; init; } = 1024 * 1024;

    /// <summary>当前进程实例稳定标识；留空时按机器名与宿主角色自动生成。</summary>
    public string InstanceKey { get; init; } = string.Empty;

    /// <summary>当前进程实例展示名称；留空时回退为实例标识。</summary>
    public string InstanceDisplayName { get; init; } = string.Empty;

    /// <summary>当前宿主角色，例如 Api 或 Worker。</summary>
    public string HostRole { get; init; } = "Api";

    /// <summary>跨实例目录登记项，仅包含身份元数据，不包含连接串或环境变量。</summary>
    public IReadOnlyList<ObservabilityInstanceCatalogEntryOptions> Instances { get; init; } =
        Array.Empty<ObservabilityInstanceCatalogEntryOptions>();
}

/// <summary>表示配置中的实例目录登记项。</summary>
public sealed class ObservabilityInstanceCatalogEntryOptions
{
    /// <summary>实例稳定标识。</summary>
    public string InstanceKey { get; init; } = string.Empty;

    /// <summary>实例展示名称。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>宿主角色。</summary>
    public string HostRole { get; init; } = string.Empty;
}
