using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Document.Persistence;

/// <summary>Host 版本保留策略单行表访问 SQL。</summary>
internal static class DocumentVersionRetentionSettingSql
{
    public static readonly Guid HostRowId = Guid.Parse("01950000-0000-7000-8000-000000000232");

    public static readonly SqlStatement SelectHost = new(
        "document.version_retention_setting.select_host",
        """
        SELECT
            Id, MinimumRetainedVersionsPerItem, MaximumRetainedHistoryVersions,
            PollSeconds, BatchSize, Version, UpdatedAtUtc
        FROM fn_document_version_retention_setting
        WHERE TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateSqlServer = new(
        "document.version_retention_setting.update.sql_server",
        """
        UPDATE fn_document_version_retention_setting SET
            MinimumRetainedVersionsPerItem = @MinimumRetainedVersionsPerItem,
            MaximumRetainedHistoryVersions = @MaximumRetainedHistoryVersions,
            PollSeconds = @PollSeconds,
            BatchSize = @BatchSize,
            Version = Version + 1,
            UpdatedAtUtc = @UpdatedAtUtc
        WHERE Id = @Id AND TenantId IS NULL AND Version = @Version;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateMySql = new(
        "document.version_retention_setting.update.mysql",
        UpdateSqlServer.Text,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertSqlServer = new(
        "document.version_retention_setting.insert.sql_server",
        """
        INSERT INTO fn_document_version_retention_setting
            (Id, TenantId, MinimumRetainedVersionsPerItem, MaximumRetainedHistoryVersions,
             PollSeconds, BatchSize, Version, UpdatedAtUtc)
        VALUES
            (@Id, NULL, @MinimumRetainedVersionsPerItem, @MaximumRetainedHistoryVersions,
             @PollSeconds, @BatchSize, 1, @UpdatedAtUtc);
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertMySql = new(
        "document.version_retention_setting.insert.mysql",
        InsertSqlServer.Text,
        SqlDataScope.HostOnly);
}
