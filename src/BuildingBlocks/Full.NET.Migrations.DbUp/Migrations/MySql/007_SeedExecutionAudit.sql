CREATE TABLE IF NOT EXISTS fn_seed_run
(
    Id char(36) NOT NULL,
    Profile varchar(16) NOT NULL,
    EnvironmentName varchar(64) NOT NULL,
    Status varchar(16) NOT NULL,
    ApplicationVersion varchar(64) NOT NULL,
    CorrelationId varchar(64) NOT NULL,
    StartedAt datetime(6) NOT NULL,
    CompletedAt datetime(6) NULL,
    ErrorCode varchar(128) NULL,
    CONSTRAINT PK_fn_seed_run PRIMARY KEY (Id),
    CONSTRAINT CK_fn_seed_run_Status
        CHECK (Status IN ('Running', 'Succeeded', 'Failed', 'Cancelled')),
    KEY IX_fn_seed_run_StartedAt (StartedAt)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_seed_run_item
(
    RunId char(36) NOT NULL,
    Contributor varchar(128) NOT NULL,
    ContributorVersion int NOT NULL,
    Status varchar(16) NOT NULL,
    CreatedCount int NOT NULL,
    UpdatedCount int NOT NULL,
    SkippedCount int NOT NULL,
    StartedAt datetime(6) NOT NULL,
    CompletedAt datetime(6) NULL,
    ErrorCode varchar(128) NULL,
    CONSTRAINT PK_fn_seed_run_item PRIMARY KEY (RunId, Contributor),
    CONSTRAINT FK_fn_seed_run_item_Run
        FOREIGN KEY (RunId) REFERENCES fn_seed_run(Id),
    CONSTRAINT CK_fn_seed_run_item_Status
        CHECK (Status IN ('Running', 'Succeeded', 'Failed', 'Cancelled')),
    CONSTRAINT CK_fn_seed_run_item_Counts
        CHECK (CreatedCount >= 0 AND UpdatedCount >= 0 AND SkippedCount >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
