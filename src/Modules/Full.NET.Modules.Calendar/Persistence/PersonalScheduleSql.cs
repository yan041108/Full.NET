using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Calendar.Persistence;

/// <summary>
/// 个人日程表的参数化 SQL。Host 写入保持 HostOnly；租户写入走 TenantRequired；
/// 当前用户读写使用 Global 并显式携带受信 TenantId 与 OwnerUserId 行守卫。
/// </summary>
internal static class PersonalScheduleSql
{
    private const string SelectColumns =
        """
        Id, TenantId, OwnerUserId, Content, StartAtUtc, EndAtUtc, Status,
        CompletedAtUtc, CreatedAtUtc, UpdatedAtUtc, Version
        """;

    private const string OwnerScopeClause =
        """
        OwnerUserId = @OwnerUserId
          AND (
            (@ScopeTenantId IS NULL AND TenantId IS NULL)
            OR TenantId = @ScopeTenantId
          )
        """;

    private const string ListWhereClause =
        $"""
        {OwnerScopeClause}
          AND (@Status IS NULL OR Status = @Status)
          AND (@FromUtc IS NULL OR StartAtUtc < @ToUtc)
          AND (@ToUtc IS NULL OR EndAtUtc > @FromUtc)
        """;

    public static readonly SqlStatement InsertHost =
        new(
            "calendar.insert_personal_schedule.host",
            """
            INSERT INTO fn_calendar_personal_schedule
                (Id, TenantId, OwnerUserId, Content, StartAtUtc, EndAtUtc, Status,
                 CompletedAtUtc, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@Id, NULL, @OwnerUserId, @Content, @StartAtUtc, @EndAtUtc, @Status,
                 NULL, @CreatedAtUtc, NULL, @Version)
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertTenant =
        new(
            "calendar.insert_personal_schedule.tenant",
            """
            INSERT INTO fn_calendar_personal_schedule
                (Id, TenantId, OwnerUserId, Content, StartAtUtc, EndAtUtc, Status,
                 CompletedAtUtc, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@Id, @TenantId, @OwnerUserId, @Content, @StartAtUtc, @EndAtUtc, @Status,
                 NULL, @CreatedAtUtc, NULL, @Version)
            """,
            SqlDataScope.TenantRequired,
            SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListSqlServer =
        new(
            "calendar.list_personal_schedules.sql_server",
            $"""
            SELECT {SelectColumns}
            FROM fn_calendar_personal_schedule
            WHERE {ListWhereClause}
            ORDER BY StartAtUtc ASC, Id
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement ListMySql =
        new(
            "calendar.list_personal_schedules.mysql",
            $"""
            SELECT {SelectColumns}
            FROM fn_calendar_personal_schedule
            WHERE {ListWhereClause}
            ORDER BY StartAtUtc ASC, Id
            LIMIT @PageSize OFFSET @Offset
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement CountForOwner =
        new(
            "calendar.count_personal_schedules",
            $"""
            SELECT COUNT(*)
            FROM fn_calendar_personal_schedule
            WHERE {ListWhereClause}
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement FindForOwnerById =
        new(
            "calendar.find_personal_schedule_for_owner",
            $"""
            SELECT {SelectColumns}
            FROM fn_calendar_personal_schedule
            WHERE Id = @Id
              AND {OwnerScopeClause}
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement UpdateForOwner =
        new(
            "calendar.update_personal_schedule",
            """
            UPDATE fn_calendar_personal_schedule
            SET Content = @Content,
                StartAtUtc = @StartAtUtc,
                EndAtUtc = @EndAtUtc,
                UpdatedAtUtc = @UpdatedAtUtc,
                Version = @NextVersion
            WHERE Id = @Id
              AND OwnerUserId = @OwnerUserId
              AND (
                (@ScopeTenantId IS NULL AND TenantId IS NULL)
                OR TenantId = @ScopeTenantId
              )
              AND Version = @Version
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement SetStatusForOwner =
        new(
            "calendar.set_personal_schedule_status",
            """
            UPDATE fn_calendar_personal_schedule
            SET Status = @Status,
                CompletedAtUtc = @CompletedAtUtc,
                UpdatedAtUtc = @UpdatedAtUtc,
                Version = @NextVersion
            WHERE Id = @Id
              AND OwnerUserId = @OwnerUserId
              AND (
                (@ScopeTenantId IS NULL AND TenantId IS NULL)
                OR TenantId = @ScopeTenantId
              )
              AND Version = @Version
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement DeleteForOwner =
        new(
            "calendar.delete_personal_schedule",
            """
            DELETE FROM fn_calendar_personal_schedule
            WHERE Id = @Id
              AND OwnerUserId = @OwnerUserId
              AND (
                (@ScopeTenantId IS NULL AND TenantId IS NULL)
                OR TenantId = @ScopeTenantId
              )
              AND Version = @Version
            """,
            SqlDataScope.Global);
}
