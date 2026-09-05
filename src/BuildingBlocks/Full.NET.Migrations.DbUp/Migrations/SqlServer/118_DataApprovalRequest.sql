-- 118：DataApproval 首个纵向切片请求表。
IF OBJECT_ID(N'dbo.fn_dataapproval_request', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_dataapproval_request
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
        Version bigint NOT NULL CONSTRAINT DF_fn_dataapproval_request_Version DEFAULT (1),
        CONSTRAINT PK_fn_dataapproval_request PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_dataapproval_request_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
        CONSTRAINT CK_fn_dataapproval_request_StatusKey
            CHECK (StatusKey IN ('pending', 'in_review', 'approved', 'rejected', 'cancelled')),
        CONSTRAINT CK_fn_dataapproval_request_Version CHECK (Version > 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'dataapproval request表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_request'), N'AfterSnapshotJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'After Snapshot(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request', @level2type=N'COLUMN', @level2name=N'AfterSnapshotJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_request'), N'BeforeSnapshotJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Before Snapshot(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request', @level2type=N'COLUMN', @level2name=N'BeforeSnapshotJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_request'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_request'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_request'), N'IdempotencyKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'幂等键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request', @level2type=N'COLUMN', @level2name=N'IdempotencyKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_request'), N'ResolvedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Resolved At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request', @level2type=N'COLUMN', @level2name=N'ResolvedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_request'), N'ScenarioKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Scenario Key', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request', @level2type=N'COLUMN', @level2name=N'ScenarioKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_request'), N'ScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_request'), N'StatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request', @level2type=N'COLUMN', @level2name=N'StatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_request'), N'SubmittedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Submitted At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request', @level2type=N'COLUMN', @level2name=N'SubmittedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_request'), N'SubmittedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Submitted By User标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request', @level2type=N'COLUMN', @level2name=N'SubmittedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_request'), N'TargetEntityId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Target Entity标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request', @level2type=N'COLUMN', @level2name=N'TargetEntityId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_request'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_request'), N'TenantScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户作用域唯一键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_request'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_request'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request', @level2type=N'COLUMN', @level2name=N'Version';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_request'), N'WorkflowDefinitionVersionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Workflow Definition Version标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request', @level2type=N'COLUMN', @level2name=N'WorkflowDefinitionVersionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_request'), N'WorkflowInstanceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Workflow Instance标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request', @level2type=N'COLUMN', @level2name=N'WorkflowInstanceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_request'), N'WorkflowRevision', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Workflow Revision', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request', @level2type=N'COLUMN', @level2name=N'WorkflowRevision';

    CREATE UNIQUE CLUSTERED INDEX UX_fn_dataapproval_request_Idempotency
        ON dbo.fn_dataapproval_request (TenantScopeKey, IdempotencyKey);

    CREATE INDEX IX_fn_dataapproval_request_SubmittedAtUtc
        ON dbo.fn_dataapproval_request (TenantScopeKey, SubmittedAtUtc DESC, Id DESC);

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'数据审批请求表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request';
END;
