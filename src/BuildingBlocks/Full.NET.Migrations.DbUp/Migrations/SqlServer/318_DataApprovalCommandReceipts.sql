-- 318：审批节点所有者效果回执及结果Outbox提交凭据；结构探测支持DbUp未记账重跑。
IF OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_dataapproval_cmd_receipt (
  Id uniqueidentifier NOT NULL,
  RequestId uniqueidentifier NOT NULL,
  InstanceId uniqueidentifier NOT NULL,
  TenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  CallId uniqueidentifier NOT NULL,
  PhaseKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  Generation int NOT NULL,
  InputHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  OutcomeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ResultJson nvarchar(max) NOT NULL,
  CreatedAtUtc datetime2(6) NOT NULL,
  CONSTRAINT pk_fn_dataapproval_cmd_receipt PRIMARY KEY CLUSTERED (Id)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'审批节点所有者效果回执及结果Outbox提交凭据',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_dataapproval_cmd_receipt';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'应用端UUID v7回执标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_dataapproval_cmd_receipt',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt'),N'RequestId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本模块拥有的审批请求',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_dataapproval_cmd_receipt',@level2type=N'COLUMN',@level2name=N'RequestId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt'),N'InstanceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信工作流根关联；无跨模块外键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_dataapproval_cmd_receipt',@level2type=N'COLUMN',@level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'审批请求可信作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_dataapproval_cmd_receipt',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt'),N'CallId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'真实Workflow节点调用标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_dataapproval_cmd_receipt',@level2type=N'COLUMN',@level2name=N'CallId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt'),N'PhaseKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'execute执行或compensate补偿',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_dataapproval_cmd_receipt',@level2type=N'COLUMN',@level2name=N'PhaseKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt'),N'Generation','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定协议请求代次',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_dataapproval_cmd_receipt',@level2type=N'COLUMN',@level2name=N'Generation';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt'),N'InputHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'Workflow绑定输入摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_dataapproval_cmd_receipt',@level2type=N'COLUMN',@level2name=N'InputHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt'),N'OutcomeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际所有者应用、取消、补偿或失败结果',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_dataapproval_cmd_receipt',@level2type=N'COLUMN',@level2name=N'OutcomeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt'),N'ResultJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'最小回执；不含规则快照',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_dataapproval_cmd_receipt',@level2type=N'COLUMN',@level2name=N'ResultJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本地回执及Outbox提交UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_dataapproval_cmd_receipt',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt') AND name=N'ck_fn_dataapproval_cmd_receipt_state')
 ALTER TABLE dbo.fn_dataapproval_cmd_receipt ADD CONSTRAINT ck_fn_dataapproval_cmd_receipt_state CHECK (Generation BETWEEN 1 AND 1000 AND PhaseKey IN ('execute','compensate') AND OutcomeKey IN ('applied','cancelled','compensated','declined','unknown'));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt') AND name=N'ck_fn_dataapproval_cmd_receipt_resultjson')
 ALTER TABLE dbo.fn_dataapproval_cmd_receipt ADD CONSTRAINT ck_fn_dataapproval_cmd_receipt_resultjson CHECK (ResultJson IS NULL OR ISJSON(ResultJson)=1);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt') AND name=N'ux_fn_dataapproval_cmd_receipt_call')
 CREATE UNIQUE INDEX ux_fn_dataapproval_cmd_receipt_call ON dbo.fn_dataapproval_cmd_receipt(CallId,PhaseKey,Generation,TenantScopeKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt') AND name=N'ix_fn_dataapproval_cmd_receipt_request')
 CREATE INDEX ix_fn_dataapproval_cmd_receipt_request ON dbo.fn_dataapproval_cmd_receipt(RequestId,InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_dataapproval_cmd_receipt') AND name=N'fk_fn_dataapproval_cmd_receipt_request')
 ALTER TABLE dbo.fn_dataapproval_cmd_receipt ADD CONSTRAINT fk_fn_dataapproval_cmd_receipt_request FOREIGN KEY (RequestId) REFERENCES dbo.fn_dataapproval_request(Id);
GO
