-- 030：Host 作用域任务定义与执行记录。

CREATE TABLE IF NOT EXISTS fn_jobs_definition
(
    Id BINARY(16) NOT NULL,
    TenantId BINARY(16) NULL,
    JobKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    DisplayName varchar(200) NOT NULL,
    Description varchar(500) NULL,
    IsEnabled tinyint(1) NOT NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    CreatedByUserId BINARY(16) NOT NULL,
    UpdatedByUserId BINARY(16) NULL,
    Version int NOT NULL DEFAULT 1,
    CONSTRAINT PK_fn_jobs_definition PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_jobs_definition_JobKey (JobKey)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_jobs_execution
(
    Id BINARY(16) NOT NULL,
    TenantId BINARY(16) NULL,
    JobDefinitionId BINARY(16) NOT NULL,
    Status varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    TriggerKind varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    ErrorMessage varchar(2000) NULL,
    StartedAtUtc datetime(6) NULL,
    FinishedAtUtc datetime(6) NULL,
    LeaseId BINARY(16) NULL,
    LeaseExpiresAtUtc datetime(6) NULL,
    AttemptCount int NOT NULL DEFAULT 0,
    CreatedAtUtc datetime(6) NOT NULL,
    CONSTRAINT PK_fn_jobs_execution PRIMARY KEY (Id),
    KEY IX_fn_jobs_execution_JobDefinitionCreatedAtUtc (JobDefinitionId, CreatedAtUtc, Id),
    KEY IX_fn_jobs_execution_PendingLease (Status, LeaseExpiresAtUtc, CreatedAtUtc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
