using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Reconciliation;

/// <summary>租户资源文件 pending/ready 孤儿对账配置。</summary>
internal sealed class PendingTenantResourceFileReconciliationOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "Files:TenantResourceReconciliation";

    /// <summary>是否启用对账循环。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>每批扫描条数。</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>单轮最大批次数。</summary>
    public int MaxBatchesPerRun { get; set; } = 10;

    /// <summary>文件必须达到的最小年龄秒数，覆盖上传成功后崩溃窗口。</summary>
    public int MinimumAgeSeconds { get; set; } = 900;

    /// <summary>轮询间隔秒数。</summary>
    public int PollSeconds { get; set; } = 300;
}

/// <summary>校验租户资源文件对账配置边界。</summary>
internal sealed class PendingTenantResourceFileReconciliationOptionsValidator
    : IValidateOptions<PendingTenantResourceFileReconciliationOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(
        string? name,
        PendingTenantResourceFileReconciliationOptions options)
    {
        var failures = new List<string>();
        ValidateRange(options.BatchSize, 1, 1000, "BatchSize", failures);
        ValidateRange(options.MaxBatchesPerRun, 1, 100, "MaxBatchesPerRun", failures);
        ValidateRange(options.MinimumAgeSeconds, 30, 86400, "MinimumAgeSeconds", failures);
        ValidateRange(options.PollSeconds, 5, 86400, "PollSeconds", failures);
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>校验整数配置范围。</summary>
    /// <param name="value">配置值。</param>
    /// <param name="minimum">最小值。</param>
    /// <param name="maximum">最大值。</param>
    /// <param name="name">配置名。</param>
    /// <param name="failures">失败集合。</param>
    private static void ValidateRange(
        int value,
        int minimum,
        int maximum,
        string name,
        ICollection<string> failures)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add(
                $"Files:TenantResourceReconciliation:{name} must be between {minimum} and {maximum}.");
        }
    }
}
