namespace Full.NET.Hosting.RateLimiting;

/// <summary>
/// Host API 全局限流配置；Identity 等模块仍可注册更严格的端点级策略。
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// 是否启用按用户或来源 IP 划分的全局限流。
    /// </summary>
    public bool EnableGlobalApiLimit { get; set; } = true;

    /// <summary>
    /// 全局限流每分钟允许的请求数；设为 0 表示仅依赖端点级策略。
    /// </summary>
    public int GlobalApiPermitLimitPerMinute { get; set; } = 1200;
}
