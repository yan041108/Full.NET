using Microsoft.Extensions.Options;

namespace Full.NET.Hosting.Observability;

/// <summary>
/// 普通 HTTP Operation Log（B2）配置。关闭本选项不得影响 Metrics、Error/Critical 或 B0/B1 Audit。
/// </summary>
public sealed class HttpOperationLogOptions
{
    public const string SectionName = "Observability:HttpOperation";

    /// <summary>总开关；false 等同于 CaptureMode=Disabled。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// HTTP Operation Log 捕获粒度；默认 Summary，可选 Disabled / Summary / SanitizedPayload，
    /// 决定请求与响应的记录详细程度。
    /// </summary>
    public HttpOperationCaptureMode CaptureMode { get; set; } = HttpOperationCaptureMode.Summary;

    /// <summary>部署选定的容量档；默认 XL 作为 10K 设计参考（Capacity-not-verified）。</summary>
    public LoggingCapacityProfile CapacityProfile { get; set; } =
        HttpOperationLogProfile.DesignTargetProfile;

    /// <summary>
    /// 成功请求采样率覆盖；null 时使用 Profile 候选。不构成不可丢承诺。
    /// </summary>
    public double? SuccessSampleRate { get; set; }

    /// <summary>5xx 与异常（包括已映射为 4xx）是否进入 Priority 通道。</summary>
    public bool AlwaysRecordErrors { get; set; } = true;

    /// <summary>超过该阈值的成功请求进入 Priority，不参加成功采样。</summary>
    public TimeSpan SlowRequestThreshold { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Priority 事件的有界容量；满载后丢弃并记指标。</summary>
    public int PriorityCapacity { get; set; } = 2_048;

    /// <summary>成功（Best Effort）事件的有界容量；满载后丢弃。</summary>
    public int BestEffortCapacity { get; set; } = 8_192;

    /// <summary>SanitizedPayload 模式下请求摘要最大字节。</summary>
    public int MaxRequestPayloadBytes { get; set; } = 2_048;

    /// <summary>SanitizedPayload 模式下响应投影最大字节；默认 0 表示不采集返回摘要。</summary>
    public int MaxResponsePayloadBytes { get; set; }

    /// <summary>B2 投影每实例每秒最多接受的事件数；不包含 B1 详情。</summary>
    public int CaptureMaxEventsPerSecond { get; set; } = 1_000;

    /// <summary>B2 投影每实例每秒最多接受的 UTF-8 字节；不包含 B1 详情。</summary>
    public int CaptureMaxBytesPerSecond { get; set; } = 2_097_152;

    /// <summary>仅记录日志捕获点的托管线程号；异步请求没有单一生命周期线程。</summary>
    public bool CaptureThreadId { get; set; }

    /// <summary>允许记录的路径前缀；空表示默认 /api。</summary>
    public string[] IncludePathPrefixes { get; set; } = ["/api"];

    /// <summary>排除前缀（健康检查、OpenAPI 等）。</summary>
    public string[] ExcludePathPrefixes { get; set; } =
        ["/health", "/openapi", "/scalar"];

    /// <summary>SanitizedPayload 白名单 Route 模板；未列出的 Route 只输出 Summary。</summary>
    public string[] PayloadRouteAllowList { get; set; } = [];
}

internal sealed class HttpOperationLogOptionsValidator : IValidateOptions<HttpOperationLogOptions>
{
    public ValidateOptionsResult Validate(string? name, HttpOperationLogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (options.SuccessSampleRate is < 0 or > 1)
        {
            failures.Add($"{HttpOperationLogOptions.SectionName}:SuccessSampleRate must be in [0,1].");
        }

        if (options.SlowRequestThreshold < TimeSpan.Zero)
        {
            failures.Add(
                $"{HttpOperationLogOptions.SectionName}:SlowRequestThreshold must be >= 0.");
        }

        if (options.PriorityCapacity <= 0 || options.BestEffortCapacity <= 0)
        {
            failures.Add(
                $"{HttpOperationLogOptions.SectionName}:PriorityCapacity and BestEffortCapacity must be > 0.");
        }

        if (options.MaxRequestPayloadBytes < 0 || options.MaxResponsePayloadBytes < 0)
        {
            failures.Add(
                $"{HttpOperationLogOptions.SectionName}:payload byte limits must be >= 0.");
        }

        if (options.MaxRequestPayloadBytes > 2_048
            || options.MaxResponsePayloadBytes > 2_048)
        {
            failures.Add(
                $"{HttpOperationLogOptions.SectionName}:payload byte limits must not exceed 2048 so projected JSON remains intact in the log envelope.");
        }

        if (options.CaptureMaxEventsPerSecond is < 1 or > 100_000)
        {
            failures.Add(
                $"{HttpOperationLogOptions.SectionName}:CaptureMaxEventsPerSecond must be between 1 and 100000.");
        }

        if (options.CaptureMaxBytesPerSecond is < 1 or > 67_108_864
            || options.MaxRequestPayloadBytes > options.CaptureMaxBytesPerSecond
            || options.MaxResponsePayloadBytes > options.CaptureMaxBytesPerSecond)
        {
            failures.Add(
                $"{HttpOperationLogOptions.SectionName}:CaptureMaxBytesPerSecond must be positive, no greater than 64 MiB, and hold one maximum request or response projection.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
