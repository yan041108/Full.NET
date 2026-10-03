using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Auditing.Persistence;

/// <summary>
/// 过期详情清理 SQL；仅清空同一操作日志行的详情和到期时间，保留摘要。
/// SQL Server 以有序 CTE 原子更新，MySQL 在短事务中跳过被其他 Worker 锁定的行。
/// </summary>
internal static class AuditDetailsRetentionSql
{
    public static readonly SqlStatement ClearExpiredSqlServer = new(
        "auditing.details.clear_expired.sql_server",
        """
        ;WITH Candidates AS
        (
            SELECT TOP (@BatchSize) Id, ContextJson, DetailsExpiresAtUtc
            FROM fn_auditing_operation_log WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE ContextJson IS NOT NULL
              AND DetailsExpiresAtUtc <= @NowUtc
            ORDER BY DetailsExpiresAtUtc, Id
        )
        UPDATE Candidates
        SET ContextJson = NULL, DetailsExpiresAtUtc = NULL;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement SelectExpiredIdsMySql = new(
        "auditing.details.select_expired_ids.my_sql",
        """
        SELECT Id
        FROM fn_auditing_operation_log
        WHERE ContextJson IS NOT NULL
          AND DetailsExpiresAtUtc <= @NowUtc
        ORDER BY DetailsExpiresAtUtc, Id
        LIMIT @BatchSize
        FOR UPDATE SKIP LOCKED;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ClearClaimedMySql = new(
        "auditing.details.clear_claimed.my_sql",
        """
        UPDATE fn_auditing_operation_log
        SET ContextJson = NULL, DetailsExpiresAtUtc = NULL
        WHERE Id IN @Ids
          AND ContextJson IS NOT NULL
          AND DetailsExpiresAtUtc <= @NowUtc;
        """,
        SqlDataScope.HostOnly);
}
