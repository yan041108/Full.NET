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
                 LeaseId, LeaseExpiresAtUtc, AttemptCount, CreatedAtUtc)
            VALUES
                (@Id, NULL, @JobDefinitionId, @Status, @TriggerKind,
                 NULL, NULL, NULL, NULL, NULL, 0, @CreatedAtUtc)
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListExecutionsSqlServer =
        new(
            "jobs.list_host_executions.sql_server",
            """
            SELECT e.Id, e.TenantId, e.JobDefinitionId, e.Status, e.TriggerKind,
                   e.ErrorMessage, e.StartedAtUtc, e.FinishedAtUtc,
                   e.LeaseId, e.LeaseExpiresAtUtc, e.AttemptCount, e.CreatedAtUtc,
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
            SELECT e.Id, e.TenantId, e.JobDefinitionId, e.Status, e.TriggerKind,
                   e.ErrorMessage, e.StartedAtUtc, e.FinishedAtUtc,
                   e.LeaseId, e.LeaseExpiresAtUtc, e.AttemptCount, e.CreatedAtUtc,
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
            SELECT e.Id, e.TenantId, e.JobDefinitionId, e.Status, e.TriggerKind,
                   e.ErrorMessage, e.StartedAtUtc, e.FinishedAtUtc,
                   e.LeaseId, e.LeaseExpiresAtUtc, e.AttemptCount, e.CreatedAtUtc,
                   d.JobKey
            FROM fn_jobs_execution e
            INNER JOIN fn_jobs_definition d ON d.Id = e.JobDefinitionId
            WHERE e.Id = @Id AND e.TenantId IS NULL
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
                  AND e.Status = @PendingStatus
                  AND (e.LeaseExpiresAtUtc IS NULL OR e.LeaseExpiresAtUtc <= @Now)
                ORDER BY e.CreatedAtUtc, e.Id
            )
            UPDATE Pending
            SET Status = @RunningStatus,
                LeaseId = @LeaseId,
                LeaseExpiresAtUtc = @LeaseExpiresAtUtc,
                StartedAtUtc = COALESCE(StartedAtUtc, @Now),
                AttemptCount = AttemptCount + 1
            OUTPUT inserted.Id, inserted.TenantId, inserted.JobDefinitionId,
                   inserted.Status, inserted.TriggerKind, inserted.ErrorMessage,
                   inserted.StartedAtUtc, inserted.FinishedAtUtc, inserted.LeaseId,
                   inserted.LeaseExpiresAtUtc, inserted.AttemptCount, inserted.CreatedAtUtc,
                   CAST(NULL AS varchar(64)) AS JobKey;
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement AcquireExecutionsMySql =
        new(
            "jobs.acquire_host_executions.mysql",
            """
            UPDATE fn_jobs_execution
            SET Status = @RunningStatus,
                LeaseId = @LeaseId,
                LeaseExpiresAtUtc = @LeaseExpiresAtUtc,
                StartedAtUtc = COALESCE(StartedAtUtc, @Now),
                AttemptCount = AttemptCount + 1
            WHERE TenantId IS NULL
              AND Status = @PendingStatus
              AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc <= @Now)
            ORDER BY CreatedAtUtc, Id
            LIMIT @BatchSize
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement SelectExecutionsByLeaseMySql =
        new(
            "jobs.select_host_executions_by_lease.mysql",
            """
            SELECT e.Id, e.TenantId, e.JobDefinitionId, e.Status, e.TriggerKind,
                   e.ErrorMessage, e.StartedAtUtc, e.FinishedAtUtc,
                   e.LeaseId, e.LeaseExpiresAtUtc, e.AttemptCount, e.CreatedAtUtc,
                   d.JobKey
            FROM fn_jobs_execution e
            INNER JOIN fn_jobs_definition d ON d.Id = e.JobDefinitionId
            WHERE e.LeaseId = @LeaseId
            ORDER BY e.CreatedAtUtc, e.Id
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
                ErrorMessage = @ErrorMessage
            WHERE Id = @Id
              AND LeaseId = @LeaseId
              AND Status = @RunningStatus
            """,
            SqlDataScope.HostOnly);
}
