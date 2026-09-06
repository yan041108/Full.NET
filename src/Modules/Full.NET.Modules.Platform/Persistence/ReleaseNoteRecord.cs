namespace Full.NET.Modules.Platform.Persistence;

/// <summary>更新日志主表行投影，列序与 <see cref="ReleaseNoteSql"/> 查询保持一致。</summary>
internal sealed class ReleaseNoteRecord
{
    /// <summary>更新日志标识。</summary>
    public Guid Id { get; init; }

    /// <summary>展示用版本标签。</summary>
    public string VersionLabel { get; init; } = string.Empty;

    /// <summary>内部排序键。</summary>
    public long VersionSortKey { get; init; }

    /// <summary>标题。</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>正文内容。</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>生命周期状态。</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>发布时间（UTC）。</summary>
    public DateTimeOffset? PublishedAtUtc { get; init; }

    /// <summary>发布人用户标识。</summary>
    public Guid? PublishedByUserId { get; init; }

    /// <summary>撤回时间（UTC）。</summary>
    public DateTimeOffset? RetractedAtUtc { get; init; }

    /// <summary>撤回人用户标识。</summary>
    public Guid? RetractedByUserId { get; init; }

    /// <summary>创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>更新时间（UTC）。</summary>
    public DateTimeOffset? UpdatedAtUtc { get; init; }

    /// <summary>创建人用户标识。</summary>
    public Guid CreatedByUserId { get; init; }

    /// <summary>更新人用户标识。</summary>
    public Guid? UpdatedByUserId { get; init; }

    /// <summary>乐观并发版本号。</summary>
    public int Version { get; init; }
}

/// <summary>用户已读记录行投影。</summary>
internal sealed class ReleaseNoteReadRecord
{
    /// <summary>已读记录标识。</summary>
    public Guid Id { get; init; }

    /// <summary>更新日志标识。</summary>
    public Guid ReleaseNoteId { get; init; }

    /// <summary>用户标识。</summary>
    public Guid UserId { get; init; }

    /// <summary>标记已读时间（UTC）。</summary>
    public DateTimeOffset ReadAtUtc { get; init; }
}

/// <summary>用户列表查询联表投影，包含已读状态。</summary>
internal sealed class MyReleaseNoteRecord
{
    /// <summary>更新日志标识。</summary>
    public Guid Id { get; init; }

    /// <summary>展示用版本标签。</summary>
    public string VersionLabel { get; init; } = string.Empty;

    /// <summary>内部排序键。</summary>
    public long VersionSortKey { get; init; }

    /// <summary>标题。</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>正文内容。</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>发布时间（UTC）。</summary>
    public DateTimeOffset PublishedAtUtc { get; init; }

    /// <summary>当前用户是否已读。</summary>
    public bool IsRead { get; init; }

    /// <summary>当前用户标记已读时间（UTC）。</summary>
    public DateTimeOffset? ReadAtUtc { get; init; }
}
