-- 339：Files准备在事务外，冻结与本地能力引用仍在Workflow事务。
CREATE TABLE IF NOT EXISTS fn_workflow_file_read_reference (
 Id binary(16) NOT NULL COMMENT '本地附件历史引用身份',
 InstanceId binary(16) NOT NULL COMMENT '本作用域真实实例',
 CallId binary(16) NOT NULL COMMENT '固定协作调用身份',
 TenantScopeKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '本地可信作用域',
 CapabilityId binary(16) NOT NULL COMMENT 'Files不透明能力，不是外租户FileId',
 DirectionKey varchar(8) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '原input或result方向',
 CapabilityFieldKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '原始能力接收字段',
 OriginalFileName varchar(255) NOT NULL COMMENT '安全下载名快照',
 ContentType varchar(128) NOT NULL COMMENT '内容类型快照',
 SizeBytes bigint NOT NULL COMMENT '实际字节数快照',
 ContentHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '固定内容摘要',
 ExpiresAtUtc datetime(6) NOT NULL COMMENT '读取期限UTC',
 CONSTRAINT pk_fn_workflow_file_read_reference PRIMARY KEY (Id)
) COMMENT='本作用域Workflow附件的不透明能力引用，无外租户原文件身份' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
DROP PROCEDURE IF EXISTS fn_workflow_collaboration_file_ensure;
DELIMITER $$
CREATE PROCEDURE fn_workflow_collaboration_file_ensure()
BEGIN
 IF EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS AS constraintsState INNER JOIN information_schema.CHECK_CONSTRAINTS AS checkState ON checkState.CONSTRAINT_SCHEMA=constraintsState.CONSTRAINT_SCHEMA AND checkState.CONSTRAINT_NAME=constraintsState.CONSTRAINT_NAME WHERE constraintsState.CONSTRAINT_SCHEMA=DATABASE() AND constraintsState.TABLE_NAME='fn_workflow_collaboration_call' AND constraintsState.CONSTRAINT_NAME='ck_fn_workflow_collaboration_call_state' AND checkState.CHECK_CLAUSE NOT LIKE '%preparing%') THEN
  ALTER TABLE fn_workflow_collaboration_call DROP CHECK ck_fn_workflow_collaboration_call_state;
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_call' AND CONSTRAINT_NAME='ck_fn_workflow_collaboration_call_state') THEN
  ALTER TABLE fn_workflow_collaboration_call ADD CONSTRAINT ck_fn_workflow_collaboration_call_state CHECK (Revision>0 AND InputFormRevision>0 AND ReservedActivities BETWEEN 2 AND 4096 AND StatusKey IN ('preparing','requested','accepted','result_ready','cancelling','cancelled','denied','completed'));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_file_read_reference' AND INDEX_NAME='ix_fn_workflow_file_read_reference_instance') THEN
  CREATE INDEX ix_fn_workflow_file_read_reference_instance ON fn_workflow_file_read_reference(TenantScopeKey,InstanceId,CapabilityId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_call' AND INDEX_NAME='ix_fn_workflow_collaboration_call_preparing') THEN
  CREATE INDEX ix_fn_workflow_collaboration_call_preparing ON fn_workflow_collaboration_call(StatusKey,UpdatedAtUtc,Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_file_read_reference' AND CONSTRAINT_NAME='fk_fn_workflow_file_read_reference_attachment') THEN
  ALTER TABLE fn_workflow_file_read_reference ADD CONSTRAINT fk_fn_workflow_file_read_reference_attachment FOREIGN KEY (Id) REFERENCES fn_workflow_form_submission_attachment(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_file_read_reference' AND CONSTRAINT_NAME='fk_fn_workflow_file_read_reference_instance') THEN
  ALTER TABLE fn_workflow_file_read_reference ADD CONSTRAINT fk_fn_workflow_file_read_reference_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_file_read_reference' AND CONSTRAINT_NAME='fk_fn_workflow_file_read_reference_call') THEN
  ALTER TABLE fn_workflow_file_read_reference ADD CONSTRAINT fk_fn_workflow_file_read_reference_call FOREIGN KEY (CallId) REFERENCES fn_workflow_collaboration_admission(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_file_read_reference' AND CONSTRAINT_NAME='ck_fn_workflow_file_read_reference_state') THEN
  ALTER TABLE fn_workflow_file_read_reference ADD CONSTRAINT ck_fn_workflow_file_read_reference_state CHECK (SizeBytes>0 AND DirectionKey IN ('input','result'));
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_collaboration_file_ensure();
DROP PROCEDURE IF EXISTS fn_workflow_collaboration_file_ensure;
