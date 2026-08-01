using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Features.WriteAuditBatch;

/// <summary>
/// B1 重要 HTTP Audit 跨请求微批配置。不暴露 FailClosed；要求 fail-closed 的动作必须归类为 B0。
/// </summary>
public sealed class AuditMicroBatchOptions
{
    public const string SectionName = "Auditing:MicroBatch";

    /// <summary>有界队列容量；满载后在入队超时内无法入队则 fail-open。</summary>
    public int Capacity { get; set; } = 4096;

    /// <summary>单批最大行数（Operation/Exception/Outbound 合计）。</summary>
    public int MaxBatchRows { get; set; } = 64;

    /// <summary>单批最大估算字节数。</summary>
    public int MaxBatchBytes { get; set; } = 262_144;

    /// <summary>未满批时的最大等待时间。</summary>
    public TimeSpan MaxBatchDelay { get; set; } = TimeSpan.FromMilliseconds(20);

    /// <summary>入队等待上限；超时后 fail-open 并记拒绝指标。</summary>
    public TimeSpan EnqueueTimeout { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>停机时最多等待当前队列排空的时间。</summary>
    public TimeSpan ShutdownFlushTimeout { get; set; } = TimeSpan.FromSeconds(5);
}

internal sealed class AuditMicroBatchOptionsValidator : IValidateOptions<AuditMicroBatchOptions>
{
    public ValidateOptionsResult Validate(string? name, AuditMicroBatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (options.Capacity <= 0)
        {
            failures.Add($"{AuditMicroBatchOptions.SectionName}:Capacity must be greater than zero.");
        }

        if (options.MaxBatchRows <= 0 || options.MaxBatchRows > options.Capacity)
        {
            failures.Add(
                $"{AuditMicroBatchOptions.SectionName}:MaxBatchRows must be in (0, Capacity].");
        }

        if (options.MaxBatchBytes <= 0)
        {
            failures.Add(
                $"{AuditMicroBatchOptions.SectionName}:MaxBatchBytes must be greater than zero.");
        }

        if (options.MaxBatchDelay <= TimeSpan.Zero)
        {
            failures.Add(
                $"{AuditMicroBatchOptions.SectionName}:MaxBatchDelay must be greater than zero.");
        }

        if (options.EnqueueTimeout <= TimeSpan.Zero)
        {
            failures.Add(
                $"{AuditMicroBatchOptions.SectionName}:EnqueueTimeout must be greater than zero.");
        }

        if (options.ShutdownFlushTimeout <= TimeSpan.Zero)
        {
            failures.Add(
                $"{AuditMicroBatchOptions.SectionName}:ShutdownFlushTimeout must be greater than zero.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
