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
