namespace Full.NET.Modules.Reporting.Persistence;

/// <summary>授权目录投影，只包含不可变版本的执行配置。</summary>
internal sealed class ReportingPublishedDefinitionRecord
{
    public Guid DefinitionId { get; set; }
    public string DefinitionKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public string QueryPortKey { get; set; } = string.Empty;
    public string ParameterSchemaJson { get; set; } = "[]";
    public string LayoutConfigJson { get; set; } = "{}";
}
