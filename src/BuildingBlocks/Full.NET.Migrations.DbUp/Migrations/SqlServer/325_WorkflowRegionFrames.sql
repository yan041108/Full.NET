-- 325：区域代次持有不可变入口/出口和外层真实分叉；不引入根级可变当前代次。
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_instance') AND name=N'ux_fn_workflow_instance_version')
 CREATE UNIQUE INDEX ux_fn_workflow_instance_version ON dbo.fn_workflow_instance(Id,DefinitionVersionId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_parallel_join') AND name=N'ux_fn_workflow_parallel_join_instance')
 CREATE UNIQUE INDEX ux_fn_workflow_parallel_join_instance ON dbo.fn_workflow_parallel_join(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_execution_epoch') AND name=N'ux_fn_workflow_execution_epoch_iteration')
 CREATE UNIQUE INDEX ux_fn_workflow_execution_epoch_iteration ON dbo.fn_workflow_execution_epoch(ParentEpochId,RegionKey,IterationNumber) WHERE ParentEpochId IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_execution_epoch') AND name=N'ux_fn_workflow_execution_epoch_cause')
 CREATE UNIQUE INDEX ux_fn_workflow_execution_epoch_cause ON dbo.fn_workflow_execution_epoch(CauseStepId) WHERE CauseStepId IS NOT NULL;
GO
IF OBJECT_ID(N'dbo.fn_workflow_region_frame',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_region_frame (
  ExecutionEpochId uniqueidentifier NOT NULL,
  InstanceId uniqueidentifier NOT NULL,
  TenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  DefinitionVersionId uniqueidentifier NOT NULL,
  EntryNodeKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ExitNodeKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ParentForkActivationId uniqueidentifier NULL,
  ParentBranchKey varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
  CreatedAtUtc datetime2(6) NOT NULL,
  CONSTRAINT pk_fn_workflow_region_frame PRIMARY KEY CLUSTERED (ExecutionEpochId)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_region_frame') AND name=N'ck_fn_workflow_region_frame_state')
 ALTER TABLE dbo.fn_workflow_region_frame ADD CONSTRAINT ck_fn_workflow_region_frame_state CHECK (
  LEN(EntryNodeKey)>0 AND LEN(ExitNodeKey)>0 AND EntryNodeKey<>ExitNodeKey AND
  ((ParentForkActivationId IS NULL AND ParentBranchKey IS NULL) OR
   (ParentForkActivationId IS NOT NULL AND ParentBranchKey IS NOT NULL AND LEN(ParentBranchKey)>0)));
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_region_frame') AND name=N'fk_fn_workflow_region_frame_epoch')
 ALTER TABLE dbo.fn_workflow_region_frame ADD CONSTRAINT fk_fn_workflow_region_frame_epoch FOREIGN KEY (ExecutionEpochId,InstanceId) REFERENCES dbo.fn_workflow_execution_epoch(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_region_frame') AND name=N'fk_fn_workflow_region_frame_version')
 ALTER TABLE dbo.fn_workflow_region_frame ADD CONSTRAINT fk_fn_workflow_region_frame_version FOREIGN KEY (InstanceId,DefinitionVersionId) REFERENCES dbo.fn_workflow_instance(Id,DefinitionVersionId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_region_frame') AND name=N'fk_fn_workflow_region_frame_fork')
 ALTER TABLE dbo.fn_workflow_region_frame ADD CONSTRAINT fk_fn_workflow_region_frame_fork FOREIGN KEY (ParentForkActivationId,InstanceId) REFERENCES dbo.fn_workflow_parallel_join(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_region_frame') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流区域代次固定边界及真实外层分支身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_region_frame';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_region_frame') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_region_frame'),N'ExecutionEpochId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'区域实际执行代次UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_region_frame',@level2type=N'COLUMN',@level2name=N'ExecutionEpochId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_region_frame') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_region_frame'),N'InstanceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'区域所属真实根实例',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_region_frame',@level2type=N'COLUMN',@level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_region_frame') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_region_frame'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'从真实根绑定的可信作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_region_frame',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_region_frame') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_region_frame'),N'DefinitionVersionId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'与所属根相同的固定定义版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_region_frame',@level2type=N'COLUMN',@level2name=N'DefinitionVersionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_region_frame') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_region_frame'),N'EntryNodeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'编译验证的区域唯一入口节点引用',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_region_frame',@level2type=N'COLUMN',@level2name=N'EntryNodeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_region_frame') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_region_frame'),N'ExitNodeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'编译验证的区域唯一出口节点引用',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_region_frame',@level2type=N'COLUMN',@level2name=N'ExitNodeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_region_frame') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_region_frame'),N'ParentForkActivationId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'区域外层真实分叉激活；根区域为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_region_frame',@level2type=N'COLUMN',@level2name=N'ParentForkActivationId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_region_frame') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_region_frame'),N'ParentBranchKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'外层分叉固化的实际分支键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_region_frame',@level2type=N'COLUMN',@level2name=N'ParentBranchKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_region_frame') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_region_frame'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'区域帧创建UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_region_frame',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
