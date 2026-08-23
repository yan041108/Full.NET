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

    /// <summary>检查点保留天数；超过后由 Worker 清理，默认 7 天。</summary>
    public int RetentionDays { get; set; } = 7;

    /// <summary>Worker 清理扫描间隔秒数，默认 3600。</summary>
    public int PollSeconds { get; set; } = 3600;

    /// <summary>单次清理循环最多删除的检查点目录数，默认 20，用于限制磁盘 IO 与锁占用。</summary>
    public int MaxDeletesPerRun { get; set; } = 20;

    /// <summary>
    /// 磁盘检查点目录上限；0 表示不按数量触发清理。
    /// </summary>
    public int MaxCheckpointCount { get; set; }

    /// <summary>
    /// 产品 Rollback 成功后立即删除对应检查点；与 Worker 保留策略独立，默认关闭。
    /// </summary>
    public bool DeleteAfterSucceededRollback { get; set; }
}

/// <summary>
/// 在启动期校验检查点保留选项落在安全区间，避免配置错误导致 Worker 误删、过载或扫描频率失控。
/// </summary>
internal sealed class CodeGenerationCheckpointRetentionOptionsValidator
    : IValidateOptions<CodeGenerationCheckpointRetentionOptions>
{
    /// <summary>
    /// 校验 RetentionDays、PollSeconds、MaxDeletesPerRun 与 MaxCheckpointCount 全部落在安全上下界内。
    /// </summary>
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