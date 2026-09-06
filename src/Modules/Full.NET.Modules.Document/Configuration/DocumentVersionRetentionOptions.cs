namespace Full.NET.Modules.Document.Configuration;

using Microsoft.Extensions.Options;

/// <summary>文档历史版本保留策略；当前版本永远不可被自动或手动删除逻辑绕过。</summary>
public sealed class DocumentVersionRetentionOptions
{
    public const string SectionName = "Document:VersionRetention";

    /// <summary>每个文档项至少保留的版本总数（含当前版本）。</summary>
    public int MinimumRetainedVersionsPerItem { get; set; } = 1;

    /// <summary>除当前版本外最多保留的历史版本数；0 表示不启用自动裁剪。</summary>
    public int MaximumRetainedHistoryVersions { get; set; } = 0;

    /// <summary>自动裁剪轮询间隔（秒）。</summary>
    public int PollSeconds { get; set; } = 300;

    /// <summary>单次轮询最多处理的版本删除数。</summary>
    public int BatchSize { get; set; } = 50;
}

internal sealed class DocumentVersionRetentionOptionsValidator
    : IValidateOptions<DocumentVersionRetentionOptions>
{
    public ValidateOptionsResult Validate(string? name, DocumentVersionRetentionOptions options)
    {
        var failures = new List<string>();
        ValidateRange(
            options.MinimumRetainedVersionsPerItem,
            1,
            1000,
            "Document:VersionRetention:MinimumRetainedVersionsPerItem",
            failures);
        ValidateRange(
            options.MaximumRetainedHistoryVersions,
            0,
            10000,
            "Document:VersionRetention:MaximumRetainedHistoryVersions",
            failures);
        ValidateRange(
            options.PollSeconds,
            60,
            86400,
            "Document:VersionRetention:PollSeconds",
            failures);
        ValidateRange(
            options.BatchSize,
            1,
            1000,
            "Document:VersionRetention:BatchSize",
            failures);

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
