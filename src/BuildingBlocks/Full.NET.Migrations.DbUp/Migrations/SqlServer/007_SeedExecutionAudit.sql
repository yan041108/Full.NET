IF OBJECT_ID(N'dbo.fn_seed_run', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_seed_run
    (
        Id uniqueidentifier NOT NULL,
        Profile varchar(16) NOT NULL,
        EnvironmentName nvarchar(64) NOT NULL,
        Status varchar(16) NOT NULL,
        ApplicationVersion varchar(64) NOT NULL,
        CorrelationId varchar(64) NOT NULL,
        StartedAt datetimeoffset(7) NOT NULL,
        CompletedAt datetimeoffset(7) NULL,
        ErrorCode varchar(128) NULL,
        CONSTRAINT PK_fn_seed_run PRIMARY KEY (Id),
        CONSTRAINT CK_fn_seed_run_Status
            CHECK (Status IN ('Running', 'Succeeded', 'Failed', 'Cancelled'))
    );
    CREATE INDEX IX_fn_seed_run_StartedAt ON dbo.fn_seed_run(StartedAt);
END;

IF OBJECT_ID(N'dbo.fn_seed_run_item', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_seed_run_item
    (
        RunId uniqueidentifier NOT NULL,
        Contributor varchar(128) NOT NULL,
        ContributorVersion int NOT NULL,
        Status varchar(16) NOT NULL,
        CreatedCount int NOT NULL,
        UpdatedCount int NOT NULL,
        SkippedCount int NOT NULL,
        StartedAt datetimeoffset(7) NOT NULL,
        CompletedAt datetimeoffset(7) NULL,
        ErrorCode varchar(128) NULL,
        CONSTRAINT PK_fn_seed_run_item PRIMARY KEY (RunId, Contributor),
        CONSTRAINT FK_fn_seed_run_item_Run
            FOREIGN KEY (RunId) REFERENCES dbo.fn_seed_run(Id),
        CONSTRAINT CK_fn_seed_run_item_Status
            CHECK (Status IN ('Running', 'Succeeded', 'Failed', 'Cancelled')),
        CONSTRAINT CK_fn_seed_run_item_Counts
            CHECK (CreatedCount >= 0 AND UpdatedCount >= 0 AND SkippedCount >= 0)
    );
END;
