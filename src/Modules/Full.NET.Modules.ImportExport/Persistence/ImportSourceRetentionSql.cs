using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.ImportExport.Persistence;

/// <summary>目录只返回定位元数据；读取、标记和完成均在可信租户范围内执行。</summary>
internal static class ImportSourceRetentionSql
{
    private const string Due = """
        SourceExpiresAtUtc <= @Now AND SourceReleasedAtUtc IS NULL
        AND StatusKey NOT IN ('queued','executing')
        AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc <= @Now)
        """;
    private const string DirectorySource = """
        FROM fn_import_export_task
        WHERE
        """ + " " + Due + "\n" + """

          AND (@AfterExpiresAtUtc IS NULL OR SourceExpiresAtUtc > @AfterExpiresAtUtc
            OR (SourceExpiresAtUtc = @AfterExpiresAtUtc AND Id > @AfterId))
        ORDER BY SourceExpiresAtUtc, Id
        """;

    internal static readonly SqlStatement ListDueSqlServer = new("import_export.source_retention.directory.sqlserver",
        "SELECT TOP (@Take) Id, TenantId, SourceExpiresAtUtc " + DirectorySource, SqlDataScope.Global);
    internal static readonly SqlStatement ListDueMySql = new("import_export.source_retention.directory.mysql",
        "SELECT Id, TenantId, SourceExpiresAtUtc " + DirectorySource + " LIMIT @Take", SqlDataScope.Global);

    /// <summary>过期意图与排队 CAS 互斥，标记后不能因物理删除失败而重新执行来源。</summary>
    internal static readonly SqlStatement BeginRelease = new("import_export.source_retention.begin_release", """
        UPDATE fn_import_export_task
        SET SourceReleaseStartedAtUtc=@Now, Version=Version+1
        WHERE TenantId=@TenantId AND Id=@Id AND SourceFileId=@FileId AND Version=@Version
          AND SourceReleaseStartedAtUtc IS NULL AND
        """ + " " + Due, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement CompleteRelease = new("import_export.source_retention.complete_release", """
        UPDATE fn_import_export_task
        SET SourceReleasedAtUtc=@Now, Version=Version+1
        WHERE TenantId=@TenantId AND Id=@Id AND SourceFileId=@FileId
          AND SourceReleaseStartedAtUtc IS NOT NULL AND SourceReleasedAtUtc IS NULL
          AND StatusKey NOT IN ('queued','executing')
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}

/// <summary>全局目录的最小定位事实，不含文件、用户或任务正文。</summary>
internal sealed class ImportSourceRetentionCandidate
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public DateTimeOffset SourceExpiresAtUtc { get; init; }
}
