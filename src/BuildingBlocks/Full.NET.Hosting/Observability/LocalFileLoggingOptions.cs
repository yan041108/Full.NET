using Microsoft.Extensions.Options;

namespace Full.NET.Hosting.Observability;

/// <summary>本地文件出口的滚动、保留和目录预算；只保存受限 JSON 快照。</summary>
public sealed class LocalFileLoggingOptions
{
    /// <summary>显式启用；关闭时不创建目录或连接文件。</summary>
    public bool Enabled { get; set; }
    /// <summary>文件根目录；相对路径基于宿主 ContentRoot。</summary>
    public string Directory { get; set; } = "App_Data/logs";
    /// <summary>单文件上限，满额后滚动；单位字节。</summary>
    public long FileSizeLimitBytes { get; set; } = 67_108_864;
    /// <summary>按文件日期清理的保留天数，不删除当前写入文件。</summary>
    public int RetentionDays { get; set; } = 14;
    /// <summary>单宿主应用目录内自有文件的合计字节上限；包含待刷新的逻辑字节。</summary>
    public long MaxDirectoryBytes { get; set; } = 1_073_741_824;
    /// <summary>优先历史保留目标占目录字节/文件名额的百分比，默认 20，范围 0–50；0 关闭，修改须重启。</summary>
    /// <remarks>普通写入回收时尽量保住字节目标，最多多保留一个滚动文件，但仍留出普通通道一个完整文件的空间；保护文件数不超过对应名额。
    /// 优先写入仍可替换旧优先文件，到期文件不受保护；不承诺磁盘故障隔离。</remarks>
    public int PriorityReservePercent { get; set; } = 20;
    /// <summary>后台刷新间隔；不表示掉电持久化承诺。</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>与 <see cref="LoggingOptions.DeliveryMode"/> 联校；非法组合在宿主启动期抛出，避免运行时才创建目录。</summary>
    internal void Validate(LoggingOptions logging)
    {
        if (!Enabled) return;
        var errors = new List<string>();
        if (logging.DeliveryMode != LoggingDeliveryMode.Local)
            errors.Add("LocalFile.Enabled requires DeliveryMode=Local.");
        if (string.IsNullOrWhiteSpace(Directory) || Directory.Length > 2048 || Directory.Any(char.IsControl))
            errors.Add("LocalFile.Directory must be a nonempty directory path.");
        if (FileSizeLimitBytes < logging.MaxEventBytes || FileSizeLimitBytes > 1_073_741_824)
            errors.Add("LocalFile.FileSizeLimitBytes must hold one maximum-sized event and be at most 1 GiB.");
        if (MaxDirectoryBytes < FileSizeLimitBytes * 2 || MaxDirectoryBytes > 1_099_511_627_776)
            errors.Add("LocalFile.MaxDirectoryBytes must hold two complete log files and be at most 1 TiB.");
        if (RetentionDays is < 1 or > 3650)
            errors.Add("LocalFile.RetentionDays must be between 1 and 3650.");
        if (PriorityReservePercent is < 0 or > 50)
            errors.Add("LocalFile.PriorityReservePercent must be between 0 and 50.");
        if (FlushInterval < TimeSpan.FromMilliseconds(100) || FlushInterval > TimeSpan.FromSeconds(30))
            errors.Add("LocalFile.FlushInterval must be between 100 ms and 30 seconds.");
        if (errors.Count != 0)
            throw new OptionsValidationException("FullNet:Logging:LocalFile", typeof(LocalFileLoggingOptions), errors);
    }

    /// <summary>Serilog 注册后用于检测 LocalFile 配置漂移；任一字段变化都要求重启。</summary>
    internal bool Matches(LocalFileLoggingOptions other) => Enabled == other.Enabled
        && Directory == other.Directory && FileSizeLimitBytes == other.FileSizeLimitBytes
        && RetentionDays == other.RetentionDays && MaxDirectoryBytes == other.MaxDirectoryBytes
        && PriorityReservePercent == other.PriorityReservePercent
        && FlushInterval == other.FlushInterval;
}
