-- 抢单只在真实领取后创建Todo；冻结候选不替代领取时的当前授权来源。
IF OBJECT_ID(N'dbo.fn_workflow_claim',N'U') IS NULL
BEGIN
CREATE TABLE dbo.fn_workflow_claim (
    Id uniqueidentifier NOT NULL,
    InstanceId uniqueidentifier NOT NULL,
    StatusKey nvarchar(24) COLLATE Latin1_General_100_BIN2 NOT NULL,
    Revision bigint NOT NULL,
    ClaimCycle int NOT NULL,
    AllowRelease bit NOT NULL,
    CurrentTodoId uniqueidentifier NULL,
    CurrentAssigneeUserId uniqueidentifier NULL,
    DueAtUtc datetimeoffset(7) NULL,
    NextReminderAtUtc datetimeoffset(7) NULL,
    EscalateAtUtc datetimeoffset(7) NULL,
    MaxReminderCount int NOT NULL,
    ReminderIntervalMinutes int NOT NULL,
    ReminderCount int NOT NULL,
    EscalationRecipientUserId uniqueidentifier NULL,
    EscalatedAtUtc datetimeoffset(7) NULL,
    CreatedAtUtc datetimeoffset(7) NOT NULL,
    CompletedAtUtc datetimeoffset(7) NULL,
    CONSTRAINT pk_fn_workflow_claim PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT fk_fn_workflow_claim_step FOREIGN KEY (Id) REFERENCES dbo.fn_workflow_step(Id),
    CONSTRAINT fk_fn_workflow_claim_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id),
    CONSTRAINT fk_fn_workflow_claim_todo FOREIGN KEY (CurrentTodoId) REFERENCES dbo.fn_workflow_todo(Id)
);
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流候选领取池；到达时没有个人待办', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'活动步骤及候选池标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim'),N'InstanceId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属流程实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim', @level2type=N'COLUMN', @level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim'),N'StatusKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'候选池状态：可领取、已领取、已完成或已取消', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim', @level2type=N'COLUMN', @level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim'),N'Revision','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'候选池并发修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim', @level2type=N'COLUMN', @level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim'),N'ClaimCycle','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际领取代次，释放不能重置预算', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim', @level2type=N'COLUMN', @level2name=N'ClaimCycle';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim'),N'AllowRelease','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布策略是否允许本人释放', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim', @level2type=N'COLUMN', @level2name=N'AllowRelease';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim'),N'CurrentTodoId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前实际领取待办标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim', @level2type=N'COLUMN', @level2name=N'CurrentTodoId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim'),N'CurrentAssigneeUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前真实持有人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim', @level2type=N'COLUMN', @level2name=N'CurrentAssigneeUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim'),N'DueAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'到达时确定且释放不重置的绝对截止时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim', @level2type=N'COLUMN', @level2name=N'DueAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim'),N'NextReminderAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'剩余提醒时间，未领取不发个人提醒', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim', @level2type=N'COLUMN', @level2name=N'NextReminderAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim'),N'EscalateAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'绝对升级时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim', @level2type=N'COLUMN', @level2name=N'EscalateAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim'),N'MaxReminderCount','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最大提醒次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim', @level2type=N'COLUMN', @level2name=N'MaxReminderCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim'),N'ReminderIntervalMinutes','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'提醒间隔分钟数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim', @level2type=N'COLUMN', @level2name=N'ReminderIntervalMinutes';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim'),N'ReminderCount','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已经发送的提醒次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim', @level2type=N'COLUMN', @level2name=N'ReminderCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim'),N'EscalationRecipientUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'升级接收人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim', @level2type=N'COLUMN', @level2name=N'EscalationRecipientUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim'),N'EscalatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际升级时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim', @level2type=N'COLUMN', @level2name=N'EscalatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'候选池到达时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim'),N'CompletedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'候选池终结时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim', @level2type=N'COLUMN', @level2name=N'CompletedAtUtc';
GO
IF OBJECT_ID(N'dbo.fn_workflow_claim_candidate',N'U') IS NULL
BEGIN
CREATE TABLE dbo.fn_workflow_claim_candidate (
    Id uniqueidentifier NOT NULL,
    InstanceId uniqueidentifier NOT NULL,
    PoolId uniqueidentifier NOT NULL,
    UserId uniqueidentifier NOT NULL,
    SequenceNo int NOT NULL,
    CONSTRAINT pk_fn_workflow_claim_candidate PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT fk_fn_workflow_claim_candidate_pool FOREIGN KEY (PoolId) REFERENCES dbo.fn_workflow_claim(Id),
    CONSTRAINT fk_fn_workflow_claim_candidate_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id)
);
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim_candidate') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流到达时固定的候选人，不授予后来领取时的权限', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim_candidate';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim_candidate') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim_candidate'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定候选事实标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim_candidate', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim_candidate') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim_candidate'),N'InstanceId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属流程实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim_candidate', @level2type=N'COLUMN', @level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim_candidate') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim_candidate'),N'PoolId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属候选池标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim_candidate', @level2type=N'COLUMN', @level2name=N'PoolId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim_candidate') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim_candidate'),N'UserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际解析且去重的候选人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim_candidate', @level2type=N'COLUMN', @level2name=N'UserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim_candidate') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim_candidate'),N'SequenceNo','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'到达时稳定顺序', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim_candidate', @level2type=N'COLUMN', @level2name=N'SequenceNo';
GO
IF OBJECT_ID(N'dbo.fn_workflow_claim_attempt',N'U') IS NULL
BEGIN
CREATE TABLE dbo.fn_workflow_claim_attempt (
    Id uniqueidentifier NOT NULL,
    InstanceId uniqueidentifier NOT NULL,
    PoolId uniqueidentifier NOT NULL,
    TodoId uniqueidentifier NOT NULL,
    AssigneeUserId uniqueidentifier NOT NULL,
    OriginalAssigneeUserId uniqueidentifier NOT NULL,
    ClaimedByUserId uniqueidentifier NOT NULL,
    AuthorizationKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    ClaimCycle int NOT NULL,
    StatusKey nvarchar(24) COLLATE Latin1_General_100_BIN2 NOT NULL,
    Revision bigint NOT NULL,
    CreatedAtUtc datetimeoffset(7) NOT NULL,
    CompletedAtUtc datetimeoffset(7) NULL,
    CONSTRAINT pk_fn_workflow_claim_attempt PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT fk_fn_workflow_claim_attempt_pool FOREIGN KEY (PoolId) REFERENCES dbo.fn_workflow_claim(Id),
    CONSTRAINT fk_fn_workflow_claim_attempt_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id),
    CONSTRAINT fk_fn_workflow_claim_attempt_todo FOREIGN KEY (TodoId) REFERENCES dbo.fn_workflow_todo(Id)
);
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim_attempt') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流每次真实领取历史；交接重绑同一领取事实', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim_attempt';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim_attempt'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际领取事实标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim_attempt', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim_attempt'),N'InstanceId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属流程实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim_attempt', @level2type=N'COLUMN', @level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim_attempt'),N'PoolId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属候选池标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim_attempt', @level2type=N'COLUMN', @level2name=N'PoolId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim_attempt'),N'TodoId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前实际待办标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim_attempt', @level2type=N'COLUMN', @level2name=N'TodoId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim_attempt'),N'AssigneeUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前真实持有人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim_attempt', @level2type=N'COLUMN', @level2name=N'AssigneeUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim_attempt'),N'OriginalAssigneeUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本次领取的原始持有人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim_attempt', @level2type=N'COLUMN', @level2name=N'OriginalAssigneeUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim_attempt'),N'ClaimedByUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际执行领取或指派的用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim_attempt', @level2type=N'COLUMN', @level2name=N'ClaimedByUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim_attempt'),N'AuthorizationKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本次领取或指派的精确授权依据', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim_attempt', @level2type=N'COLUMN', @level2name=N'AuthorizationKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim_attempt'),N'ClaimCycle','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本次领取代次', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim_attempt', @level2type=N'COLUMN', @level2name=N'ClaimCycle';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim_attempt'),N'StatusKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'领取事实状态：活动、释放、完成或取消', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim_attempt', @level2type=N'COLUMN', @level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim_attempt'),N'Revision','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'领取事实并发修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim_attempt', @level2type=N'COLUMN', @level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim_attempt'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'真实领取时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim_attempt', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_claim_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_claim_attempt'),N'CompletedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本次领取关闭时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_claim_attempt', @level2type=N'COLUMN', @level2name=N'CompletedAtUtc';
GO
IF COL_LENGTH(N'dbo.fn_workflow_todo_transfer',N'ClaimAttemptId') IS NULL
    ALTER TABLE dbo.fn_workflow_todo_transfer ADD ClaimAttemptId uniqueidentifier NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_transfer'),N'ClaimAttemptId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'绑定本次实际领取事实，不能同时绑定其他人工成员', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_transfer', @level2type=N'COLUMN', @level2name=N'ClaimAttemptId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND name=N'fk_fn_workflow_todo_transfer_claim_attempt')
    ALTER TABLE dbo.fn_workflow_todo_transfer ADD CONSTRAINT fk_fn_workflow_todo_transfer_claim_attempt FOREIGN KEY (ClaimAttemptId) REFERENCES dbo.fn_workflow_claim_attempt(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_claim') AND name=N'ck_fn_workflow_claim_policy')
    ALTER TABLE dbo.fn_workflow_claim ADD CONSTRAINT ck_fn_workflow_claim_policy CHECK (Revision>=1 AND ClaimCycle BETWEEN 0 AND 32 AND MaxReminderCount>=0 AND ReminderCount BETWEEN 0 AND MaxReminderCount AND ReminderIntervalMinutes>=0 AND (MaxReminderCount=0 OR ReminderIntervalMinutes>=1));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_claim') AND name=N'ck_fn_workflow_claim_state')
    ALTER TABLE dbo.fn_workflow_claim ADD CONSTRAINT ck_fn_workflow_claim_state CHECK (((StatusKey='available' AND ClaimCycle<32 AND CurrentTodoId IS NULL AND CurrentAssigneeUserId IS NULL AND CompletedAtUtc IS NULL) OR (StatusKey='claimed' AND CurrentTodoId IS NOT NULL AND CurrentAssigneeUserId IS NOT NULL AND ClaimCycle>=1 AND CompletedAtUtc IS NULL) OR (StatusKey='completed' AND ClaimCycle>=1 AND CurrentTodoId IS NOT NULL AND CurrentAssigneeUserId IS NOT NULL AND CompletedAtUtc IS NOT NULL) OR (StatusKey='cancelled' AND CompletedAtUtc IS NOT NULL AND ((CurrentTodoId IS NULL AND CurrentAssigneeUserId IS NULL) OR (CurrentTodoId IS NOT NULL AND CurrentAssigneeUserId IS NOT NULL)))));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_claim_candidate') AND name=N'ck_fn_workflow_claim_candidate_sequence')
    ALTER TABLE dbo.fn_workflow_claim_candidate ADD CONSTRAINT ck_fn_workflow_claim_candidate_sequence CHECK (SequenceNo BETWEEN 1 AND 20);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_claim_attempt') AND name=N'ck_fn_workflow_claim_attempt_policy')
    ALTER TABLE dbo.fn_workflow_claim_attempt ADD CONSTRAINT ck_fn_workflow_claim_attempt_policy CHECK (Revision>=1 AND ClaimCycle BETWEEN 1 AND 32 AND (AuthorizationKey='workflow.claim_pools.assign' OR (AuthorizationKey='workflow.claim_pools.claim' AND ClaimedByUserId=OriginalAssigneeUserId)));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_claim_attempt') AND name=N'ck_fn_workflow_claim_attempt_state')
    ALTER TABLE dbo.fn_workflow_claim_attempt ADD CONSTRAINT ck_fn_workflow_claim_attempt_state CHECK (((StatusKey='active' AND CompletedAtUtc IS NULL) OR (StatusKey IN ('released','completed','cancelled') AND CompletedAtUtc IS NOT NULL)));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND name=N'ck_fn_workflow_todo_transfer_claim_member')
    ALTER TABLE dbo.fn_workflow_todo_transfer ADD CONSTRAINT ck_fn_workflow_todo_transfer_claim_member CHECK (((CASE WHEN SlotId IS NULL THEN 0 ELSE 1 END)+(CASE WHEN TaskMemberId IS NULL THEN 0 ELSE 1 END)+(CASE WHEN VoteBallotId IS NULL THEN 0 ELSE 1 END)+(CASE WHEN ClaimAttemptId IS NULL THEN 0 ELSE 1 END))<=1);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_claim') AND name=N'ix_fn_workflow_claim_instance_status')
    CREATE INDEX ix_fn_workflow_claim_instance_status ON dbo.fn_workflow_claim(InstanceId, StatusKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_claim') AND name=N'ix_fn_workflow_claim_due')
    CREATE INDEX ix_fn_workflow_claim_due ON dbo.fn_workflow_claim(StatusKey, DueAtUtc, Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_claim') AND name=N'uq_fn_workflow_claim_todo')
    CREATE UNIQUE INDEX uq_fn_workflow_claim_todo ON dbo.fn_workflow_claim(CurrentTodoId) WHERE CurrentTodoId IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_claim_candidate') AND name=N'uq_fn_workflow_claim_candidate_user')
    CREATE UNIQUE INDEX uq_fn_workflow_claim_candidate_user ON dbo.fn_workflow_claim_candidate(PoolId, UserId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_claim_candidate') AND name=N'uq_fn_workflow_claim_candidate_sequence')
    CREATE UNIQUE INDEX uq_fn_workflow_claim_candidate_sequence ON dbo.fn_workflow_claim_candidate(PoolId, SequenceNo);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_claim_candidate') AND name=N'ix_fn_workflow_claim_candidate_user_pool')
    CREATE INDEX ix_fn_workflow_claim_candidate_user_pool ON dbo.fn_workflow_claim_candidate(UserId, PoolId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_claim_attempt') AND name=N'uq_fn_workflow_claim_attempt_todo')
    CREATE UNIQUE INDEX uq_fn_workflow_claim_attempt_todo ON dbo.fn_workflow_claim_attempt(TodoId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_claim_attempt') AND name=N'uq_fn_workflow_claim_attempt_cycle')
    CREATE UNIQUE INDEX uq_fn_workflow_claim_attempt_cycle ON dbo.fn_workflow_claim_attempt(PoolId, ClaimCycle);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_claim_attempt') AND name=N'ix_fn_workflow_claim_attempt_instance')
    CREATE INDEX ix_fn_workflow_claim_attempt_instance ON dbo.fn_workflow_claim_attempt(InstanceId, StatusKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND name=N'ix_fn_workflow_todo_transfer_claim_attempt')
    CREATE INDEX ix_fn_workflow_todo_transfer_claim_attempt ON dbo.fn_workflow_todo_transfer(ClaimAttemptId);
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND name=N'ck_fn_workflow_todo_transfer_human_member')
    ALTER TABLE dbo.fn_workflow_todo_transfer DROP CONSTRAINT ck_fn_workflow_todo_transfer_human_member;
GO
