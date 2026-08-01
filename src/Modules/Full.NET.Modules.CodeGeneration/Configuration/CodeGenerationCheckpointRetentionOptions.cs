using Microsoft.Extensions.Options;

namespace Full.NET.Modules.CodeGeneration.Configuration;

/// <summary>
/// 配置 Worker 对已成功回滚且过冷却期的本地检查点目录执行显式清理。
/// </summary>
internal sealed class CodeGenerationCheckpointRetentionOptions
{
    public const string SectionName = "CodeGeneration:CheckpointRetention";

    /// <summary>
    /// 生产默认关闭；须与 Apply 工作区配置一并审阅后再启用。
    /// </summary>
    public bool Enabled { get; set; }

    public int RetentionDays { get; set; } = 7;

    public int PollSeconds { get; set; } = 3600;

    public int MaxDeletesPerRun { get; set; } = 20;

    /// <summary>
    /// 磁盘检查点目录上限；0 表示不按数量触发清理。
    /// </summary>
    public int MaxCheckpointCount { get; set; }
}

internal sealed class CodeGenerationCheckpointRetentionOptionsValidator
    : IValidateOptions<CodeGenerationCheckpointRetentionOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        CodeGenerationCheckpointRetentionOptions options)
    {
        var failures = new List<string>();
        ValidateRange(
            options.RetentionDays,
            1,
            3650,
            "CodeGeneration:CheckpointRetention:RetentionDays",
            failures);
        ValidateRange(
            options.PollSeconds,
            60,
            86400,
            "CodeGeneration:CheckpointRetention:PollSeconds",
            failures);
        ValidateRange(
            options.MaxDeletesPerRun,
            1,
            500,
            "CodeGeneration:CheckpointRetention:MaxDeletesPerRun",
            failures);
        if (options.MaxCheckpointCount is < 0 or > 100_000)
        {
            failures.Add(
                "CodeGeneration:CheckpointRetention:MaxCheckpointCount "
                + "must be between 0 and 100000.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRange(
        int value,
        int minimum,
        int maximum,
        string key,
        ICollection<string> failures)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add(
                $"{key} must be between {minimum} and {maximum}.");
        }
    }
}