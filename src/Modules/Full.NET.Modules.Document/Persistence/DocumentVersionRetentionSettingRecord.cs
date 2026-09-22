namespace Full.NET.Modules.Document.Persistence;

internal sealed class DocumentVersionRetentionSettingRecord
{
    public Guid Id { get; init; }

    public int MinimumRetainedVersionsPerItem { get; init; }

    public int MaximumRetainedHistoryVersions { get; init; }

    public int PollSeconds { get; init; }

    public int BatchSize { get; init; }

    public long Version { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }
}
