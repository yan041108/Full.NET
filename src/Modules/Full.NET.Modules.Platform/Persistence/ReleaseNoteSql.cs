using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Platform.Persistence;

/// <summary>
/// 平台更新日志 SQL：Host 管理写路径为 HostOnly；终端用户已发布列表与已读为 Global。
/// </summary>
internal static class ReleaseNoteSql
{
    private const string SelectColumns =
        """
        Id, VersionLabel, VersionSortKey, Title, Content, Status,
        PublishedAtUtc, PublishedByUserId, RetractedAtUtc, RetractedByUserId,
        CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UpdatedByUserId, Version
        """;

    private const string HostWhereClause =
        """
        (@Title IS NULL OR Title LIKE @TitlePattern)
          AND (@Status IS NULL OR Status = @Status)
          AND (@VersionLabel IS NULL OR VersionLabel LIKE @VersionLabelPattern)
        """;

    private const string MySelectColumns =
        """
        note.Id, note.VersionLabel, note.VersionSortKey, note.Title, note.Content,
        note.PublishedAtUtc,
        CASE WHEN readState.Id IS NULL THEN 0 ELSE 1 END AS IsRead,
        readState.ReadAtUtc
        """;

    private const string MyFromClause =
        """
        FROM fn_platform_release_note AS note
        LEFT JOIN fn_platform_release_note_read AS readState
          ON readState.ReleaseNoteId = note.Id
         AND readState.UserId = @UserId
        WHERE note.Status = @PublishedStatus
        """;

    public static readonly SqlStatement ListHostSqlServer =
        new(
            "platform.list_host_release_notes.sql_server",
            $"""
            SELECT {SelectColumns}
            FROM fn_platform_release_note
            WHERE {HostWhereClause}
            ORDER BY VersionSortKey DESC, Id
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostMySql =
        new(
            "platform.list_host_release_notes.mysql",
            $"""
            SELECT {SelectColumns}
            FROM fn_platform_release_note
            WHERE {HostWhereClause}
            ORDER BY VersionSortKey DESC, Id
            LIMIT @PageSize OFFSET @Offset
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement CountHost =
        new(
            "platform.count_host_release_notes",
            $"""
            SELECT COUNT(*)
            FROM fn_platform_release_note
            WHERE {HostWhereClause}
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement FindHostById =
        new(
            "platform.find_host_release_note_by_id",
            $"""
            SELECT {SelectColumns}
            FROM fn_platform_release_note
            WHERE Id = @Id
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement FindHostByVersionLabel =
        new(
            "platform.find_host_release_note_by_version_label",
            $"""
            SELECT {SelectColumns}
            FROM fn_platform_release_note
            WHERE VersionLabel = @VersionLabel
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement Insert =
        new(
            "platform.insert_host_release_note",
            """
            INSERT INTO fn_platform_release_note
                (Id, VersionLabel, VersionSortKey, Title, Content, Status,
                 PublishedAtUtc, PublishedByUserId, RetractedAtUtc, RetractedByUserId,
                 CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UpdatedByUserId, Version)
            VALUES
                (@Id, @VersionLabel, @VersionSortKey, @Title, @Content, @Status,
                 NULL, NULL, NULL, NULL,
                 @CreatedAtUtc, NULL, @CreatedByUserId, NULL, @Version)
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateDraft =
        new(
            "platform.update_host_release_note_draft",
            """
            UPDATE fn_platform_release_note
            SET VersionLabel = @VersionLabel,
                VersionSortKey = @VersionSortKey,
                Title = @Title,
                Content = @Content,
                UpdatedAtUtc = @UpdatedAtUtc,
                UpdatedByUserId = @UpdatedByUserId,
                Version = @NextVersion
            WHERE Id = @Id
              AND Status = @DraftStatus
              AND Version = @Version
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement Publish =
        new(
            "platform.publish_host_release_note",
            """
            UPDATE fn_platform_release_note
            SET Status = @PublishedStatus,
                PublishedAtUtc = @PublishedAtUtc,
                PublishedByUserId = @PublishedByUserId,
                UpdatedAtUtc = @UpdatedAtUtc,
                UpdatedByUserId = @UpdatedByUserId,
                Version = @NextVersion
            WHERE Id = @Id
              AND Status = @DraftStatus
              AND Version = @Version
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement Retract =
        new(
            "platform.retract_host_release_note",
            """
            UPDATE fn_platform_release_note
            SET Status = @RetractedStatus,
                RetractedAtUtc = @RetractedAtUtc,
                RetractedByUserId = @RetractedByUserId,
                UpdatedAtUtc = @UpdatedAtUtc,
                UpdatedByUserId = @UpdatedByUserId,
                Version = @NextVersion
            WHERE Id = @Id
              AND Status = @PublishedStatus
              AND Version = @Version
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteDraft =
        new(
            "platform.delete_host_release_note_draft",
            """
            DELETE FROM fn_platform_release_note
            WHERE Id = @Id
              AND Status = @DraftStatus
              AND Version = @Version
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListPublishedForUserSqlServer =
        new(
            "platform.list_my_release_notes.sql_server",
            $"""
            SELECT {MySelectColumns}
            {MyFromClause}
            ORDER BY note.VersionSortKey DESC, note.Id
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement ListPublishedForUserMySql =
        new(
            "platform.list_my_release_notes.mysql",
            $"""
            SELECT {MySelectColumns}
            {MyFromClause}
            ORDER BY note.VersionSortKey DESC, note.Id
            LIMIT @PageSize OFFSET @Offset
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement CountPublishedForUser =
        new(
            "platform.count_my_release_notes",
            $"""
            SELECT COUNT(*)
            {MyFromClause}
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement FindLatestUnreadForUser =
        new(
            "platform.find_latest_unread_release_note_for_user",
            $"""
            SELECT TOP (1) {MySelectColumns}
            {MyFromClause}
              AND readState.Id IS NULL
            ORDER BY note.VersionSortKey DESC, note.Id
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement FindLatestUnreadForUserMySql =
        new(
            "platform.find_latest_unread_release_note_for_user.mysql",
            $"""
            SELECT {MySelectColumns}
            {MyFromClause}
              AND readState.Id IS NULL
            ORDER BY note.VersionSortKey DESC, note.Id
            LIMIT 1
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement FindPublishedByIdForUser =
        new(
            "platform.find_published_release_note_for_user_by_id",
            $"""
            SELECT {MySelectColumns}
            {MyFromClause}
              AND note.Id = @ReleaseNoteId
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement FindReadByUserAndReleaseNote =
        new(
            "platform.find_release_note_read_by_user",
            """
            SELECT Id, ReleaseNoteId, UserId, ReadAtUtc
            FROM fn_platform_release_note_read
            WHERE ReleaseNoteId = @ReleaseNoteId
              AND UserId = @UserId
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement InsertRead =
        new(
            "platform.insert_release_note_read",
            """
            INSERT INTO fn_platform_release_note_read
                (Id, ReleaseNoteId, UserId, ReadAtUtc)
            VALUES
                (@Id, @ReleaseNoteId, @UserId, @ReadAtUtc)
            """,
            SqlDataScope.Global);
}
