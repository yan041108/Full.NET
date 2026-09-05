-- 118：DataApproval 首个纵向切片请求表。
IF OBJECT_ID(N'dbo.fn_data_approval_request', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_data_approval_request
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ScopeKey varchar(16) NOT NULL,
        TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ScenarioKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        TargetEntityId uniqueidentifier NOT NULL,
        StatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        BeforeSnapshotJson nvarchar(max) NULL,
        AfterSnapshotJson nvarchar(max) NOT NULL,
        WorkflowInstanceId uniqueidentifier NULL,
        WorkflowRevision bigint NULL,
        WorkflowDefinitionVersionId uniqueidentifier NOT NULL,
        SubmittedByUserId uniqueidentifier NOT NULL,
        SubmittedAtUtc datetimeoffset(7) NOT NULL,
        ResolvedAtUtc datetimeoffset(7) NULL,
        IdempotencyKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        Version bigint NOT NULL CONSTRAINT DF_fn_data_approval_request_Version DEFAULT (1),
        CONSTRAINT PK_fn_data_approval_request PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_data_approval_request_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
        CONSTRAINT CK_fn_data_approval_request_StatusKey
            CHECK (StatusKey IN ('pending', 'in_review', 'approved', 'rejected', 'cancelled')),
        CONSTRAINT CK_fn_data_approval_request_Version CHECK (Version > 0)
    );

    CREATE UNIQUE CLUSTERED INDEX UX_fn_data_approval_request_Idempotency
        ON dbo.fn_data_approval_request (TenantScopeKey, IdempotencyKey);

    CREATE INDEX IX_fn_data_approval_request_SubmittedAtUtc
        ON dbo.fn_data_approval_request (TenantScopeKey, SubmittedAtUtc DESC, Id DESC);

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_data_approval_request')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'数据审批请求表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_data_approval_request';
END;
