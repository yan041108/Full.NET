using Microsoft.Extensions.Options;

namespace Full.NET.Modules.DataApproval.Execution;

/// <summary>DataApproval 批准后业务应用恢复 Worker 配置。</summary>
internal sealed class DataApprovalRequestApplicationRecoveryWorkerOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "DataApproval:ApplicationRecoveryWorker";

    /// <summary>每批扫描数量。</summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>空闲轮询间隔（毫秒）。</summary>
    public int PollMilliseconds { get; set; } = 5000;

    /// <summary>同一请求两次应用尝试之间的最小间隔（秒）。</summary>
    public int RetryDelaySeconds { get; set; } = 30;
}

/// <summary>校验 DataApproval 应用恢复 Worker 配置。</summary>
internal sealed class DataApprovalRequestApplicationRecoveryWorkerOptionsValidator
    : IValidateOptions<DataApprovalRequestApplicationRecoveryWorkerOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DataApprovalRequestApplicationRecoveryWorkerOptions options)
    {
        var failures = new List<string>();
        if (options.BatchSize is < 1 or > 50)
        {
            failures.Add("DataApproval:ApplicationRecoveryWorker:BatchSize must be between 1 and 50.");
        }

        if (options.PollMilliseconds is < 500 or > 60000)
        {
            failures.Add("DataApproval:ApplicationRecoveryWorker:PollMilliseconds must be between 500 and 60000.");
        }

        if (options.RetryDelaySeconds is < 5 or > 3600)
        {
            failures.Add("DataApproval:ApplicationRecoveryWorker:RetryDelaySeconds must be between 5 and 3600.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
