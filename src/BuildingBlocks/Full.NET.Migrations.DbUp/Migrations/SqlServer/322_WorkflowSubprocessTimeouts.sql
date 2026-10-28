-- 322：子流程截止与取消收敛事实；结构探测支持DDL未记账重跑。
IF OBJECT_ID(N'dbo.fn_workflow_subprocess_timeout',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_subprocess_timeout (
  CallId uniqueidentifier NOT NULL,
  InstanceId uniqueidentifier NOT NULL,
  TenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  DueAtUtc datetime2(6) NOT NULL,
  PauseModeKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
  RemainingMilliseconds bigint NULL,
  PausedAtUtc datetime2(6) NULL,
  StatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
  Revision bigint NOT NULL,
  CONSTRAINT pk_fn_workflow_subprocess_timeout PRIMARY KEY CLUSTERED (CallId)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_timeout') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流子流程截止、冻结剩余时长及清理收敛状态',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_timeout';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_timeout') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_timeout'),N'CallId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际父调用UUID v7身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_timeout',@level2type=N'COLUMN',@level2name=N'CallId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_timeout') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_timeout'),N'InstanceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'父实例身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_timeout',@level2type=N'COLUMN',@level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_timeout') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_timeout'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'父实例可信作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_timeout',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_timeout') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_timeout'),N'DueAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定或恢复后的截止UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_timeout',@level2type=N'COLUMN',@level2name=N'DueAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_timeout') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_timeout'),N'PauseModeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'暂停时freeze冻结或elapsed流逝',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_timeout',@level2type=N'COLUMN',@level2name=N'PauseModeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_timeout') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_timeout'),N'RemainingMilliseconds','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结剩余毫秒',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_timeout',@level2type=N'COLUMN',@level2name=N'RemainingMilliseconds';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_timeout') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_timeout'),N'PausedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_timeout',@level2type=N'COLUMN',@level2name=N'PausedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_timeout') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_timeout'),N'StatusKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'等待清理就绪及关闭状态',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_timeout',@level2type=N'COLUMN',@level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_timeout') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_timeout'),N'Revision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'乐观并发修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_timeout',@level2type=N'COLUMN',@level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_timeout') AND name=N'ck_fn_workflow_subprocess_timeout_state')
 ALTER TABLE dbo.fn_workflow_subprocess_timeout ADD CONSTRAINT ck_fn_workflow_subprocess_timeout_state CHECK (Revision>0 AND PauseModeKey IN ('freeze','elapsed') AND StatusKey IN ('waiting','closing','ready','applied','cancelled') AND ((RemainingMilliseconds IS NULL AND PausedAtUtc IS NULL) OR (PauseModeKey='freeze' AND RemainingMilliseconds IS NOT NULL AND RemainingMilliseconds BETWEEN 0 AND 31536000000 AND PausedAtUtc IS NOT NULL)));
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_timeout') AND name=N'fk_fn_workflow_subprocess_timeout_call')
 ALTER TABLE dbo.fn_workflow_subprocess_timeout ADD CONSTRAINT fk_fn_workflow_subprocess_timeout_call FOREIGN KEY (CallId) REFERENCES fn_workflow_subprocess_call(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_timeout') AND name=N'fk_fn_workflow_subprocess_timeout_instance')
 ALTER TABLE dbo.fn_workflow_subprocess_timeout ADD CONSTRAINT fk_fn_workflow_subprocess_timeout_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id);
GO
