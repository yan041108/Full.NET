using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Jobs.Persistence;

/// <summary>
/// Jobs 模块 SQL 语句集合。覆盖三大聚合：
/// 1) JobDefinition 定义：分页列表（SQL Server/MySQL 适配）、按 Id/JobKey 查找、启用分组去重、
///    插入、更新、禁用、硬删除、活跃计划/执行计数防御性校验；
/// 2) JobSchedule 计划：按定义分页列表（含关联定义 JOIN）、按 Id 查找详情、启用定义下拉选项、
///    插入、更新、暂停/恢复、硬删除、按定义级联清理；
/// 3) JobExecution 执行：按定义分页列表、按 Id 查找、插入待处理、申领租约、更新状态/错误/重试、
///    按定义清空终态记录。
/// Host 作用域语句均限定 TenantId IS NULL。
/// </summary>
internal static class JobSql
{
    public static readonly SqlStatement ListDefinitionsSqlServer =
        new(
            "jobs.list_host_definitions.sql_server",
            """
            SELECT Id, TenantId, JobKey, HandlerKind, ArgsJson, DisplayName, Description, GroupName, IsEnabled,
                   AllowConcurrentExecutions,
                   CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UpdatedByUserId, Version
            FROM fn_jobs_definition
            WHERE TenantId IS NULL
            ORDER BY JobKey
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListDefinitionsMySql =
        new(
            "jobs.list_host_definitions.mysql",
            """
            SELECT Id, TenantId, JobKey, HandlerKind, ArgsJson, DisplayName, Description, GroupName, IsEnabled,
                   AllowConcurrentExecutions,
                   CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UpdatedByUserId, Version
            FROM fn_jobs_definition
            WHERE TenantId IS NULL
            ORDER BY JobKey
            LIMIT @PageSize OFFSET @Offset
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement CountDefinitions =
        new(
            "jobs.count_host_definitions",
            """
            SELECT COUNT(*)
            FROM fn_jobs_definition
            WHERE TenantId IS NULL
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement FindDefinitionById =
        new(
            "jobs.find_host_definition_by_id",
            """
            SELECT Id, TenantId, JobKey, HandlerKind, ArgsJson, DisplayName, Description, GroupName, IsEnabled,
                   AllowConcurrentExecutions,
                   CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UpdatedByUserId, Version
            FROM fn_jobs_definition
            WHERE Id = @Id AND TenantId IS NULL
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement FindDefinitionsByIds =
        new(
            "jobs.find_host_definitions_by_ids",
            """
            SELECT Id, TenantId, JobKey, HandlerKind, ArgsJson, DisplayName, Description, GroupName, IsEnabled,
                   AllowConcurrentExecutions,
                   CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UpdatedByUserId, Version
            FROM fn_jobs_definition
            WHERE TenantId IS NULL
              AND Id IN @Ids
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement FindDefinitionByJobKey =
        new(
            "jobs.find_host_definition_by_job_key",
            """
            SELECT Id, TenantId, JobKey, HandlerKind, ArgsJson, DisplayName, Description, GroupName, IsEnabled,
                   AllowConcurrentExecutions,
                   CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UpdatedByUserId, Version
            FROM fn_jobs_definition
            WHERE JobKey = @JobKey AND TenantId IS NULL
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertDefinition =
        new(
            "jobs.insert_host_definition",
            """
            INSERT INTO fn_jobs_definition
                (Id, TenantId, JobKey, HandlerKind, ArgsJson, DisplayName, Description, GroupName, IsEnabled,
                 AllowConcurrentExecutions,
                 CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UpdatedByUserId, Version)
            VALUES
                (@Id, NULL, @JobKey, @HandlerKind, @ArgsJson, @DisplayName, @Description, @GroupName, @IsEnabled,
                 @AllowConcurrentExecutions,
                 @CreatedAtUtc, NULL, @CreatedByUserId, NULL, @Version)
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateDefinition =
        new(
            "jobs.update_host_definition",
            """
            UPDATE fn_jobs_definition
            SET DisplayName = @DisplayName,
                Description = @Description,
                GroupName = @GroupName,
                HandlerKind = @HandlerKind,
                ArgsJson = @ArgsJson,
                AllowConcurrentExecutions = @AllowConcurrentExecutions,
                UpdatedAtUtc = @UpdatedAtUtc,
                UpdatedByUserId = @UpdatedByUserId,
                Version = @NextVersion
            WHERE Id = @Id
              AND TenantId IS NULL
              AND Version = @Version
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement DisableDefinition =
        new(
            "jobs.disable_host_definition",
            """
            UPDATE fn_jobs_definition
            SET IsEnabled = 0,
                UpdatedAtUtc = @UpdatedAtUtc,
                UpdatedByUserId = @UpdatedByUserId,
                Version = @NextVersion
            WHERE Id = @Id
              AND TenantId IS NULL
              AND IsEnabled = 1
              AND Version = @Version
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertExecution =
        new(
            "jobs.insert_host_execution",
            """
            INSERT INTO fn_jobs_execution
                (Id, TenantId, JobDefinitionId, Status, TriggerKind,
                 ErrorMessage, StartedAtUtc, FinishedAtUtc,
                 LeaseId, LeaseExpiresAtUtc, NextAttemptAtUtc,
                 AttemptCount, CreatedAtUtc)
            VALUES
                (@Id, NULL, @JobDefinitionId, @Status, @TriggerKind,
                 NULL, NULL, NULL, NULL, NULL, NULL, 0, @CreatedAtUtc)
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListExecutionsSqlServer =
        new(
            "jobs.list_host_executions.sql_server",
            """
            SELECT e.Id, e.TenantId, e.JobDefinitionId, e.JobScheduleId,
                   e.Status, e.TriggerKind, e.ScheduledForUtc,
                   e.ErrorMessage, e.StartedAtUtc, e.FinishedAtUtc,
                   e.LeaseId, e.LeaseExpiresAtUtc, e.NextAttemptAtUtc,
                   e.AttemptCount, e.CreatedAtUtc,
                   d.JobKey
            FROM fn_jobs_execution e
            INNER JOIN fn_jobs_definition d ON d.Id = e.JobDefinitionId
            WHERE e.TenantId IS NULL
              AND (@JobDefinitionId IS NULL OR e.JobDefinitionId = @JobDefinitionId)
              AND (@JobScheduleId IS NULL OR e.JobScheduleId = @JobScheduleId)
              AND (@Status IS NULL OR e.Status = @Status)
              AND (@FromUtc IS NULL OR e.CreatedAtUtc >= @FromUtc)
              AND (@ToUtc IS NULL OR e.CreatedAtUtc <= @ToUtc)
            ORDER BY e.CreatedAtUtc DESC, e.Id
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListExecutionsMySql =
        new(
            "jobs.list_host_executions.mysql",
            """
            SELECT e.Id, e.TenantId, e.JobDefinitionId, e.JobScheduleId,
                   e.Status, e.TriggerKind, e.ScheduledForUtc,
                   e.ErrorMessage, e.StartedAtUtc, e.FinishedAtUtc,
                   e.LeaseId, e.LeaseExpiresAtUtc, e.NextAttemptAtUtc,
                   e.AttemptCount, e.CreatedAtUtc,
                   d.JobKey
            FROM fn_jobs_execution e
            INNER JOIN fn_jobs_definition d ON d.Id = e.JobDefinitionId
            WHERE e.TenantId IS NULL
              AND (@JobDefinitionId IS NULL OR e.JobDefinitionId = @JobDefinitionId)
              AND (@JobScheduleId IS NULL OR e.JobScheduleId = @JobScheduleId)
              AND (@Status IS NULL OR e.Status = @Status)
              AND (@FromUtc IS NULL OR e.CreatedAtUtc >= @FromUtc)
              AND (@ToUtc IS NULL OR e.CreatedAtUtc <= @ToUtc)
            ORDER BY e.CreatedAtUtc DESC, e.Id
            LIMIT @PageSize OFFSET @Offset
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement CountExecutions =
        new(
            "jobs.count_host_executions",
            """
            SELECT COUNT(*)
            FROM fn_jobs_execution
            WHERE TenantId IS NULL
              AND (@JobDefinitionId IS NULL OR JobDefinitionId = @JobDefinitionId)
              AND (@JobScheduleId IS NULL OR JobScheduleId = @JobScheduleId)
              AND (@Status IS NULL OR Status = @Status)
              AND (@FromUtc IS NULL OR CreatedAtUtc >= @FromUtc)
              AND (@ToUtc IS NULL OR CreatedAtUtc <= @ToUtc)
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement FindExecutionById =
        new(
            "jobs.find_host_execution_by_id",
            """
            SELECT e.Id, e.TenantId, e.JobDefinitionId, e.JobScheduleId,
                   e.Status, e.TriggerKind, e.ScheduledForUtc,
                   e.ErrorMessage, e.StartedAtUtc, e.FinishedAtUtc,
                   e.LeaseId, e.LeaseExpiresAtUtc, e.NextAttemptAtUtc,
                   e.AttemptCount, e.CreatedAtUtc,
                   d.JobKey
            FROM fn_jobs_execution e
            INNER JOIN fn_jobs_definition d ON d.Id = e.JobDefinitionId
            WHERE e.Id = @Id AND e.TenantId IS NULL
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement FindScheduleById =
        new(
            "jobs.find_host_schedule_by_id",
            """
            SELECT s.Id, s.TenantId, s.JobDefinitionId, s.TriggerKind, s.CronExpression,
                   s.TimeZoneId, s.OneTimeAtUtc, s.MisfirePolicy, s.IsEnabled,
                   s.NextExecutionAtUtc, s.LastExecutionAtUtc, s.CompletedAtUtc,
                   s.NumberOfRuns, s.NumberOfErrors, s.StartTime, s.EndTime, s.Args,
                   s.CreatedAtUtc, s.CreatedByUserId, s.UpdatedAtUtc, s.UpdatedByUserId,
                   s.Version, d.AllowConcurrentExecutions
            FROM fn_jobs_schedule AS s
            INNER JOIN fn_jobs_definition AS d
                ON d.Id = s.JobDefinitionId AND d.TenantId IS NULL
            WHERE s.Id = @Id AND s.TenantId IS NULL
            """,
            SqlDataScope.HostOnly);

    private const string ScheduleDetailProjection = """
        s.Id, s.TenantId, s.JobDefinitionId, s.TriggerKind, s.CronExpression,
        s.TimeZoneId, s.OneTimeAtUtc, s.MisfirePolicy, s.IsEnabled,
        s.NextExecutionAtUtc, s.LastExecutionAtUtc, s.CompletedAtUtc,
        s.NumberOfRuns, s.NumberOfErrors, s.StartTime, s.EndTime, s.Args,
        s.CreatedAtUtc, s.CreatedByUserId, s.UpdatedAtUtc, s.UpdatedByUserId,
        s.Version, d.JobKey AS JobDefinitionJobKey, d.DisplayName AS JobDefinitionDisplayName
        """;

    private const string ScheduleListWhereClause = """
        s.TenantId IS NULL
          AND (@JobDefinitionId IS NULL
               OR s.JobDefinitionId = @JobDefinitionId)
          AND (@TriggerKind IS NULL
               OR s.TriggerKind = @TriggerKind)
          AND (@IsEnabled IS NULL
               OR s.IsEnabled = @IsEnabled)
          AND (@Search IS NULL
               OR d.DisplayName LIKE @Search
               OR d.JobKey LIKE @Search)
        """;

    public static readonly SqlStatement FindScheduleDetailById =
        new(
            "jobs.find_host_schedule_detail_by_id",
            $$"""
            SELECT {{ScheduleDetailProjection}}
            FROM fn_jobs_schedule AS s
            INNER JOIN fn_jobs_definition AS d
                ON d.Id = s.JobDefinitionId AND d.TenantId IS NULL
            WHERE s.Id = @Id AND s.TenantId IS NULL
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListSchedulesSqlServer =
        new(
            "jobs.list_host_schedules.sql_server",
            $$"""
            SELECT {{ScheduleDetailProjection}}
            FROM fn_jobs_schedule AS s
            INNER JOIN fn_jobs_definition AS d
                ON d.Id = s.JobDefinitionId AND d.TenantId IS NULL
            WHERE {{ScheduleListWhereClause}}
            ORDER BY s.CreatedAtUtc DESC, s.Id
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListSchedulesMySql =
        new(
            "jobs.list_host_schedules.mysql",
            $$"""
            SELECT {{ScheduleDetailProjection}}
            FROM fn_jobs_schedule AS s
            INNER JOIN fn_jobs_definition AS d
                ON d.Id = s.JobDefinitionId AND d.TenantId IS NULL
            WHERE {{ScheduleListWhereClause}}
            ORDER BY s.CreatedAtUtc DESC, s.Id
            LIMIT @PageSize OFFSET @Offset
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement CountSchedules =
        new(
            "jobs.count_host_schedules",
            $$"""
            SELECT COUNT(*)
            FROM fn_jobs_schedule AS s
            INNER JOIN fn_jobs_definition AS d
                ON d.Id = s.JobDefinitionId AND d.TenantId IS NULL
            WHERE {{ScheduleListWhereClause}}
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListEnabledScheduleDefinitionOptions =
        new(
            "jobs.list_enabled_schedule_definition_options",
            """
            SELECT Id, JobKey, HandlerKind, DisplayName
            FROM fn_jobs_definition
            WHERE TenantId IS NULL
              AND IsEnabled = 1
            ORDER BY DisplayName, JobKey
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertSchedule =
        new(
            "jobs.insert_host_schedule",
            """
            INSERT INTO fn_jobs_schedule
                (Id, TenantId, JobDefinitionId, TriggerKind, CronExpression,
                 TimeZoneId, OneTimeAtUtc, MisfirePolicy, IsEnabled,
                 NextExecutionAtUtc, LastExecutionAtUtc, CompletedAtUtc,
                 NumberOfRuns, NumberOfErrors, StartTime, EndTime, Args,
                 CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId,
                 Version)
            VALUES
                (@Id, NULL, @JobDefinitionId, @TriggerKind, @CronExpression,
                 @TimeZoneId, @OneTimeAtUtc, @MisfirePolicy, @IsEnabled,
                 @NextExecutionAtUtc, NULL, NULL,
                 0, 0, @StartTime, @EndTime, @Args,
                 @CreatedAtUtc, @CreatedByUserId, NULL, NULL, @Version)
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement PauseSchedule =
        new(
            "jobs.pause_host_schedule",
            """
            UPDATE fn_jobs_schedule
            SET IsEnabled = 0,
                UpdatedAtUtc = @UpdatedAtUtc,
                UpdatedByUserId = @UpdatedByUserId,
                Version = @NextVersion
            WHERE Id = @Id
              AND TenantId IS NULL
              AND IsEnabled = 1
              AND CompletedAtUtc IS NULL
              AND Version = @Version
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateSchedule =
        new(
            "jobs.update_host_schedule",
            """
            UPDATE fn_jobs_schedule
            SET TriggerKind = @TriggerKind,
                CronExpression = @CronExpression,
                TimeZoneId = @TimeZoneId,
                OneTimeAtUtc = @OneTimeAtUtc,
                MisfirePolicy = @MisfirePolicy,
                NextExecutionAtUtc = @NextExecutionAtUtc,
                StartTime = @StartTime,
                EndTime = @EndTime,
                Args = @Args,
                UpdatedAtUtc = @UpdatedAtUtc,
                UpdatedByUserId = @UpdatedByUserId,
                Version = @NextVersion
            WHERE Id = @Id
              AND TenantId IS NULL
              AND CompletedAtUtc IS NULL
              AND Version = @Version
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ResumeSchedule =
        new(
            "jobs.resume_host_schedule",
            """
            UPDATE fn_jobs_schedule
            SET IsEnabled = 1,
                NextExecutionAtUtc = @NextExecutionAtUtc,
                UpdatedAtUtc = @UpdatedAtUtc,
                UpdatedByUserId = @UpdatedByUserId,
                Version = @NextVersion
            WHERE Id = @Id
              AND TenantId IS NULL
              AND IsEnabled = 0
              AND CompletedAtUtc IS NULL
              AND Version = @Version
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement SelectDueSchedulesSqlServer =
        new(
            "jobs.select_due_host_schedules.sql_server",
            """
            SELECT TOP (@BatchSize)
                   s.Id, s.TenantId, s.JobDefinitionId, s.TriggerKind,
                   s.CronExpression, s.TimeZoneId, s.OneTimeAtUtc,
                   s.MisfirePolicy, s.IsEnabled, s.NextExecutionAtUtc,
                   s.LastExecutionAtUtc, s.CompletedAtUtc,
                   s.NumberOfRuns, s.NumberOfErrors, s.StartTime, s.EndTime, s.Args,
                   s.CreatedAtUtc, s.CreatedByUserId,
                   s.UpdatedAtUtc, s.UpdatedByUserId, s.Version,
                   d.AllowConcurrentExecutions
            FROM fn_jobs_schedule AS s WITH (UPDLOCK, READPAST, ROWLOCK)
            INNER JOIN fn_jobs_definition AS d
                ON d.Id = s.JobDefinitionId
               AND d.TenantId IS NULL
               AND d.IsEnabled = 1
            WHERE s.TenantId IS NULL
              AND s.IsEnabled = 1
              AND s.CompletedAtUtc IS NULL
              AND s.NextExecutionAtUtc <= @Now
            ORDER BY s.NextExecutionAtUtc, s.Id
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement SelectDueSchedulesMySql =
        new(
            "jobs.select_due_host_schedules.mysql",
            """
            SELECT s.Id, s.TenantId, s.JobDefinitionId, s.TriggerKind,
                   s.CronExpression, s.TimeZoneId, s.OneTimeAtUtc,
                   s.MisfirePolicy, s.IsEnabled, s.NextExecutionAtUtc,
                   s.LastExecutionAtUtc, s.CompletedAtUtc,
                   s.NumberOfRuns, s.NumberOfErrors, s.StartTime, s.EndTime, s.Args,
                   s.CreatedAtUtc, s.CreatedByUserId,
                   s.UpdatedAtUtc, s.UpdatedByUserId, s.Version,
                   d.AllowConcurrentExecutions
            FROM fn_jobs_schedule AS s
            INNER JOIN fn_jobs_definition AS d
                ON d.Id = s.JobDefinitionId
               AND d.TenantId IS NULL
               AND d.IsEnabled = 1
            WHERE s.TenantId IS NULL
              AND s.IsEnabled = 1
              AND s.CompletedAtUtc IS NULL
              AND s.NextExecutionAtUtc <= @Now
            ORDER BY s.NextExecutionAtUtc, s.Id
            LIMIT @BatchSize
            FOR UPDATE SKIP LOCKED
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertScheduledExecution =
        new(
            "jobs.insert_scheduled_host_execution",
            """
            INSERT INTO fn_jobs_execution
                (Id, TenantId, JobDefinitionId, JobScheduleId,
                 Status, TriggerKind, ScheduledForUtc,
                 ErrorMessage, StartedAtUtc, FinishedAtUtc,
                 LeaseId, LeaseExpiresAtUtc, NextAttemptAtUtc,
                 AttemptCount, CreatedAtUtc)
            VALUES
                (@Id, NULL, @JobDefinitionId, @JobScheduleId,
                 @Status, @TriggerKind, @ScheduledForUtc,
                 NULL, NULL, NULL, NULL, NULL, NULL, 0, @CreatedAtUtc)
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement AdvanceSchedule =
        new(
            "jobs.advance_host_schedule",
            """
            UPDATE fn_jobs_schedule
            SET IsEnabled = @IsEnabled,
                NextExecutionAtUtc = @NextExecutionAtUtc,
                LastExecutionAtUtc =
                    COALESCE(@LastExecutionAtUtc, LastExecutionAtUtc),
                NumberOfRuns = NumberOfRuns +
                    CASE WHEN @LastExecutionAtUtc IS NOT NULL THEN 1 ELSE 0 END,
                CompletedAtUtc = @CompletedAtUtc,
                UpdatedAtUtc = @UpdatedAtUtc,
                Version = @NextVersion
            WHERE Id = @Id
              AND TenantId IS NULL
              AND IsEnabled = 1
              AND CompletedAtUtc IS NULL
              AND Version = @Version
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ReadBacklogSqlServer =
        new(
            "jobs.read_backlog.sql_server",
            """
            SELECT COUNT_BIG(*) AS PendingCount,
                   MIN(
                       CASE WHEN NextAttemptAtUtc IS NULL
                                      OR NextAttemptAtUtc <= @ObservedAtUtc
                           THEN CreatedAtUtc
                       END
                   ) AS OldestClaimableCreatedAtUtc,
                   COUNT_BIG(
                       CASE WHEN NextAttemptAtUtc IS NOT NULL
                                      AND NextAttemptAtUtc <= @ObservedAtUtc
                           THEN 1
                       END
                   ) AS DueRetryCount,
                   MIN(
                       CASE WHEN NextAttemptAtUtc IS NOT NULL
                                      AND NextAttemptAtUtc <= @ObservedAtUtc
                           THEN NextAttemptAtUtc
                       END
                   ) AS OldestDueRetryAtUtc
            FROM fn_jobs_execution
            WHERE TenantId IS NULL
              AND Status = @PendingStatus
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ReadBacklogMySql =
        new(
            "jobs.read_backlog.my_sql",
            """
            SELECT COUNT(*) AS PendingCount,
                   MIN(
                       CASE WHEN NextAttemptAtUtc IS NULL
                                      OR NextAttemptAtUtc <= @ObservedAtUtc
                           THEN CreatedAtUtc
                       END
                   ) AS OldestClaimableCreatedAtUtc,
                   COALESCE(
                       SUM(
                           CASE WHEN NextAttemptAtUtc IS NOT NULL
                                          AND NextAttemptAtUtc <= @ObservedAtUtc
                               THEN 1
                               ELSE 0
                           END
                       ),
                       0
                   ) AS DueRetryCount,
                   MIN(
                       CASE WHEN NextAttemptAtUtc IS NOT NULL
                                      AND NextAttemptAtUtc <= @ObservedAtUtc
                           THEN NextAttemptAtUtc
                       END
                   ) AS OldestDueRetryAtUtc
            FROM fn_jobs_execution
            WHERE TenantId IS NULL
              AND Status = @PendingStatus
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement AcquireExecutionsSqlServer =
        new(
            "jobs.acquire_host_executions.sql_server",
            """
            ;WITH Candidate AS
            (
                SELECT e.*,
                       d.AllowConcurrentExecutions,
                       ROW_NUMBER() OVER (
                           PARTITION BY CASE
                               WHEN d.AllowConcurrentExecutions = 0 THEN e.JobDefinitionId
                               ELSE e.Id
                           END
                           ORDER BY e.CreatedAtUtc, e.Id
                       ) AS OverlapRank
                FROM fn_jobs_execution e WITH (UPDLOCK, READPAST, ROWLOCK)
                INNER JOIN fn_jobs_definition d
                    ON d.Id = e.JobDefinitionId
                   AND d.TenantId IS NULL
                WHERE e.TenantId IS NULL
                  AND (
                      (e.Status = @PendingStatus
                       AND (e.LeaseExpiresAtUtc IS NULL OR e.LeaseExpiresAtUtc <= @Now)
                       AND (e.NextAttemptAtUtc IS NULL OR e.NextAttemptAtUtc <= @Now)
                       AND (
                           d.AllowConcurrentExecutions = 1
                           OR NOT EXISTS (
                               SELECT 1
                               FROM fn_jobs_execution r WITH (READPAST)
                               WHERE r.TenantId IS NULL
                                 AND r.JobDefinitionId = e.JobDefinitionId
                                 AND r.Status = @RunningStatus
                                 AND r.LeaseExpiresAtUtc > @Now
                           )
                       ))
                      OR (e.Status = @RunningStatus AND e.LeaseExpiresAtUtc <= @Now)
                  )
            ),
            Pending AS
            (
                SELECT TOP (@BatchSize)
                       Id, TenantId, JobDefinitionId, JobScheduleId, Status,
                       TriggerKind, ScheduledForUtc, ErrorMessage, StartedAtUtc,
                       FinishedAtUtc, LeaseId, LeaseExpiresAtUtc, NextAttemptAtUtc,
                       AttemptCount, CreatedAtUtc
                FROM Candidate
                WHERE AllowConcurrentExecutions = 1 OR OverlapRank = 1
                ORDER BY CreatedAtUtc, Id
            )
            UPDATE Pending
            SET Status = @RunningStatus,
                LeaseId = @LeaseId,
                LeaseExpiresAtUtc = @LeaseExpiresAtUtc,
                NextAttemptAtUtc = NULL,
                StartedAtUtc = COALESCE(StartedAtUtc, @Now),
                AttemptCount = AttemptCount + 1
            OUTPUT inserted.Id, inserted.TenantId, inserted.JobDefinitionId,
                   inserted.JobScheduleId, inserted.Status, inserted.TriggerKind,
                   inserted.ScheduledForUtc, inserted.ErrorMessage,
                   inserted.StartedAtUtc, inserted.FinishedAtUtc, inserted.LeaseId,
                   inserted.LeaseExpiresAtUtc, inserted.NextAttemptAtUtc,
                   inserted.AttemptCount, inserted.CreatedAtUtc,
                   CAST(NULL AS varchar(64)) AS JobKey;
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement SelectClaimableExecutionIdsMySql =
        new(
            "jobs.select_claimable_host_execution_ids.mysql",
            """
            SELECT e.Id
            FROM fn_jobs_execution e
            INNER JOIN fn_jobs_definition d
                ON d.Id = e.JobDefinitionId
               AND d.TenantId IS NULL
            INNER JOIN (
                SELECT inner_e.Id,
                       inner_d.AllowConcurrentExecutions,
                       ROW_NUMBER() OVER (
                           PARTITION BY CASE
                               WHEN inner_d.AllowConcurrentExecutions = 0
                                   THEN inner_e.JobDefinitionId
                               ELSE inner_e.Id
                           END
                           ORDER BY inner_e.CreatedAtUtc, inner_e.Id
                       ) AS OverlapRank
                FROM fn_jobs_execution inner_e
                INNER JOIN fn_jobs_definition inner_d
                    ON inner_d.Id = inner_e.JobDefinitionId
                   AND inner_d.TenantId IS NULL
                WHERE inner_e.TenantId IS NULL
                  AND (
                      (inner_e.Status = @PendingStatus
                       AND (inner_e.LeaseExpiresAtUtc IS NULL
                            OR inner_e.LeaseExpiresAtUtc <= @Now)
                       AND (inner_e.NextAttemptAtUtc IS NULL
                            OR inner_e.NextAttemptAtUtc <= @Now)
                       AND (
                           inner_d.AllowConcurrentExecutions = 1
                           OR NOT EXISTS (
                               SELECT 1
                               FROM fn_jobs_execution r
                               WHERE r.TenantId IS NULL
                                 AND r.JobDefinitionId = inner_e.JobDefinitionId
                                 AND r.Status = @RunningStatus
                                 AND r.LeaseExpiresAtUtc > @Now
                           )
                       ))
                      OR (inner_e.Status = @RunningStatus
                          AND inner_e.LeaseExpiresAtUtc <= @Now)
                  )
            ) ranked ON ranked.Id = e.Id
            WHERE ranked.AllowConcurrentExecutions = 1 OR ranked.OverlapRank = 1
            ORDER BY e.CreatedAtUtc, e.Id
            LIMIT @BatchSize
            FOR UPDATE SKIP LOCKED
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ClaimExecutionsByIdsMySql =
        new(
            "jobs.claim_host_executions_by_ids.mysql",
            """
            UPDATE fn_jobs_execution
            SET Status = @RunningStatus,
                LeaseId = @LeaseId,
                LeaseExpiresAtUtc = @LeaseExpiresAtUtc,
                NextAttemptAtUtc = NULL,
                StartedAtUtc = COALESCE(StartedAtUtc, @Now),
                AttemptCount = AttemptCount + 1
            WHERE TenantId IS NULL
              AND Id IN @Ids
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement SelectExecutionsByLeaseMySql =
        new(
            "jobs.select_host_executions_by_lease.mysql",
            """
            SELECT e.Id, e.TenantId, e.JobDefinitionId, e.JobScheduleId,
                   e.Status, e.TriggerKind, e.ScheduledForUtc,
                   e.ErrorMessage, e.StartedAtUtc, e.FinishedAtUtc,
                   e.LeaseId, e.LeaseExpiresAtUtc, e.NextAttemptAtUtc,
                   e.AttemptCount, e.CreatedAtUtc,
                   d.JobKey
            FROM fn_jobs_execution e
            INNER JOIN fn_jobs_definition d ON d.Id = e.JobDefinitionId
            WHERE e.LeaseId = @LeaseId
            ORDER BY e.CreatedAtUtc, e.Id
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement RenewExecutionLease =
        new(
            "jobs.renew_host_execution_lease",
            """
            UPDATE fn_jobs_execution
            SET LeaseExpiresAtUtc = @LeaseExpiresAtUtc
            WHERE TenantId IS NULL
              AND LeaseId = @LeaseId
              AND Status = @RunningStatus
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement MarkExecutionSucceeded =
        new(
            "jobs.mark_host_execution_succeeded",
            """
            UPDATE fn_jobs_execution
            SET Status = @SucceededStatus,
                FinishedAtUtc = @FinishedAtUtc,
                LeaseId = NULL,
                LeaseExpiresAtUtc = NULL,
                NextAttemptAtUtc = NULL,
                ErrorMessage = NULL
            WHERE Id = @Id
              AND LeaseId = @LeaseId
              AND Status = @RunningStatus
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement MarkExecutionFailed =
        new(
            "jobs.mark_host_execution_failed",
            """
            UPDATE fn_jobs_execution
            SET Status = @FailedStatus,
                FinishedAtUtc = @FinishedAtUtc,
                LeaseId = NULL,
                LeaseExpiresAtUtc = NULL,
                NextAttemptAtUtc = NULL,
                ErrorMessage = @ErrorMessage
            WHERE Id = @Id
              AND LeaseId = @LeaseId
              AND Status = @RunningStatus
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement RescheduleExecution =
        new(
            "jobs.reschedule_host_execution",
            """
            UPDATE fn_jobs_execution
            SET Status = @PendingStatus,
                FinishedAtUtc = NULL,
                LeaseId = NULL,
                LeaseExpiresAtUtc = NULL,
                NextAttemptAtUtc = @NextAttemptAtUtc,
                ErrorMessage = @ErrorMessage
            WHERE Id = @Id
              AND LeaseId = @LeaseId
              AND Status = @RunningStatus
            """,
            SqlDataScope.HostOnly);

    /// <summary>查询已启用作业定义的去重分组名，对应 Admin.NET ListJobGroup。</summary>
    public static readonly SqlStatement ListJobGroups =
        new(
            "jobs.list_host_job_groups",
            """
            SELECT DISTINCT GroupName
            FROM fn_jobs_definition
            WHERE TenantId IS NULL
              AND GroupName IS NOT NULL
              AND GroupName <> ''
            ORDER BY GroupName
            """,
            SqlDataScope.HostOnly);

    /// <summary>统计作业定义下仍处于启用状态的计划数，用于删除前置校验。</summary>
    public static readonly SqlStatement CountActiveSchedulesByDefinition =
        new(
            "jobs.count_active_schedules_by_definition",
            """
            SELECT COUNT(*)
            FROM fn_jobs_schedule
            WHERE TenantId IS NULL
              AND JobDefinitionId = @JobDefinitionId
              AND IsEnabled = 1
            """,
            SqlDataScope.HostOnly);

    /// <summary>统计作业定义下未终结的执行记录数（pending/running），用于删除前置校验。</summary>
    public static readonly SqlStatement CountActiveExecutionsByDefinition =
        new(
            "jobs.count_active_executions_by_definition",
            """
            SELECT COUNT(*)
            FROM fn_jobs_execution
            WHERE TenantId IS NULL
              AND JobDefinitionId = @JobDefinitionId
              AND Status IN ('pending', 'running')
            """,
            SqlDataScope.HostOnly);

    /// <summary>
    /// 判断作业定义是否已有有效 running 租约，供调度物化 gate 使用。
    /// </summary>
    public static readonly SqlStatement HasActiveRunningForDefinition =
        new(
            "jobs.has_active_running_for_definition",
            """
            SELECT COUNT(*)
            FROM fn_jobs_execution
            WHERE TenantId IS NULL
              AND JobDefinitionId = @JobDefinitionId
              AND Status = @RunningStatus
              AND LeaseExpiresAtUtc > @Now
            """,
            SqlDataScope.HostOnly);

    /// <summary>删除作业定义关联的全部计划，解除外键约束后才能删除定义本身。</summary>
    public static readonly SqlStatement DeleteSchedulesByDefinition =
        new(
            "jobs.delete_schedules_by_definition",
            """
            DELETE FROM fn_jobs_schedule
            WHERE TenantId IS NULL
              AND JobDefinitionId = @JobDefinitionId
            """,
            SqlDataScope.HostOnly);

    /// <summary>硬删除作业定义，调用前必须已清理关联计划并确认无活跃执行。</summary>
    public static readonly SqlStatement DeleteDefinition =
        new(
            "jobs.delete_host_definition",
            """
            DELETE FROM fn_jobs_definition
            WHERE Id = @Id
              AND TenantId IS NULL
              AND IsEnabled = 0
              AND Version = @Version
            """,
            SqlDataScope.HostOnly);

    /// <summary>统计任务计划下未终结的执行记录数，用于删除前置校验。</summary>
    public static readonly SqlStatement CountActiveExecutionsBySchedule =
        new(
            "jobs.count_active_executions_by_schedule",
            """
            SELECT COUNT(*)
            FROM fn_jobs_execution
            WHERE TenantId IS NULL
              AND JobScheduleId = @JobScheduleId
              AND Status IN ('pending', 'running')
            """,
            SqlDataScope.HostOnly);

    /// <summary>硬删除任务计划，调用前必须确认无活跃执行。</summary>
    public static readonly SqlStatement DeleteSchedule =
        new(
            "jobs.delete_host_schedule",
            """
            DELETE FROM fn_jobs_schedule
            WHERE Id = @Id
              AND TenantId IS NULL
              AND Version = @Version
            """,
            SqlDataScope.HostOnly);

    /// <summary>清空作业定义下的终态执行记录（成功/失败），保留 pending/running。</summary>
    public static readonly SqlStatement ClearExecutionsByDefinition =
        new(
            "jobs.clear_executions_by_definition",
            """
            DELETE FROM fn_jobs_execution
            WHERE TenantId IS NULL
              AND JobDefinitionId = @JobDefinitionId
              AND Status IN ('succeeded', 'failed')
            """,
            SqlDataScope.HostOnly);

    /// <summary>递增任务计划出错次数，执行记录终态为 failed 时由执行器调用。</summary>
    public static readonly SqlStatement IncrementScheduleErrorCount =
        new(
            "jobs.increment_schedule_error_count",
            """
            UPDATE fn_jobs_schedule
            SET NumberOfErrors = NumberOfErrors + 1
            WHERE Id = @Id
              AND TenantId IS NULL
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement UpsertWorkerHeartbeat =
        new(
            "jobs.upsert_worker_heartbeat",
            """
            MERGE fn_jobs_worker_instance AS target
            USING (SELECT @InstanceId AS InstanceId) AS source
                ON target.InstanceId = source.InstanceId
            WHEN MATCHED THEN
                UPDATE SET LastHeartbeatAtUtc = @LastHeartbeatAtUtc,
                           WorkerVersion = @WorkerVersion
            WHEN NOT MATCHED THEN
                INSERT (InstanceId, TenantId, HostProfile, StartedAtUtc,
                        LastHeartbeatAtUtc, WorkerVersion)
                VALUES (@InstanceId, NULL, @HostProfile, @StartedAtUtc,
                        @LastHeartbeatAtUtc, @WorkerVersion);
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement UpsertWorkerHeartbeatMySql =
        new(
            "jobs.upsert_worker_heartbeat.mysql",
            """
            INSERT INTO fn_jobs_worker_instance
                (InstanceId, TenantId, HostProfile, StartedAtUtc,
                 LastHeartbeatAtUtc, WorkerVersion)
            VALUES
                (@InstanceId, NULL, @HostProfile, @StartedAtUtc,
                 @LastHeartbeatAtUtc, @WorkerVersion)
            ON DUPLICATE KEY UPDATE
                LastHeartbeatAtUtc = VALUES(LastHeartbeatAtUtc),
                WorkerVersion = VALUES(WorkerVersion)
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListWorkerInstances =
        new(
            "jobs.list_worker_instances",
            """
            SELECT InstanceId, HostProfile, StartedAtUtc,
                   LastHeartbeatAtUtc, WorkerVersion
            FROM fn_jobs_worker_instance
            WHERE TenantId IS NULL
            ORDER BY LastHeartbeatAtUtc DESC, InstanceId
            """,
            SqlDataScope.HostOnly);
}
