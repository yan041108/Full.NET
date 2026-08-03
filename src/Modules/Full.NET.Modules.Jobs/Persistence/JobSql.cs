using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Jobs.Persistence;

internal static class JobSql
{
    public static readonly SqlStatement ListDefinitionsSqlServer =
        new(
            "jobs.list_host_definitions.sql_server",
            """
            SELECT Id, TenantId, JobKey, DisplayName, Description, IsEnabled,
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
            SELECT Id, TenantId, JobKey, DisplayName, Description, IsEnabled,
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
            SELECT Id, TenantId, JobKey, DisplayName, Description, IsEnabled,
                   CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UpdatedByUserId, Version
            FROM fn_jobs_definition
            WHERE Id = @Id AND TenantId IS NULL
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement FindDefinitionsByIds =
        new(
            "jobs.find_host_definitions_by_ids",
            """
            SELECT Id, TenantId, JobKey, DisplayName, Description, IsEnabled,
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
            SELECT Id, TenantId, JobKey, DisplayName, Description, IsEnabled,
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
                (Id, TenantId, JobKey, DisplayName, Description, IsEnabled,
                 CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UpdatedByUserId, Version)
            VALUES
                (@Id, NULL, @JobKey, @DisplayName, @Description, @IsEnabled,
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
            SELECT Id, TenantId, JobDefinitionId, TriggerKind, CronExpression,
                   TimeZoneId, OneTimeAtUtc, MisfirePolicy, IsEnabled,
                   NextExecutionAtUtc, LastExecutionAtUtc, CompletedAtUtc,
                   CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId,
                   Version
            FROM fn_jobs_schedule
            WHERE Id = @Id AND TenantId IS NULL
            """,
            SqlDataScope.HostOnly);

    private const string ScheduleDetailProjection = """
        s.Id, s.TenantId, s.JobDefinitionId, s.TriggerKind, s.CronExpression,
        s.TimeZoneId, s.OneTimeAtUtc, s.MisfirePolicy, s.IsEnabled,
        s.NextExecutionAtUtc, s.LastExecutionAtUtc, s.CompletedAtUtc,
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
            SELECT Id, JobKey, DisplayName
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
                 CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId,
                 Version)
            VALUES
                (@Id, NULL, @JobDefinitionId, @TriggerKind, @CronExpression,
                 @TimeZoneId, @OneTimeAtUtc, @MisfirePolicy, @IsEnabled,
                 @NextExecutionAtUtc, NULL, NULL,
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
                   s.CreatedAtUtc, s.CreatedByUserId,
                   s.UpdatedAtUtc, s.UpdatedByUserId, s.Version
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
                   s.CreatedAtUtc, s.CreatedByUserId,
                   s.UpdatedAtUtc, s.UpdatedByUserId, s.Version
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
            ;WITH Pending AS
            (
                SELECT TOP (@BatchSize) e.*
                FROM fn_jobs_execution e WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE e.TenantId IS NULL
                  AND (
                      (e.Status = @PendingStatus
                       AND (e.LeaseExpiresAtUtc IS NULL OR e.LeaseExpiresAtUtc <= @Now)
                       AND (e.NextAttemptAtUtc IS NULL OR e.NextAttemptAtUtc <= @Now))
                      OR (e.Status = @RunningStatus AND e.LeaseExpiresAtUtc <= @Now)
                  )
                ORDER BY e.CreatedAtUtc, e.Id
            )
            UPDATE Pending
            SET Status = @RunningStatus,
                LeaseId = @LeaseId,
                LeaseExpiresAtUtc = @LeaseExpiresAtUtc,
                NextAttemptAtUtc = NULL,
                StartedAtUtc = COALESCE(StartedAtUtc, @Now),
                AttemptCount = AttemptCount + 1
            OUTPUT inserted.Id, inserted.TenantId, inserted.JobDefinitionId,
                   inserted.Status, inserted.TriggerKind, inserted.ErrorMessage,
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
            SELECT Id
            FROM fn_jobs_execution
            WHERE TenantId IS NULL
              AND (
                  (Status = @PendingStatus
                   AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc <= @Now)
                   AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= @Now))
                  OR (Status = @RunningStatus AND LeaseExpiresAtUtc <= @Now)
              )
            ORDER BY CreatedAtUtc, Id
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
}
