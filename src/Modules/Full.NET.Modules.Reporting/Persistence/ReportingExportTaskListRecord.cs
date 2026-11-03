namespace Full.NET.Modules.Reporting.Persistence;

/// <summary>本人任务分页只读取公开摘要字段，不读取执行正文或原会话绑定。</summary>
internal sealed class ReportingExportTaskListRecord
{
    public Guid Id { get; set; }
    public Guid DefinitionId { get; set; }
    public int VersionNumber { get; set; }
    public string DefinitionKey { get; set; } = string.Empty;
    public string DefinitionName { get; set; } = string.Empty;
    public string FormatKey { get; set; } = string.Empty;
    public string StatusKey { get; set; } = string.Empty;
    public string? OutputFileName { get; set; }
    public int RowCount { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid RequestedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
