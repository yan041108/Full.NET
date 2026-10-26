-- 314：Workflow工作日历不可变版本及工作时长等待；DDL分段探测并清理帮助过程。
CREATE TABLE IF NOT EXISTS fn_workflow_calendar (
  Id binary(16) NOT NULL COMMENT '工作日历UUID v7标识',
  TenantId binary(16) NULL COMMENT '可信租户标识，Host为空',
  ScopeKey varchar(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '可信Host或Tenant作用域',
  TenantScopeKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '可信租户复合作用域键',
  CalendarKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '作用域内稳定日历键',
  DisplayName varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '日历显示名称',
  DraftJson longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '待发布工作日历闭合草稿',
  Revision bigint NOT NULL COMMENT '草稿及发布的乐观锁修订',
  LatestVersionNumber int NULL COMMENT '最近发布版本号，未发布为空',
  CreatedById binary(16) NOT NULL COMMENT '实际创建者标识',
  UpdatedById binary(16) NOT NULL COMMENT '实际最近修改者标识',
  CreatedAtUtc datetime(6) NOT NULL COMMENT '创建UTC微秒时间',
  UpdatedAtUtc datetime(6) NOT NULL COMMENT '最近变更UTC微秒时间',
  CONSTRAINT pk_fn_workflow_calendar PRIMARY KEY (Id)
) COMMENT='Workflow作用域内工作日历草稿与发布头' ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS fn_workflow_calendar_version (
  Id binary(16) NOT NULL COMMENT '工作日历不可变版本UUID v7标识',
  CalendarId binary(16) NOT NULL COMMENT '所属Workflow日历标识',
  VersionNumber int NOT NULL COMMENT '所属日历内单调发布版本号',
  CanonicalJson longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '固定工作窗口、例外日及序列化时区规则',
  ContentHash varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '固定发布快照SHA256摘要',
  TimeZoneKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '发布时固定的时区标识',
  PublishedById binary(16) NOT NULL COMMENT '实际发布者标识',
  PublishedAtUtc datetime(6) NOT NULL COMMENT '发布UTC微秒时间',
  CONSTRAINT pk_fn_workflow_calendar_version PRIMARY KEY (Id)
) COMMENT='Workflow不可变工作日历版本，包含固定时区规则' ENGINE=InnoDB;
DROP PROCEDURE IF EXISTS fn_workflow_ensure_calendar;
DELIMITER $$
CREATE PROCEDURE fn_workflow_ensure_calendar()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_delay' AND COLUMN_NAME='CalendarVersionId') THEN
  ALTER TABLE fn_workflow_delay ADD COLUMN CalendarVersionId binary(16) NULL COMMENT '本次工作时长等待固定的日历版本';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_delay' AND COLUMN_NAME='CalendarHash') THEN
  ALTER TABLE fn_workflow_delay ADD COLUMN CalendarHash varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL COMMENT '本次工作日历快照摘要';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_delay' AND COLUMN_NAME='CalendarJson') THEN
  ALTER TABLE fn_workflow_delay ADD COLUMN CalendarJson longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL COMMENT '本次激活固定的日历及原时区规则快照';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_calendar' AND INDEX_NAME='ux_fn_workflow_calendar_scope_key') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_calendar_scope_key ON fn_workflow_calendar(TenantScopeKey,CalendarKey);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_calendar' AND INDEX_NAME='ix_fn_workflow_calendar_scope_created') THEN
  CREATE INDEX ix_fn_workflow_calendar_scope_created ON fn_workflow_calendar(TenantScopeKey,CreatedAtUtc,Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_calendar' AND INDEX_NAME='ix_fn_workflow_calendar_latest') THEN
  CREATE INDEX ix_fn_workflow_calendar_latest ON fn_workflow_calendar(Id,LatestVersionNumber);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_calendar_version' AND INDEX_NAME='ux_fn_workflow_calendar_version_number') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_calendar_version_number ON fn_workflow_calendar_version(CalendarId,VersionNumber);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_calendar' AND CONSTRAINT_NAME='ck_fn_workflow_calendar_state') THEN
  ALTER TABLE fn_workflow_calendar ADD CONSTRAINT ck_fn_workflow_calendar_state CHECK (Revision>0 AND (LatestVersionNumber IS NULL OR LatestVersionNumber>0));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_calendar' AND CONSTRAINT_NAME='ck_fn_workflow_calendar_scope') THEN
  ALTER TABLE fn_workflow_calendar ADD CONSTRAINT ck_fn_workflow_calendar_scope CHECK ((TenantId IS NULL AND ScopeKey='host' AND TenantScopeKey='host') OR (TenantId IS NOT NULL AND ScopeKey='tenant' AND TenantScopeKey LIKE 'tenant:%'));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_calendar' AND CONSTRAINT_NAME='ck_fn_workflow_calendar_draft') THEN
  ALTER TABLE fn_workflow_calendar ADD CONSTRAINT ck_fn_workflow_calendar_draft CHECK (JSON_VALID(DraftJson));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_calendar_version' AND CONSTRAINT_NAME='ck_fn_workflow_calendar_version_state') THEN
  ALTER TABLE fn_workflow_calendar_version ADD CONSTRAINT ck_fn_workflow_calendar_version_state CHECK (VersionNumber>0 AND CHAR_LENGTH(ContentHash)=64 AND JSON_VALID(CanonicalJson));
 END IF;
 IF EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_delay' AND CONSTRAINT_NAME='ck_fn_workflow_delay_state') THEN
  ALTER TABLE fn_workflow_delay DROP CHECK ck_fn_workflow_delay_state;
 END IF;
 ALTER TABLE fn_workflow_delay ADD CONSTRAINT ck_fn_workflow_delay_state CHECK (StatusKey IN ('waiting','fired','cancelled') AND ModeKey IN ('relative','absolute','working') AND Revision>0 AND InputFormRevision>0);
 IF EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_delay' AND CONSTRAINT_NAME='ck_fn_workflow_delay_pause') THEN
  ALTER TABLE fn_workflow_delay DROP CHECK ck_fn_workflow_delay_pause;
 END IF;
 ALTER TABLE fn_workflow_delay ADD CONSTRAINT ck_fn_workflow_delay_pause CHECK ((PausedAtUtc IS NULL AND RemainingMilliseconds IS NULL) OR (ModeKey IN ('relative','working') AND PausedAtUtc IS NOT NULL AND RemainingMilliseconds BETWEEN 0 AND 31622400000));
 IF EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_delay' AND CONSTRAINT_NAME='ck_fn_workflow_delay_calendar') THEN
  ALTER TABLE fn_workflow_delay DROP CHECK ck_fn_workflow_delay_calendar;
 END IF;
 ALTER TABLE fn_workflow_delay ADD CONSTRAINT ck_fn_workflow_delay_calendar CHECK ((ModeKey='working' AND CalendarVersionId IS NOT NULL AND CalendarHash IS NOT NULL AND CHAR_LENGTH(CalendarHash)=64 AND CalendarJson IS NOT NULL AND JSON_VALID(CalendarJson)) OR (ModeKey IN ('relative','absolute') AND CalendarVersionId IS NULL AND CalendarHash IS NULL AND CalendarJson IS NULL));
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_calendar_version' AND CONSTRAINT_NAME='fk_fn_workflow_calendar_version_calendar') THEN
  ALTER TABLE fn_workflow_calendar_version ADD CONSTRAINT fk_fn_workflow_calendar_version_calendar FOREIGN KEY (CalendarId) REFERENCES fn_workflow_calendar(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_calendar' AND CONSTRAINT_NAME='fk_fn_workflow_calendar_latest') THEN
  ALTER TABLE fn_workflow_calendar ADD CONSTRAINT fk_fn_workflow_calendar_latest FOREIGN KEY (Id,LatestVersionNumber) REFERENCES fn_workflow_calendar_version(CalendarId,VersionNumber);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_delay' AND CONSTRAINT_NAME='fk_fn_workflow_delay_calendar_version') THEN
  ALTER TABLE fn_workflow_delay ADD CONSTRAINT fk_fn_workflow_delay_calendar_version FOREIGN KEY (CalendarVersionId) REFERENCES fn_workflow_calendar_version(Id);
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_ensure_calendar();
DROP PROCEDURE IF EXISTS fn_workflow_ensure_calendar;
