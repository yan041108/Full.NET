-- 312：分发前建立尝试，收到结果与应用表单分开持久接纳，旧Worker只能补自己的尝试事实。
IF OBJECT_ID(N'dbo.fn_workflow_connector_call',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_connector_call (
  Id uniqueidentifier NOT NULL,
  InstanceId uniqueidentifier NOT NULL,
  TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  NodeKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ContinuationNodeKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ConnectorKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ConnectorVersion int NOT NULL,
  ConnectorHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ModeKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
  InputJson nvarchar(max) NOT NULL,
  InputHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  InputFormRevision bigint NOT NULL,
  RequestJson nvarchar(max) NOT NULL,
  RequestHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ParallelJoinId uniqueidentifier NULL,
  ParallelBranchKey varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
  StatusKey varchar(24) COLLATE Latin1_General_100_BIN2 NOT NULL,
  CurrentAttemptId uniqueidentifier NULL,
  SelectedAttemptId uniqueidentifier NULL,
  LeaseGeneration bigint NOT NULL,
  LeaseOwnerKey varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
  LeaseExpiresAtUtc datetime2(6) NULL,
  AttemptCount int NOT NULL,
  ApplyAttemptCount int NOT NULL,
  LastErrorKey varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
  DueAtUtc datetime2(6) NOT NULL,
  CancelRequestedAtUtc datetime2(6) NULL,
  Revision bigint NOT NULL,
  CreatedAtUtc datetime2(6) NOT NULL,
  CompletedAtUtc datetime2(6) NULL,
  AppliedFormRevision bigint NULL,
  CONSTRAINT pk_fn_workflow_connector_call PRIMARY KEY CLUSTERED (Id),
  CONSTRAINT fk_fn_workflow_connector_call_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id),
  CONSTRAINT fk_fn_workflow_connector_call_step FOREIGN KEY (Id) REFERENCES dbo.fn_workflow_step(Id),
  CONSTRAINT ck_fn_workflow_connector_call_state CHECK (StatusKey IN ('pending','dispatched','retryable','result_ready','applying','applied','outcome_unknown','failed','rejected','cancelled') AND ModeKey IN ('sync','async') AND ConnectorVersion>0 AND InputFormRevision>0 AND LeaseGeneration>=0 AND AttemptCount BETWEEN 0 AND 5 AND Revision>0),
  CONSTRAINT ck_fn_workflow_connector_call_lease CHECK ((StatusKey IN ('dispatched','applying') AND LeaseOwnerKey IS NOT NULL AND LeaseExpiresAtUtc IS NOT NULL) OR (StatusKey NOT IN ('dispatched','applying') AND LeaseOwnerKey IS NULL AND LeaseExpiresAtUtc IS NULL))
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'ApplyAttemptCount','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'结果接纳准备的有界尝试次数',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'ApplyAttemptCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'LastErrorKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'激活当前稳定错误码，不含异常或凭据原文',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'LastErrorKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=0 AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流受控连接器激活与当前分发接纳状态',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'Id','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'复用连接器步骤的UUID v7激活标识及稳定业务幂等键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'InstanceId','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'所属Workflow实例标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'激活时绑定的可信租户作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'NodeKey','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'不可变定义中的连接器节点键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'NodeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'ContinuationNodeKey','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'结果接纳后才推进的固定后继节点键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'ContinuationNodeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'ConnectorKey','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'部署侧固定连接器键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'ConnectorKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'ConnectorVersion','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'激活绑定的连接器配置版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'ConnectorVersion';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'ConnectorHash','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定连接器完整配置的SHA256摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'ConnectorHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'ModeKey','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'sync同步等待或async耐久分发',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'ModeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'InputJson','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'激活不可变表单输入快照',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'InputJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'InputHash','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原表单输入的SHA256摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'InputHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'InputFormRevision','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定输入所属主表单或分支修订号',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'InputFormRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'RequestJson','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'按发布映射形成的最小请求体，不含凭据',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'RequestJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'RequestHash','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定业务请求体的SHA256摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'RequestHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'ParallelJoinId','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'激活所属分叉汇合身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'ParallelJoinId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'ParallelBranchKey','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'激活所属稳定分支键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'ParallelBranchKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'StatusKey','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'分发、结果等待、接纳、拒绝、未知或取消状态',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'CurrentAttemptId','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'当前HTTP尝试身份，读取时必须同时匹配本激活',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'CurrentAttemptId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'SelectedAttemptId','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'被接纳的固定响应尝试身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'SelectedAttemptId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'LeaseGeneration','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'HTTP分发或结果接纳的租约世代',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'LeaseGeneration';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'LeaseOwnerKey','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'当前Worker租约所有者',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'LeaseOwnerKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'LeaseExpiresAtUtc','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'当前租约UTC截止时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'LeaseExpiresAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'AttemptCount','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'已持久建立的HTTP尝试次数',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'AttemptCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'DueAtUtc','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'首次分发或退避后下一次可执行时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'DueAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'CancelRequestedAtUtc','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'取消请求时间，已在途副作用仍保留对账事实',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'CancelRequestedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'Revision','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'激活当前状态的并发裁决修订号',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'连接器激活创建时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'CompletedAtUtc','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'当前激活收敛时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'CompletedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_call'),N'AppliedFormRevision','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'结果接纳后的主表单或分支修订号',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_call',@level2type=N'COLUMN',@level2name=N'AppliedFormRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND name=N'ix_fn_workflow_connector_call_due')
 CREATE INDEX ix_fn_workflow_connector_call_due ON dbo.fn_workflow_connector_call(StatusKey,DueAtUtc,Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_connector_call') AND name=N'ix_fn_workflow_connector_call_instance')
 CREATE INDEX ix_fn_workflow_connector_call_instance ON dbo.fn_workflow_connector_call(TenantScopeKey,InstanceId,CreatedAtUtc,Id);
GO
IF OBJECT_ID(N'dbo.fn_workflow_connector_attempt',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_connector_attempt (
  Id uniqueidentifier NOT NULL,
  CallId uniqueidentifier NOT NULL,
  TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  LeaseGeneration bigint NOT NULL,
  LeaseOwnerKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  RequestHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  StatusKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
  OutcomeKey varchar(24) COLLATE Latin1_General_100_BIN2 NULL,
  ResponseJson nvarchar(max) NULL,
  ResponseHash varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
  HttpStatusCode int NULL,
  ErrorKey varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
  StartedAtUtc datetime2(6) NOT NULL,
  CompletedAtUtc datetime2(6) NULL,
  CONSTRAINT pk_fn_workflow_connector_attempt PRIMARY KEY CLUSTERED (Id),
  CONSTRAINT fk_fn_workflow_connector_attempt_call FOREIGN KEY (CallId) REFERENCES dbo.fn_workflow_connector_call(Id),
  CONSTRAINT uq_fn_workflow_connector_attempt_lease UNIQUE (CallId,LeaseGeneration),
  CONSTRAINT ck_fn_workflow_connector_attempt_state CHECK (LeaseGeneration>0 AND ((StatusKey='inflight' AND OutcomeKey IS NULL AND CompletedAtUtc IS NULL) OR (StatusKey='received' AND OutcomeKey IN ('succeeded','rejected','failed','retryable','outcome_unknown') AND CompletedAtUtc IS NOT NULL)))
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_attempt') AND minor_id=0 AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流连接器每次外部调用的不可变请求与迟到响应事实',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_attempt';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_attempt'),N'Id','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本次HTTP尝试UUID v7标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_attempt',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_attempt'),N'CallId','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本次尝试所属固定连接器激活',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_attempt',@level2type=N'COLUMN',@level2name=N'CallId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_attempt'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原激活可信租户作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_attempt',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_attempt'),N'LeaseGeneration','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'发起HTTP时固定的租约世代',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_attempt',@level2type=N'COLUMN',@level2name=N'LeaseGeneration';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_attempt'),N'LeaseOwnerKey','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'发起HTTP的固定Worker身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_attempt',@level2type=N'COLUMN',@level2name=N'LeaseOwnerKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_attempt'),N'RequestHash','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本次尝试固定请求摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_attempt',@level2type=N'COLUMN',@level2name=N'RequestHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_attempt'),N'StatusKey','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'inflight在途或received已收到结果',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_attempt',@level2type=N'COLUMN',@level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_attempt'),N'OutcomeKey','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'确定成功、拒绝、失败、可重试或未知结果',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_attempt',@level2type=N'COLUMN',@level2name=N'OutcomeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_attempt'),N'ResponseJson','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'有界原始响应，仅归本次尝试且不进入普通日志',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_attempt',@level2type=N'COLUMN',@level2name=N'ResponseJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_attempt'),N'ResponseHash','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'有界响应的SHA256摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_attempt',@level2type=N'COLUMN',@level2name=N'ResponseHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_attempt'),N'HttpStatusCode','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际收到的HTTP状态码',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_attempt',@level2type=N'COLUMN',@level2name=N'HttpStatusCode';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_attempt'),N'ErrorKey','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'无凭据和异常原文的稳定错误机器码',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_attempt',@level2type=N'COLUMN',@level2name=N'ErrorKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_attempt'),N'StartedAtUtc','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本次外部调用持久建立时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_attempt',@level2type=N'COLUMN',@level2name=N'StartedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_attempt'),N'CompletedAtUtc','ColumnId') AND name=N'MS_Description')
  EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'收到本次结果的时间，可能晚于租约或实例取消',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_attempt',@level2type=N'COLUMN',@level2name=N'CompletedAtUtc';
