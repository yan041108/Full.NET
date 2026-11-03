-- 339：附件能力准备完成前不得发布源请求；外文件只保存本地不透明引用。
IF OBJECT_ID(N'dbo.fn_workflow_file_read_reference',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_workflow_file_read_reference (
  Id uniqueidentifier NOT NULL,
  InstanceId uniqueidentifier NOT NULL,
  CallId uniqueidentifier NOT NULL,
  TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  CapabilityId uniqueidentifier NOT NULL,
  DirectionKey varchar(8) COLLATE Latin1_General_100_BIN2 NOT NULL,
  CapabilityFieldKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  OriginalFileName nvarchar(255) NOT NULL,
  ContentType varchar(128) NOT NULL,
  SizeBytes bigint NOT NULL,
  ContentHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ExpiresAtUtc datetime2(6) NOT NULL,
  CONSTRAINT pk_fn_workflow_file_read_reference PRIMARY KEY CLUSTERED (Id)
 );
END;
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND name=N'ck_fn_workflow_collaboration_call_state' AND definition NOT LIKE '%preparing%')
 ALTER TABLE dbo.fn_workflow_collaboration_call DROP CONSTRAINT ck_fn_workflow_collaboration_call_state;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND name=N'ck_fn_workflow_collaboration_call_state')
 ALTER TABLE dbo.fn_workflow_collaboration_call ADD CONSTRAINT ck_fn_workflow_collaboration_call_state CHECK (Revision>0 AND InputFormRevision>0 AND ReservedActivities BETWEEN 2 AND 4096 AND StatusKey IN ('preparing','requested','accepted','result_ready','cancelling','cancelled','denied','completed'));
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_file_read_reference') AND name=N'ix_fn_workflow_file_read_reference_instance')
 CREATE INDEX ix_fn_workflow_file_read_reference_instance ON dbo.fn_workflow_file_read_reference(TenantScopeKey,InstanceId,CapabilityId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_collaboration_call') AND name=N'ix_fn_workflow_collaboration_call_preparing')
 CREATE INDEX ix_fn_workflow_collaboration_call_preparing ON dbo.fn_workflow_collaboration_call(StatusKey,UpdatedAtUtc,Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_file_read_reference') AND name=N'fk_fn_workflow_file_read_reference_attachment')
 ALTER TABLE dbo.fn_workflow_file_read_reference ADD CONSTRAINT fk_fn_workflow_file_read_reference_attachment FOREIGN KEY (Id) REFERENCES dbo.fn_workflow_form_submission_attachment(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_file_read_reference') AND name=N'fk_fn_workflow_file_read_reference_instance')
 ALTER TABLE dbo.fn_workflow_file_read_reference ADD CONSTRAINT fk_fn_workflow_file_read_reference_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_file_read_reference') AND name=N'fk_fn_workflow_file_read_reference_call')
 ALTER TABLE dbo.fn_workflow_file_read_reference ADD CONSTRAINT fk_fn_workflow_file_read_reference_call FOREIGN KEY (CallId) REFERENCES dbo.fn_workflow_collaboration_admission(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_file_read_reference') AND name=N'ck_fn_workflow_file_read_reference_state')
 ALTER TABLE dbo.fn_workflow_file_read_reference ADD CONSTRAINT ck_fn_workflow_file_read_reference_state CHECK (SizeBytes>0 AND DirectionKey IN ('input','result'));
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_file_read_reference') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本作用域Workflow附件的不透明能力引用，无外租户原文件身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_file_read_reference';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_file_read_reference') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_file_read_reference'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本地附件历史引用身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_file_read_reference',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_file_read_reference') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_file_read_reference'),N'InstanceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本作用域真实实例',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_file_read_reference',@level2type=N'COLUMN',@level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_file_read_reference') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_file_read_reference'),N'CallId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定协作调用身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_file_read_reference',@level2type=N'COLUMN',@level2name=N'CallId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_file_read_reference') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_file_read_reference'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本地可信作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_file_read_reference',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_file_read_reference') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_file_read_reference'),N'CapabilityId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'Files不透明能力，不是外租户FileId',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_file_read_reference',@level2type=N'COLUMN',@level2name=N'CapabilityId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_file_read_reference') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_file_read_reference'),N'DirectionKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原input或result方向',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_file_read_reference',@level2type=N'COLUMN',@level2name=N'DirectionKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_file_read_reference') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_file_read_reference'),N'CapabilityFieldKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原始能力接收字段',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_file_read_reference',@level2type=N'COLUMN',@level2name=N'CapabilityFieldKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_file_read_reference') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_file_read_reference'),N'OriginalFileName','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'安全下载名快照',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_file_read_reference',@level2type=N'COLUMN',@level2name=N'OriginalFileName';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_file_read_reference') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_file_read_reference'),N'ContentType','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'内容类型快照',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_file_read_reference',@level2type=N'COLUMN',@level2name=N'ContentType';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_file_read_reference') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_file_read_reference'),N'SizeBytes','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际字节数快照',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_file_read_reference',@level2type=N'COLUMN',@level2name=N'SizeBytes';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_file_read_reference') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_file_read_reference'),N'ContentHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定内容摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_file_read_reference',@level2type=N'COLUMN',@level2name=N'ContentHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_file_read_reference') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_file_read_reference'),N'ExpiresAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'读取期限UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_file_read_reference',@level2type=N'COLUMN',@level2name=N'ExpiresAtUtc';
GO
