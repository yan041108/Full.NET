-- 323：真实执行代次；初始回填仅补不存在的根事实，不覆盖既有代次。
IF OBJECT_ID(N'dbo.fn_workflow_execution_epoch',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_execution_epoch (
  Id uniqueidentifier NOT NULL,
  InstanceId uniqueidentifier NOT NULL,
  TenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ParentEpochId uniqueidentifier NULL,
  CauseStepId uniqueidentifier NULL,
  RegionKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  IterationNumber int NOT NULL,
  Depth int NOT NULL,
  StatusKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
  Revision bigint NOT NULL,
  CreatedAtUtc datetime2(6) NOT NULL,
  ClosedAtUtc datetime2(6) NULL,
  CONSTRAINT pk_fn_workflow_execution_epoch PRIMARY KEY CLUSTERED (Id)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_execution_epoch') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流真实执行代次与区域因果身份；租约不能替代代次',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_execution_epoch';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_execution_epoch') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_execution_epoch'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'执行代次UUID v7；初始代次复用根UUID',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_execution_epoch',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_execution_epoch') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_execution_epoch'),N'InstanceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际根实例身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_execution_epoch',@level2type=N'COLUMN',@level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_execution_epoch') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_execution_epoch'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'根实例可信作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_execution_epoch',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_execution_epoch') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_execution_epoch'),N'ParentEpochId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'外层真实代次；初始代次为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_execution_epoch',@level2type=N'COLUMN',@level2name=N'ParentEpochId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_execution_epoch') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_execution_epoch'),N'CauseStepId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'产生新代次的实际因果步骤',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_execution_epoch',@level2type=N'COLUMN',@level2name=N'CauseStepId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_execution_epoch') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_execution_epoch'),N'RegionKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'编译固定的执行区域键；初始为root',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_execution_epoch',@level2type=N'COLUMN',@level2name=N'RegionKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_execution_epoch') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_execution_epoch'),N'IterationNumber','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'区域内固化迭代序号；初始为零',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_execution_epoch',@level2type=N'COLUMN',@level2name=N'IterationNumber';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_execution_epoch') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_execution_epoch'),N'Depth','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'有界嵌套深度；初始为零',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_execution_epoch',@level2type=N'COLUMN',@level2name=N'Depth';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_execution_epoch') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_execution_epoch'),N'StatusKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'活动或已关闭；暂停不关闭',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_execution_epoch',@level2type=N'COLUMN',@level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_execution_epoch') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_execution_epoch'),N'Revision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'乐观并发修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_execution_epoch',@level2type=N'COLUMN',@level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_execution_epoch') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_execution_epoch'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'代次创建UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_execution_epoch',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_execution_epoch') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_execution_epoch'),N'ClosedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'代次关闭UTC时间；存量缺时间时为迁移观察时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_execution_epoch',@level2type=N'COLUMN',@level2name=N'ClosedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_execution_epoch') AND name=N'ix_fn_workflow_execution_epoch_instance')
 CREATE INDEX ix_fn_workflow_execution_epoch_instance ON dbo.fn_workflow_execution_epoch(InstanceId,TenantScopeKey,StatusKey,Id);
