-- 315：活动级SLA固定系统处置与显式暂停策略；每个结构段可独立恢复。
IF OBJECT_ID(N'dbo.fn_workflow_sla',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_sla (
  Id uniqueidentifier NOT NULL,
  InstanceId uniqueidentifier NOT NULL,
  TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ActionKey varchar(24) COLLATE Latin1_General_100_BIN2 NOT NULL,
  PauseModeKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
  DueAtUtc datetime2(6) NOT NULL,
  RemainingMilliseconds bigint NULL,
  PausedAtUtc datetime2(6) NULL,
  StatusKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
  Revision bigint NOT NULL,
  CreatedAtUtc datetime2(6) NOT NULL,
  CompletedAtUtc datetime2(6) NULL,
  CONSTRAINT pk_fn_workflow_sla PRIMARY KEY CLUSTERED (Id)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_sla') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'Workflow活动截止及系统处置事实',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_sla';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_sla') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_sla'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'真实步骤标识；复用应用端UUID v7激活身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_sla',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_sla') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_sla'),N'InstanceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'所属Workflow实例标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_sla',@level2type=N'COLUMN',@level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_sla') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_sla'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'来自可信父实例的作用域键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_sla',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_sla') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_sla'),N'ActionKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'发布固定的系统处置；不代表个人审批票',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_sla',@level2type=N'COLUMN',@level2name=N'ActionKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_sla') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_sla'),N'PauseModeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'elapsed继续计时或freeze冻结剩余时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_sla',@level2type=N'COLUMN',@level2name=N'PauseModeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_sla') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_sla'),N'DueAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'该次活动绝对截止UTC微秒时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_sla',@level2type=N'COLUMN',@level2name=N'DueAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_sla') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_sla'),N'RemainingMilliseconds','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'显式冻结时向上取整的剩余墙钟毫秒',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_sla',@level2type=N'COLUMN',@level2name=N'RemainingMilliseconds';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_sla') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_sla'),N'PausedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'显式冻结时间；继续计时模式为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_sla',@level2type=N'COLUMN',@level2name=N'PausedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_sla') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_sla'),N'StatusKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'waiting待处理、fired已处置、cancelled已失效',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_sla',@level2type=N'COLUMN',@level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_sla') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_sla'),N'Revision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'截止事实乐观锁修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_sla',@level2type=N'COLUMN',@level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_sla') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_sla'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'活动截止创建UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_sla',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_sla') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_sla'),N'CompletedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'处置或失效确认UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_sla',@level2type=N'COLUMN',@level2name=N'CompletedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_sla') AND name=N'ck_fn_workflow_sla_state')
 ALTER TABLE dbo.fn_workflow_sla ADD CONSTRAINT ck_fn_workflow_sla_state CHECK (StatusKey IN ('waiting','fired','cancelled') AND Revision>0 AND ActionKey IN ('suspend','auto_reject','auto_pass') AND PauseModeKey IN ('elapsed','freeze') AND ((StatusKey='waiting' AND CompletedAtUtc IS NULL) OR (StatusKey<>'waiting' AND CompletedAtUtc IS NOT NULL)));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_sla') AND name=N'ck_fn_workflow_sla_pause')
 ALTER TABLE dbo.fn_workflow_sla ADD CONSTRAINT ck_fn_workflow_sla_pause CHECK ((PausedAtUtc IS NULL AND RemainingMilliseconds IS NULL) OR (PauseModeKey='freeze' AND PausedAtUtc IS NOT NULL AND RemainingMilliseconds IS NOT NULL AND RemainingMilliseconds>=0 AND RemainingMilliseconds<=31536000000));
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_sla') AND name=N'ix_fn_workflow_sla_due')
 CREATE INDEX ix_fn_workflow_sla_due ON dbo.fn_workflow_sla(StatusKey,DueAtUtc,Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_sla') AND name=N'ix_fn_workflow_sla_instance')
 CREATE INDEX ix_fn_workflow_sla_instance ON dbo.fn_workflow_sla(InstanceId,StatusKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_sla') AND name=N'fk_fn_workflow_sla_step')
 ALTER TABLE dbo.fn_workflow_sla ADD CONSTRAINT fk_fn_workflow_sla_step FOREIGN KEY (Id) REFERENCES dbo.fn_workflow_step(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_sla') AND name=N'fk_fn_workflow_sla_instance')
 ALTER TABLE dbo.fn_workflow_sla ADD CONSTRAINT fk_fn_workflow_sla_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id);
GO
