-- 328：固定真实路由决策；工作只接受匹配原决策/目标代次/唯一后继身份的跨代次来源。
IF OBJECT_ID(N'dbo.fn_workflow_route_decision',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_route_decision (
  Id uniqueidentifier NOT NULL,
  InstanceId uniqueidentifier NOT NULL,
  TenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ExecutionEpochId uniqueidentifier NOT NULL,
  DefinitionVersionId uniqueidentifier NOT NULL,
  NodeKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
  InputJson nvarchar(max) NOT NULL,
  InputHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  InputFormRevision bigint NOT NULL,
  PolicyHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  StatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
  SelectedTargetKey varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
  NextNodeKey varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
  TargetEpochId uniqueidentifier NULL,
  NextWorkId uniqueidentifier NULL,
  ParallelJoinId uniqueidentifier NULL,
  ParallelBranchKey varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
  TargetParallelJoinId uniqueidentifier NULL,
  TargetParallelBranchKey varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
  Revision bigint NOT NULL,
  CreatedAtUtc datetime2(6) NOT NULL,
  AppliedAtUtc datetime2(6) NULL,
  CONSTRAINT pk_fn_workflow_route_decision PRIMARY KEY CLUSTERED (Id)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流耐久路由固定输入、选择与唯一后继工作身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_decision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_decision'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'真实路由步骤及决策UUID v7身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_decision',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_decision'),N'InstanceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'所属实际根实例',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_decision',@level2type=N'COLUMN',@level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_decision'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'根实例可信作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_decision',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_decision'),N'ExecutionEpochId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际来源执行代次',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_decision',@level2type=N'COLUMN',@level2name=N'ExecutionEpochId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_decision'),N'DefinitionVersionId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原根固定发布版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_decision',@level2type=N'COLUMN',@level2name=N'DefinitionVersionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_decision'),N'NodeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'发布路由节点引用',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_decision',@level2type=N'COLUMN',@level2name=N'NodeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_decision'),N'InputJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原分支或主表单固定输入',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_decision',@level2type=N'COLUMN',@level2name=N'InputJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_decision'),N'InputHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定输入SHA256摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_decision',@level2type=N'COLUMN',@level2name=N'InputHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_decision'),N'InputFormRevision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原输入修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_decision',@level2type=N'COLUMN',@level2name=N'InputFormRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_decision'),N'PolicyHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定发布路由策略SHA256摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_decision',@level2type=N'COLUMN',@level2name=N'PolicyHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_decision'),N'StatusKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'decision_unknown或ready或applied或cancelled',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_decision',@level2type=N'COLUMN',@level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_decision'),N'SelectedTargetKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'已发布目标机器键；正常后继为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_decision',@level2type=N'COLUMN',@level2name=N'SelectedTargetKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_decision'),N'NextNodeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定实际后继节点引用',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_decision',@level2type=N'COLUMN',@level2name=N'NextNodeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_decision'),N'TargetEpochId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'应用决策后实际后继代次',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_decision',@level2type=N'COLUMN',@level2name=N'TargetEpochId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_decision'),N'NextWorkId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'应用事务预留的唯一后继Work身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_decision',@level2type=N'COLUMN',@level2name=N'NextWorkId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_decision'),N'ParallelJoinId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'来源真实分叉身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_decision',@level2type=N'COLUMN',@level2name=N'ParallelJoinId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_decision'),N'ParallelBranchKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'来源真实分支键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_decision',@level2type=N'COLUMN',@level2name=N'ParallelBranchKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_decision'),N'TargetParallelJoinId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'后继恢复的真实分叉身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_decision',@level2type=N'COLUMN',@level2name=N'TargetParallelJoinId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_decision'),N'TargetParallelBranchKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'后继真实分支键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_decision',@level2type=N'COLUMN',@level2name=N'TargetParallelBranchKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_decision'),N'Revision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'决策乐观并发修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_decision',@level2type=N'COLUMN',@level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_decision'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原路由激活UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_decision',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_decision'),N'AppliedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'决策与后继同事务应用UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_decision',@level2type=N'COLUMN',@level2name=N'AppliedAtUtc';
