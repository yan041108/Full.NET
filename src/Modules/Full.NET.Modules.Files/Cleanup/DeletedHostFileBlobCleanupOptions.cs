using Full.NET.Modules.Files.Storage;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Cleanup;

internal sealed class DeletedHostFileBlobCleanupOptions
{
    public const string SectionName = "Files:Cleanup";

    /// <summary>生产默认关闭；部署方确认 Worker 可访问同一 Blob 根目录后才能启用。</summary>
    public bool Enabled { get; set; }

    public int BatchSize { get; set; } = 100;

    public int MaxBatchesPerRun { get; set; } = 10;

    public int PollSeconds { get; set; } = 300;
}

internal sealed class DeletedHostFileBlobCleanupOptionsValidator(
    IOptions<LocalFileStorageOptions> storageOptions)
    : IValidateOptions<DeletedHostFileBlobCleanupOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        DeletedHostFileBlobCleanupOptions options)
    {
        var failures = new List<string>();
        ValidateRange(
            options.BatchSize,
            1,
            1000,
            "Files:Cleanup:BatchSize",
            failures);
        ValidateRange(
            options.MaxBatchesPerRun,
            1,
            100,
            "Files:Cleanup:MaxBatchesPerRun",
            failures);
        ValidateRange(
            options.PollSeconds,
            5,
            86400,
            "Files:Cleanup:PollSeconds",
            failures);

        if (options.Enabled)
        {
            var storageValidation = new LocalFileStorageOptionsValidator()
                .Validate(Options.DefaultName, storageOptions.Value);
            if (storageValidation.Failed)
            {
                failures.AddRange(storageValidation.Failures);
            }
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
            failures.Add($"{key} must be between {minimum} and {maximum}.");
        }
    }
}
