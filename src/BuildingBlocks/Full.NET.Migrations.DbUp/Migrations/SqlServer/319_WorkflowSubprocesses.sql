-- 319：工作流固定子版本的真实父活动等待及预算预留；结构探测支持DbUp未记账重跑。
IF OBJECT_ID(N'dbo.fn_workflow_subprocess_call',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_subprocess_call (
  Id uniqueidentifier NOT NULL,
  InstanceId uniqueidentifier NOT NULL,
  TenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  NodeKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ContinuationNodeKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ChildDefinitionVersionId uniqueidentifier NOT NULL,
  ChildFormVersionId uniqueidentifier NOT NULL,
  DefinitionHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  FormHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  PolicyJson nvarchar(max) NOT NULL,
  ParentInputJson nvarchar(max) NOT NULL,
  InputFormRevision bigint NOT NULL,
  ParallelJoinId uniqueidentifier NULL,
  ParallelBranchKey varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
  Depth int NOT NULL,
  ChildActivityBudget int NOT NULL,
  StatusKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  CancellationRequested bit NOT NULL,
  ErrorCode varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
  Revision bigint NOT NULL,
  CreatedAtUtc datetime2(6) NOT NULL,
  UpdatedAtUtc datetime2(6) NOT NULL,
  CONSTRAINT pk_fn_workflow_subprocess_call PRIMARY KEY CLUSTERED (Id)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流固定子版本的真实父活动等待及预算预留',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_call';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_call'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'复用真实父步骤的应用端UUID v7标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_call',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_call'),N'InstanceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'拥有该活动的父实例',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_call',@level2type=N'COLUMN',@level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_call'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信父实例作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_call',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_call'),N'NodeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定父节点键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_call',@level2type=N'COLUMN',@level2name=N'NodeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_call'),N'ContinuationNodeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'该次调用的后继节点',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_call',@level2type=N'COLUMN',@level2name=N'ContinuationNodeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_call'),N'ChildDefinitionVersionId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定子定义版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_call',@level2type=N'COLUMN',@level2name=N'ChildDefinitionVersionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_call'),N'ChildFormVersionId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定子表单版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_call',@level2type=N'COLUMN',@level2name=N'ChildFormVersionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_call'),N'DefinitionHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'子定义内容摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_call',@level2type=N'COLUMN',@level2name=N'DefinitionHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_call'),N'FormHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'子表单内容摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_call',@level2type=N'COLUMN',@level2name=N'FormHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_call'),N'PolicyJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定父节点策略及服务端子版本绑定',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_call',@level2type=N'COLUMN',@level2name=N'PolicyJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_call'),N'ParentInputJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'父活动原始输入；只向子实例传输显式映射字段',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_call',@level2type=N'COLUMN',@level2name=N'ParentInputJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_call'),N'InputFormRevision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'父输入表单或分支修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_call',@level2type=N'COLUMN',@level2name=N'InputFormRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_call'),N'ParallelJoinId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原并行分叉身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_call',@level2type=N'COLUMN',@level2name=N'ParallelJoinId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_call'),N'ParallelBranchKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原并行分支键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_call',@level2type=N'COLUMN',@level2name=N'ParallelBranchKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_call'),N'Depth','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'当前子根在整组实例中的深度',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_call',@level2type=N'COLUMN',@level2name=N'Depth';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_call'),N'ChildActivityBudget','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'从父额度固定预留的子活动总额',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_call',@level2type=N'COLUMN',@level2name=N'ChildActivityBudget';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_call'),N'StatusKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'请求、运行、结果等待、完成、故障或取消状态',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_call',@level2type=N'COLUMN',@level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_call'),N'CancellationRequested','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'父活动取消意图',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_call',@level2type=N'COLUMN',@level2name=N'CancellationRequested';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_call'),N'ErrorCode','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'不包含业务快照的稳定故障码',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_call',@level2type=N'COLUMN',@level2name=N'ErrorCode';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_call'),N'Revision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'乐观并发修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_call',@level2type=N'COLUMN',@level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_call'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建UTC微秒时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_call',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_call'),N'UpdatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'更新UTC微秒时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_call',@level2type=N'COLUMN',@level2name=N'UpdatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND name=N'ck_fn_workflow_subprocess_call_state')
 ALTER TABLE dbo.fn_workflow_subprocess_call ADD CONSTRAINT ck_fn_workflow_subprocess_call_state CHECK (InputFormRevision>0 AND Depth BETWEEN 1 AND 8 AND ChildActivityBudget BETWEEN 2 AND 4096 AND Revision>0 AND StatusKey IN ('requested','running','result_ready','applied','faulted','cancelled') AND ((ParallelJoinId IS NULL AND ParallelBranchKey IS NULL) OR (ParallelJoinId IS NOT NULL AND ParallelBranchKey IS NOT NULL)));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND name=N'ck_fn_workflow_subprocess_call_policyjson')
 ALTER TABLE dbo.fn_workflow_subprocess_call ADD CONSTRAINT ck_fn_workflow_subprocess_call_policyjson CHECK (PolicyJson IS NULL OR ISJSON(PolicyJson)=1);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND name=N'ck_fn_workflow_subprocess_call_parentinputjson')
 ALTER TABLE dbo.fn_workflow_subprocess_call ADD CONSTRAINT ck_fn_workflow_subprocess_call_parentinputjson CHECK (ParentInputJson IS NULL OR ISJSON(ParentInputJson)=1);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND name=N'ux_fn_workflow_subprocess_call_node')
 CREATE UNIQUE INDEX ux_fn_workflow_subprocess_call_node ON dbo.fn_workflow_subprocess_call(InstanceId,NodeKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND name=N'ix_fn_workflow_subprocess_call_pending')
 CREATE INDEX ix_fn_workflow_subprocess_call_pending ON dbo.fn_workflow_subprocess_call(StatusKey,UpdatedAtUtc,Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND name=N'fk_fn_workflow_subprocess_call_step')
 ALTER TABLE dbo.fn_workflow_subprocess_call ADD CONSTRAINT fk_fn_workflow_subprocess_call_step FOREIGN KEY (Id) REFERENCES dbo.fn_workflow_step(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND name=N'fk_fn_workflow_subprocess_call_instance')
 ALTER TABLE dbo.fn_workflow_subprocess_call ADD CONSTRAINT fk_fn_workflow_subprocess_call_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND name=N'fk_fn_workflow_subprocess_call_version')
 ALTER TABLE dbo.fn_workflow_subprocess_call ADD CONSTRAINT fk_fn_workflow_subprocess_call_version FOREIGN KEY (ChildDefinitionVersionId) REFERENCES dbo.fn_workflow_definition_version(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_call') AND name=N'fk_fn_workflow_subprocess_call_form')
 ALTER TABLE dbo.fn_workflow_subprocess_call ADD CONSTRAINT fk_fn_workflow_subprocess_call_form FOREIGN KEY (ChildFormVersionId) REFERENCES dbo.fn_workflow_form_version(Id);
GO

-- 319：工作流真实子项输入、原子根关联及不可变结果；结构探测支持DbUp未记账重跑。
IF OBJECT_ID(N'dbo.fn_workflow_subprocess_item',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_subprocess_item (
  Id uniqueidentifier NOT NULL,
  CallId uniqueidentifier NOT NULL,
  InstanceId uniqueidentifier NOT NULL,
  TenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ItemKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  SequenceNo int NOT NULL,
  StarterUserId uniqueidentifier NOT NULL,
  InputJson nvarchar(max) NOT NULL,
  InputHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ChildInstanceId uniqueidentifier NULL,
  StatusKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ResultJson nvarchar(max) NULL,
  ResultHash char(64) COLLATE Latin1_General_100_BIN2 NULL,
  ChildRevision bigint NULL,
  Revision bigint NOT NULL,
  CreatedAtUtc datetime2(6) NOT NULL,
  UpdatedAtUtc datetime2(6) NOT NULL,
  CONSTRAINT pk_fn_workflow_subprocess_item PRIMARY KEY CLUSTERED (Id)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流真实子项输入、原子根关联及不可变结果',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_item';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_item'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'应用端UUID v7子项身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_item',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_item'),N'CallId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'真实父步骤调用',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_item',@level2type=N'COLUMN',@level2name=N'CallId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_item'),N'InstanceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'拥有该项的父实例',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_item',@level2type=N'COLUMN',@level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_item'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信父子同作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_item',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_item'),N'ItemKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定子项机器键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_item',@level2type=N'COLUMN',@level2name=N'ItemKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_item'),N'SequenceNo','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'稳定子项顺序',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_item',@level2type=N'COLUMN',@level2name=N'SequenceNo';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_item'),N'StarterUserId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'激活时固定并验证的子发起人',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_item',@level2type=N'COLUMN',@level2name=N'StarterUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_item'),N'InputJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'已授权且已校验的最小子输入',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_item',@level2type=N'COLUMN',@level2name=N'InputJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_item'),N'InputHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定子输入摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_item',@level2type=N'COLUMN',@level2name=N'InputHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_item'),N'ChildInstanceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'与子根创建同事务保存的关联',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_item',@level2type=N'COLUMN',@level2name=N'ChildInstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_item'),N'StatusKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'待启动、运行、成功、拒绝、取消或未知状态',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_item',@level2type=N'COLUMN',@level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_item'),N'ResultJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'只包含已授权映射的固定子结果',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_item',@level2type=N'COLUMN',@level2name=N'ResultJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_item'),N'ResultHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定子结果摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_item',@level2type=N'COLUMN',@level2name=N'ResultHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_item'),N'ChildRevision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'确认子结果时的真实根修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_item',@level2type=N'COLUMN',@level2name=N'ChildRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_item'),N'Revision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'项乐观并发修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_item',@level2type=N'COLUMN',@level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_item'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建UTC微秒时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_item',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_subprocess_item'),N'UpdatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'更新UTC微秒时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_subprocess_item',@level2type=N'COLUMN',@level2name=N'UpdatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND name=N'ck_fn_workflow_subprocess_item_state')
 ALTER TABLE dbo.fn_workflow_subprocess_item ADD CONSTRAINT ck_fn_workflow_subprocess_item_state CHECK (SequenceNo BETWEEN 1 AND 32 AND Revision>0 AND StatusKey IN ('pending','running','succeeded','rejected','cancelled','unknown') AND (StatusKey<>'pending' OR ChildInstanceId IS NULL) AND (StatusKey NOT IN ('running','succeeded','rejected') OR ChildInstanceId IS NOT NULL) AND ((ResultJson IS NULL AND ResultHash IS NULL) OR (ResultJson IS NOT NULL AND ResultHash IS NOT NULL)) AND (ChildRevision IS NULL OR ChildRevision>0));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND name=N'ck_fn_workflow_subprocess_item_inputjson')
 ALTER TABLE dbo.fn_workflow_subprocess_item ADD CONSTRAINT ck_fn_workflow_subprocess_item_inputjson CHECK (InputJson IS NULL OR ISJSON(InputJson)=1);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND name=N'ck_fn_workflow_subprocess_item_resultjson')
 ALTER TABLE dbo.fn_workflow_subprocess_item ADD CONSTRAINT ck_fn_workflow_subprocess_item_resultjson CHECK (ResultJson IS NULL OR ISJSON(ResultJson)=1);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND name=N'ux_fn_workflow_subprocess_item_sequence')
 CREATE UNIQUE INDEX ux_fn_workflow_subprocess_item_sequence ON dbo.fn_workflow_subprocess_item(CallId,SequenceNo);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND name=N'ux_fn_workflow_subprocess_item_key')
 CREATE UNIQUE INDEX ux_fn_workflow_subprocess_item_key ON dbo.fn_workflow_subprocess_item(CallId,ItemKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND name=N'ux_fn_workflow_subprocess_item_child')
 CREATE UNIQUE INDEX ux_fn_workflow_subprocess_item_child ON dbo.fn_workflow_subprocess_item(ChildInstanceId) WHERE ChildInstanceId IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND name=N'fk_fn_workflow_subprocess_item_call')
 ALTER TABLE dbo.fn_workflow_subprocess_item ADD CONSTRAINT fk_fn_workflow_subprocess_item_call FOREIGN KEY (CallId) REFERENCES dbo.fn_workflow_subprocess_call(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND name=N'fk_fn_workflow_subprocess_item_instance')
 ALTER TABLE dbo.fn_workflow_subprocess_item ADD CONSTRAINT fk_fn_workflow_subprocess_item_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_subprocess_item') AND name=N'fk_fn_workflow_subprocess_item_child')
 ALTER TABLE dbo.fn_workflow_subprocess_item ADD CONSTRAINT fk_fn_workflow_subprocess_item_child FOREIGN KEY (ChildInstanceId) REFERENCES dbo.fn_workflow_instance(Id);
