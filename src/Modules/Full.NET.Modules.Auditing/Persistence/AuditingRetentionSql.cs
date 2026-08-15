using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Retention;

namespace Full.NET.Modules.Auditing.Persistence;

/// <summary>
/// 审计保留策略清理 SQL 语句集合。为 Access/Operation/Exception/Outbound 四类日志分别提供：
/// SQL Server 原生 DELETE TOP(N) WHERE OccurredAtUtc 小于截止时间；
/// MySQL 分两段式：先 SELECT 锁定批次 Id 再 DELETE BY ID，避免 DELETE LIMIT 触发表锁。
/// 所有删除语句使用精确截止时间戳而非批量时间窗口，保证可恢复与可观测。
/// </summary>
internal static class AuditingRetentionSql
{
    private static readonly SqlStatement DeleteAccessSqlServer = new(
        "auditing.retention.delete_access.sql_server",
        """
        ;WITH Candidates AS
        (
            SELECT TOP (@BatchSize) Id
            FROM fn_auditing_access_log WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE OccurredAtUtc < @CutoffUtc
            ORDER BY OccurredAtUtc, Id
        )
        DELETE FROM Candidates;
        """,
        SqlDataScope.HostOnly);

    private static readonly SqlStatement DeleteOperationSqlServer = new(
        "auditing.retention.delete_operation.sql_server",
        """
        ;WITH Candidates AS
        (
            SELECT TOP (@BatchSize) Id
            FROM fn_auditing_operation_log WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE OccurredAtUtc < @CutoffUtc
            ORDER BY OccurredAtUtc, Id
        )
        DELETE FROM Candidates;
        """,
        SqlDataScope.HostOnly);

    private static readonly SqlStatement DeleteExceptionSqlServer = new(
        "auditing.retention.delete_exception.sql_server",
        """
        ;WITH Candidates AS
        (
            SELECT TOP (@BatchSize) Id
            FROM fn_auditing_exception_log WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE OccurredAtUtc < @CutoffUtc
            ORDER BY OccurredAtUtc, Id
        )
        DELETE FROM Candidates;
        """,
        SqlDataScope.HostOnly);

    private static readonly SqlStatement DeleteOutboundSqlServer = new(
        "auditing.retention.delete_outbound.sql_server",
        """
        ;WITH Candidates AS
        (
            SELECT TOP (@BatchSize) Id
            FROM fn_auditing_outbound_call WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE OccurredAtUtc < @CutoffUtc
            ORDER BY OccurredAtUtc, Id
        )
        DELETE FROM Candidates;
        """,
        SqlDataScope.HostOnly);

    private static readonly SqlStatement SelectAccessIdsMySql = new(
        "auditing.retention.select_access_ids.my_sql",
        """
        SELECT Id
        FROM fn_auditing_access_log
        WHERE OccurredAtUtc < @CutoffUtc
        ORDER BY OccurredAtUtc, Id
        LIMIT @BatchSize
        FOR UPDATE SKIP LOCKED;
        """,
        SqlDataScope.HostOnly);

    private static readonly SqlStatement SelectOperationIdsMySql = new(
        "auditing.retention.select_operation_ids.my_sql",
        """
        SELECT Id
        FROM fn_auditing_operation_log
        WHERE OccurredAtUtc < @CutoffUtc
        ORDER BY OccurredAtUtc, Id
        LIMIT @BatchSize
        FOR UPDATE SKIP LOCKED;
        """,
        SqlDataScope.HostOnly);

    private static readonly SqlStatement SelectExceptionIdsMySql = new(
        "auditing.retention.select_exception_ids.my_sql",
        """
        SELECT Id
        FROM fn_auditing_exception_log
        WHERE OccurredAtUtc < @CutoffUtc
        ORDER BY OccurredAtUtc, Id
        LIMIT @BatchSize
        FOR UPDATE SKIP LOCKED;
        """,
        SqlDataScope.HostOnly);

    private static readonly SqlStatement SelectOutboundIdsMySql = new(
        "auditing.retention.select_outbound_ids.my_sql",
        """
        SELECT Id
        FROM fn_auditing_outbound_call
        WHERE OccurredAtUtc < @CutoffUtc
        ORDER BY OccurredAtUtc, Id
        LIMIT @BatchSize
        FOR UPDATE SKIP LOCKED;
        """,
        SqlDataScope.HostOnly);

    private static readonly SqlStatement DeleteAccessIdsMySql = new(
        "auditing.retention.delete_access_ids.my_sql",
        """
        DELETE FROM fn_auditing_access_log
        WHERE Id IN @Ids;
        """,
        SqlDataScope.HostOnly);

    private static readonly SqlStatement DeleteOperationIdsMySql = new(
        "auditing.retention.delete_operation_ids.my_sql",
        """
        DELETE FROM fn_auditing_operation_log
        WHERE Id IN @Ids;
        """,
        SqlDataScope.HostOnly);

    private static readonly SqlStatement DeleteExceptionIdsMySql = new(
        "auditing.retention.delete_exception_ids.my_sql",
        """
        DELETE FROM fn_auditing_exception_log
        WHERE Id IN @Ids;
        """,
        SqlDataScope.HostOnly);

    private static readonly SqlStatement DeleteOutboundIdsMySql = new(
        "auditing.retention.delete_outbound_ids.my_sql",
        """
        DELETE FROM fn_auditing_outbound_call
        WHERE Id IN @Ids;
        """,
        SqlDataScope.HostOnly);

    public static SqlStatement GetSqlServerDelete(
        AuditingRetentionCategory category) =>
        category switch
        {
            AuditingRetentionCategory.Access => DeleteAccessSqlServer,
            AuditingRetentionCategory.Operation => DeleteOperationSqlServer,
            AuditingRetentionCategory.Exception => DeleteExceptionSqlServer,
            AuditingRetentionCategory.Outbound => DeleteOutboundSqlServer,
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

    public static SqlStatement GetMySqlSelect(
        AuditingRetentionCategory category) =>
        category switch
        {
            AuditingRetentionCategory.Access => SelectAccessIdsMySql,
            AuditingRetentionCategory.Operation => SelectOperationIdsMySql,
            AuditingRetentionCategory.Exception => SelectExceptionIdsMySql,
            AuditingRetentionCategory.Outbound => SelectOutboundIdsMySql,
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

    public static SqlStatement GetMySqlDelete(
        AuditingRetentionCategory category) =>
        category switch
        {
            AuditingRetentionCategory.Access => DeleteAccessIdsMySql,
            AuditingRetentionCategory.Operation => DeleteOperationIdsMySql,
            AuditingRetentionCategory.Exception => DeleteExceptionIdsMySql,
            AuditingRetentionCategory.Outbound => DeleteOutboundIdsMySql,
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };
}
