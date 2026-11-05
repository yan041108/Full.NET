-- 375：导入源文件有界保留；先补列与回填，再收紧约束，允许未记账和部分 DDL 恢复。
IF COL_LENGTH(N'dbo.fn_import_export_task',N'SourceExpiresAtUtc') IS NULL
 ALTER TABLE dbo.fn_import_export_task ADD SourceExpiresAtUtc datetime2(6) NULL;
GO
IF COL_LENGTH(N'dbo.fn_import_export_task',N'SourceReleaseStartedAtUtc') IS NULL
 ALTER TABLE dbo.fn_import_export_task ADD SourceReleaseStartedAtUtc datetime2(6) NULL;
IF COL_LENGTH(N'dbo.fn_import_export_task',N'SourceReleasedAtUtc') IS NULL
 ALTER TABLE dbo.fn_import_export_task ADD SourceReleasedAtUtc datetime2(6) NULL;
GO
UPDATE dbo.fn_import_export_task SET SourceExpiresAtUtc=DATEADD(DAY,7,CreatedAtUtc) WHERE SourceExpiresAtUtc IS NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.fn_import_export_task') AND name=N'SourceExpiresAtUtc' AND is_nullable=1)
 ALTER TABLE dbo.fn_import_export_task ALTER COLUMN SourceExpiresAtUtc datetime2(6) NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_import_export_task') AND parent_column_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'),N'SourceExpiresAtUtc','ColumnId'))
 ALTER TABLE dbo.fn_import_export_task ADD CONSTRAINT df_fn_import_export_task_source_expires DEFAULT (DATEADD(DAY,7,SYSUTCDATETIME())) FOR SourceExpiresAtUtc;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_import_export_task') AND name=N'ix_fn_import_export_task_source_retention')
 CREATE INDEX ix_fn_import_export_task_source_retention ON dbo.fn_import_export_task(SourceReleasedAtUtc,SourceExpiresAtUtc,Id) INCLUDE(TenantId,StatusKey,SourceReleaseStartedAtUtc,LeaseExpiresAtUtc);
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_import_export_task') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'),N'SourceExpiresAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'源工作簿固定保留截止时间，排队和执行中延后清理',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_import_export_task',@level2type=N'COLUMN',@level2name=N'SourceExpiresAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_import_export_task') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'),N'SourceReleaseStartedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'源文件释放意图持久化时间，阻止重新排队',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_import_export_task',@level2type=N'COLUMN',@level2name=N'SourceReleaseStartedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_import_export_task') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'),N'SourceReleasedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'源文件提供程序确认幂等释放完成的时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_import_export_task',@level2type=N'COLUMN',@level2name=N'SourceReleasedAtUtc';
GO