GO
IF OBJECT_ID(N'dbo.fn_workflow_connector_resolution',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_connector_resolution (
  Id uniqueidentifier NOT NULL,
  CallId uniqueidentifier NOT NULL,
  InstanceId uniqueidentifier NOT NULL,
  TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ActorUserId uniqueidentifier NOT NULL,
  ActionKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
  AttemptId uniqueidentifier NULL,
  EvidenceReference nvarchar(256) NULL,
  Reason nvarchar(512) NOT NULL,
  IdempotencyKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
  RequestHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ResponseJson nvarchar(max) NOT NULL,
  CreatedAtUtc datetime2(6) NOT NULL,
  CONSTRAINT pk_fn_workflow_connector_resolution PRIMARY KEY CLUSTERED (Id),
  CONSTRAINT fk_fn_workflow_connector_resolution_call FOREIGN KEY (CallId) REFERENCES dbo.fn_workflow_connector_call(Id),
  CONSTRAINT fk_fn_workflow_connector_resolution_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id),
  CONSTRAINT uq_fn_workflow_connector_resolution_request UNIQUE (TenantScopeKey,CallId,ActorUserId,IdempotencyKey)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_resolution') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流连接器人工对账证据与不可变首次操作回执',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_resolution';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_resolution') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_resolution'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'对账操作UUID v7标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_resolution',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_resolution') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_resolution'),N'CallId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'被对账的原连接器激活',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_resolution',@level2type=N'COLUMN',@level2name=N'CallId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_resolution') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_resolution'),N'InstanceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原激活所属实例',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_resolution',@level2type=N'COLUMN',@level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_resolution') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_resolution'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信租户作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_resolution',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_resolution') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_resolution'),N'ActorUserId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'具有精确对账权限的真实操作人',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_resolution',@level2type=N'COLUMN',@level2name=N'ActorUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_resolution') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_resolution'),N'ActionKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'闭合对账动作键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_resolution',@level2type=N'COLUMN',@level2name=N'ActionKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_resolution') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_resolution'),N'AttemptId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'接纳的真实原尝试标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_resolution',@level2type=N'COLUMN',@level2name=N'AttemptId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_resolution') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_resolution'),N'EvidenceReference','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'人工核对的凭据或工单引用',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_resolution',@level2type=N'COLUMN',@level2name=N'EvidenceReference';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_resolution') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_resolution'),N'Reason','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'操作人明确填写的对账理由',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_resolution',@level2type=N'COLUMN',@level2name=N'Reason';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_resolution') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_resolution'),N'IdempotencyKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'同活动同操作人的语义幂等键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_resolution',@level2type=N'COLUMN',@level2name=N'IdempotencyKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_resolution') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_resolution'),N'RequestHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'规范对账请求SHA256摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_resolution',@level2type=N'COLUMN',@level2name=N'RequestHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_resolution') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_resolution'),N'ResponseJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'首次已提交对账响应快照',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_resolution',@level2type=N'COLUMN',@level2name=N'ResponseJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_connector_resolution') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_connector_resolution'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'首次对账提交时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_connector_resolution',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
