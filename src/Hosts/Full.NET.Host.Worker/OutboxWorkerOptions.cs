using Microsoft.Extensions.Options;

namespace Full.NET.Host.Worker;

/// <summary>
/// 定义 Outbox Worker 的批大小、租约、轮询与最大尝试边界。
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

    /// <summary>获取或设置租约秒数；到期后其他 Worker 可回收卡住的消息。</summary>
    public int LeaseSeconds { get; set; } = 30;

    /// <summary>获取或设置空轮询等待毫秒数。</summary>
    public int PollMilliseconds { get; set; } = 1000;

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

        if (options.LeaseSeconds is < 5 or > 3600)
        {
            failures.Add("OutboxWorker:LeaseSeconds must be between 5 and 3600.");
        }

        if (options.PollMilliseconds is < 100 or > 60000)
        {
            failures.Add("OutboxWorker:PollMilliseconds must be between 100 and 60000.");
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
