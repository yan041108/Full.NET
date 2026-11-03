-- 本人日程原操作Inbox及准确接收方回执，不保存日程正文
IF OBJECT_ID(N'dbo.fn_calendar_schedule_completion',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_calendar_schedule_completion (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ScopeKey varchar(32) NOT NULL,
        OwnerUserId uniqueidentifier NOT NULL,
        RunId uniqueidentifier NOT NULL,
        OperationId uniqueidentifier NOT NULL,
        ScheduleId uniqueidentifier NOT NULL,
        ExpectedVersion int NOT NULL,
        RequestDigest char(64) NOT NULL,
        Outcome varchar(16) NOT NULL,
        Reason varchar(48) NULL,
        ActualVersion int NULL,
        CompletedAtUtc datetime2(6) NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_calendar_schedule_completion PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UX_fn_calendar_schedule_completion_Operation UNIQUE (ScopeKey,OperationId)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_calendar_schedule_completion') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_schedule_completion'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'应用端UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_calendar_schedule_completion',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_calendar_schedule_completion') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_schedule_completion'),N'TenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信租户，Host为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_calendar_schedule_completion',@level2type=N'COLUMN',@level2name=N'TenantId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_calendar_schedule_completion') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_schedule_completion'),N'ScopeKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信范围唯一键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_calendar_schedule_completion',@level2type=N'COLUMN',@level2name=N'ScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_calendar_schedule_completion') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_schedule_completion'),N'OwnerUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原操作本人主体',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_calendar_schedule_completion',@level2type=N'COLUMN',@level2name=N'OwnerUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_calendar_schedule_completion') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_schedule_completion'),N'RunId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'真实AI运行身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_calendar_schedule_completion',@level2type=N'COLUMN',@level2name=N'RunId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_calendar_schedule_completion') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_schedule_completion'),N'OperationId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原写节点稳定操作身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_calendar_schedule_completion',@level2type=N'COLUMN',@level2name=N'OperationId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_calendar_schedule_completion') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_schedule_completion'),N'ScheduleId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'准确个人日程身份，无跨模块外键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_calendar_schedule_completion',@level2type=N'COLUMN',@level2name=N'ScheduleId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_calendar_schedule_completion') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_schedule_completion'),N'ExpectedVersion','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原批准pending版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_calendar_schedule_completion',@level2type=N'COLUMN',@level2name=N'ExpectedVersion';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_calendar_schedule_completion') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_schedule_completion'),N'RequestDigest','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'不可更换原请求摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_calendar_schedule_completion',@level2type=N'COLUMN',@level2name=N'RequestDigest';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_calendar_schedule_completion') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_schedule_completion'),N'Outcome','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'completed或rejected实际事实',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_calendar_schedule_completion',@level2type=N'COLUMN',@level2name=N'Outcome';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_calendar_schedule_completion') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_schedule_completion'),N'Reason','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'安全拒绝原因',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_calendar_schedule_completion',@level2type=N'COLUMN',@level2name=N'Reason';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_calendar_schedule_completion') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_schedule_completion'),N'ActualVersion','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'成功CAS后的准确版本，拒绝为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_calendar_schedule_completion',@level2type=N'COLUMN',@level2name=N'ActualVersion';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_calendar_schedule_completion') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_schedule_completion'),N'CompletedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本操作实际完成UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_calendar_schedule_completion',@level2type=N'COLUMN',@level2name=N'CompletedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_calendar_schedule_completion') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_schedule_completion'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原操作回执提交UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_calendar_schedule_completion',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_calendar_schedule_completion') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本人日程原操作Inbox及准确接收方回执，不保存日程正文',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_calendar_schedule_completion';
