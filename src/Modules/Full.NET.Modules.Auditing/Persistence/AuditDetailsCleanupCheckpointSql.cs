using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Auditing.Persistence;

/// <summary>详情清理的跨实例检查点；只读取 Auditing 自有过期行并写入固定单行状态。</summary>
internal static class AuditDetailsCleanupCheckpointSql
{
    public static readonly SqlStatement ReadSqlServer = new(
        "auditing.details.checkpoint.read.sql_server",
        """
        SELECT LastSuccessfulCleanupAtUtc, OldestExpiredAtUtc
        FROM fn_auditing_details_cleanup_state
        WHERE StateKey = 1;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ReadMySql = new(
        "auditing.details.checkpoint.read.my_sql",
        """
        SELECT LastSuccessfulCleanupAtUtc, OldestExpiredAtUtc
        FROM fn_auditing_details_cleanup_state
        WHERE StateKey = 1;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement OldestExpiredSqlServer = new(
        "auditing.details.checkpoint.oldest.sql_server",
        """
        SELECT TOP (1) DetailsExpiresAtUtc
        FROM fn_auditing_operation_log
        WHERE ContextJson IS NOT NULL
          AND DetailsExpiresAtUtc <= @ObservedAtUtc
        ORDER BY DetailsExpiresAtUtc, Id;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement OldestExpiredMySql = new(
        "auditing.details.checkpoint.oldest.my_sql",
        """
        SELECT DetailsExpiresAtUtc
        FROM fn_auditing_operation_log
        WHERE ContextJson IS NOT NULL
          AND DetailsExpiresAtUtc <= @ObservedAtUtc
        ORDER BY DetailsExpiresAtUtc, Id
        LIMIT 1;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpsertSqlServer = new(
        "auditing.details.checkpoint.upsert.sql_server",
        """
        UPDATE fn_auditing_details_cleanup_state WITH (UPDLOCK, HOLDLOCK)
        SET OldestExpiredAtUtc = CASE
                WHEN LastSuccessfulCleanupAtUtc < @ObservedAtUtc
                    THEN @OldestExpiredAtUtc
                ELSE OldestExpiredAtUtc END,
            LastSuccessfulCleanupAtUtc = CASE
                WHEN LastSuccessfulCleanupAtUtc < @ObservedAtUtc
                    THEN @ObservedAtUtc
                ELSE LastSuccessfulCleanupAtUtc END
        WHERE StateKey = 1;
        IF @@ROWCOUNT = 0
            INSERT INTO fn_auditing_details_cleanup_state
                (Id, StateKey, LastSuccessfulCleanupAtUtc, OldestExpiredAtUtc)
            VALUES (@Id, 1, @ObservedAtUtc, @OldestExpiredAtUtc);
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpsertMySql = new(
        "auditing.details.checkpoint.upsert.my_sql",
        """
        INSERT INTO fn_auditing_details_cleanup_state
            (Id, StateKey, LastSuccessfulCleanupAtUtc, OldestExpiredAtUtc)
        VALUES (@Id, 1, @ObservedAtUtc, @OldestExpiredAtUtc)
        ON DUPLICATE KEY UPDATE
            OldestExpiredAtUtc = IF(
                LastSuccessfulCleanupAtUtc < @ObservedAtUtc,
                @OldestExpiredAtUtc,
                OldestExpiredAtUtc),
            LastSuccessfulCleanupAtUtc = GREATEST(
                LastSuccessfulCleanupAtUtc, @ObservedAtUtc);
        """,
        SqlDataScope.HostOnly);
}
