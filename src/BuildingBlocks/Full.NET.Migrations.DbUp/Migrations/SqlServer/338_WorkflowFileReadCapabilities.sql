-- 338：能力由Files自己的原持有事实派生；Workflow无权以外租户裸FileId读取。
IF OBJECT_ID(N'dbo.fn_files_read_capability',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_files_read_capability (
  Id uniqueidentifier NOT NULL,
  CallId uniqueidentifier NOT NULL,
  IssuerTenantId uniqueidentifier NULL,
  IssuerScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  RecipientTenantId uniqueidentifier NULL,
  RecipientScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  DirectionKey varchar(8) COLLATE Latin1_General_100_BIN2 NOT NULL,
  FieldKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  SourceFileId uniqueidentifier NOT NULL,
  OwnerReferenceId uniqueidentifier NOT NULL,
  OriginalFileName nvarchar(255) NOT NULL,
  ContentType varchar(128) NOT NULL,
  SizeBytes bigint NOT NULL,
  ContentHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  StatusKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ExpiresAtUtc datetime2(6) NOT NULL,
  CreatedAtUtc datetime2(6) NOT NULL,
  RevokedAtUtc datetime2(6) NULL,
  Revision bigint NOT NULL,
  CONSTRAINT pk_fn_files_read_capability PRIMARY KEY CLUSTERED (Id)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_files_read_capability') AND name=N'ux_fn_files_read_capability_key')
 CREATE UNIQUE INDEX ux_fn_files_read_capability_key ON dbo.fn_files_read_capability(CallId,IssuerScopeKey,DirectionKey,FieldKey,SourceFileId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_files_read_capability') AND name=N'ix_fn_files_read_capability_expiry')
 CREATE  INDEX ix_fn_files_read_capability_expiry ON dbo.fn_files_read_capability(StatusKey,ExpiresAtUtc,Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_files_read_capability') AND name=N'ix_fn_files_read_capability_issuer')
 CREATE  INDEX ix_fn_files_read_capability_issuer ON dbo.fn_files_read_capability(CallId,IssuerScopeKey,StatusKey,Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_files_read_capability') AND name=N'ck_fn_files_read_capability_state')
 ALTER TABLE dbo.fn_files_read_capability ADD CONSTRAINT ck_fn_files_read_capability_state CHECK (Revision>0 AND SizeBytes>0 AND IssuerScopeKey<>RecipientScopeKey AND DirectionKey IN ('input','result') AND StatusKey IN ('active','revoked') AND ExpiresAtUtc>CreatedAtUtc AND ((StatusKey='active' AND RevokedAtUtc IS NULL) OR (StatusKey='revoked' AND RevokedAtUtc IS NOT NULL)) AND ((IssuerTenantId IS NULL AND IssuerScopeKey='host') OR (IssuerTenantId IS NOT NULL AND IssuerScopeKey<>'host')) AND ((RecipientTenantId IS NULL AND RecipientScopeKey='host') OR (RecipientTenantId IS NOT NULL AND RecipientScopeKey<>'host')));
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_files_read_capability') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'Workflow关联的不透明文件读取能力，接收方不获得原文件身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_files_read_capability';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_files_read_capability') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_read_capability'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'不透明读取能力UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_files_read_capability',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_files_read_capability') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_read_capability'),N'CallId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定Workflow调用身份，无跨模块外键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_files_read_capability',@level2type=N'COLUMN',@level2name=N'CallId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_files_read_capability') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_read_capability'),N'IssuerTenantId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'签发租户，空表示Host',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_files_read_capability',@level2type=N'COLUMN',@level2name=N'IssuerTenantId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_files_read_capability') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_read_capability'),N'IssuerScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信签发作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_files_read_capability',@level2type=N'COLUMN',@level2name=N'IssuerScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_files_read_capability') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_read_capability'),N'RecipientTenantId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'接收租户，空表示Host',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_files_read_capability',@level2type=N'COLUMN',@level2name=N'RecipientTenantId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_files_read_capability') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_read_capability'),N'RecipientScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定可信接收作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_files_read_capability',@level2type=N'COLUMN',@level2name=N'RecipientScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_files_read_capability') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_read_capability'),N'DirectionKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'input或result协议方向',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_files_read_capability',@level2type=N'COLUMN',@level2name=N'DirectionKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_files_read_capability') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_read_capability'),N'FieldKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'最初接收方字段键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_files_read_capability',@level2type=N'COLUMN',@level2name=N'FieldKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_files_read_capability') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_read_capability'),N'SourceFileId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'Files内部原文件身份，不返回接收方',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_files_read_capability',@level2type=N'COLUMN',@level2name=N'SourceFileId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_files_read_capability') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_read_capability'),N'OwnerReferenceId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本地活动引用或租户资源所有权身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_files_read_capability',@level2type=N'COLUMN',@level2name=N'OwnerReferenceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_files_read_capability') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_read_capability'),N'OriginalFileName','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'安全下载名快照',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_files_read_capability',@level2type=N'COLUMN',@level2name=N'OriginalFileName';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_files_read_capability') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_read_capability'),N'ContentType','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'内容类型快照',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_files_read_capability',@level2type=N'COLUMN',@level2name=N'ContentType';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_files_read_capability') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_read_capability'),N'SizeBytes','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际文件字节数快照',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_files_read_capability',@level2type=N'COLUMN',@level2name=N'SizeBytes';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_files_read_capability') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_read_capability'),N'ContentHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定小写SHA256内容摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_files_read_capability',@level2type=N'COLUMN',@level2name=N'ContentHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_files_read_capability') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_read_capability'),N'StatusKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'active或revoked状态',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_files_read_capability',@level2type=N'COLUMN',@level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_files_read_capability') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_read_capability'),N'ExpiresAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'不可延长的读取截止UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_files_read_capability',@level2type=N'COLUMN',@level2name=N'ExpiresAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_files_read_capability') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_read_capability'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'能力首次签发UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_files_read_capability',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_files_read_capability') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_read_capability'),N'RevokedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'撤销或到期收敛UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_files_read_capability',@level2type=N'COLUMN',@level2name=N'RevokedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_files_read_capability') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_read_capability'),N'Revision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'单调修订号',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_files_read_capability',@level2type=N'COLUMN',@level2name=N'Revision';
GO
