using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>认证事件只读查询；所有筛选值均作为参数传入。</summary>
internal static class AuthenticationEventSql
{
    private const string Filter = """
        FROM fn_identity_auth_audit
        WHERE OccurredAtUtc >= @FromUtc AND OccurredAtUtc < @ToUtc
          AND (@UserId IS NULL OR UserId = @UserId)
          AND (@EventType IS NULL OR EventType = @EventType)
          AND (@Succeeded IS NULL OR Succeeded = @Succeeded)
        """;

    private const string Projection = """
        Id, UserId, SessionId, EventType, ResultCode, Succeeded,
        ContextTenantId, OccurredAtUtc, ActorUserId, TraceId,
        AuthenticationMethod, ClientId, CenterSessionId, ApplicationSessionId
        """;

    public static readonly SqlStatement Count = new(
        "identity.count_authentication_events",
        "SELECT COUNT(*) " + Filter,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListSqlServer = new(
        "identity.list_authentication_events.sql_server",
        "SELECT TOP (@FetchSize) " + Projection + " " + Filter
            + " AND (@CursorOccurredAtUtc IS NULL OR OccurredAtUtc < @CursorOccurredAtUtc"
            + " OR (OccurredAtUtc = @CursorOccurredAtUtc AND Id < @CursorId))"
            + " ORDER BY OccurredAtUtc DESC, Id DESC",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListMySql = new(
        "identity.list_authentication_events.my_sql",
        "SELECT " + Projection + " " + Filter
            + " AND (@CursorOccurredAtUtc IS NULL OR OccurredAtUtc < @CursorOccurredAtUtc"
            + " OR (OccurredAtUtc = @CursorOccurredAtUtc AND Id < @CursorId))"
            + " ORDER BY OccurredAtUtc DESC, Id DESC LIMIT @FetchSize",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement GetById = new(
        "identity.get_authentication_event_by_id",
        "SELECT " + Projection + " FROM fn_identity_auth_audit WHERE Id = @Id",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ExportSqlServer = new(
        "identity.export_authentication_events.sql_server",
        "SELECT TOP (@MaxRows) " + Projection + " " + Filter
            + " ORDER BY OccurredAtUtc DESC, Id DESC",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ExportMySql = new(
        "identity.export_authentication_events.my_sql",
        "SELECT " + Projection + " " + Filter
            + " ORDER BY OccurredAtUtc DESC, Id DESC LIMIT @MaxRows",
        SqlDataScope.HostOnly);
}