GO

-- 319：工作流根及子根的层级活动额度；子额度不重复分配；结构探测支持DbUp未记账重跑。
IF OBJECT_ID(N'dbo.fn_workflow_exec_budget',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_exec_budget (
  InstanceId uniqueidentifier NOT NULL,
  Granted int NOT NULL,
  Reserved int NOT NULL,
  Revision bigint NOT NULL,
  CONSTRAINT pk_fn_workflow_exec_budget PRIMARY KEY CLUSTERED (InstanceId)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_exec_budget') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流根及子根的层级活动额度；子额度不重复分配',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_exec_budget';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_exec_budget') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_exec_budget'),N'InstanceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'复用应用端UUID v7实例身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_exec_budget',@level2type=N'COLUMN',@level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_exec_budget') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_exec_budget'),N'Granted','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'由父项预留或普通根默认分配的总额',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_exec_budget',@level2type=N'COLUMN',@level2name=N'Granted';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_exec_budget') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_exec_budget'),N'Reserved','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'已预留给所有子项的总额；取消不自动返还',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_exec_budget',@level2type=N'COLUMN',@level2name=N'Reserved';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_exec_budget') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_exec_budget'),N'Revision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'预算乐观并发修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_exec_budget',@level2type=N'COLUMN',@level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_exec_budget') AND name=N'ck_fn_workflow_exec_budget_state')
 ALTER TABLE dbo.fn_workflow_exec_budget ADD CONSTRAINT ck_fn_workflow_exec_budget_state CHECK (Granted BETWEEN 2 AND 4096 AND Reserved BETWEEN 0 AND Granted AND Revision>0);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_exec_budget') AND name=N'fk_fn_workflow_exec_budget_instance')
 ALTER TABLE dbo.fn_workflow_exec_budget ADD CONSTRAINT fk_fn_workflow_exec_budget_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id);
