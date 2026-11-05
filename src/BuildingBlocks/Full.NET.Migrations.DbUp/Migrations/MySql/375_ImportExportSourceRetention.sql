-- 375：来源保留、耐久释放意图与完成事实；MySQL 隐式提交后按结构继续恢复。
DROP PROCEDURE IF EXISTS fn_import_export_retention_ensure;
DELIMITER $$
CREATE PROCEDURE fn_import_export_retention_ensure()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_import_export_task' AND COLUMN_NAME='SourceExpiresAtUtc') THEN
  ALTER TABLE fn_import_export_task ADD COLUMN SourceExpiresAtUtc datetime(6) NULL COMMENT '源工作簿固定保留截止时间，排队和执行中延后清理';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_import_export_task' AND COLUMN_NAME='SourceReleaseStartedAtUtc') THEN
  ALTER TABLE fn_import_export_task ADD COLUMN SourceReleaseStartedAtUtc datetime(6) NULL COMMENT '源文件释放意图持久化时间，阻止重新排队';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_import_export_task' AND COLUMN_NAME='SourceReleasedAtUtc') THEN
  ALTER TABLE fn_import_export_task ADD COLUMN SourceReleasedAtUtc datetime(6) NULL COMMENT '源文件提供程序确认幂等释放完成的时间';
 END IF;
 UPDATE fn_import_export_task SET SourceExpiresAtUtc=DATE_ADD(CreatedAtUtc,INTERVAL 7 DAY) WHERE SourceExpiresAtUtc IS NULL;
 IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_import_export_task' AND COLUMN_NAME='SourceExpiresAtUtc' AND (IS_NULLABLE='YES' OR COLUMN_DEFAULT IS NULL)) THEN
  ALTER TABLE fn_import_export_task MODIFY COLUMN SourceExpiresAtUtc datetime(6) NOT NULL DEFAULT (DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 7 DAY)) COMMENT '源工作簿固定保留截止时间，排队和执行中延后清理';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_import_export_task' AND INDEX_NAME='ix_fn_import_export_task_source_retention') THEN
  CREATE INDEX ix_fn_import_export_task_source_retention ON fn_import_export_task(SourceReleasedAtUtc,SourceExpiresAtUtc,Id,TenantId,StatusKey,SourceReleaseStartedAtUtc,LeaseExpiresAtUtc);
 END IF;
END$$
DELIMITER ;
CALL fn_import_export_retention_ensure();
DROP PROCEDURE IF EXISTS fn_import_export_retention_ensure;
