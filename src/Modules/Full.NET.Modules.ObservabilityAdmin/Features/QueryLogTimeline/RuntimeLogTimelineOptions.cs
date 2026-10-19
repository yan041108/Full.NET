namespace Full.NET.Modules.ObservabilityAdmin.Features.QueryLogTimeline;

/// <summary>时间线运行日志来源；默认均关闭，重启后生效，不改变日志投递模式。</summary>
public sealed class RuntimeLogTimelineOptions
{
    /// <summary>配置节。</summary>
    public const string SectionName = "FullNet:ObservabilityAdmin:Timeline";
    /// <summary>启用文件控制面的有界尾读，使用显式 LogRootPath 或已启用 Local 的应用目录；仅覆盖当前实例可见文件。</summary>
    public bool LocalFilesEnabled { get; set; }
    /// <summary>启用 ES 安全摘要查询。</summary>
    public bool ElasticsearchEnabled { get; set; }
    /// <summary>无路径和凭据的 HTTPS ES 根地址。</summary>
    public string ElasticsearchEndpoint { get; set; } = string.Empty;
    /// <summary>只允许 fn-logs- 前缀的受控索引模式。</summary>
    public string ElasticsearchIndexPattern { get; set; } = "fn-logs-*";
    /// <summary>ES 只读 API Key，由 Secret 注入，不得写进文档或日志。</summary>
    public string ElasticsearchApiKey { get; set; } = string.Empty;
}
