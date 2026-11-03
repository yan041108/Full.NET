-- 文档第一批：隐式提交 DDL 每一步均可恢复。
DROP PROCEDURE IF EXISTS fn_migrate_document_batch_one;
DELIMITER $$
CREATE PROCEDURE fn_migrate_document_batch_one()
BEGIN
IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_item' AND COLUMN_NAME = 'VisibilityMode') THEN
    ALTER TABLE fn_document_item ADD VisibilityMode varchar(16) NOT NULL DEFAULT 'host' COMMENT '文档可见模式，host 或 restricted';
END IF;
IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_item' AND COLUMN_NAME = 'AccessRevision') THEN
    ALTER TABLE fn_document_item ADD AccessRevision bigint NOT NULL DEFAULT 1 COMMENT '文档访问修订号，撤销历史分享会话';
END IF;
IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_item' AND COLUMN_NAME = 'LastVersionNumber') THEN
    ALTER TABLE fn_document_item ADD LastVersionNumber int NOT NULL DEFAULT 0 COMMENT '已提交版本最大编号，删除后不回退';
END IF;
IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_permission' AND COLUMN_NAME = 'Actions') THEN
    ALTER TABLE fn_document_permission ADD Actions int NOT NULL DEFAULT 0 COMMENT '用户动作位集合，读写删分享独立组合';
END IF;
IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_share' AND COLUMN_NAME = 'PolicyRevision') THEN
    ALTER TABLE fn_document_share ADD PolicyRevision bigint NOT NULL DEFAULT 1 COMMENT '分享策略修订号，不随访问计数变化';
END IF;
IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_item' AND COLUMN_NAME = 'IsPurging') THEN
    ALTER TABLE fn_document_item ADD IsPurging boolean NOT NULL DEFAULT 0 COMMENT '永久删除正在分批清理，禁止恢复';
END IF;
UPDATE fn_document_item SET DocumentNo = CONCAT('DOC-', LOWER(HEX(Id))) WHERE DocumentNo IS NULL OR DocumentNo = '';
UPDATE fn_document_item i LEFT JOIN (SELECT DocumentItemId, MAX(VersionNumber) AS MaxNumber FROM
 (SELECT DocumentItemId, VersionNumber FROM fn_document_version UNION ALL SELECT DocumentItemId, VersionNumber FROM fn_document_version_deletion_audit) n
 GROUP BY DocumentItemId) h ON h.DocumentItemId = i.Id SET i.LastVersionNumber = h.MaxNumber WHERE i.LastVersionNumber < h.MaxNumber;
IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_permission' AND COLUMN_NAME = 'PermissionLevel') THEN
    UPDATE fn_document_permission SET Actions = CASE PermissionLevel WHEN 'read' THEN 1 WHEN 'write' THEN 3 WHEN 'delete' THEN 5 WHEN 'share' THEN 9 WHEN 'manage' THEN 15 ELSE Actions END WHERE Actions = 0;
    IF EXISTS (SELECT 1 FROM fn_document_permission WHERE Actions = 0) THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Unknown document permission action'; END IF;
    ALTER TABLE fn_document_permission DROP COLUMN PermissionLevel;
END IF;
UPDATE fn_document_item i SET i.VisibilityMode = 'restricted' WHERE i.VisibilityMode = 'host' AND EXISTS (SELECT 1 FROM fn_document_permission p WHERE p.DocumentId = i.Id);
CREATE TABLE IF NOT EXISTS fn_document_share_session (
 Id binary(16) NOT NULL COMMENT '应用生成的分享会话标识',
 ShareId binary(16) NOT NULL COMMENT '分享标识',
 DocumentId binary(16) NOT NULL COMMENT '文档标识',
 VersionId binary(16) NOT NULL COMMENT '固定文件版本标识',
 RequestKey binary(16) NOT NULL COMMENT '一次打开的请求幂等键',
 AccessRevision bigint NOT NULL COMMENT '文档授权修订',
 PolicyRevision bigint NOT NULL COMMENT '分享策略修订',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '首次扣次时间',
 ExpiresAtUtc datetime(6) NOT NULL COMMENT '不可延长的会话到期时间',
 CONSTRAINT PK_fn_document_share_session PRIMARY KEY (Id)
) COMMENT='文档分享访问会话表';
IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_share_session' AND INDEX_NAME = 'UX_fn_document_share_session_ShareId_RequestKey') THEN CREATE UNIQUE INDEX UX_fn_document_share_session_ShareId_RequestKey ON fn_document_share_session (ShareId, RequestKey); END IF;
IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_share_session' AND INDEX_NAME = 'IX_fn_document_share_session_ExpiresAtUtc') THEN CREATE INDEX IX_fn_document_share_session_ExpiresAtUtc ON fn_document_share_session (ExpiresAtUtc, Id); END IF;
CREATE TABLE IF NOT EXISTS fn_document_version_operation (
 Id binary(16) NOT NULL COMMENT '应用生成的操作标识',
 RequestKey binary(16) NOT NULL COMMENT '调用者提供的请求幂等键',
 DocumentId binary(16) NOT NULL COMMENT '文档标识',
 ActorUserId binary(16) NOT NULL COMMENT '受信调用者',
 VersionId binary(16) NOT NULL COMMENT '已提交版本标识',
 FileId binary(16) NOT NULL COMMENT '输入文件标识',
 ChangeDescription varchar(2000) NULL COMMENT '版本变更说明',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '提交时间',
 CONSTRAINT PK_fn_document_version_operation PRIMARY KEY (Id)
) COMMENT='文档版本幂等操作表';
IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_operation' AND INDEX_NAME = 'UX_fn_document_version_operation_Request') THEN CREATE UNIQUE INDEX UX_fn_document_version_operation_Request ON fn_document_version_operation (DocumentId, ActorUserId, RequestKey); END IF;
CREATE TABLE IF NOT EXISTS fn_document_permission_audit (
 Id binary(16) NOT NULL COMMENT '应用生成的治理审计标识',
 DocumentId binary(16) NOT NULL COMMENT '文档标识，不级联删除审计',
 ActorUserId binary(16) NOT NULL COMMENT '受信任治理操作者',
 PreviousMode varchar(16) NOT NULL COMMENT '变更前可见模式',
 NewMode varchar(16) NOT NULL COMMENT '变更后可见模式',
 BeforeActionsJson longtext NOT NULL COMMENT '变更前动作集合',
 AfterActionsJson longtext NOT NULL COMMENT '变更后动作集合',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '治理提交时间',
 CONSTRAINT PK_fn_document_permission_audit PRIMARY KEY (Id)
) COMMENT='文档权限治理审计表';
IF EXISTS (SELECT 1 FROM fn_document_permission WHERE TenantId IS NULL GROUP BY DocumentId, UserId HAVING COUNT(1) > 1) THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Duplicate document ACL subjects'; END IF;
IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_permission' AND INDEX_NAME = 'UX_fn_document_permission_HostSubject') THEN CREATE UNIQUE INDEX UX_fn_document_permission_HostSubject ON fn_document_permission (DocumentId, UserId); END IF;
END$$
DELIMITER ;
CALL fn_migrate_document_batch_one();
DROP PROCEDURE IF EXISTS fn_migrate_document_batch_one;
