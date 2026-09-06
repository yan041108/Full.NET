namespace Full.NET.Modules.Printing.Persistence;

/// <summary>打印模板草稿持久化行。</summary>
internal sealed class PrintingTemplateRecord
{
    public Guid Id { get; set; }
    public string TemplateKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FormSchemaKey { get; set; } = string.Empty;
    public string LayoutHtml { get; set; } = string.Empty;
    public int LatestPublishedVersionNumber { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public int Version { get; set; }
}

/// <summary>打印模板发布版本持久化行。</summary>
internal sealed class PrintingTemplateVersionRecord
{
    public Guid Id { get; set; }
    public Guid TemplateId { get; set; }
    public int VersionNumber { get; set; }
    public string LayoutHtml { get; set; } = string.Empty;
    public string? ChangeNote { get; set; }
    public Guid PublishedByUserId { get; set; }
    public DateTimeOffset PublishedAtUtc { get; set; }
}
