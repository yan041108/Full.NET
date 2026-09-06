using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Platform.Persistence;

/// <summary>授权备份执行器 SQL 语句集合，全部声明为 <see cref="SqlDataScope.HostOnly"/>。</summary>
internal static class BackupExecutorSql
{
    private const string TaskColumns =
        """
        Id, TaskKey, DisplayName, Description, DatabaseProvider,
        IsEnabled, SortOrder, CreatedAtUtc, UpdatedAtUtc
        """;

    private const string RunColumns =
        """
        run.Id, run.TaskId, task.TaskKey, task.DisplayName AS TaskDisplayName,
        run.Status, run.StartedAtUtc, run.CompletedAtUtc,
        run.ArtifactFileName, run.ArtifactSizeBytes, run.ArtifactContentType,
        run.SummaryMessage, run.CreatedAtUtc
        """;

    private const string RunFromClause =
        """
        FROM fn_platform_backup_run AS run
        INNER JOIN fn_platform_backup_task AS task ON task.Id = run.TaskId
        """;

    private const string RunWhereClause =
        """
        (@TaskId IS NULL OR run.TaskId = @TaskId)
          AND (@Status IS NULL OR run.Status = @Status)
          AND (@FromUtc IS NULL OR run.StartedAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR run.StartedAtUtc <= @ToUtc)
        """;

    public static readonly SqlStatement ListTasks =
        new(
            "platform.list_backup_tasks",
            $"""
            SELECT {TaskColumns}
            FROM fn_platform_backup_task
            ORDER BY SortOrder, TaskKey
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement FindTaskById =
        new(
            "platform.find_backup_task_by_id",
            $"""
            SELECT {TaskColumns}
            FROM fn_platform_backup_task
            WHERE Id = @Id
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement CountEnabledTasks =
        new(
            "platform.count_enabled_backup_tasks",
            """
            SELECT COUNT(1)
            FROM fn_platform_backup_task
            WHERE IsEnabled = 1
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement CountRunsSqlServer =
        new(
            "platform.count_backup_runs.sql_server",
            $"""
            SELECT COUNT(1)
            {RunFromClause}
            WHERE {RunWhereClause}
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement CountRunsMySql =
        new(
            "platform.count_backup_runs.mysql",
            $"""
            SELECT COUNT(1)
            {RunFromClause}
            WHERE {RunWhereClause}
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListRunsSqlServer =
        new(
            "platform.list_backup_runs.sql_server",
            $"""
            SELECT {RunColumns}
            {RunFromClause}
            WHERE {RunWhereClause}
            ORDER BY run.StartedAtUtc DESC, run.Id
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListRunsMySql =
        new(
            "platform.list_backup_runs.mysql",
            $"""
            SELECT {RunColumns}
            {RunFromClause}
            WHERE {RunWhereClause}
            ORDER BY run.StartedAtUtc DESC, run.Id
            LIMIT @PageSize OFFSET @Offset
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement FindRunById =
        new(
            "platform.find_backup_run_by_id",
            $"""
            SELECT {RunColumns}
            {RunFromClause}
            WHERE run.Id = @Id
            """,
            SqlDataScope.HostOnly);
}
