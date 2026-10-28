-- 327：实际活动因果边及耐久工作、分叉和到达来源；不以时间或序号补猜旧来源。
IF COL_LENGTH('dbo.fn_workflow_activity_work','SourceStepId') IS NULL
 ALTER TABLE dbo.fn_workflow_activity_work ADD SourceStepId uniqueidentifier NULL;
GO
IF COL_LENGTH('dbo.fn_workflow_parallel_join','ForkStepId') IS NULL
 ALTER TABLE dbo.fn_workflow_parallel_join ADD ForkStepId uniqueidentifier NULL;
GO
IF COL_LENGTH('dbo.fn_workflow_parallel_branch_arrival','SourceStepId') IS NULL
 ALTER TABLE dbo.fn_workflow_parallel_branch_arrival ADD SourceStepId uniqueidentifier NULL;
GO
IF COL_LENGTH('dbo.fn_workflow_parallel_branch_arrival','InstanceId') IS NULL
 ALTER TABLE dbo.fn_workflow_parallel_branch_arrival ADD InstanceId uniqueidentifier NULL;
GO
UPDATE dbo.fn_workflow_parallel_branch_arrival SET InstanceId=(SELECT activation.InstanceId FROM dbo.fn_workflow_parallel_join AS activation WHERE activation.Id=fn_workflow_parallel_branch_arrival.ParallelJoinId) WHERE InstanceId IS NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_parallel_branch_arrival') AND name=N'InstanceId' AND is_nullable=1)
 ALTER TABLE dbo.fn_workflow_parallel_branch_arrival ALTER COLUMN InstanceId uniqueidentifier NOT NULL;
GO
IF OBJECT_ID(N'dbo.fn_workflow_step_cause',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_step_cause (
  Id uniqueidentifier NOT NULL,
  InstanceId uniqueidentifier NOT NULL,
  TenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  StepId uniqueidentifier NOT NULL,
  ParentStepId uniqueidentifier NOT NULL,
  KindKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
  CreatedAtUtc datetime2(6) NOT NULL,
  CONSTRAINT pk_fn_workflow_step_cause PRIMARY KEY CLUSTERED (Id)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_step_cause') AND name=N'ux_fn_workflow_step_cause_edge')
 CREATE UNIQUE INDEX ux_fn_workflow_step_cause_edge ON dbo.fn_workflow_step_cause(StepId,ParentStepId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_step_cause') AND name=N'fk_fn_workflow_step_cause_child')
 ALTER TABLE dbo.fn_workflow_step_cause ADD CONSTRAINT fk_fn_workflow_step_cause_child FOREIGN KEY (StepId,InstanceId) REFERENCES dbo.fn_workflow_step(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_step_cause') AND name=N'fk_fn_workflow_step_cause_parent')
 ALTER TABLE dbo.fn_workflow_step_cause ADD CONSTRAINT fk_fn_workflow_step_cause_parent FOREIGN KEY (ParentStepId,InstanceId) REFERENCES dbo.fn_workflow_step(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_activity_work') AND name=N'fk_fn_workflow_activity_work_source')
 ALTER TABLE dbo.fn_workflow_activity_work ADD CONSTRAINT fk_fn_workflow_activity_work_source FOREIGN KEY (SourceStepId,InstanceId) REFERENCES dbo.fn_workflow_step(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_parallel_join') AND name=N'fk_fn_workflow_parallel_join_fork_step')
 ALTER TABLE dbo.fn_workflow_parallel_join ADD CONSTRAINT fk_fn_workflow_parallel_join_fork_step FOREIGN KEY (ForkStepId,InstanceId) REFERENCES dbo.fn_workflow_step(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_parallel_branch_arrival') AND name=N'fk_fn_workflow_parallel_branch_arrival_source')
 ALTER TABLE dbo.fn_workflow_parallel_branch_arrival ADD CONSTRAINT fk_fn_workflow_parallel_branch_arrival_source FOREIGN KEY (SourceStepId,InstanceId) REFERENCES dbo.fn_workflow_step(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_parallel_branch_arrival') AND name=N'fk_fn_workflow_parallel_branch_arrival_root_join')
 ALTER TABLE dbo.fn_workflow_parallel_branch_arrival ADD CONSTRAINT fk_fn_workflow_parallel_branch_arrival_root_join FOREIGN KEY (ParallelJoinId,InstanceId) REFERENCES dbo.fn_workflow_parallel_join(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_step_cause') AND name=N'ck_fn_workflow_step_cause_kind')
 ALTER TABLE dbo.fn_workflow_step_cause ADD CONSTRAINT ck_fn_workflow_step_cause_kind CHECK (StepId<>ParentStepId AND KindKey IN ('sequence','fork','join','region','return'));
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_step_cause') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流不可变实际步骤因果事实',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_step_cause';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_step_cause') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step_cause'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'因果边UUID v7身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_step_cause',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_step_cause') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step_cause'),N'InstanceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'所属实际根实例',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_step_cause',@level2type=N'COLUMN',@level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_step_cause') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step_cause'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'根实例可信作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_step_cause',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_step_cause') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step_cause'),N'StepId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本次新建实际步骤',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_step_cause',@level2type=N'COLUMN',@level2name=N'StepId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_step_cause') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step_cause'),N'ParentStepId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'明确因果父步骤；不得按时间推断',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_step_cause',@level2type=N'COLUMN',@level2name=N'ParentStepId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_step_cause') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step_cause'),N'KindKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'顺序分叉汇合区域或退回因果类型',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_step_cause',@level2type=N'COLUMN',@level2name=N'KindKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_step_cause') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step_cause'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'因果事实创建UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_step_cause',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'),N'SourceStepId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'续办输入的真实来源步骤；旧记录缺少证明时为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activity_work',@level2type=N'COLUMN',@level2name=N'SourceStepId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_parallel_join') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_parallel_join'),N'ForkStepId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'该次实际分叉步骤；旧记录缺少证明时为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_parallel_join',@level2type=N'COLUMN',@level2name=N'ForkStepId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_parallel_branch_arrival') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_parallel_branch_arrival'),N'SourceStepId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'该次分支到达的真实来源步骤；旧记录缺少证明时为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_parallel_branch_arrival',@level2type=N'COLUMN',@level2name=N'SourceStepId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_parallel_branch_arrival') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_parallel_branch_arrival'),N'InstanceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'从真实分叉回填的根实例身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_parallel_branch_arrival',@level2type=N'COLUMN',@level2name=N'InstanceId';
GO

IF COL_LENGTH('dbo.fn_workflow_activity_work','CauseKindKey') IS NULL
 ALTER TABLE dbo.fn_workflow_activity_work ADD CauseKindKey varchar(32) COLLATE Latin1_General_100_BIN2 NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_activity_work') AND name=N'ck_fn_workflow_activity_work_cause_kind')
 ALTER TABLE dbo.fn_workflow_activity_work ADD CONSTRAINT ck_fn_workflow_activity_work_cause_kind CHECK (CauseKindKey IS NULL OR CauseKindKey IN ('sequence','fork','join','region','return'));
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'),N'CauseKindKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'显式推进因果类型；旧工作未知时为空，不按来源状态推断',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activity_work',@level2type=N'COLUMN',@level2name=N'CauseKindKey';
GO
