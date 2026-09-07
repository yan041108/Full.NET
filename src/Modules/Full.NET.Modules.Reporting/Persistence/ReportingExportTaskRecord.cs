namespace Full.NET.Modules.Reporting.Persistence;

/// <summary>报表导出任务持久化行。</summary>
internal sealed class ReportingExportTaskRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid DefinitionId { get; set; }
    public int VersionNumber { get; set; }
    public string DefinitionKey { get; set; } = string.Empty;
    public string DefinitionName { get; set; } = string.Empty;
    public string FormatKey { get; set; } = string.Empty;
    public string ParametersJson { get; set; } = "[]";
    public string StatusKey { get; set; } = string.Empty;
    public Guid? OutputFileId { get; set; }
    public string? OutputFileName { get; set; }
    public int RowCount { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid RequestedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    /// <summary>当前执行租约；未领取或已完成后为空。</summary>
    public Guid? LeaseId { get; set; }
    /// <summary>租约到期时间；到期后允许其他 Worker 重新领取。</summary>
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    /// <summary>创建导出时主体权限码 JSON 快照，供崩溃恢复重建授权。</summary>
    public string? ActorPermissionCodesJson { get; set; }
    public long Version { get; set; }
}
