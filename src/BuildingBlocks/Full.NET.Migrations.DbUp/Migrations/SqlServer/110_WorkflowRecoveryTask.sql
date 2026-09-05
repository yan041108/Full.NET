-- 110：新增工作流恢复任务表，供 Worker 扫描过期租约、卡住实例和未完成步骤后领取、续租、重试与死信。
-- SQL Server 使用持久化计算列加过滤唯一索引表达“同一实例/种类/步骤最多一条未关闭任务”。
-- 不可逆风险：删除表会丢失未完成的恢复任务；已死信任务在人工重试前不会再自动入队。
IF OBJECT_ID(N'dbo.fn_workflow_recovery_task', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_recovery_task
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ScopeKey varchar(16) NOT NULL,
        TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        InstanceId uniqueidentifier NOT NULL,
        StepId uniqueidentifier NULL,
        KindKey varchar(32) NOT NULL,
        StatusKey varchar(24) NOT NULL,
        AttemptCount int NOT NULL,
        Revision bigint NOT NULL,
        LeaseOwnerKey nvarchar(128) NULL,
        LeaseExpiresAtUtc datetime2(6) NULL,
        LeaseGeneration int NOT NULL,
        NextAttemptAtUtc datetime2(6) NULL,
        LastError nvarchar(512) NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        UpdatedAtUtc datetime2(6) NOT NULL,
        OpenOccupancyKey AS (
            CASE WHEN StatusKey IN ('pending', 'failed', 'dead_lettered')
                THEN CONCAT(
                    TenantScopeKey,
                    N'|',
                    CONVERT(nvarchar(36), InstanceId) COLLATE Latin1_General_100_BIN2,
                    N'|',
                    CONVERT(nvarchar(32), KindKey) COLLATE Latin1_General_100_BIN2,
                    N'|',
                    CONVERT(nvarchar(36), ISNULL(StepId, '00000000-0000-0000-0000-000000000000'))
                        COLLATE Latin1_General_100_BIN2)
            END) PERSISTED,
        CONSTRAINT PK_fn_workflow_recovery_task PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_workflow_recovery_task_Instance FOREIGN KEY (InstanceId)
            REFERENCES dbo.fn_workflow_instance(Id),
        CONSTRAINT FK_fn_workflow_recovery_task_Step FOREIGN KEY (StepId)
            REFERENCES dbo.fn_workflow_step(Id),
        CONSTRAINT CK_fn_workflow_recovery_task_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
        CONSTRAINT CK_fn_workflow_recovery_task_Kind CHECK (KindKey IN ('expired_lease', 'stuck_instance', 'incomplete_step')),
        CONSTRAINT CK_fn_workflow_recovery_task_Status CHECK (StatusKey IN ('pending', 'succeeded', 'failed', 'dead_lettered', 'cancelled')),
        CONSTRAINT CK_fn_workflow_recovery_task_Attempt CHECK (AttemptCount >= 0),
        CONSTRAINT CK_fn_workflow_recovery_task_Revision CHECK (Revision > 0),
        CONSTRAINT CK_fn_workflow_recovery_task_Generation CHECK (LeaseGeneration >= 0)
    );

    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流恢复任务表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_recovery_task';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_recovery_task', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_recovery_task', @level2type=N'COLUMN', @level2name=N'TenantId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_recovery_task', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户作用域唯一键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_recovery_task', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_recovery_task', @level2type=N'COLUMN', @level2name=N'InstanceId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程步骤标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_recovery_task', @level2type=N'COLUMN', @level2name=N'StepId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'恢复种类键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_recovery_task', @level2type=N'COLUMN', @level2name=N'KindKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'恢复任务状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_recovery_task', @level2type=N'COLUMN', @level2name=N'StatusKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已尝试次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_recovery_task', @level2type=N'COLUMN', @level2name=N'AttemptCount';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_recovery_task', @level2type=N'COLUMN', @level2name=N'Revision';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'执行租约持有者键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_recovery_task', @level2type=N'COLUMN', @level2name=N'LeaseOwnerKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租约过期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_recovery_task', @level2type=N'COLUMN', @level2name=N'LeaseExpiresAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租约世代', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_recovery_task', @level2type=N'COLUMN', @level2name=N'LeaseGeneration';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'下次尝试时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_recovery_task', @level2type=N'COLUMN', @level2name=N'NextAttemptAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最后错误摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_recovery_task', @level2type=N'COLUMN', @level2name=N'LastError';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_recovery_task', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_recovery_task', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'未关闭恢复任务占用键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_recovery_task', @level2type=N'COLUMN', @level2name=N'OpenOccupancyKey';
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_recovery_task')
      AND name = N'UX_fn_workflow_recovery_task_OpenOccupancy')
    CREATE UNIQUE INDEX UX_fn_workflow_recovery_task_OpenOccupancy
        ON dbo.fn_workflow_recovery_task(OpenOccupancyKey)
        WHERE StatusKey IN ('pending', 'failed', 'dead_lettered');

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_recovery_task')
      AND name = N'IX_fn_workflow_recovery_task_Claim')
    CREATE INDEX IX_fn_workflow_recovery_task_Claim
        ON dbo.fn_workflow_recovery_task(StatusKey, NextAttemptAtUtc, CreatedAtUtc, Id);
