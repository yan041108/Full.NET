-- 310：延时事实与步骤、续办均由Workflow拥有；DDL分段可重复恢复。
IF OBJECT_ID(N'dbo.fn_workflow_delay',N'U') IS NULL
BEGIN
  CREATE TABLE dbo.fn_workflow_delay (
    Id uniqueidentifier NOT NULL,
    InstanceId uniqueidentifier NOT NULL,
    TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    NodeKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    ContinuationNodeKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    InputJson nvarchar(max) NOT NULL,
    InputHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    InputFormRevision bigint NOT NULL,
    ParallelJoinId uniqueidentifier NULL,
    ParallelBranchKey varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
    ModeKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    TimeZoneKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    DueAtUtc datetime2(6) NOT NULL,
    RemainingMilliseconds bigint NULL,
    PausedAtUtc datetime2(6) NULL,
    StatusKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    Revision bigint NOT NULL,
    CreatedAtUtc datetime2(6) NOT NULL,
    CompletedAtUtc datetime2(6) NULL,
    CONSTRAINT pk_fn_workflow_delay PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT fk_fn_workflow_delay_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id),
    CONSTRAINT fk_fn_workflow_delay_step FOREIGN KEY (Id) REFERENCES dbo.fn_workflow_step(Id),
    CONSTRAINT ck_fn_workflow_delay_state CHECK (StatusKey IN ('waiting','fired','cancelled') AND ModeKey IN ('relative','absolute') AND Revision>0 AND InputFormRevision>0),
    CONSTRAINT ck_fn_workflow_delay_pause CHECK ((PausedAtUtc IS NULL AND RemainingMilliseconds IS NULL) OR (ModeKey IN ('relative','working') AND PausedAtUtc IS NOT NULL AND RemainingMilliseconds BETWEEN 0 AND 31622400000))
  );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND minor_id=0 AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流耐久延时激活事实，唤醒与后继续办原子收敛',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_delay';
GO
-- 与步骤及MySQL统一到UTC微秒，避免恢复后两个事实的截止时间因精度不同而漂移。
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND name=N'PausedAtUtc' AND (system_type_id<>42 OR scale<>6))
  AND EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND name=N'ck_fn_workflow_delay_pause')
  ALTER TABLE dbo.fn_workflow_delay DROP CONSTRAINT ck_fn_workflow_delay_pause;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND name IN (N'DueAtUtc',N'PausedAtUtc') AND (system_type_id<>42 OR scale<>6))
  AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND name=N'ix_fn_workflow_delay_due')
  DROP INDEX ix_fn_workflow_delay_due ON dbo.fn_workflow_delay;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND name=N'DueAtUtc' AND (system_type_id<>42 OR scale<>6))
  ALTER TABLE dbo.fn_workflow_delay ALTER COLUMN DueAtUtc datetime2(6) NOT NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND name=N'PausedAtUtc' AND (system_type_id<>42 OR scale<>6))
  ALTER TABLE dbo.fn_workflow_delay ALTER COLUMN PausedAtUtc datetime2(6) NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND name=N'CreatedAtUtc' AND (system_type_id<>42 OR scale<>6))
  ALTER TABLE dbo.fn_workflow_delay ALTER COLUMN CreatedAtUtc datetime2(6) NOT NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND name=N'CompletedAtUtc' AND (system_type_id<>42 OR scale<>6))
  ALTER TABLE dbo.fn_workflow_delay ALTER COLUMN CompletedAtUtc datetime2(6) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND name=N'ck_fn_workflow_delay_pause')
  ALTER TABLE dbo.fn_workflow_delay WITH CHECK ADD CONSTRAINT ck_fn_workflow_delay_pause CHECK ((PausedAtUtc IS NULL AND RemainingMilliseconds IS NULL) OR (ModeKey IN ('relative','working') AND PausedAtUtc IS NOT NULL AND RemainingMilliseconds BETWEEN 0 AND 31622400000));
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND name=N'ix_fn_workflow_delay_due')
  CREATE INDEX ix_fn_workflow_delay_due ON dbo.fn_workflow_delay(StatusKey,DueAtUtc,Id) INCLUDE(InstanceId,PausedAtUtc);
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_delay'),N'Id','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'复用延时激活步骤的UUID v7标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_delay',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_delay'),N'InstanceId','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'所属Workflow实例标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_delay',@level2type=N'COLUMN',@level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_delay'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'激活时的可信租户作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_delay',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_delay'),N'NodeKey','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'发布版本中的延时节点键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_delay',@level2type=N'COLUMN',@level2name=N'NodeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_delay'),N'ContinuationNodeKey','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'等待结束后才解析的固定后继节点键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_delay',@level2type=N'COLUMN',@level2name=N'ContinuationNodeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_delay'),N'InputJson','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本次激活不可变表单输入快照',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_delay',@level2type=N'COLUMN',@level2name=N'InputJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_delay'),N'InputHash','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定输入的SHA256摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_delay',@level2type=N'COLUMN',@level2name=N'InputHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_delay'),N'InputFormRevision','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'输入所属主表单或分支修订号',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_delay',@level2type=N'COLUMN',@level2name=N'InputFormRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_delay'),N'ParallelJoinId','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本次激活所属分叉汇合身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_delay',@level2type=N'COLUMN',@level2name=N'ParallelJoinId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_delay'),N'ParallelBranchKey','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本次激活所属稳定分支键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_delay',@level2type=N'COLUMN',@level2name=N'ParallelBranchKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_delay'),N'ModeKey','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'relative墙钟时长或absolute绝对时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_delay',@level2type=N'COLUMN',@level2name=N'ModeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_delay'),N'TimeZoneKey','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'配置原时区标识，绝对输入必须另带明确偏移',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_delay',@level2type=N'COLUMN',@level2name=N'TimeZoneKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_delay'),N'DueAtUtc','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'已解析的UTC唤醒时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_delay',@level2type=N'COLUMN',@level2name=N'DueAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_delay'),N'RemainingMilliseconds','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'暂停时冻结的非负相对等待毫秒数',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_delay',@level2type=N'COLUMN',@level2name=N'RemainingMilliseconds';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_delay'),N'PausedAtUtc','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'相对等待最近暂停时间，绝对时间不平移',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_delay',@level2type=N'COLUMN',@level2name=N'PausedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_delay'),N'StatusKey','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'waiting等待、fired已唤醒或cancelled已取消',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_delay',@level2type=N'COLUMN',@level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_delay'),N'Revision','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'唤醒与暂停恢复并发裁决修订号',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_delay',@level2type=N'COLUMN',@level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_delay'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'激活创建时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_delay',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_delay'),N'CompletedAtUtc','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'唤醒或取消完成时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_delay',@level2type=N'COLUMN',@level2name=N'CompletedAtUtc';
GO
