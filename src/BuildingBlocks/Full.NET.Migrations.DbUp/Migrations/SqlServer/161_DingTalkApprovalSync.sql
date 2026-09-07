-- 161：钉钉审批镜像同步记录表。

IF OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_dingtalk_approval_sync
    (
        Id uniqueidentifier NOT NULL,
        TenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        WorkflowInstanceId uniqueidentifier NOT NULL,
        IdempotencyKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DingTalkProcessInstanceId varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
        ProcessCode varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        OriginatorUserId varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DeptId bigint NOT NULL,
        Title nvarchar(256) NOT NULL,
        Summary nvarchar(2000) NULL,
        StatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ExternalStatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NULL,
        ExternalResultKey varchar(32) COLLATE Latin1_General_100_BIN2 NULL,
        LastErrorCode varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        LastSyncedAtUtc datetimeoffset(7) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        CreatedByUserId uniqueidentifier NOT NULL,
        CONSTRAINT PK_fn_notifications_dingtalk_approval_sync PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_notifications_dingtalk_approval_sync_StatusKey
            CHECK (StatusKey IN (
                N'pending_outbound', N'outbound_failed', N'running', N'completed', N'terminated'))
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知钉钉审批同步表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_dingtalk_approval_sync';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_dingtalk_approval_sync', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync'), N'CreatedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_dingtalk_approval_sync', @level2type=N'COLUMN', @level2name=N'CreatedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync'), N'DeptId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'部门标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_dingtalk_approval_sync', @level2type=N'COLUMN', @level2name=N'DeptId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync'), N'DingTalkProcessInstanceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'钉钉流程实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_dingtalk_approval_sync', @level2type=N'COLUMN', @level2name=N'DingTalkProcessInstanceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync'), N'ExternalResultKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'外部结果键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_dingtalk_approval_sync', @level2type=N'COLUMN', @level2name=N'ExternalResultKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync'), N'ExternalStatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'外部回执状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_dingtalk_approval_sync', @level2type=N'COLUMN', @level2name=N'ExternalStatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_dingtalk_approval_sync', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync'), N'IdempotencyKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'幂等键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_dingtalk_approval_sync', @level2type=N'COLUMN', @level2name=N'IdempotencyKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync'), N'LastErrorCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最后错误码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_dingtalk_approval_sync', @level2type=N'COLUMN', @level2name=N'LastErrorCode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync'), N'LastSyncedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Last Synced At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_dingtalk_approval_sync', @level2type=N'COLUMN', @level2name=N'LastSyncedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync'), N'OriginatorUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发起人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_dingtalk_approval_sync', @level2type=N'COLUMN', @level2name=N'OriginatorUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync'), N'ProcessCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程编码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_dingtalk_approval_sync', @level2type=N'COLUMN', @level2name=N'ProcessCode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync'), N'StatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_dingtalk_approval_sync', @level2type=N'COLUMN', @level2name=N'StatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync'), N'Summary', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_dingtalk_approval_sync', @level2type=N'COLUMN', @level2name=N'Summary';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync'), N'TenantScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户作用域唯一键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_dingtalk_approval_sync', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync'), N'Title', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'标题', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_dingtalk_approval_sync', @level2type=N'COLUMN', @level2name=N'Title';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_dingtalk_approval_sync', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync'), N'WorkflowInstanceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_dingtalk_approval_sync', @level2type=N'COLUMN', @level2name=N'WorkflowInstanceId';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'钉钉审批镜像同步记录表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_dingtalk_approval_sync';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync')
      AND indexObject.name = N'UX_fn_notifications_dingtalk_approval_sync_Scope_Workflow'
)
    CREATE UNIQUE INDEX UX_fn_notifications_dingtalk_approval_sync_Scope_Workflow
        ON dbo.fn_notifications_dingtalk_approval_sync(TenantScopeKey, WorkflowInstanceId);

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync')
      AND indexObject.name = N'IX_fn_notifications_dingtalk_approval_sync_StatusKey'
)
    CREATE INDEX IX_fn_notifications_dingtalk_approval_sync_StatusKey
        ON dbo.fn_notifications_dingtalk_approval_sync(StatusKey, UpdatedAtUtc, CreatedAtUtc);

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_notifications_dingtalk_approval_sync')
      AND indexObject.name = N'IX_fn_notifications_dingtalk_approval_sync_ProcessInstance'
)
    CREATE INDEX IX_fn_notifications_dingtalk_approval_sync_ProcessInstance
        ON dbo.fn_notifications_dingtalk_approval_sync(DingTalkProcessInstanceId)
        WHERE DingTalkProcessInstanceId IS NOT NULL;