GO
IF OBJECT_ID(N'dbo.fn_workflow_route_resolution',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_route_resolution (
  Id uniqueidentifier NOT NULL,
  RouteId uniqueidentifier NOT NULL,
  InstanceId uniqueidentifier NOT NULL,
  TenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ActorUserId uniqueidentifier NOT NULL,
  IdempotencyKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
  RequestHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  Reason varchar(512) COLLATE Latin1_General_100_BIN2 NOT NULL,
  CreatedAtUtc datetime2(6) NOT NULL,
  CONSTRAINT pk_fn_workflow_route_resolution PRIMARY KEY CLUSTERED (Id)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_resolution') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流不确定路由的精确授权修复幂等收据',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_resolution';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_resolution') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_resolution'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'修复收据UUID v7身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_resolution',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_resolution') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_resolution'),N'RouteId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原真实路由决策身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_resolution',@level2type=N'COLUMN',@level2name=N'RouteId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_resolution') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_resolution'),N'InstanceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'所属实际根实例',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_resolution',@level2type=N'COLUMN',@level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_resolution') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_resolution'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'根实例可信作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_resolution',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_resolution') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_resolution'),N'ActorUserId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际授权修复操作者；不跨模块外键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_resolution',@level2type=N'COLUMN',@level2name=N'ActorUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_resolution') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_resolution'),N'IdempotencyKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'操作者修复幂等键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_resolution',@level2type=N'COLUMN',@level2name=N'IdempotencyKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_resolution') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_resolution'),N'RequestHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'目标及原因固定请求摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_resolution',@level2type=N'COLUMN',@level2name=N'RequestHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_resolution') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_resolution'),N'Reason','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际修复理由',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_resolution',@level2type=N'COLUMN',@level2name=N'Reason';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_route_resolution') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_route_resolution'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'首次修复UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_route_resolution',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF COL_LENGTH('dbo.fn_workflow_activity_work','RouteDecisionId') IS NULL
 ALTER TABLE dbo.fn_workflow_activity_work ADD RouteDecisionId uniqueidentifier NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'),N'RouteDecisionId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际路由应用收据；普通工作为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_activity_work',@level2type=N'COLUMN',@level2name=N'RouteDecisionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND name=N'ux_fn_workflow_route_decision_instance')
 CREATE UNIQUE INDEX ux_fn_workflow_route_decision_instance ON dbo.fn_workflow_route_decision(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND name=N'ux_fn_workflow_route_decision_work')
 CREATE UNIQUE INDEX ux_fn_workflow_route_decision_work ON dbo.fn_workflow_route_decision(NextWorkId) WHERE NextWorkId IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_route_resolution') AND name=N'ux_fn_workflow_route_resolution_receipt')
 CREATE UNIQUE INDEX ux_fn_workflow_route_resolution_receipt ON dbo.fn_workflow_route_resolution(RouteId,ActorUserId,IdempotencyKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND name=N'fk_fn_workflow_route_decision_step')
 ALTER TABLE dbo.fn_workflow_route_decision ADD CONSTRAINT fk_fn_workflow_route_decision_step FOREIGN KEY (Id,InstanceId) REFERENCES dbo.fn_workflow_step(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND name=N'fk_fn_workflow_route_decision_source_epoch')
 ALTER TABLE dbo.fn_workflow_route_decision ADD CONSTRAINT fk_fn_workflow_route_decision_source_epoch FOREIGN KEY (ExecutionEpochId,InstanceId) REFERENCES dbo.fn_workflow_execution_epoch(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND name=N'fk_fn_workflow_route_decision_target_epoch')
 ALTER TABLE dbo.fn_workflow_route_decision ADD CONSTRAINT fk_fn_workflow_route_decision_target_epoch FOREIGN KEY (TargetEpochId,InstanceId) REFERENCES dbo.fn_workflow_execution_epoch(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND name=N'fk_fn_workflow_route_decision_version')
 ALTER TABLE dbo.fn_workflow_route_decision ADD CONSTRAINT fk_fn_workflow_route_decision_version FOREIGN KEY (InstanceId,DefinitionVersionId) REFERENCES dbo.fn_workflow_instance(Id,DefinitionVersionId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND name=N'fk_fn_workflow_route_decision_source_fork')
 ALTER TABLE dbo.fn_workflow_route_decision ADD CONSTRAINT fk_fn_workflow_route_decision_source_fork FOREIGN KEY (ParallelJoinId,InstanceId) REFERENCES dbo.fn_workflow_parallel_join(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND name=N'fk_fn_workflow_route_decision_target_fork')
 ALTER TABLE dbo.fn_workflow_route_decision ADD CONSTRAINT fk_fn_workflow_route_decision_target_fork FOREIGN KEY (TargetParallelJoinId,InstanceId) REFERENCES dbo.fn_workflow_parallel_join(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_route_resolution') AND name=N'fk_fn_workflow_route_resolution_route')
 ALTER TABLE dbo.fn_workflow_route_resolution ADD CONSTRAINT fk_fn_workflow_route_resolution_route FOREIGN KEY (RouteId,InstanceId) REFERENCES dbo.fn_workflow_route_decision(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_activity_work') AND name=N'fk_fn_workflow_activity_work_route')
 ALTER TABLE dbo.fn_workflow_activity_work ADD CONSTRAINT fk_fn_workflow_activity_work_route FOREIGN KEY (RouteDecisionId,InstanceId) REFERENCES dbo.fn_workflow_route_decision(Id,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_route_decision') AND name=N'ck_fn_workflow_route_decision_state')
 ALTER TABLE dbo.fn_workflow_route_decision ADD CONSTRAINT ck_fn_workflow_route_decision_state CHECK (Revision>0 AND InputFormRevision>0 AND StatusKey IN ('decision_unknown','ready','applied','cancelled') AND ((ParallelJoinId IS NULL AND ParallelBranchKey IS NULL) OR (ParallelJoinId IS NOT NULL AND ParallelBranchKey IS NOT NULL)) AND ((TargetParallelJoinId IS NULL AND TargetParallelBranchKey IS NULL) OR (TargetParallelJoinId IS NOT NULL AND TargetParallelBranchKey IS NOT NULL)) AND (StatusKey<>'decision_unknown' OR (SelectedTargetKey IS NULL AND NextNodeKey IS NULL AND TargetEpochId IS NULL AND NextWorkId IS NULL AND AppliedAtUtc IS NULL)) AND (StatusKey NOT IN ('ready','applied') OR (NextNodeKey IS NOT NULL AND NextWorkId IS NOT NULL)) AND (StatusKey<>'applied' OR (TargetEpochId IS NOT NULL AND AppliedAtUtc IS NOT NULL)));
GO
