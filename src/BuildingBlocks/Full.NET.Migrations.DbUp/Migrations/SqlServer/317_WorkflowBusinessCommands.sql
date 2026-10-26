-- 317：工作流真实业务命令等待与精确补偿意图；结构探测支持DbUp未记账重跑。
IF OBJECT_ID(N'dbo.fn_workflow_business_cmd',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_business_cmd (
  Id uniqueidentifier NOT NULL,
  InstanceId uniqueidentifier NOT NULL,
  TenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  NodeKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ContinuationNodeKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ActionKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ActionVersion int NOT NULL,
  ActionHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  BindingJson nvarchar(max) NOT NULL,
  InputHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  StatusKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  CompensationRequested bit NOT NULL,
  Generation int NOT NULL,
  OwnerInputHash char(64) COLLATE Latin1_General_100_BIN2 NULL,
  AppliedVersion bigint NULL,
  CompensatedVersion bigint NULL,
  ErrorCode varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
  ResultJson nvarchar(max) NULL,
  Revision bigint NOT NULL,
  CreatedAtUtc datetime2(6) NOT NULL,
  UpdatedAtUtc datetime2(6) NOT NULL,
  CONSTRAINT pk_fn_workflow_business_cmd PRIMARY KEY CLUSTERED (Id)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作流真实业务命令等待与精确补偿意图',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_business_cmd';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_business_cmd'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'复用真实业务步骤的应用端UUID v7标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_business_cmd',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_business_cmd'),N'InstanceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本模块根实例标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_business_cmd',@level2type=N'COLUMN',@level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_business_cmd'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信实例租户作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_business_cmd',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_business_cmd'),N'NodeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'发布资产中的业务节点键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_business_cmd',@level2type=N'COLUMN',@level2name=N'NodeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_business_cmd'),N'ContinuationNodeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'发布资产中的唯一结束节点',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_business_cmd',@level2type=N'COLUMN',@level2name=N'ContinuationNodeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_business_cmd'),N'ActionKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际所有者静态注册的动作键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_business_cmd',@level2type=N'COLUMN',@level2name=N'ActionKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_business_cmd'),N'ActionVersion','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定动作版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_business_cmd',@level2type=N'COLUMN',@level2name=N'ActionVersion';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_business_cmd'),N'ActionHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'静态动作元数据SHA256摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_business_cmd',@level2type=N'COLUMN',@level2name=N'ActionHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_business_cmd'),N'BindingJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定根业务绑定；不含业务快照',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_business_cmd',@level2type=N'COLUMN',@level2name=N'BindingJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_business_cmd'),N'InputHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'绑定的规范化协议摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_business_cmd',@level2type=N'COLUMN',@level2name=N'InputHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_business_cmd'),N'StatusKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'请求、效果等待、已应用、取消、补偿或故障状态',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_business_cmd',@level2type=N'COLUMN',@level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_business_cmd'),N'CompensationRequested','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'根取消后固化的补偿意图',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_business_cmd',@level2type=N'COLUMN',@level2name=N'CompensationRequested';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_business_cmd'),N'Generation','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可靠执行与补偿投递代次',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_business_cmd',@level2type=N'COLUMN',@level2name=N'Generation';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_business_cmd'),N'OwnerInputHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'所有者授权业务输入摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_business_cmd',@level2type=N'COLUMN',@level2name=N'OwnerInputHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_business_cmd'),N'AppliedVersion','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际应用后的所有者版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_business_cmd',@level2type=N'COLUMN',@level2name=N'AppliedVersion';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_business_cmd'),N'CompensatedVersion','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际补偿后的所有者版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_business_cmd',@level2type=N'COLUMN',@level2name=N'CompensatedVersion';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_business_cmd'),N'ErrorCode','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'受控机器错误码；不记录业务快照',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_business_cmd',@level2type=N'COLUMN',@level2name=N'ErrorCode';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_business_cmd'),N'ResultJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际所有者最小结果回执',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_business_cmd',@level2type=N'COLUMN',@level2name=N'ResultJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_business_cmd'),N'Revision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本地乐观并发修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_business_cmd',@level2type=N'COLUMN',@level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_business_cmd'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建UTC微秒时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_business_cmd',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_business_cmd'),N'UpdatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'更新UTC微秒时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_business_cmd',@level2type=N'COLUMN',@level2name=N'UpdatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND name=N'ck_fn_workflow_business_cmd_state')
 ALTER TABLE dbo.fn_workflow_business_cmd ADD CONSTRAINT ck_fn_workflow_business_cmd_state CHECK (Revision>0 AND ActionVersion BETWEEN 1 AND 65535 AND Generation BETWEEN 1 AND 1000 AND StatusKey IN ('requested','result_ready','applied','compensating','cancelled','compensated','declined','faulted') AND (AppliedVersion IS NULL OR AppliedVersion>0) AND (CompensatedVersion IS NULL OR (AppliedVersion IS NOT NULL AND CompensatedVersion>AppliedVersion)) AND (StatusKey NOT IN ('applied','result_ready') OR (OwnerInputHash IS NOT NULL AND AppliedVersion IS NOT NULL)) AND (StatusKey<>'cancelled' OR (AppliedVersion IS NULL AND CompensatedVersion IS NULL)) AND (StatusKey<>'compensated' OR (OwnerInputHash IS NOT NULL AND AppliedVersion IS NOT NULL AND CompensatedVersion IS NOT NULL)));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND name=N'ck_fn_workflow_business_cmd_bindingjson')
 ALTER TABLE dbo.fn_workflow_business_cmd ADD CONSTRAINT ck_fn_workflow_business_cmd_bindingjson CHECK (BindingJson IS NULL OR ISJSON(BindingJson)=1);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND name=N'ck_fn_workflow_business_cmd_resultjson')
 ALTER TABLE dbo.fn_workflow_business_cmd ADD CONSTRAINT ck_fn_workflow_business_cmd_resultjson CHECK (ResultJson IS NULL OR ISJSON(ResultJson)=1);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND name=N'ux_fn_workflow_business_cmd_node')
 CREATE UNIQUE INDEX ux_fn_workflow_business_cmd_node ON dbo.fn_workflow_business_cmd(InstanceId,NodeKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND name=N'ix_fn_workflow_business_cmd_ready')
 CREATE INDEX ix_fn_workflow_business_cmd_ready ON dbo.fn_workflow_business_cmd(StatusKey,UpdatedAtUtc,Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND name=N'ix_fn_workflow_business_cmd_compensation')
 CREATE INDEX ix_fn_workflow_business_cmd_compensation ON dbo.fn_workflow_business_cmd(CompensationRequested,UpdatedAtUtc,Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND name=N'fk_fn_workflow_business_cmd_step')
 ALTER TABLE dbo.fn_workflow_business_cmd ADD CONSTRAINT fk_fn_workflow_business_cmd_step FOREIGN KEY (Id) REFERENCES dbo.fn_workflow_step(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_business_cmd') AND name=N'fk_fn_workflow_business_cmd_instance')
 ALTER TABLE dbo.fn_workflow_business_cmd ADD CONSTRAINT fk_fn_workflow_business_cmd_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id);
GO
