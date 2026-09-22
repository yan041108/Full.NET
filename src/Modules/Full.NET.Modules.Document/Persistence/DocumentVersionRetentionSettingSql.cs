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

    public static readonly SqlStatement UpsertSqlServer = new(
        "document.version_retention_setting.upsert.sql_server",
        """
        MERGE fn_document_version_retention_setting AS target
        USING (SELECT @Id AS Id) AS source
        ON target.TenantId IS NULL
        WHEN MATCHED THEN
            UPDATE SET
                MinimumRetainedVersionsPerItem = @MinimumRetainedVersionsPerItem,
                MaximumRetainedHistoryVersions = @MaximumRetainedHistoryVersions,
                PollSeconds = @PollSeconds,
                BatchSize = @BatchSize,
                Version = target.Version + 1,
                UpdatedAtUtc = @UpdatedAtUtc
        WHEN NOT MATCHED THEN
            INSERT (Id, TenantId, MinimumRetainedVersionsPerItem, MaximumRetainedHistoryVersions,
                    PollSeconds, BatchSize, Version, UpdatedAtUtc)
            VALUES (@Id, NULL, @MinimumRetainedVersionsPerItem, @MaximumRetainedHistoryVersions,
                    @PollSeconds, @BatchSize, 1, @UpdatedAtUtc);
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpsertMySql = new(
        "document.version_retention_setting.upsert.mysql",
        """
        INSERT INTO fn_document_version_retention_setting
            (Id, TenantId, MinimumRetainedVersionsPerItem, MaximumRetainedHistoryVersions,
             PollSeconds, BatchSize, Version, UpdatedAtUtc)
        VALUES
            (@Id, NULL, @MinimumRetainedVersionsPerItem, @MaximumRetainedHistoryVersions,
             @PollSeconds, @BatchSize, 1, @UpdatedAtUtc)
        ON DUPLICATE KEY UPDATE
            MinimumRetainedVersionsPerItem = VALUES(MinimumRetainedVersionsPerItem),
            MaximumRetainedHistoryVersions = VALUES(MaximumRetainedHistoryVersions),
            PollSeconds = VALUES(PollSeconds),
            BatchSize = VALUES(BatchSize),
            Version = Version + 1,
            UpdatedAtUtc = VALUES(UpdatedAtUtc)
        """,
        SqlDataScope.HostOnly);
}
