-- 314：Workflow工作日历不可变版本及工作时长等待；每段结构探测可重复恢复。
IF OBJECT_ID(N'dbo.fn_workflow_calendar',N'U') IS NULL
BEGIN
  CREATE TABLE dbo.fn_workflow_calendar (
    Id uniqueidentifier NOT NULL,
    TenantId uniqueidentifier NULL,
    ScopeKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CalendarKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    DisplayName nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
    DraftJson nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    Revision bigint NOT NULL,
    LatestVersionNumber int NULL,
    CreatedById uniqueidentifier NOT NULL,
    UpdatedById uniqueidentifier NOT NULL,
    CreatedAtUtc datetime2(6) NOT NULL,
    UpdatedAtUtc datetime2(6) NOT NULL,
    CONSTRAINT pk_fn_workflow_calendar PRIMARY KEY CLUSTERED (Id)
  );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_calendar') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'Workflow作用域内工作日历草稿与发布头',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_calendar';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_calendar') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_calendar'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作日历UUID v7标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_calendar',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_calendar') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_calendar'),N'TenantId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信租户标识，Host为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_calendar',@level2type=N'COLUMN',@level2name=N'TenantId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_calendar') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_calendar'),N'ScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信Host或Tenant作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_calendar',@level2type=N'COLUMN',@level2name=N'ScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_calendar') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_calendar'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信租户复合作用域键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_calendar',@level2type=N'COLUMN',@level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_calendar') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_calendar'),N'CalendarKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'作用域内稳定日历键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_calendar',@level2type=N'COLUMN',@level2name=N'CalendarKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_calendar') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_calendar'),N'DisplayName','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'日历显示名称',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_calendar',@level2type=N'COLUMN',@level2name=N'DisplayName';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_calendar') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_calendar'),N'DraftJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'待发布工作日历闭合草稿',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_calendar',@level2type=N'COLUMN',@level2name=N'DraftJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_calendar') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_calendar'),N'Revision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'草稿及发布的乐观锁修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_calendar',@level2type=N'COLUMN',@level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_calendar') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_calendar'),N'LatestVersionNumber','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'最近发布版本号，未发布为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_calendar',@level2type=N'COLUMN',@level2name=N'LatestVersionNumber';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_calendar') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_calendar'),N'CreatedById','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际创建者标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_calendar',@level2type=N'COLUMN',@level2name=N'CreatedById';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_calendar') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_calendar'),N'UpdatedById','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际最近修改者标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_calendar',@level2type=N'COLUMN',@level2name=N'UpdatedById';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_calendar') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_calendar'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建UTC微秒时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_calendar',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_calendar') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_calendar'),N'UpdatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'最近变更UTC微秒时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_calendar',@level2type=N'COLUMN',@level2name=N'UpdatedAtUtc';
GO
IF OBJECT_ID(N'dbo.fn_workflow_calendar_version',N'U') IS NULL
BEGIN
  CREATE TABLE dbo.fn_workflow_calendar_version (
    Id uniqueidentifier NOT NULL,
    CalendarId uniqueidentifier NOT NULL,
    VersionNumber int NOT NULL,
    CanonicalJson nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    ContentHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    TimeZoneKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    PublishedById uniqueidentifier NOT NULL,
    PublishedAtUtc datetime2(6) NOT NULL,
    CONSTRAINT pk_fn_workflow_calendar_version PRIMARY KEY CLUSTERED (Id)
  );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_calendar_version') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'Workflow不可变工作日历版本，包含固定时区规则',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_calendar_version';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_calendar_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_calendar_version'),N'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作日历不可变版本UUID v7标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_calendar_version',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_calendar_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_calendar_version'),N'CalendarId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'所属Workflow日历标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_calendar_version',@level2type=N'COLUMN',@level2name=N'CalendarId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_calendar_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_calendar_version'),N'VersionNumber','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'所属日历内单调发布版本号',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_calendar_version',@level2type=N'COLUMN',@level2name=N'VersionNumber';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_calendar_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_calendar_version'),N'CanonicalJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定工作窗口、例外日及序列化时区规则',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_calendar_version',@level2type=N'COLUMN',@level2name=N'CanonicalJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_calendar_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_calendar_version'),N'ContentHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定发布快照SHA256摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_calendar_version',@level2type=N'COLUMN',@level2name=N'ContentHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_calendar_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_calendar_version'),N'TimeZoneKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'发布时固定的时区标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_calendar_version',@level2type=N'COLUMN',@level2name=N'TimeZoneKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_calendar_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_calendar_version'),N'PublishedById','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际发布者标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_calendar_version',@level2type=N'COLUMN',@level2name=N'PublishedById';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_calendar_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_calendar_version'),N'PublishedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'发布UTC微秒时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_calendar_version',@level2type=N'COLUMN',@level2name=N'PublishedAtUtc';
