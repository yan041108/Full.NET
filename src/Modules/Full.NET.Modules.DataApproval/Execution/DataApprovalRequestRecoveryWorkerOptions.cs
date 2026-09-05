using Microsoft.Extensions.Options;

namespace Full.NET.Modules.DataApproval.Execution;

/// <summary>DataApproval 请求工作流关联恢复 Worker 边界。</summary>
internal sealed class DataApprovalRequestRecoveryWorkerOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "DataApproval:RequestRecoveryWorker";

    /// <summary>每轮最多处理的请求数量。</summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>未满批时两次轮询之间的等待毫秒数。</summary>
    public int PollMilliseconds { get; set; } = 2000;

    /// <summary>失败后再次自动重试前的最短等待秒数。</summary>
    public int RetryDelaySeconds { get; set; } = 30;
}

/// <summary>校验 DataApproval 恢复 Worker 配置。</summary>
internal sealed class DataApprovalRequestRecoveryWorkerOptionsValidator
    : IValidateOptions<DataApprovalRequestRecoveryWorkerOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DataApprovalRequestRecoveryWorkerOptions options)
    {
        var failures = new List<string>();
        if (options.BatchSize is < 1 or > 50)
        {
            failures.Add("DataApproval:RequestRecoveryWorker:BatchSize must be between 1 and 50.");
        }

        if (options.PollMilliseconds is < 500 or > 60000)
        {
            failures.Add("DataApproval:RequestRecoveryWorker:PollMilliseconds must be between 500 and 60000.");
        }

        if (options.RetryDelaySeconds is < 5 or > 3600)
        {
            failures.Add("DataApproval:RequestRecoveryWorker:RetryDelaySeconds must be between 5 and 3600.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
