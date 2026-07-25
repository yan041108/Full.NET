-- 030：Host 作用域任务定义与执行记录。

IF OBJECT_ID(N'dbo.fn_jobs_definition', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_jobs_definition
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        JobKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DisplayName nvarchar(200) NOT NULL,
        Description nvarchar(500) NULL,
        IsEnabled bit NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        CreatedByUserId uniqueidentifier NOT NULL,
        UpdatedByUserId uniqueidentifier NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_jobs_definition_Version DEFAULT (1),
        CONSTRAINT PK_fn_jobs_definition PRIMARY KEY CLUSTERED (Id)
    );

    CREATE UNIQUE INDEX UX_fn_jobs_definition_JobKey
        ON dbo.fn_jobs_definition(JobKey)
        WHERE TenantId IS NULL;
END;

IF OBJECT_ID(N'dbo.fn_jobs_execution', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_jobs_execution
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        JobDefinitionId uniqueidentifier NOT NULL,
        Status varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        TriggerKind varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ErrorMessage nvarchar(2000) NULL,
        StartedAtUtc datetimeoffset(7) NULL,
        FinishedAtUtc datetimeoffset(7) NULL,
        LeaseId uniqueidentifier NULL,
        LeaseExpiresAtUtc datetimeoffset(7) NULL,
        AttemptCount int NOT NULL
            CONSTRAINT DF_fn_jobs_execution_AttemptCount DEFAULT (0),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_jobs_execution PRIMARY KEY CLUSTERED (Id)
    );

    CREATE INDEX IX_fn_jobs_execution_JobDefinitionCreatedAtUtc
        ON dbo.fn_jobs_execution(JobDefinitionId, CreatedAtUtc DESC, Id);

    CREATE INDEX IX_fn_jobs_execution_PendingLease
        ON dbo.fn_jobs_execution(Status, LeaseExpiresAtUtc, CreatedAtUtc)
        WHERE Status = 'pending';
END;