GO
-- 先建立同根候选键，再添加父代次与因果来源的复合外键。
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_execution_epoch') AND name=N'ux_fn_workflow_execution_epoch_instance')
 CREATE UNIQUE INDEX ux_fn_workflow_execution_epoch_instance ON dbo.fn_workflow_execution_epoch(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_step') AND name=N'ux_fn_workflow_step_instance')
 CREATE UNIQUE INDEX ux_fn_workflow_step_instance ON dbo.fn_workflow_step(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_execution_epoch') AND name=N'ck_fn_workflow_execution_epoch_state')
 ALTER TABLE dbo.fn_workflow_execution_epoch ADD CONSTRAINT ck_fn_workflow_execution_epoch_state CHECK (Revision>0 AND ((StatusKey='active' AND ClosedAtUtc IS NULL) OR (StatusKey='closed' AND ClosedAtUtc IS NOT NULL)) AND ((Id=InstanceId AND ParentEpochId IS NULL AND CauseStepId IS NULL AND RegionKey='root' AND IterationNumber=0 AND Depth=0) OR (Id<>InstanceId AND ParentEpochId IS NOT NULL AND CauseStepId IS NOT NULL AND IterationNumber BETWEEN 1 AND 32 AND Depth BETWEEN 1 AND 8)));
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_execution_epoch') AND name=N'fk_fn_workflow_execution_epoch_instance')
 ALTER TABLE dbo.fn_workflow_execution_epoch ADD CONSTRAINT fk_fn_workflow_execution_epoch_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_execution_epoch') AND name=N'fk_fn_workflow_execution_epoch_parent')
 ALTER TABLE dbo.fn_workflow_execution_epoch ADD CONSTRAINT fk_fn_workflow_execution_epoch_parent FOREIGN KEY (ParentEpochId,InstanceId) REFERENCES fn_workflow_execution_epoch(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_execution_epoch') AND name=N'fk_fn_workflow_execution_epoch_cause')
 ALTER TABLE dbo.fn_workflow_execution_epoch ADD CONSTRAINT fk_fn_workflow_execution_epoch_cause FOREIGN KEY (CauseStepId,InstanceId) REFERENCES fn_workflow_step(Id,InstanceId);
GO
INSERT INTO fn_workflow_execution_epoch (Id,InstanceId,TenantScopeKey,ParentEpochId,CauseStepId,RegionKey,IterationNumber,Depth,StatusKey,Revision,CreatedAtUtc,ClosedAtUtc)
SELECT source.Id,source.Id,source.TenantScopeKey,NULL,NULL,'root',0,0,
 CASE WHEN source.StatusKey IN ('active','suspended') THEN 'active' ELSE 'closed' END,1,source.StartedAtUtc,
 CASE WHEN source.StatusKey IN ('active','suspended') THEN NULL ELSE COALESCE(source.CompletedAtUtc,source.CancelledAtUtc,SYSUTCDATETIME()) END
FROM fn_workflow_instance AS source WHERE NOT EXISTS (SELECT 1 FROM fn_workflow_execution_epoch AS existing WHERE existing.Id=source.Id);
GO

-- 活动与耐久工作必须持有同一实例的实际代次，部分重跑只补空值。
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_execution_epoch') AND name=N'ux_fn_workflow_execution_epoch_instance')
 CREATE UNIQUE INDEX ux_fn_workflow_execution_epoch_instance ON dbo.fn_workflow_execution_epoch(Id,InstanceId);
GO
IF COL_LENGTH(N'dbo.fn_workflow_step',N'ExecutionEpochId') IS NULL
 ALTER TABLE dbo.fn_workflow_step ADD ExecutionEpochId uniqueidentifier NULL;
GO
UPDATE dbo.fn_workflow_step SET ExecutionEpochId=InstanceId WHERE ExecutionEpochId IS NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_step') AND name=N'ExecutionEpochId' AND is_nullable=1)
 ALTER TABLE dbo.fn_workflow_step ALTER COLUMN ExecutionEpochId uniqueidentifier NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_step') AND name=N'fk_fn_workflow_step_epoch')
 ALTER TABLE dbo.fn_workflow_step ADD CONSTRAINT fk_fn_workflow_step_epoch FOREIGN KEY (ExecutionEpochId,InstanceId) REFERENCES dbo.fn_workflow_execution_epoch(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_step') AND name=N'ix_fn_workflow_step_epoch')
 CREATE INDEX ix_fn_workflow_step_epoch ON dbo.fn_workflow_step(InstanceId,ExecutionEpochId,Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_step') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step'),N'ExecutionEpochId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'真实执行代次；续办沿来源传递，重入不能复用旧同名活动',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_step',@level2type=N'COLUMN',@level2name=N'ExecutionEpochId';
GO
IF COL_LENGTH(N'dbo.fn_workflow_activity_work',N'ExecutionEpochId') IS NULL
 ALTER TABLE dbo.fn_workflow_activity_work ADD ExecutionEpochId uniqueidentifier NULL;
GO
UPDATE dbo.fn_workflow_activity_work SET ExecutionEpochId=InstanceId WHERE ExecutionEpochId IS NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_activity_work') AND name=N'ExecutionEpochId' AND is_nullable=1)
 ALTER TABLE dbo.fn_workflow_activity_work ALTER COLUMN ExecutionEpochId uniqueidentifier NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_activity_work') AND name=N'fk_fn_workflow_activity_work_epoch')
 ALTER TABLE dbo.fn_workflow_activity_work ADD CONSTRAINT fk_fn_workflow_activity_work_epoch FOREIGN KEY (ExecutionEpochId,InstanceId) REFERENCES dbo.fn_workflow_execution_epoch(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_activity_work') AND name=N'ix_fn_workflow_activity_work_epoch')
 CREATE INDEX ix_fn_workflow_activity_work_epoch ON dbo.fn_workflow_activity_work(InstanceId,ExecutionEpochId,Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'),N'ExecutionEpochId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'真实执行代次；续办沿来源传递，重入不能复用旧同名活动',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activity_work',@level2type=N'COLUMN',@level2name=N'ExecutionEpochId';
GO

-- 子调用的节点幂等只属于实际代次；旧全根节点唯一键不能限制下一轮。
IF COL_LENGTH(N'dbo.fn_workflow_subprocess_call',N'ExecutionEpochId') IS NULL
 ALTER TABLE dbo.fn_workflow_subprocess_call ADD ExecutionEpochId uniqueidentifier NULL;
GO
UPDATE dbo.fn_workflow_subprocess_call SET ExecutionEpochId=step.ExecutionEpochId
FROM dbo.fn_workflow_subprocess_call INNER JOIN dbo.fn_workflow_step AS step
 ON step.Id=fn_workflow_subprocess_call.Id AND step.InstanceId=fn_workflow_subprocess_call.InstanceId
WHERE fn_workflow_subprocess_call.ExecutionEpochId IS NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND name=N'ExecutionEpochId' AND is_nullable=1)
 ALTER TABLE dbo.fn_workflow_subprocess_call ALTER COLUMN ExecutionEpochId uniqueidentifier NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_step') AND name=N'ux_fn_workflow_step_epoch')
 CREATE UNIQUE INDEX ux_fn_workflow_step_epoch ON dbo.fn_workflow_step(Id,ExecutionEpochId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND name=N'fk_fn_workflow_subprocess_call_epoch')
 ALTER TABLE dbo.fn_workflow_subprocess_call ADD CONSTRAINT fk_fn_workflow_subprocess_call_epoch FOREIGN KEY (Id,ExecutionEpochId) REFERENCES dbo.fn_workflow_step(Id,ExecutionEpochId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND name=N'ux_fn_workflow_subprocess_call_epoch_node')
 CREATE UNIQUE INDEX ux_fn_workflow_subprocess_call_epoch_node ON dbo.fn_workflow_subprocess_call(InstanceId,ExecutionEpochId,NodeKey);
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND name=N'ux_fn_workflow_subprocess_call_node')
 DROP INDEX ux_fn_workflow_subprocess_call_node ON dbo.fn_workflow_subprocess_call;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_call'),N'ExecutionEpochId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'真实父活动代次；同名节点只有同代次才能复用调用',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_call',@level2type=N'COLUMN',@level2name=N'ExecutionEpochId';
GO
