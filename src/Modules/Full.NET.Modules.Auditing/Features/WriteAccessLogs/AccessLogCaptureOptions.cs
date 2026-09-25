using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Features.WriteAccessLogs;

/// <summary>
/// B2 访问日志入库配置。生产默认关闭，部署方确认容量和保留策略后开启；
/// 队列满时丢弃记录，不延迟业务请求。
/// </summary>
internal sealed class AccessLogCaptureOptions
{
    public const string SectionName = "Auditing:AccessLogCapture";

    public bool Enabled { get; set; }

    public int Capacity { get; set; } = 8192;

    public int MaxBatchRows { get; set; } = 100;

    public TimeSpan MaxBatchDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    public TimeSpan ShutdownFlushTimeout { get; set; } = TimeSpan.FromSeconds(5);
}

internal sealed class AccessLogCaptureOptionsValidator : IValidateOptions<AccessLogCaptureOptions>
{
    public ValidateOptionsResult Validate(string? name, AccessLogCaptureOptions options)
    {
        var failures = new List<string>();
        if (options.Capacity <= 0)
        {
            failures.Add("Auditing:AccessLogCapture:Capacity must be greater than zero.");
        }

        // 每行 11 个参数；限制批量行数以低于 SQL Server 的参数上限。
        if (options.MaxBatchRows <= 0 || options.MaxBatchRows > 100
            || options.MaxBatchRows > options.Capacity)
        {
            failures.Add("Auditing:AccessLogCapture:MaxBatchRows must be in (0, min(100, Capacity)].");
        }

        if (options.MaxBatchDelay <= TimeSpan.Zero)
        {
            failures.Add("Auditing:AccessLogCapture:MaxBatchDelay must be greater than zero.");
        }

        if (options.ShutdownFlushTimeout <= TimeSpan.Zero)
        {
            failures.Add("Auditing:AccessLogCapture:ShutdownFlushTimeout must be greater than zero.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
