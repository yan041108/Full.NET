-- 329：退回冻结原目标并新建代次；只有原入/出工作收据可以授权跨代次来源。
IF OBJECT_ID(N'dbo.fn_workflow_return_round',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_return_round (
 Id uniqueidentifier NOT NULL,
 InstanceId uniqueidentifier NOT NULL,
 TenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 DefinitionVersionId uniqueidentifier NOT NULL,
 ParentEpochId uniqueidentifier NOT NULL,
 EntrySourceEpochId uniqueidentifier NOT NULL,
 SourceStepId uniqueidentifier NOT NULL,
 TargetStepId uniqueidentifier NOT NULL,
 EntryNodeKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
 ExitNodeKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
 NextNodeKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
 RegionNodesJson nvarchar(max) NOT NULL,
 RegionHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 TargetSnapshotHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 SourceParallelJoinId uniqueidentifier NULL,
 SourceParallelBranchKey varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
 ParallelJoinId uniqueidentifier NULL,
 ParallelBranchKey varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
 EntryWorkId uniqueidentifier NOT NULL,
 InputHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 InputFormRevision bigint NOT NULL,
 ExitWorkId uniqueidentifier NULL,
 ExitSourceEpochId uniqueidentifier NULL,
 ExitStepId uniqueidentifier NULL,
 ExitInputHash varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
 ExitInputFormRevision bigint NULL,
 Revision bigint NOT NULL,
 CreatedAtUtc datetime2(6) NOT NULL,
 ExitedAtUtc datetime2(6) NULL,
 CONSTRAINT pk_fn_workflow_return_round PRIMARY KEY CLUSTERED (Id)
 );
END;
GO
-- 已建表但未记账的旧半批迁移只从真实来源步骤回填，不猜父代次。
IF COL_LENGTH(N'dbo.fn_workflow_return_round',N'EntrySourceEpochId') IS NULL
 ALTER TABLE dbo.fn_workflow_return_round ADD EntrySourceEpochId uniqueidentifier NULL;
GO
UPDATE dbo.fn_workflow_return_round SET EntrySourceEpochId=(SELECT step.ExecutionEpochId FROM dbo.fn_workflow_step AS step WHERE step.Id=fn_workflow_return_round.SourceStepId AND step.InstanceId=fn_workflow_return_round.InstanceId)
 WHERE EntrySourceEpochId IS NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND name=N'EntrySourceEpochId' AND is_nullable=1)
 ALTER TABLE dbo.fn_workflow_return_round ALTER COLUMN EntrySourceEpochId uniqueidentifier NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'EntrySourceEpochId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原退回操作来源的真实代次；可随旧fork关闭，与存活父代次分离',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'EntrySourceEpochId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流退回新代次及不可变目标、区域和唯一入出工作收据',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'退回轮次与新子代次UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'InstanceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'所属真实根实例',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'根实例可信作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'DefinitionVersionId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'根固定定义版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'DefinitionVersionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'ParentEpochId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'退回进入前的实际外层代次',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'ParentEpochId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'SourceStepId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原已退回来源步骤',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'SourceStepId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'TargetStepId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原已完成目标及其不可变人员快照',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'TargetStepId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'EntryNodeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定重入入口节点引用',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'EntryNodeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'ExitNodeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定本轮唯一正常出口',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'ExitNodeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'NextNodeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'正常出口后的真实后继引用',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'NextNodeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'RegionNodesJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'有界结构区域固定节点集合',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'RegionNodesJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'RegionHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'结构区域节点集合SHA256摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'RegionHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'TargetSnapshotHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原目标人员及分组快照SHA256摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'TargetSnapshotHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'ParallelJoinId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本轮真实外层分叉身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'ParallelJoinId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'ParallelBranchKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本轮真实外层分支键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'ParallelBranchKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'EntryWorkId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'进入新代次的唯一工作身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'EntryWorkId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'InputHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原进入工作固定输入SHA256摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'InputHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'InputFormRevision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原进入工作固定输入修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'InputFormRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'ExitWorkId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'退出到外层代次的唯一工作身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'ExitWorkId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'ExitStepId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本轮真实完成出口步骤',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'ExitStepId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'ExitInputHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'退出工作固定输入SHA256摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'ExitInputHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'ExitInputFormRevision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'退出工作固定输入修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'ExitInputFormRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'Revision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'轮次唯一退出CAS修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'进入本轮UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'ExitedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本轮正常退出UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'ExitedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'SourceParallelJoinId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原已退回来源步骤的真实分叉身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'SourceParallelJoinId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'SourceParallelBranchKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原已退回来源步骤的真实分支键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'SourceParallelBranchKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_return_round'),N'ExitSourceEpochId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'真实完成出口来源代次；级联退出保留原子代次',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_return_round',@level2type=N'COLUMN',@level2name=N'ExitSourceEpochId';
