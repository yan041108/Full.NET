namespace Full.NET.Modules.Document.Configuration;

/// <summary>
/// Document 模块运行时选项。
/// </summary>
public sealed class DocumentOptions
{
    public const string SectionName = "Document";

    /// <summary>
    /// 匿名分享访问端点每分钟允许的请求数（按客户端 IP 分区）。
    /// </summary>
    public int AnonymousShareAccessRateLimitPermitLimitPerMinute { get; set; } = 30;
}
