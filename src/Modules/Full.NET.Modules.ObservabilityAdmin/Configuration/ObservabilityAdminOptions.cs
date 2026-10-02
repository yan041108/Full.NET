namespace Full.NET.Modules.ObservabilityAdmin.Configuration;

/// <summary>
/// 定义 Host 日志控制面的固定根目录与有界读取限制。
/// </summary>
public sealed class ObservabilityAdminOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "FullNet:ObservabilityAdmin";

    /// <summary>日志文件根目录；默认 "logs"，控制面仅允许读取该目录下的文件。</summary>
    public string LogRootPath { get; init; } = "logs";

    /// <summary>列目录时返回的最大文件数；默认 100，防止目录枚举过载。</summary>
    public int MaximumListFiles { get; init; } = 100;

    /// <summary>尾部读取的默认行数；默认 200。</summary>
    public int DefaultTailLines { get; init; } = 200;

    /// <summary>尾部读取允许的最大行数；默认 5000。</summary>
    public int MaximumTailLines { get; init; } = 5_000;

    /// <summary>尾部读取的默认字节数；默认 262144（256 KiB）。</summary>
    public int DefaultTailBytes { get; init; } = 256 * 1024;

    /// <summary>尾部读取允许的最大字节数；默认 1048576（1 MiB）。</summary>
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
