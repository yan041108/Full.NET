using Microsoft.Extensions.Options;

namespace Full.NET.Host.Worker;

/// <summary>
/// 定义 Outbox Worker 的批大小、有界并发、租约、轮询、指标采样与最大尝试边界。
/// </summary>
/// <remarks>
/// 默认拓扑依赖数据库租约支持多副本安全消费；只有在真实压力证据证明不足时，才允许继续讨论额外选主机制。
/// </remarks>
public sealed class OutboxWorkerOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "OutboxWorker";

    /// <summary>获取或设置每轮最多领取的消息数量。</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>获取或设置单个 Worker 进程内同时处理的最大消息数量。</summary>
    public int MaxConcurrency { get; set; } = 1;

    /// <summary>获取或设置租约秒数；到期后其他 Worker 可回收卡住的消息。</summary>
    public int LeaseSeconds { get; set; } = 30;

    /// <summary>获取或设置批次租约的主动续期间隔秒数。</summary>
    public int LeaseRenewalSeconds { get; set; } = 10;

    /// <summary>获取或设置空轮询等待毫秒数。</summary>
    public int PollMilliseconds { get; set; } = 1000;

    /// <summary>空队列连续轮询时指数退避的最大等待毫秒数。</summary>
    public int MaximumIdlePollMilliseconds { get; set; } = 30_000;

    /// <summary>获取或设置数据库准入拒绝后暂停新一轮领取的毫秒数。</summary>
    public int DatabaseCapacityBackoffMilliseconds { get; set; } = 1_000;

    /// <summary>获取或设置积压指标的数据库采样周期秒数。</summary>
    public int BacklogSampleSeconds { get; set; } = 30;

    /// <summary>获取或设置单条消息允许的最大总尝试次数（含当前领取）。</summary>
    public int MaxAttempts { get; set; } = 5;
}

internal sealed class OutboxWorkerOptionsValidator : IValidateOptions<OutboxWorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, OutboxWorkerOptions options)
    {
        var failures = new List<string>();
        if (options.BatchSize is < 1 or > 200)
        {
            failures.Add("OutboxWorker:BatchSize must be between 1 and 200.");
        }

        if (options.MaxConcurrency is < 1 or > 16)
        {
            failures.Add(
                "OutboxWorker:MaxConcurrency must be between 1 and 16.");
        }

        if (options.MaxConcurrency > options.BatchSize)
        {
            failures.Add(
                "OutboxWorker:MaxConcurrency must not exceed BatchSize.");
        }

        if (options.LeaseSeconds is < 5 or > 3600)
        {
            failures.Add("OutboxWorker:LeaseSeconds must be between 5 and 3600.");
        }

        if (options.LeaseRenewalSeconds is < 1 or > 1200)
        {
            failures.Add(
                "OutboxWorker:LeaseRenewalSeconds must be between 1 and 1200.");
        }

        if (options.LeaseRenewalSeconds > options.LeaseSeconds / 2)
        {
            failures.Add(
                "OutboxWorker:LeaseRenewalSeconds must not exceed half of LeaseSeconds.");
        }

        if (options.PollMilliseconds is < 100 or > 60000)
        {
            failures.Add("OutboxWorker:PollMilliseconds must be between 100 and 60000.");
        }

        if (options.MaximumIdlePollMilliseconds is < 100 or > 300_000)
        {
            failures.Add(
                "OutboxWorker:MaximumIdlePollMilliseconds must be between 100 and 300000.");
        }

        if (options.MaximumIdlePollMilliseconds < options.PollMilliseconds)
        {
            failures.Add(
                "OutboxWorker:MaximumIdlePollMilliseconds must not be less than PollMilliseconds.");
        }

        if (options.DatabaseCapacityBackoffMilliseconds is < 100 or > 300_000)
        {
            failures.Add(
                "OutboxWorker:DatabaseCapacityBackoffMilliseconds must be between 100 and 300000.");
        }

        if (options.BacklogSampleSeconds is < 5 or > 3600)
        {
            failures.Add(
                "OutboxWorker:BacklogSampleSeconds must be between 5 and 3600.");
        }

        if (options.MaxAttempts is < 1 or > 100)
        {
            failures.Add("OutboxWorker:MaxAttempts must be between 1 and 100.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
