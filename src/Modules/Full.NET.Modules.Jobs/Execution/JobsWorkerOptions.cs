using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Jobs.Execution;

/// <summary>定义 Jobs Worker 的批大小与空轮询等待边界。</summary>
internal sealed class JobsWorkerOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "Jobs:Worker";

    /// <summary>获取或设置每轮最多领取的任务数量。</summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>获取或设置两次轮询之间的等待毫秒数。</summary>
    public int PollMilliseconds { get; set; } = 2000;
}

internal sealed class JobsWorkerOptionsValidator : IValidateOptions<JobsWorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, JobsWorkerOptions options)
    {
        var failures = new List<string>();
        if (options.BatchSize is < 1 or > 50)
        {
            failures.Add("Jobs:Worker:BatchSize must be between 1 and 50.");
        }

        if (options.PollMilliseconds is < 100 or > 60000)
        {
            failures.Add(
                "Jobs:Worker:PollMilliseconds must be between 100 and 60000.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
