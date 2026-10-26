-- 316：规则写入与审批命令凭据同事务；逐段恢复约束和索引。
IF OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_serialnumbers_approval_cmd (
  Id uniqueidentifier NOT NULL,
  ApprovalRequestId uniqueidentifier NOT NULL,
  RuleId uniqueidentifier NOT NULL,
  RequestedByUserId uniqueidentifier NOT NULL,
  ScenarioKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  InputHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  BeforeSnapshotJson nvarchar(max) NULL,
  StatusKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
  AppliedVersion bigint NULL,
  CompensatedVersion bigint NULL,
  Revision bigint NOT NULL,
  CreatedAtUtc datetime2(6) NOT NULL,
  UpdatedAtUtc datetime2(6) NOT NULL,
  CONSTRAINT pk_fn_serialnumbers_approval_cmd PRIMARY KEY CLUSTERED (Id)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'流水号审批命令的应用及精确补偿凭据',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_serialnumbers_approval_cmd';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'应用端UUID v7命令身份；重投复用同一标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_serialnumbers_approval_cmd',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd'),N'ApprovalRequestId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'DataApproval请求关联快照；不建立跨模块外键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_serialnumbers_approval_cmd',@level2type=N'COLUMN',@level2name=N'ApprovalRequestId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd'),N'RuleId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'SerialNumbers拥有的目标规则标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_serialnumbers_approval_cmd',@level2type=N'COLUMN',@level2name=N'RuleId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd'),N'RequestedByUserId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原始审批授权主体；不代表个人再次执行审批',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_serialnumbers_approval_cmd',@level2type=N'COLUMN',@level2name=N'RequestedByUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd'),N'ScenarioKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定审批场景键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_serialnumbers_approval_cmd',@level2type=N'COLUMN',@level2name=N'ScenarioKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd'),N'InputHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'规范化授权输入的SHA256摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_serialnumbers_approval_cmd',@level2type=N'COLUMN',@level2name=N'InputHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd'),N'BeforeSnapshotJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本次命令应用前的所有者规则快照',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_serialnumbers_approval_cmd',@level2type=N'COLUMN',@level2name=N'BeforeSnapshotJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd'),N'StatusKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'applied已应用、cancelled无效果取消、compensated已补偿',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_serialnumbers_approval_cmd',@level2type=N'COLUMN',@level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd'),N'AppliedVersion','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'规则实际应用后的修订；无效果取消为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_serialnumbers_approval_cmd',@level2type=N'COLUMN',@level2name=N'AppliedVersion';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd'),N'CompensatedVersion','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'规则实际补偿后的修订；未补偿为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_serialnumbers_approval_cmd',@level2type=N'COLUMN',@level2name=N'CompensatedVersion';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd'),N'Revision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'凭据乐观并发修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_serialnumbers_approval_cmd',@level2type=N'COLUMN',@level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'命令凭据创建UTC微秒时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_serialnumbers_approval_cmd',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd'),N'UpdatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'凭据最近变更UTC微秒时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_serialnumbers_approval_cmd',@level2type=N'COLUMN',@level2name=N'UpdatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd') AND name=N'ck_fn_serialnumbers_approval_cmd_state')
 ALTER TABLE dbo.fn_serialnumbers_approval_cmd ADD CONSTRAINT ck_fn_serialnumbers_approval_cmd_state CHECK (Revision>0 AND ScenarioKey IN ('serial_numbers.host_rule.update','serial_numbers.host_rule.disable') AND ((StatusKey='cancelled' AND BeforeSnapshotJson IS NULL AND AppliedVersion IS NULL AND CompensatedVersion IS NULL) OR (StatusKey='applied' AND BeforeSnapshotJson IS NOT NULL AND AppliedVersion IS NOT NULL AND AppliedVersion>0 AND CompensatedVersion IS NULL) OR (StatusKey='compensated' AND BeforeSnapshotJson IS NOT NULL AND AppliedVersion IS NOT NULL AND AppliedVersion>0 AND CompensatedVersion IS NOT NULL AND CompensatedVersion>AppliedVersion)));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd') AND name=N'ck_fn_serialnumbers_approval_cmd_snapshot')
 ALTER TABLE dbo.fn_serialnumbers_approval_cmd ADD CONSTRAINT ck_fn_serialnumbers_approval_cmd_snapshot CHECK (BeforeSnapshotJson IS NULL OR ISJSON(BeforeSnapshotJson)=1);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd') AND name=N'ix_fn_serialnumbers_approval_cmd_rule')
 CREATE INDEX ix_fn_serialnumbers_approval_cmd_rule ON dbo.fn_serialnumbers_approval_cmd(RuleId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_serialnumbers_approval_cmd') AND name=N'fk_fn_serialnumbers_approval_cmd_rule')
 ALTER TABLE dbo.fn_serialnumbers_approval_cmd ADD CONSTRAINT fk_fn_serialnumbers_approval_cmd_rule FOREIGN KEY (RuleId) REFERENCES dbo.fn_serialnumbers_rule(Id);
GO
