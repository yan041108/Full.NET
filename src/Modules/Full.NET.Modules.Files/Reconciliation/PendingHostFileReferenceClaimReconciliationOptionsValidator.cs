using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Reconciliation;

/// <summary>校验 Claim 环扫和释放窗口，启动及直接运行共用安全边界。</summary>
internal sealed class PendingHostFileReferenceClaimReconciliationOptionsValidator : IValidateOptions<PendingHostFileReferenceClaimReconciliationOptions>
{
    public ValidateOptionsResult Validate(string? name, PendingHostFileReferenceClaimReconciliationOptions options)
    {
        var failures = new List<string>();
        Range(options.BatchSize, 1, 1000, "BatchSize");
        Range(options.MaxBatchesPerRun, 1, 100, "MaxBatchesPerRun");
        Range(options.MinimumAgeSeconds, 30, 86400, "MinimumAgeSeconds");
        Range(options.ReleaseGraceSeconds, 30, 86400, "ReleaseGraceSeconds");
        Range(options.PollSeconds, 5, 86400, "PollSeconds");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);

        void Range(int value, int minimum, int maximum, string key)
        {
            if (value < minimum || value > maximum)
                failures.Add($"Files:ReferenceClaimReconciliation:{key} must be between {minimum} and {maximum}.");
        }
    }
}
