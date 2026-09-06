namespace Full.NET.Modules.Reporting.Persistence;

/// <summary>报表分组持久化行。</summary>
internal sealed class ReportingGroupRecord
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public int Version { get; set; }
}

/// <summary>报表定义草稿持久化行。</summary>
internal sealed class ReportingDefinitionRecord
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid DataSourceId { get; set; }
    public string DefinitionKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string QueryPortKey { get; set; } = string.Empty;
    public string ParameterSchemaJson { get; set; } = "[]";
    public string LayoutConfigJson { get; set; } = "{}";
    public int LatestPublishedVersionNumber { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public int Version { get; set; }
}

/// <summary>报表定义发布版本持久化行。</summary>
internal sealed class ReportingDefinitionVersionRecord
{
    public Guid Id { get; set; }
    public Guid DefinitionId { get; set; }
    public int VersionNumber { get; set; }
    public Guid DataSourceId { get; set; }
    public string QueryPortKey { get; set; } = string.Empty;
    public string ParameterSchemaJson { get; set; } = "[]";
    public string LayoutConfigJson { get; set; } = "{}";
    public string? ChangeNote { get; set; }
    public Guid PublishedByUserId { get; set; }
    public DateTimeOffset PublishedAtUtc { get; set; }
}
