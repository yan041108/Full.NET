using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Reconciliation;

internal sealed class PendingHostFileReconciliationOptions
{
    public const string SectionName = "Files:UploadReconciliation";

    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 100;
    public int MaxBatchesPerRun { get; set; } = 10;
    public int MinimumAgeSeconds { get; set; } = 300;
    public int PollSeconds { get; set; } = 300;
}

internal sealed class PendingHostFileReconciliationOptionsValidator
    : IValidateOptions<PendingHostFileReconciliationOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        PendingHostFileReconciliationOptions options)
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
                $"Files:UploadReconciliation:{name} must be between {minimum} and {maximum}.");
        }
    }
}