GO
-- 独立批准消费与不可变发送意图，同AI Outbox原子提交
IF OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_schedule_completion_intent (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ScopeKey varchar(32) NOT NULL,
        OwnerUserId uniqueidentifier NOT NULL,
        RunId uniqueidentifier NOT NULL,
        OperationId uniqueidentifier NOT NULL,
        ScheduleId uniqueidentifier NOT NULL,
        ExpectedVersion int NOT NULL,
        RequestJson nvarchar(max) NOT NULL,
        RequestDigest char(64) NOT NULL,
        Outcome varchar(16) NOT NULL,
        ReceiptJson nvarchar(max) NULL,
        Version int NOT NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        UpdatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_ai_schedule_completion_intent PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UX_fn_ai_schedule_completion_intent_Operation UNIQUE (ScopeKey,OperationId),
        CONSTRAINT UX_fn_ai_schedule_completion_intent_Target UNIQUE (ScopeKey,ScheduleId,ExpectedVersion)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent'),N'ScheduleId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'不可变准确日程目标，原操作唯一占用',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_schedule_completion_intent',@level2type=N'COLUMN',@level2name=N'ScheduleId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent'),N'ExpectedVersion','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原操作准确pending版本，未知不得换新操作',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_schedule_completion_intent',@level2type=N'COLUMN',@level2name=N'ExpectedVersion';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'应用端UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_schedule_completion_intent',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent'),N'TenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信租户，Host为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_schedule_completion_intent',@level2type=N'COLUMN',@level2name=N'TenantId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent'),N'ScopeKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信范围唯一键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_schedule_completion_intent',@level2type=N'COLUMN',@level2name=N'ScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent'),N'OwnerUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原运行主体',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_schedule_completion_intent',@level2type=N'COLUMN',@level2name=N'OwnerUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent'),N'RunId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'真实静态运行身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_schedule_completion_intent',@level2type=N'COLUMN',@level2name=N'RunId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent'),N'OperationId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原写节点稳定操作身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_schedule_completion_intent',@level2type=N'COLUMN',@level2name=N'OperationId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent'),N'RequestJson','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'服务器冻结原会话与准确批准参数，不公开返回',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_schedule_completion_intent',@level2type=N'COLUMN',@level2name=N'RequestJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent'),N'RequestDigest','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'不可变原请求摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_schedule_completion_intent',@level2type=N'COLUMN',@level2name=N'RequestDigest';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent'),N'Outcome','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'accepted或unknown或接收方completed或rejected',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_schedule_completion_intent',@level2type=N'COLUMN',@level2name=N'Outcome';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent'),N'ReceiptJson','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'准确Calendar原操作回执，尚未确认时为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_schedule_completion_intent',@level2type=N'COLUMN',@level2name=N'ReceiptJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent'),N'Version','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'意图结果乐观版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_schedule_completion_intent',@level2type=N'COLUMN',@level2name=N'Version';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'意图提交UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_schedule_completion_intent',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent'),N'UpdatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'结果核对UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_schedule_completion_intent',@level2type=N'COLUMN',@level2name=N'UpdatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'独立批准消费与不可变发送意图，同AI Outbox原子提交',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_schedule_completion_intent';
GO
-- 原操作唯一键与状态约束独立探测，支持迁移记账前中断恢复。
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_calendar_schedule_completion') AND name=N'UX_fn_calendar_schedule_completion_Operation')
    CREATE UNIQUE INDEX UX_fn_calendar_schedule_completion_Operation ON dbo.fn_calendar_schedule_completion (ScopeKey,OperationId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_calendar_schedule_completion') AND name=N'CK_fn_calendar_schedule_completion_Bounds')
    ALTER TABLE dbo.fn_calendar_schedule_completion ADD CONSTRAINT CK_fn_calendar_schedule_completion_Bounds CHECK (ExpectedVersion>0 AND ExpectedVersion<2147483647 AND ((Outcome='completed' AND Reason IS NULL AND ActualVersion IS NOT NULL AND ActualVersion=ExpectedVersion+1 AND CompletedAtUtc IS NOT NULL) OR (Outcome='rejected' AND Reason IS NOT NULL AND Reason IN ('authorization_unavailable','intent_expired','target_changed') AND ActualVersion IS NULL AND CompletedAtUtc IS NULL)));
GO
-- 原操作唯一键与状态约束独立探测，支持迁移记账前中断恢复。
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent') AND name=N'UX_fn_ai_schedule_completion_intent_Operation')
    CREATE UNIQUE INDEX UX_fn_ai_schedule_completion_intent_Operation ON dbo.fn_ai_schedule_completion_intent (ScopeKey,OperationId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent') AND name=N'CK_fn_ai_schedule_completion_intent_Bounds')
    ALTER TABLE dbo.fn_ai_schedule_completion_intent ADD CONSTRAINT CK_fn_ai_schedule_completion_intent_Bounds CHECK (Version>0 AND ExpectedVersion>0 AND ExpectedVersion<2147483647 AND ((Outcome IN ('accepted','unknown') AND ReceiptJson IS NULL) OR (Outcome IN ('completed','rejected') AND ReceiptJson IS NOT NULL)));
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_ai_schedule_completion_intent') AND name=N'UX_fn_ai_schedule_completion_intent_Target')
    CREATE UNIQUE INDEX UX_fn_ai_schedule_completion_intent_Target ON dbo.fn_ai_schedule_completion_intent (ScopeKey,ScheduleId,ExpectedVersion);
GO