GO
IF COL_LENGTH(N'dbo.fn_workflow_activity_work',N'ReturnRoundId') IS NULL
 ALTER TABLE dbo.fn_workflow_activity_work ADD ReturnRoundId uniqueidentifier NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'),N'ReturnRoundId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原退回轮次入出收据；普通工作为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activity_work',@level2type=N'COLUMN',@level2name=N'ReturnRoundId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND name=N'ux_fn_workflow_return_round_instance')
 CREATE UNIQUE INDEX ux_fn_workflow_return_round_instance ON dbo.fn_workflow_return_round(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND name=N'ux_fn_workflow_return_round_entry_work')
 CREATE UNIQUE INDEX ux_fn_workflow_return_round_entry_work ON dbo.fn_workflow_return_round(EntryWorkId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_return_round') AND name=N'ux_fn_workflow_return_round_exit_work')
 CREATE UNIQUE INDEX ux_fn_workflow_return_round_exit_work ON dbo.fn_workflow_return_round(ExitWorkId) WHERE ExitWorkId IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'fk_fn_workflow_return_round_epoch')
 ALTER TABLE dbo.fn_workflow_return_round ADD CONSTRAINT fk_fn_workflow_return_round_epoch FOREIGN KEY (Id,InstanceId) REFERENCES dbo.fn_workflow_execution_epoch(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'fk_fn_workflow_return_round_parent_epoch')
 ALTER TABLE dbo.fn_workflow_return_round ADD CONSTRAINT fk_fn_workflow_return_round_parent_epoch FOREIGN KEY (ParentEpochId,InstanceId) REFERENCES dbo.fn_workflow_execution_epoch(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'fk_fn_workflow_return_round_source')
 ALTER TABLE dbo.fn_workflow_return_round ADD CONSTRAINT fk_fn_workflow_return_round_source FOREIGN KEY (SourceStepId,InstanceId) REFERENCES dbo.fn_workflow_step(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'fk_fn_workflow_return_round_target')
 ALTER TABLE dbo.fn_workflow_return_round ADD CONSTRAINT fk_fn_workflow_return_round_target FOREIGN KEY (TargetStepId,InstanceId) REFERENCES dbo.fn_workflow_step(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'fk_fn_workflow_return_round_exit')
 ALTER TABLE dbo.fn_workflow_return_round ADD CONSTRAINT fk_fn_workflow_return_round_exit FOREIGN KEY (ExitStepId,InstanceId) REFERENCES dbo.fn_workflow_step(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'fk_fn_workflow_return_round_version')
 ALTER TABLE dbo.fn_workflow_return_round ADD CONSTRAINT fk_fn_workflow_return_round_version FOREIGN KEY (InstanceId,DefinitionVersionId) REFERENCES dbo.fn_workflow_instance(Id,DefinitionVersionId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'fk_fn_workflow_return_round_fork')
 ALTER TABLE dbo.fn_workflow_return_round ADD CONSTRAINT fk_fn_workflow_return_round_fork FOREIGN KEY (ParallelJoinId,InstanceId) REFERENCES dbo.fn_workflow_parallel_join(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'fk_fn_workflow_activity_work_return')
 ALTER TABLE dbo.fn_workflow_activity_work ADD CONSTRAINT fk_fn_workflow_activity_work_return FOREIGN KEY (ReturnRoundId,InstanceId) REFERENCES dbo.fn_workflow_return_round(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'ck_fn_workflow_return_round_state')
 ALTER TABLE dbo.fn_workflow_return_round ADD CONSTRAINT ck_fn_workflow_return_round_state CHECK (((SourceParallelJoinId IS NULL AND SourceParallelBranchKey IS NULL) OR (SourceParallelJoinId IS NOT NULL AND SourceParallelBranchKey IS NOT NULL)) AND Revision IN (1,2) AND InputFormRevision>0 AND EntryNodeKey<>ExitNodeKey AND ((ParallelJoinId IS NULL AND ParallelBranchKey IS NULL) OR (ParallelJoinId IS NOT NULL AND ParallelBranchKey IS NOT NULL)) AND ((Revision=1 AND ExitWorkId IS NULL AND ExitStepId IS NULL AND ExitSourceEpochId IS NULL AND ExitInputHash IS NULL AND ExitInputFormRevision IS NULL AND ExitedAtUtc IS NULL) OR (Revision=2 AND ExitWorkId IS NOT NULL AND ExitStepId IS NOT NULL AND ExitSourceEpochId IS NOT NULL AND ExitInputHash IS NOT NULL AND ExitInputFormRevision>0 AND ExitedAtUtc IS NOT NULL AND ExitWorkId<>EntryWorkId)));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'ck_fn_workflow_activity_work_receipt')
 ALTER TABLE dbo.fn_workflow_activity_work ADD CONSTRAINT ck_fn_workflow_activity_work_receipt CHECK ((CASE WHEN ReturnRoundId IS NULL THEN 0 ELSE 1 END + CASE WHEN RouteDecisionId IS NULL THEN 0 ELSE 1 END + CASE WHEN ResumeBarrierId IS NULL THEN 0 ELSE 1 END)<=1);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'fk_fn_workflow_return_round_source_fork')
 ALTER TABLE dbo.fn_workflow_return_round ADD CONSTRAINT fk_fn_workflow_return_round_source_fork FOREIGN KEY (SourceParallelJoinId,InstanceId) REFERENCES dbo.fn_workflow_parallel_join(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'fk_fn_workflow_return_round_exit_epoch')
 ALTER TABLE dbo.fn_workflow_return_round ADD CONSTRAINT fk_fn_workflow_return_round_exit_epoch FOREIGN KEY (ExitSourceEpochId,InstanceId) REFERENCES dbo.fn_workflow_execution_epoch(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'fk_fn_workflow_return_round_entry_epoch')
 ALTER TABLE dbo.fn_workflow_return_round ADD CONSTRAINT fk_fn_workflow_return_round_entry_epoch FOREIGN KEY (EntrySourceEpochId,InstanceId) REFERENCES dbo.fn_workflow_execution_epoch(Id,InstanceId);
GO
