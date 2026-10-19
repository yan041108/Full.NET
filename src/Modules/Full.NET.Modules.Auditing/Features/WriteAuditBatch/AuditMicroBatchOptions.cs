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

    /// <summary>队列、等待入队及正在写入的 B1 信封计费字节上限。</summary>
    public long QueueMaxBytes { get; set; } = 67_108_864;

    /// <summary>单批最大行数（Operation/Exception/Outbound 合计）。</summary>
    public int MaxBatchRows { get; set; } = 64;

    /// <summary>单批最大计费字节数；单条超过上限时 fail-open 拒绝写入。</summary>
    public int MaxBatchBytes { get; set; } = 262_144;

    /// <summary>未满批时的最大等待时间。</summary>
    public TimeSpan MaxBatchDelay { get; set; } = TimeSpan.FromMilliseconds(20);

    /// <summary>入队等待上限；超时后 fail-open 并记拒绝指标。</summary>
    public TimeSpan EnqueueTimeout { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>接受前开始计时；过期且尚未执行的信封直接 fail-open，不再向数据库提交。</summary>
    public TimeSpan MaxQueueAge { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>单批所有写入与毒记录隔离共享的取消期限；真实 I/O 结束后才归还预约。</summary>
    public TimeSpan BatchExecutionTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>单批写入尝试总上限，包含首次写入和全部隔离分支，防止数据故障放大。</summary>
    public int MaxIsolationAttempts { get; set; } = 15;

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

        if (options.MaxBatchRows <= 0 || options.MaxBatchRows > options.Capacity || options.MaxBatchRows > 125)
        {
            failures.Add(
                $"{AuditMicroBatchOptions.SectionName}:MaxBatchRows must be in (0, min(Capacity, 125)] to fit the SQL parameter budget.");
        }

        if (options.MaxBatchBytes <= 0)
        {
            failures.Add(
                $"{AuditMicroBatchOptions.SectionName}:MaxBatchBytes must be greater than zero.");
        }

        if (options.QueueMaxBytes < options.MaxBatchBytes)
        {
            failures.Add(
                $"{AuditMicroBatchOptions.SectionName}:QueueMaxBytes must hold at least one full batch.");
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

        if (options.MaxQueueAge <= TimeSpan.Zero || options.MaxQueueAge > TimeSpan.FromMinutes(1))
            failures.Add($"{AuditMicroBatchOptions.SectionName}:MaxQueueAge must be in (0, 1 minute].");
        if (options.BatchExecutionTimeout <= TimeSpan.Zero || options.BatchExecutionTimeout > TimeSpan.FromSeconds(30))
            failures.Add($"{AuditMicroBatchOptions.SectionName}:BatchExecutionTimeout must be in (0, 30 seconds].");
        if (options.MaxIsolationAttempts is < 1 or > 255)
            failures.Add($"{AuditMicroBatchOptions.SectionName}:MaxIsolationAttempts must be in [1, 255].");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
