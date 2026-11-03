using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Files.Persistence;

/// <summary>复用文件 pending→ready 状态机持久化上传操作身份，避免重试创建第二份文件。</summary>
internal static class HostFileUploadOperationSql
{
    public static readonly SqlStatement Find = new("files.host_upload_operation.find", """
        SELECT Id, OriginalFileName, ContentType, SizeBytes, ContentHash, CreatedByUserId, StorageState, CASE WHEN DeletedAtUtc IS NULL THEN 0 ELSE 1 END AS IsDeleted
        FROM fn_files_file WHERE TenantId IS NULL AND UploadOperationKey = @UploadOperationKey
        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement Insert = HostFileSql.Insert with
    {
        Name = "files.host_upload_operation.insert",
        Text = HostFileSql.Insert.Text.Replace("(Id,", "(UploadOperationKey, Id,", StringComparison.Ordinal)
            .Replace("(@Id,", "(@UploadOperationKey, @Id,", StringComparison.Ordinal),
    };
}

/// <summary>已持久化上传意图，即使尚未 ready 也阻止盲目重复发布。</summary>
internal sealed class HostFileUploadOperationRecord
{
    public Guid Id { get; init; }
    public string OriginalFileName { get; init; } = "";
    public string ContentType { get; init; } = "";
    public long SizeBytes { get; init; }
    public string? ContentHash { get; init; }
    public Guid CreatedByUserId { get; init; }
    public string StorageState { get; init; } = "";
    public bool IsDeleted { get; init; }
}
