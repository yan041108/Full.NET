-- 111：为工作流待办固化发布版本中的超时、催办和升级调度状态。
-- 全部列可空或有安全默认值，历史待办保持“未配置超时”；重复执行和部分 DDL 恢复均安全。
IF COL_LENGTH(N'dbo.fn_workflow_todo', N'DueAtUtc') IS NULL
    ALTER TABLE dbo.fn_workflow_todo ADD DueAtUtc datetime2(6) NULL;
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_todo')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo'), N'DueAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'待办逾期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo', @level2type=N'COLUMN', @level2name=N'DueAtUtc';
IF COL_LENGTH(N'dbo.fn_workflow_todo', N'NextReminderAtUtc') IS NULL
    ALTER TABLE dbo.fn_workflow_todo ADD NextReminderAtUtc datetime2(6) NULL;
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_todo')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo'), N'NextReminderAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'下一催办时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo', @level2type=N'COLUMN', @level2name=N'NextReminderAtUtc';
IF COL_LENGTH(N'dbo.fn_workflow_todo', N'EscalateAtUtc') IS NULL
    ALTER TABLE dbo.fn_workflow_todo ADD EscalateAtUtc datetime2(6) NULL;
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_todo')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo'), N'EscalateAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'升级通知时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo', @level2type=N'COLUMN', @level2name=N'EscalateAtUtc';
IF COL_LENGTH(N'dbo.fn_workflow_todo', N'MaxReminderCount') IS NULL
    ALTER TABLE dbo.fn_workflow_todo ADD MaxReminderCount int NOT NULL CONSTRAINT DF_fn_workflow_todo_MaxReminderCount DEFAULT (0);
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_todo')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo'), N'MaxReminderCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最大催办次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo', @level2type=N'COLUMN', @level2name=N'MaxReminderCount';
IF COL_LENGTH(N'dbo.fn_workflow_todo', N'ReminderIntervalMinutes') IS NULL
    ALTER TABLE dbo.fn_workflow_todo ADD ReminderIntervalMinutes int NOT NULL CONSTRAINT DF_fn_workflow_todo_ReminderIntervalMinutes DEFAULT (0);
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_todo')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo'), N'ReminderIntervalMinutes', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'催办间隔分钟数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo', @level2type=N'COLUMN', @level2name=N'ReminderIntervalMinutes';
IF COL_LENGTH(N'dbo.fn_workflow_todo', N'ReminderCount') IS NULL
    ALTER TABLE dbo.fn_workflow_todo ADD ReminderCount int NOT NULL CONSTRAINT DF_fn_workflow_todo_ReminderCount DEFAULT (0);
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_todo')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo'), N'ReminderCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已发送催办次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo', @level2type=N'COLUMN', @level2name=N'ReminderCount';
IF COL_LENGTH(N'dbo.fn_workflow_todo', N'EscalationRecipientUserId') IS NULL
    ALTER TABLE dbo.fn_workflow_todo ADD EscalationRecipientUserId uniqueidentifier NULL;
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_todo')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo'), N'EscalationRecipientUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定升级通知接收人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo', @level2type=N'COLUMN', @level2name=N'EscalationRecipientUserId';
IF COL_LENGTH(N'dbo.fn_workflow_todo', N'LastReminderAtUtc') IS NULL
    ALTER TABLE dbo.fn_workflow_todo ADD LastReminderAtUtc datetime2(6) NULL;
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_todo')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo'), N'LastReminderAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最后催办时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo', @level2type=N'COLUMN', @level2name=N'LastReminderAtUtc';
IF COL_LENGTH(N'dbo.fn_workflow_todo', N'EscalatedAtUtc') IS NULL
    ALTER TABLE dbo.fn_workflow_todo ADD EscalatedAtUtc datetime2(6) NULL;
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_todo')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo'), N'EscalatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已升级时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo', @level2type=N'COLUMN', @level2name=N'EscalatedAtUtc';
IF COL_LENGTH(N'dbo.fn_workflow_todo', N'NextTimeoutSignalAtUtc') IS NULL
    ALTER TABLE dbo.fn_workflow_todo ADD NextTimeoutSignalAtUtc datetime2(6) NULL;
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_todo')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo'), N'NextTimeoutSignalAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'下一超时信号时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo', @level2type=N'COLUMN', @level2name=N'NextTimeoutSignalAtUtc';

GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_fn_workflow_todo_ReminderCounts')
    ALTER TABLE dbo.fn_workflow_todo ADD CONSTRAINT CK_fn_workflow_todo_ReminderCounts
        CHECK (MaxReminderCount >= 0 AND ReminderCount >= 0 AND ReminderCount <= MaxReminderCount);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_todo') AND name = N'IX_fn_workflow_todo_TimeoutScan')
    CREATE INDEX IX_fn_workflow_todo_TimeoutScan
        ON dbo.fn_workflow_todo(StatusKey, NextTimeoutSignalAtUtc, Id)
        INCLUDE (InstanceId, AssigneeUserId, Revision)
        WHERE NextTimeoutSignalAtUtc IS NOT NULL;
