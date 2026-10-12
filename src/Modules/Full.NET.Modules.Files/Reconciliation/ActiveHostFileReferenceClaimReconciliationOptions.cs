using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Reconciliation;

/// <summary>不可复用引用的 Active 对账预算；关闭开关可停止历史补偿，不影响可靠删除事件。</summary>
internal sealed class ActiveHostFileReferenceClaimReconciliationOptions
{
    public const string SectionName = "Files:ActiveReferenceClaimReconciliation";
    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 100;
    public int MaxBatchesPerRun { get; set; } = 10;
    public int MinimumAgeSeconds { get; set; } = 900;
    public int PollSeconds { get; set; } = 300;
}

/// <summary>约束扫描预算和保护窗口，拒绝零预算、负龄及无界轮询。</summary>
internal sealed class ActiveHostFileReferenceClaimReconciliationOptionsValidator
    : IValidateOptions<ActiveHostFileReferenceClaimReconciliationOptions>
{
    public ValidateOptionsResult Validate(string? name, ActiveHostFileReferenceClaimReconciliationOptions options)
    {
        var failures = new List<string>();
        Check(options.BatchSize, 1, 1000, nameof(options.BatchSize));
        Check(options.MaxBatchesPerRun, 1, 100, nameof(options.MaxBatchesPerRun));
        Check(options.MinimumAgeSeconds, 30, 86400, nameof(options.MinimumAgeSeconds));
        Check(options.PollSeconds, 5, 86400, nameof(options.PollSeconds));
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);

        void Check(int value, int minimum, int maximum, string field)
        {
            if (value < minimum || value > maximum)
                failures.Add($"{ActiveHostFileReferenceClaimReconciliationOptions.SectionName}:{field} must be between {minimum} and {maximum}.");
        }
    }
}