GO

-- 通用节点键上限为128；向前收敛已运行317的开发数据库，索引缺失也可重跑恢复。
IF COL_LENGTH(N'dbo.fn_workflow_business_cmd',N'NodeKey')<128 OR COL_LENGTH(N'dbo.fn_workflow_business_cmd',N'ContinuationNodeKey')<128
BEGIN
 IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND name=N'ux_fn_workflow_business_cmd_node')
  DROP INDEX ux_fn_workflow_business_cmd_node ON dbo.fn_workflow_business_cmd;
 ALTER TABLE dbo.fn_workflow_business_cmd ALTER COLUMN NodeKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL;
 ALTER TABLE dbo.fn_workflow_business_cmd ALTER COLUMN ContinuationNodeKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL;
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND name=N'ux_fn_workflow_business_cmd_node')
 CREATE UNIQUE INDEX ux_fn_workflow_business_cmd_node ON dbo.fn_workflow_business_cmd(InstanceId,NodeKey);
GO

-- 319：子根取消使用系统归因，恢复旧311约束时也保留当前目标口径。
-- 311：自动字段活动结果与步骤同事务落库，未记账重跑不覆盖已有事实。
-- 自动字段动作拥有独立系统审计；仍禁止人工动作省略操作人。
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_domain_audit') AND name=N'ck_fn_workflow_domain_audit_actor' AND (CHARINDEX(N'activity.execute',definition)=0 OR CHARINDEX(N'parent_subprocess',definition)=0))
  ALTER TABLE dbo.fn_workflow_domain_audit DROP CONSTRAINT ck_fn_workflow_domain_audit_actor;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_domain_audit') AND name=N'ck_fn_workflow_domain_audit_actor')
  ALTER TABLE dbo.fn_workflow_domain_audit WITH CHECK ADD CONSTRAINT ck_fn_workflow_domain_audit_actor
    CHECK (ActorUserId IS NOT NULL OR (OperationKey IN ('assignee.handle','activity.execute') AND ResourceTypeKey='step') OR
      (OperationKey='instance.cancel' AND ResourceTypeKey='instance' AND JSON_VALUE(DetailJson,'$.sourceKey') IS NOT NULL AND
       JSON_VALUE(DetailJson,'$.sourceKey')='parent_subprocess' AND JSON_VALUE(DetailJson,'$.parentInstanceId') IS NOT NULL AND JSON_VALUE(DetailJson,'$.parentStepId') IS NOT NULL));
GO
