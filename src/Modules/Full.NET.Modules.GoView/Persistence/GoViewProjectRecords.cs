namespace Full.NET.Modules.GoView.Persistence;

/// <summary>GoView 大屏项目草稿持久化行。</summary>
internal sealed class GoViewProjectRecord
{
    public Guid Id { get; set; }
    public string ProjectKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CanvasJson { get; set; } = string.Empty;
    public int LatestPublishedVersionNumber { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public int Version { get; set; }
}

/// <summary>GoView 大屏项目发布版本持久化行。</summary>
internal sealed class GoViewProjectVersionRecord
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public int VersionNumber { get; set; }
    public string CanvasJson { get; set; } = string.Empty;
    public string? ChangeNote { get; set; }
    public Guid PublishedByUserId { get; set; }
    public DateTimeOffset PublishedAtUtc { get; set; }
}