GO
IF COL_LENGTH(N'dbo.fn_workflow_delay',N'CalendarVersionId') IS NULL ALTER TABLE dbo.fn_workflow_delay ADD CalendarVersionId uniqueidentifier NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_delay'),N'CalendarVersionId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本次工作时长等待固定的日历版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_delay',@level2type=N'COLUMN',@level2name=N'CalendarVersionId';
GO
IF COL_LENGTH(N'dbo.fn_workflow_delay',N'CalendarHash') IS NULL ALTER TABLE dbo.fn_workflow_delay ADD CalendarHash varchar(64) COLLATE Latin1_General_100_BIN2 NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_delay'),N'CalendarHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本次工作日历快照摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_delay',@level2type=N'COLUMN',@level2name=N'CalendarHash';
GO
IF COL_LENGTH(N'dbo.fn_workflow_delay',N'CalendarJson') IS NULL ALTER TABLE dbo.fn_workflow_delay ADD CalendarJson nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_delay'),N'CalendarJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本次激活固定的日历及原时区规则快照',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_workflow_delay',@level2type=N'COLUMN',@level2name=N'CalendarJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_calendar') AND name=N'ux_fn_workflow_calendar_scope_key') CREATE UNIQUE INDEX ux_fn_workflow_calendar_scope_key ON dbo.fn_workflow_calendar(TenantScopeKey,CalendarKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_calendar') AND name=N'ix_fn_workflow_calendar_scope_created') CREATE INDEX ix_fn_workflow_calendar_scope_created ON dbo.fn_workflow_calendar(TenantScopeKey,CreatedAtUtc,Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_calendar') AND name=N'ix_fn_workflow_calendar_latest') CREATE INDEX ix_fn_workflow_calendar_latest ON dbo.fn_workflow_calendar(Id,LatestVersionNumber);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_calendar_version') AND name=N'ux_fn_workflow_calendar_version_number') CREATE UNIQUE INDEX ux_fn_workflow_calendar_version_number ON dbo.fn_workflow_calendar_version(CalendarId,VersionNumber);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_calendar') AND name=N'ck_fn_workflow_calendar_state') ALTER TABLE dbo.fn_workflow_calendar WITH CHECK ADD CONSTRAINT ck_fn_workflow_calendar_state CHECK (Revision>0 AND (LatestVersionNumber IS NULL OR LatestVersionNumber>0));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_calendar') AND name=N'ck_fn_workflow_calendar_scope') ALTER TABLE dbo.fn_workflow_calendar WITH CHECK ADD CONSTRAINT ck_fn_workflow_calendar_scope CHECK ((TenantId IS NULL AND ScopeKey='host' AND TenantScopeKey='host') OR (TenantId IS NOT NULL AND ScopeKey='tenant' AND TenantScopeKey LIKE 'tenant:%'));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_calendar') AND name=N'ck_fn_workflow_calendar_draft') ALTER TABLE dbo.fn_workflow_calendar WITH CHECK ADD CONSTRAINT ck_fn_workflow_calendar_draft CHECK (ISJSON(DraftJson)=1);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_calendar_version') AND name=N'ck_fn_workflow_calendar_version_state') ALTER TABLE dbo.fn_workflow_calendar_version WITH CHECK ADD CONSTRAINT ck_fn_workflow_calendar_version_state CHECK (VersionNumber>0 AND LEN(ContentHash)=64 AND ISJSON(CanonicalJson)=1);
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND name=N'ck_fn_workflow_delay_state')
  ALTER TABLE dbo.fn_workflow_delay DROP CONSTRAINT ck_fn_workflow_delay_state;
ALTER TABLE dbo.fn_workflow_delay WITH CHECK ADD CONSTRAINT ck_fn_workflow_delay_state CHECK (StatusKey IN ('waiting','fired','cancelled') AND ModeKey IN ('relative','absolute','working') AND Revision>0 AND InputFormRevision>0);
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND name=N'ck_fn_workflow_delay_pause')
  ALTER TABLE dbo.fn_workflow_delay DROP CONSTRAINT ck_fn_workflow_delay_pause;
ALTER TABLE dbo.fn_workflow_delay WITH CHECK ADD CONSTRAINT ck_fn_workflow_delay_pause CHECK ((PausedAtUtc IS NULL AND RemainingMilliseconds IS NULL) OR (ModeKey IN ('relative','working') AND PausedAtUtc IS NOT NULL AND RemainingMilliseconds BETWEEN 0 AND 31622400000));
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND name=N'ck_fn_workflow_delay_calendar')
  ALTER TABLE dbo.fn_workflow_delay DROP CONSTRAINT ck_fn_workflow_delay_calendar;
ALTER TABLE dbo.fn_workflow_delay WITH CHECK ADD CONSTRAINT ck_fn_workflow_delay_calendar CHECK ((ModeKey='working' AND CalendarVersionId IS NOT NULL AND CalendarHash IS NOT NULL AND LEN(CalendarHash)=64 AND CalendarJson IS NOT NULL AND ISJSON(CalendarJson)=1) OR (ModeKey IN ('relative','absolute') AND CalendarVersionId IS NULL AND CalendarHash IS NULL AND CalendarJson IS NULL));
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_calendar_version') AND name=N'fk_fn_workflow_calendar_version_calendar') ALTER TABLE dbo.fn_workflow_calendar_version WITH CHECK ADD CONSTRAINT fk_fn_workflow_calendar_version_calendar FOREIGN KEY (CalendarId) REFERENCES dbo.fn_workflow_calendar(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_calendar') AND name=N'fk_fn_workflow_calendar_latest') ALTER TABLE dbo.fn_workflow_calendar WITH CHECK ADD CONSTRAINT fk_fn_workflow_calendar_latest FOREIGN KEY (Id,LatestVersionNumber) REFERENCES dbo.fn_workflow_calendar_version(CalendarId,VersionNumber);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_delay') AND name=N'fk_fn_workflow_delay_calendar_version') ALTER TABLE dbo.fn_workflow_delay WITH CHECK ADD CONSTRAINT fk_fn_workflow_delay_calendar_version FOREIGN KEY (CalendarVersionId) REFERENCES dbo.fn_workflow_calendar_version(Id);
GO
