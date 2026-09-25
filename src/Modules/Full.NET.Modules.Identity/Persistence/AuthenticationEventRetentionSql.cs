using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>认证审计按时间有界清理，MySQL 在事务内先领取再删除。</summary>
internal static class AuthenticationEventRetentionSql
{
    public static readonly SqlStatement DeleteSqlServer = new(
        "identity.retention.delete_authentication_events.sql_server",
        """
        ;WITH Candidates AS
        (
            SELECT TOP (@BatchSize) Id
            FROM fn_identity_auth_audit WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE OccurredAtUtc < @CutoffUtc
            ORDER BY OccurredAtUtc, Id
        )
        DELETE FROM Candidates;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement SelectIdsMySql = new(
        "identity.retention.select_authentication_event_ids.my_sql",
        """
        SELECT Id FROM fn_identity_auth_audit
        WHERE OccurredAtUtc < @CutoffUtc
        ORDER BY OccurredAtUtc, Id
        LIMIT @BatchSize
        FOR UPDATE SKIP LOCKED;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteIdsMySql = new(
        "identity.retention.delete_authentication_event_ids.my_sql",
        "DELETE FROM fn_identity_auth_audit WHERE Id IN @Ids;",
        SqlDataScope.HostOnly);
}
