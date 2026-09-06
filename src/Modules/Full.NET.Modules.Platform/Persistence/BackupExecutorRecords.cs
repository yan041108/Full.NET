namespace Full.NET.Modules.Platform.Persistence;

/// <summary>备份任务行投影。</summary>
internal sealed class BackupTaskRecord
{
    public Guid Id { get; init; }

    public string TaskKey { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string DatabaseProvider { get; init; } = string.Empty;

    public bool IsEnabled { get; init; }

    public int SortOrder { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? UpdatedAtUtc { get; init; }
}

/// <summary>备份运行行投影，联接任务键与展示名。</summary>
internal sealed class BackupRunRecord
{
    public Guid Id { get; init; }

    public Guid TaskId { get; init; }

    public string TaskKey { get; init; } = string.Empty;

    public string TaskDisplayName { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public string? ArtifactFileName { get; init; }

    public long? ArtifactSizeBytes { get; init; }

    public string? ArtifactContentType { get; init; }

    public string? SummaryMessage { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }
}
