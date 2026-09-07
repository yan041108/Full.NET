-- 203：前向修复 165–199 已发布表的 UUID 存储，不修改历史脚本。
-- 必须在维护窗口暂停 API/Worker 写入后运行；MySQL DDL 自动提交，失败后修正数据并重跑本脚本。
-- 先验证全部列和值，再以 VARBINARY(36) 保存原字节、转换标准 UUID 网络字节序、收敛 BINARY(16)。
-- VARBINARY 中允许已转换的 16 字节值，支持中断续跑；不关闭外键检查，不删除业务数据。
DROP TEMPORARY TABLE IF EXISTS fn_migrations_uuid_preflight;
CREATE TEMPORARY TABLE fn_migrations_uuid_preflight (
    InvalidCount bigint NOT NULL COMMENT '必须为零的无效值数量',
    CONSTRAINT CK_fn_migrations_uuid_preflight_Valid CHECK (InvalidCount = 0)
);

-- 预检 fn_document_version_deletion_audit.Id；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_deletion_audit' AND COLUMN_NAME = 'Id'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_deletion_audit' AND COLUMN_NAME = 'Id');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_document_version_deletion_audit
WHERE Id IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(Id) = 16)
    OR (OCTET_LENGTH(Id) = 36 AND REGEXP_LIKE(CONVERT(Id USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_document_version_deletion_audit.DocumentItemId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_deletion_audit' AND COLUMN_NAME = 'DocumentItemId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_deletion_audit' AND COLUMN_NAME = 'DocumentItemId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_document_version_deletion_audit
WHERE DocumentItemId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(DocumentItemId) = 16)
    OR (OCTET_LENGTH(DocumentItemId) = 36 AND REGEXP_LIKE(CONVERT(DocumentItemId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_document_version_deletion_audit.VersionId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_deletion_audit' AND COLUMN_NAME = 'VersionId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_deletion_audit' AND COLUMN_NAME = 'VersionId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_document_version_deletion_audit
WHERE VersionId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(VersionId) = 16)
    OR (OCTET_LENGTH(VersionId) = 36 AND REGEXP_LIKE(CONVERT(VersionId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_document_version_deletion_audit.FileId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_deletion_audit' AND COLUMN_NAME = 'FileId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_deletion_audit' AND COLUMN_NAME = 'FileId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_document_version_deletion_audit
WHERE FileId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(FileId) = 16)
    OR (OCTET_LENGTH(FileId) = 36 AND REGEXP_LIKE(CONVERT(FileId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_document_version_deletion_audit.UploadedByUserId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_deletion_audit' AND COLUMN_NAME = 'UploadedByUserId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_deletion_audit' AND COLUMN_NAME = 'UploadedByUserId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_document_version_deletion_audit
WHERE UploadedByUserId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(UploadedByUserId) = 16)
    OR (OCTET_LENGTH(UploadedByUserId) = 36 AND REGEXP_LIKE(CONVERT(UploadedByUserId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_document_version_deletion_audit.DeletedByUserId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_deletion_audit' AND COLUMN_NAME = 'DeletedByUserId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_deletion_audit' AND COLUMN_NAME = 'DeletedByUserId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_document_version_deletion_audit
WHERE DeletedByUserId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(DeletedByUserId) = 16)
    OR (OCTET_LENGTH(DeletedByUserId) = 36 AND REGEXP_LIKE(CONVERT(DeletedByUserId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_document_access_log.Id；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_access_log' AND COLUMN_NAME = 'Id'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_access_log' AND COLUMN_NAME = 'Id');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_document_access_log
WHERE Id IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(Id) = 16)
    OR (OCTET_LENGTH(Id) = 36 AND REGEXP_LIKE(CONVERT(Id USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_document_access_log.DocumentItemId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_access_log' AND COLUMN_NAME = 'DocumentItemId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_access_log' AND COLUMN_NAME = 'DocumentItemId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_document_access_log
WHERE DocumentItemId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(DocumentItemId) = 16)
    OR (OCTET_LENGTH(DocumentItemId) = 36 AND REGEXP_LIKE(CONVERT(DocumentItemId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_document_access_log.ActorUserId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_access_log' AND COLUMN_NAME = 'ActorUserId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_access_log' AND COLUMN_NAME = 'ActorUserId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_document_access_log
WHERE ActorUserId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(ActorUserId) = 16)
    OR (OCTET_LENGTH(ActorUserId) = 36 AND REGEXP_LIKE(CONVERT(ActorUserId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_document_preview_task.Id；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'Id'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'Id');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_document_preview_task
WHERE Id IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(Id) = 16)
    OR (OCTET_LENGTH(Id) = 36 AND REGEXP_LIKE(CONVERT(Id USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_document_preview_task.DocumentItemId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'DocumentItemId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'DocumentItemId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_document_preview_task
WHERE DocumentItemId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(DocumentItemId) = 16)
    OR (OCTET_LENGTH(DocumentItemId) = 36 AND REGEXP_LIKE(CONVERT(DocumentItemId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_document_preview_task.VersionId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'VersionId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'VersionId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_document_preview_task
WHERE VersionId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(VersionId) = 16)
    OR (OCTET_LENGTH(VersionId) = 36 AND REGEXP_LIKE(CONVERT(VersionId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_document_preview_task.SourceFileId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'SourceFileId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'SourceFileId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_document_preview_task
WHERE SourceFileId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(SourceFileId) = 16)
    OR (OCTET_LENGTH(SourceFileId) = 36 AND REGEXP_LIKE(CONVERT(SourceFileId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_document_preview_task.OutputFileId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'OutputFileId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'OutputFileId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_document_preview_task
WHERE OutputFileId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(OutputFileId) = 16)
    OR (OCTET_LENGTH(OutputFileId) = 36 AND REGEXP_LIKE(CONVERT(OutputFileId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_document_preview_task.RequestedByUserId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'RequestedByUserId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'RequestedByUserId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_document_preview_task
WHERE RequestedByUserId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(RequestedByUserId) = 16)
    OR (OCTET_LENGTH(RequestedByUserId) = 36 AND REGEXP_LIKE(CONVERT(RequestedByUserId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_import_export_task.Id；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_import_export_task' AND COLUMN_NAME = 'Id'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_import_export_task' AND COLUMN_NAME = 'Id');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_import_export_task
WHERE Id IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(Id) = 16)
    OR (OCTET_LENGTH(Id) = 36 AND REGEXP_LIKE(CONVERT(Id USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_import_export_task.TenantId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_import_export_task' AND COLUMN_NAME = 'TenantId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_import_export_task' AND COLUMN_NAME = 'TenantId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_import_export_task
WHERE TenantId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(TenantId) = 16)
    OR (OCTET_LENGTH(TenantId) = 36 AND REGEXP_LIKE(CONVERT(TenantId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_import_export_task.SourceFileId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_import_export_task' AND COLUMN_NAME = 'SourceFileId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_import_export_task' AND COLUMN_NAME = 'SourceFileId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_import_export_task
WHERE SourceFileId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(SourceFileId) = 16)
    OR (OCTET_LENGTH(SourceFileId) = 36 AND REGEXP_LIKE(CONVERT(SourceFileId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_import_export_task.RequestedByUserId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_import_export_task' AND COLUMN_NAME = 'RequestedByUserId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_import_export_task' AND COLUMN_NAME = 'RequestedByUserId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_import_export_task
WHERE RequestedByUserId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(RequestedByUserId) = 16)
    OR (OCTET_LENGTH(RequestedByUserId) = 36 AND REGEXP_LIKE(CONVERT(RequestedByUserId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_import_export_task.ErrorReceiptFileId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_import_export_task' AND COLUMN_NAME = 'ErrorReceiptFileId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_import_export_task' AND COLUMN_NAME = 'ErrorReceiptFileId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_import_export_task
WHERE ErrorReceiptFileId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(ErrorReceiptFileId) = 16)
    OR (OCTET_LENGTH(ErrorReceiptFileId) = 36 AND REGEXP_LIKE(CONVERT(ErrorReceiptFileId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_reporting_data_source.Id；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_data_source' AND COLUMN_NAME = 'Id'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_data_source' AND COLUMN_NAME = 'Id');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_reporting_data_source
WHERE Id IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(Id) = 16)
    OR (OCTET_LENGTH(Id) = 36 AND REGEXP_LIKE(CONVERT(Id USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_reporting_data_source.TenantId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_data_source' AND COLUMN_NAME = 'TenantId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_data_source' AND COLUMN_NAME = 'TenantId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_reporting_data_source
WHERE TenantId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(TenantId) = 16)
    OR (OCTET_LENGTH(TenantId) = 36 AND REGEXP_LIKE(CONVERT(TenantId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_reporting_export_task.Id；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_export_task' AND COLUMN_NAME = 'Id'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_export_task' AND COLUMN_NAME = 'Id');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_reporting_export_task
WHERE Id IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(Id) = 16)
    OR (OCTET_LENGTH(Id) = 36 AND REGEXP_LIKE(CONVERT(Id USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_reporting_export_task.TenantId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_export_task' AND COLUMN_NAME = 'TenantId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_export_task' AND COLUMN_NAME = 'TenantId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_reporting_export_task
WHERE TenantId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(TenantId) = 16)
    OR (OCTET_LENGTH(TenantId) = 36 AND REGEXP_LIKE(CONVERT(TenantId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_reporting_export_task.DefinitionId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_export_task' AND COLUMN_NAME = 'DefinitionId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_export_task' AND COLUMN_NAME = 'DefinitionId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_reporting_export_task
WHERE DefinitionId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(DefinitionId) = 16)
    OR (OCTET_LENGTH(DefinitionId) = 36 AND REGEXP_LIKE(CONVERT(DefinitionId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_reporting_export_task.OutputFileId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_export_task' AND COLUMN_NAME = 'OutputFileId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_export_task' AND COLUMN_NAME = 'OutputFileId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_reporting_export_task
WHERE OutputFileId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(OutputFileId) = 16)
    OR (OCTET_LENGTH(OutputFileId) = 36 AND REGEXP_LIKE(CONVERT(OutputFileId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_reporting_export_task.RequestedByUserId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_export_task' AND COLUMN_NAME = 'RequestedByUserId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_export_task' AND COLUMN_NAME = 'RequestedByUserId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_reporting_export_task
WHERE RequestedByUserId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(RequestedByUserId) = 16)
    OR (OCTET_LENGTH(RequestedByUserId) = 36 AND REGEXP_LIKE(CONVERT(RequestedByUserId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_printing_template.Id；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_printing_template' AND COLUMN_NAME = 'Id'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_printing_template' AND COLUMN_NAME = 'Id');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_printing_template
WHERE Id IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(Id) = 16)
    OR (OCTET_LENGTH(Id) = 36 AND REGEXP_LIKE(CONVERT(Id USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_printing_template_version.Id；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_printing_template_version' AND COLUMN_NAME = 'Id'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_printing_template_version' AND COLUMN_NAME = 'Id');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_printing_template_version
WHERE Id IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(Id) = 16)
    OR (OCTET_LENGTH(Id) = 36 AND REGEXP_LIKE(CONVERT(Id USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_printing_template_version.TemplateId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_printing_template_version' AND COLUMN_NAME = 'TemplateId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_printing_template_version' AND COLUMN_NAME = 'TemplateId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_printing_template_version
WHERE TemplateId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(TemplateId) = 16)
    OR (OCTET_LENGTH(TemplateId) = 36 AND REGEXP_LIKE(CONVERT(TemplateId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_printing_template_version.PublishedByUserId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_printing_template_version' AND COLUMN_NAME = 'PublishedByUserId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_printing_template_version' AND COLUMN_NAME = 'PublishedByUserId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_printing_template_version
WHERE PublishedByUserId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(PublishedByUserId) = 16)
    OR (OCTET_LENGTH(PublishedByUserId) = 36 AND REGEXP_LIKE(CONVERT(PublishedByUserId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_ai_model_config.Id；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_model_config' AND COLUMN_NAME = 'Id'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_model_config' AND COLUMN_NAME = 'Id');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_ai_model_config
WHERE Id IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(Id) = 16)
    OR (OCTET_LENGTH(Id) = 36 AND REGEXP_LIKE(CONVERT(Id USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_ai_model_config.TenantId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_model_config' AND COLUMN_NAME = 'TenantId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_model_config' AND COLUMN_NAME = 'TenantId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_ai_model_config
WHERE TenantId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(TenantId) = 16)
    OR (OCTET_LENGTH(TenantId) = 36 AND REGEXP_LIKE(CONVERT(TenantId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_ai_tenant_quota.Id；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_tenant_quota' AND COLUMN_NAME = 'Id'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_tenant_quota' AND COLUMN_NAME = 'Id');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_ai_tenant_quota
WHERE Id IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(Id) = 16)
    OR (OCTET_LENGTH(Id) = 36 AND REGEXP_LIKE(CONVERT(Id USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_ai_tenant_quota.TenantId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_tenant_quota' AND COLUMN_NAME = 'TenantId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_tenant_quota' AND COLUMN_NAME = 'TenantId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_ai_tenant_quota
WHERE TenantId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(TenantId) = 16)
    OR (OCTET_LENGTH(TenantId) = 36 AND REGEXP_LIKE(CONVERT(TenantId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_ai_chat_session.Id；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_session' AND COLUMN_NAME = 'Id'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_session' AND COLUMN_NAME = 'Id');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_ai_chat_session
WHERE Id IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(Id) = 16)
    OR (OCTET_LENGTH(Id) = 36 AND REGEXP_LIKE(CONVERT(Id USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_ai_chat_session.TenantId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_session' AND COLUMN_NAME = 'TenantId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_session' AND COLUMN_NAME = 'TenantId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_ai_chat_session
WHERE TenantId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(TenantId) = 16)
    OR (OCTET_LENGTH(TenantId) = 36 AND REGEXP_LIKE(CONVERT(TenantId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_ai_chat_session.OwnerUserId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_session' AND COLUMN_NAME = 'OwnerUserId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_session' AND COLUMN_NAME = 'OwnerUserId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_ai_chat_session
WHERE OwnerUserId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(OwnerUserId) = 16)
    OR (OCTET_LENGTH(OwnerUserId) = 36 AND REGEXP_LIKE(CONVERT(OwnerUserId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_ai_chat_session.ModelConfigId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_session' AND COLUMN_NAME = 'ModelConfigId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_session' AND COLUMN_NAME = 'ModelConfigId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_ai_chat_session
WHERE ModelConfigId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(ModelConfigId) = 16)
    OR (OCTET_LENGTH(ModelConfigId) = 36 AND REGEXP_LIKE(CONVERT(ModelConfigId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_ai_chat_message.Id；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_message' AND COLUMN_NAME = 'Id'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_message' AND COLUMN_NAME = 'Id');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_ai_chat_message
WHERE Id IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(Id) = 16)
    OR (OCTET_LENGTH(Id) = 36 AND REGEXP_LIKE(CONVERT(Id USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_ai_chat_message.SessionId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_message' AND COLUMN_NAME = 'SessionId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_message' AND COLUMN_NAME = 'SessionId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_ai_chat_message
WHERE SessionId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(SessionId) = 16)
    OR (OCTET_LENGTH(SessionId) = 36 AND REGEXP_LIKE(CONVERT(SessionId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_ai_agent_tool_call.Id；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_agent_tool_call' AND COLUMN_NAME = 'Id'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_agent_tool_call' AND COLUMN_NAME = 'Id');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_ai_agent_tool_call
WHERE Id IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(Id) = 16)
    OR (OCTET_LENGTH(Id) = 36 AND REGEXP_LIKE(CONVERT(Id USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_ai_agent_tool_call.TenantId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_agent_tool_call' AND COLUMN_NAME = 'TenantId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_agent_tool_call' AND COLUMN_NAME = 'TenantId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_ai_agent_tool_call
WHERE TenantId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(TenantId) = 16)
    OR (OCTET_LENGTH(TenantId) = 36 AND REGEXP_LIKE(CONVERT(TenantId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_ai_agent_tool_call.ActorUserId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_agent_tool_call' AND COLUMN_NAME = 'ActorUserId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_agent_tool_call' AND COLUMN_NAME = 'ActorUserId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_ai_agent_tool_call
WHERE ActorUserId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(ActorUserId) = 16)
    OR (OCTET_LENGTH(ActorUserId) = 36 AND REGEXP_LIKE(CONVERT(ActorUserId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_payment_merchant_config.Id；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_merchant_config' AND COLUMN_NAME = 'Id'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_merchant_config' AND COLUMN_NAME = 'Id');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_payment_merchant_config
WHERE Id IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(Id) = 16)
    OR (OCTET_LENGTH(Id) = 36 AND REGEXP_LIKE(CONVERT(Id USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_payment_merchant_config.TenantId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_merchant_config' AND COLUMN_NAME = 'TenantId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_merchant_config' AND COLUMN_NAME = 'TenantId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_payment_merchant_config
WHERE TenantId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(TenantId) = 16)
    OR (OCTET_LENGTH(TenantId) = 36 AND REGEXP_LIKE(CONVERT(TenantId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_payment_order.Id；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_order' AND COLUMN_NAME = 'Id'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_order' AND COLUMN_NAME = 'Id');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_payment_order
WHERE Id IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(Id) = 16)
    OR (OCTET_LENGTH(Id) = 36 AND REGEXP_LIKE(CONVERT(Id USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_payment_order.TenantId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_order' AND COLUMN_NAME = 'TenantId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_order' AND COLUMN_NAME = 'TenantId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_payment_order
WHERE TenantId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(TenantId) = 16)
    OR (OCTET_LENGTH(TenantId) = 36 AND REGEXP_LIKE(CONVERT(TenantId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_payment_order.MerchantConfigId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_order' AND COLUMN_NAME = 'MerchantConfigId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_order' AND COLUMN_NAME = 'MerchantConfigId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_payment_order
WHERE MerchantConfigId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(MerchantConfigId) = 16)
    OR (OCTET_LENGTH(MerchantConfigId) = 36 AND REGEXP_LIKE(CONVERT(MerchantConfigId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_payment_notify_receipt.Id；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_notify_receipt' AND COLUMN_NAME = 'Id'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_notify_receipt' AND COLUMN_NAME = 'Id');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_payment_notify_receipt
WHERE Id IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(Id) = 16)
    OR (OCTET_LENGTH(Id) = 36 AND REGEXP_LIKE(CONVERT(Id USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_payment_notify_receipt.MerchantConfigId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_notify_receipt' AND COLUMN_NAME = 'MerchantConfigId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_notify_receipt' AND COLUMN_NAME = 'MerchantConfigId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_payment_notify_receipt
WHERE MerchantConfigId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(MerchantConfigId) = 16)
    OR (OCTET_LENGTH(MerchantConfigId) = 36 AND REGEXP_LIKE(CONVERT(MerchantConfigId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_payment_refund.Id；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_refund' AND COLUMN_NAME = 'Id'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_refund' AND COLUMN_NAME = 'Id');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_payment_refund
WHERE Id IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(Id) = 16)
    OR (OCTET_LENGTH(Id) = 36 AND REGEXP_LIKE(CONVERT(Id USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_payment_refund.TenantId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_refund' AND COLUMN_NAME = 'TenantId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_refund' AND COLUMN_NAME = 'TenantId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_payment_refund
WHERE TenantId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(TenantId) = 16)
    OR (OCTET_LENGTH(TenantId) = 36 AND REGEXP_LIKE(CONVERT(TenantId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_payment_refund.OrderId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_refund' AND COLUMN_NAME = 'OrderId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_refund' AND COLUMN_NAME = 'OrderId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_payment_refund
WHERE OrderId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(OrderId) = 16)
    OR (OCTET_LENGTH(OrderId) = 36 AND REGEXP_LIKE(CONVERT(OrderId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_payment_refund.MerchantConfigId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_refund' AND COLUMN_NAME = 'MerchantConfigId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_refund' AND COLUMN_NAME = 'MerchantConfigId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_payment_refund
WHERE MerchantConfigId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(MerchantConfigId) = 16)
    OR (OCTET_LENGTH(MerchantConfigId) = 36 AND REGEXP_LIKE(CONVERT(MerchantConfigId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_goview_project.Id；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_goview_project' AND COLUMN_NAME = 'Id'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_goview_project' AND COLUMN_NAME = 'Id');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_goview_project
WHERE Id IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(Id) = 16)
    OR (OCTET_LENGTH(Id) = 36 AND REGEXP_LIKE(CONVERT(Id USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_goview_project_version.Id；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_goview_project_version' AND COLUMN_NAME = 'Id'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_goview_project_version' AND COLUMN_NAME = 'Id');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_goview_project_version
WHERE Id IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(Id) = 16)
    OR (OCTET_LENGTH(Id) = 36 AND REGEXP_LIKE(CONVERT(Id USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_goview_project_version.ProjectId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_goview_project_version' AND COLUMN_NAME = 'ProjectId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_goview_project_version' AND COLUMN_NAME = 'ProjectId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_goview_project_version
WHERE ProjectId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(ProjectId) = 16)
    OR (OCTET_LENGTH(ProjectId) = 36 AND REGEXP_LIKE(CONVERT(ProjectId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_goview_project_version.PublishedByUserId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_goview_project_version' AND COLUMN_NAME = 'PublishedByUserId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_goview_project_version' AND COLUMN_NAME = 'PublishedByUserId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_goview_project_version
WHERE PublishedByUserId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(PublishedByUserId) = 16)
    OR (OCTET_LENGTH(PublishedByUserId) = 36 AND REGEXP_LIKE(CONVERT(PublishedByUserId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_k3cloud_connection_config.Id；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_k3cloud_connection_config' AND COLUMN_NAME = 'Id'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_k3cloud_connection_config' AND COLUMN_NAME = 'Id');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_k3cloud_connection_config
WHERE Id IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(Id) = 16)
    OR (OCTET_LENGTH(Id) = 36 AND REGEXP_LIKE(CONVERT(Id USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_k3cloud_document_sync.Id；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_k3cloud_document_sync' AND COLUMN_NAME = 'Id'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_k3cloud_document_sync' AND COLUMN_NAME = 'Id');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_k3cloud_document_sync
WHERE Id IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(Id) = 16)
    OR (OCTET_LENGTH(Id) = 36 AND REGEXP_LIKE(CONVERT(Id USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_k3cloud_document_sync.ConnectionConfigId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_k3cloud_document_sync' AND COLUMN_NAME = 'ConnectionConfigId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_k3cloud_document_sync' AND COLUMN_NAME = 'ConnectionConfigId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_k3cloud_document_sync
WHERE ConnectionConfigId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(ConnectionConfigId) = 16)
    OR (OCTET_LENGTH(ConnectionConfigId) = 36 AND REGEXP_LIKE(CONVERT(ConnectionConfigId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_k3cloud_document_sync.CreatedByUserId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_k3cloud_document_sync' AND COLUMN_NAME = 'CreatedByUserId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_k3cloud_document_sync' AND COLUMN_NAME = 'CreatedByUserId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_k3cloud_document_sync
WHERE CreatedByUserId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(CreatedByUserId) = 16)
    OR (OCTET_LENGTH(CreatedByUserId) = 36 AND REGEXP_LIKE(CONVERT(CreatedByUserId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_ocr_provider_config.Id；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ocr_provider_config' AND COLUMN_NAME = 'Id'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ocr_provider_config' AND COLUMN_NAME = 'Id');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_ocr_provider_config
WHERE Id IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(Id) = 16)
    OR (OCTET_LENGTH(Id) = 36 AND REGEXP_LIKE(CONVERT(Id USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_ocr_id_card_task.Id；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ocr_id_card_task' AND COLUMN_NAME = 'Id'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ocr_id_card_task' AND COLUMN_NAME = 'Id');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_ocr_id_card_task
WHERE Id IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(Id) = 16)
    OR (OCTET_LENGTH(Id) = 36 AND REGEXP_LIKE(CONVERT(Id USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_ocr_id_card_task.SourceFileId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ocr_id_card_task' AND COLUMN_NAME = 'SourceFileId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ocr_id_card_task' AND COLUMN_NAME = 'SourceFileId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_ocr_id_card_task
WHERE SourceFileId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(SourceFileId) = 16)
    OR (OCTET_LENGTH(SourceFileId) = 36 AND REGEXP_LIKE(CONVERT(SourceFileId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_ocr_id_card_task.ConfirmedByUserId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ocr_id_card_task' AND COLUMN_NAME = 'ConfirmedByUserId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ocr_id_card_task' AND COLUMN_NAME = 'ConfirmedByUserId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_ocr_id_card_task
WHERE ConfirmedByUserId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(ConfirmedByUserId) = 16)
    OR (OCTET_LENGTH(ConfirmedByUserId) = 36 AND REGEXP_LIKE(CONVERT(ConfirmedByUserId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 预检 fn_ocr_id_card_task.CreatedByUserId；未知模式或非法文本在任何业务 DDL 前失败。
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT IF(COUNT(*) = 1, 0, 1) FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ocr_id_card_task' AND COLUMN_NAME = 'CreatedByUserId'
  AND ((DATA_TYPE IN ('char', 'varchar', 'varbinary') AND CHARACTER_MAXIMUM_LENGTH = 36)
       OR (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16));
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ocr_id_card_task' AND COLUMN_NAME = 'CreatedByUserId');
INSERT INTO fn_migrations_uuid_preflight (InvalidCount)
SELECT COUNT(*) FROM fn_ocr_id_card_task
WHERE CreatedByUserId IS NOT NULL AND NOT (
    (@uuid_type IN ('binary', 'varbinary') AND OCTET_LENGTH(CreatedByUserId) = 16)
    OR (OCTET_LENGTH(CreatedByUserId) = 36 AND REGEXP_LIKE(CONVERT(CreatedByUserId USING ascii),
        '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')));

-- 只暂时移除本模块已知文档约束；标准新库保留约束。
SET @uuid_ddl := IF(EXISTS(SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_access_log' AND COLUMN_NAME = 'DocumentItemId' AND DATA_TYPE <> 'binary') AND EXISTS(SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_access_log' AND CONSTRAINT_NAME = 'FK_fn_document_access_log_Item'), 'ALTER TABLE fn_document_access_log DROP FOREIGN KEY FK_fn_document_access_log_Item', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 只暂时移除本模块已知文档约束；标准新库保留约束。
SET @uuid_ddl := IF(EXISTS(SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'DocumentItemId' AND DATA_TYPE <> 'binary') AND EXISTS(SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND CONSTRAINT_NAME = 'FK_fn_document_preview_task_Item'), 'ALTER TABLE fn_document_preview_task DROP FOREIGN KEY FK_fn_document_preview_task_Item', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_document_version_deletion_audit.Id；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_deletion_audit' AND COLUMN_NAME = 'Id');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_deletion_audit' AND COLUMN_NAME = 'Id');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_document_version_deletion_audit MODIFY COLUMN Id VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_document_version_deletion_audit SET Id = UNHEX(REPLACE(CONVERT(Id USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(Id) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_document_version_deletion_audit MODIFY COLUMN Id BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_document_version_deletion_audit.DocumentItemId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_deletion_audit' AND COLUMN_NAME = 'DocumentItemId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_deletion_audit' AND COLUMN_NAME = 'DocumentItemId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_document_version_deletion_audit MODIFY COLUMN DocumentItemId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_document_version_deletion_audit SET DocumentItemId = UNHEX(REPLACE(CONVERT(DocumentItemId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(DocumentItemId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_document_version_deletion_audit MODIFY COLUMN DocumentItemId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_document_version_deletion_audit.VersionId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_deletion_audit' AND COLUMN_NAME = 'VersionId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_deletion_audit' AND COLUMN_NAME = 'VersionId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_document_version_deletion_audit MODIFY COLUMN VersionId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_document_version_deletion_audit SET VersionId = UNHEX(REPLACE(CONVERT(VersionId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(VersionId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_document_version_deletion_audit MODIFY COLUMN VersionId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_document_version_deletion_audit.FileId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_deletion_audit' AND COLUMN_NAME = 'FileId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_deletion_audit' AND COLUMN_NAME = 'FileId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_document_version_deletion_audit MODIFY COLUMN FileId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_document_version_deletion_audit SET FileId = UNHEX(REPLACE(CONVERT(FileId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(FileId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_document_version_deletion_audit MODIFY COLUMN FileId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_document_version_deletion_audit.UploadedByUserId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_deletion_audit' AND COLUMN_NAME = 'UploadedByUserId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_deletion_audit' AND COLUMN_NAME = 'UploadedByUserId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_document_version_deletion_audit MODIFY COLUMN UploadedByUserId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_document_version_deletion_audit SET UploadedByUserId = UNHEX(REPLACE(CONVERT(UploadedByUserId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(UploadedByUserId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_document_version_deletion_audit MODIFY COLUMN UploadedByUserId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_document_version_deletion_audit.DeletedByUserId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_deletion_audit' AND COLUMN_NAME = 'DeletedByUserId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_version_deletion_audit' AND COLUMN_NAME = 'DeletedByUserId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_document_version_deletion_audit MODIFY COLUMN DeletedByUserId VARBINARY(36) NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_document_version_deletion_audit SET DeletedByUserId = UNHEX(REPLACE(CONVERT(DeletedByUserId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(DeletedByUserId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_document_version_deletion_audit MODIFY COLUMN DeletedByUserId BINARY(16) NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_document_access_log.Id；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_access_log' AND COLUMN_NAME = 'Id');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_access_log' AND COLUMN_NAME = 'Id');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_document_access_log MODIFY COLUMN Id VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_document_access_log SET Id = UNHEX(REPLACE(CONVERT(Id USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(Id) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_document_access_log MODIFY COLUMN Id BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_document_access_log.DocumentItemId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_access_log' AND COLUMN_NAME = 'DocumentItemId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_access_log' AND COLUMN_NAME = 'DocumentItemId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_document_access_log MODIFY COLUMN DocumentItemId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_document_access_log SET DocumentItemId = UNHEX(REPLACE(CONVERT(DocumentItemId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(DocumentItemId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_document_access_log MODIFY COLUMN DocumentItemId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_document_access_log.ActorUserId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_access_log' AND COLUMN_NAME = 'ActorUserId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_access_log' AND COLUMN_NAME = 'ActorUserId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_document_access_log MODIFY COLUMN ActorUserId VARBINARY(36) NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_document_access_log SET ActorUserId = UNHEX(REPLACE(CONVERT(ActorUserId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(ActorUserId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_document_access_log MODIFY COLUMN ActorUserId BINARY(16) NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_document_preview_task.Id；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'Id');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'Id');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_document_preview_task MODIFY COLUMN Id VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_document_preview_task SET Id = UNHEX(REPLACE(CONVERT(Id USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(Id) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_document_preview_task MODIFY COLUMN Id BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_document_preview_task.DocumentItemId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'DocumentItemId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'DocumentItemId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_document_preview_task MODIFY COLUMN DocumentItemId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_document_preview_task SET DocumentItemId = UNHEX(REPLACE(CONVERT(DocumentItemId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(DocumentItemId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_document_preview_task MODIFY COLUMN DocumentItemId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_document_preview_task.VersionId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'VersionId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'VersionId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_document_preview_task MODIFY COLUMN VersionId VARBINARY(36) NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_document_preview_task SET VersionId = UNHEX(REPLACE(CONVERT(VersionId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(VersionId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_document_preview_task MODIFY COLUMN VersionId BINARY(16) NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_document_preview_task.SourceFileId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'SourceFileId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'SourceFileId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_document_preview_task MODIFY COLUMN SourceFileId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_document_preview_task SET SourceFileId = UNHEX(REPLACE(CONVERT(SourceFileId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(SourceFileId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_document_preview_task MODIFY COLUMN SourceFileId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_document_preview_task.OutputFileId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'OutputFileId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'OutputFileId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_document_preview_task MODIFY COLUMN OutputFileId VARBINARY(36) NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_document_preview_task SET OutputFileId = UNHEX(REPLACE(CONVERT(OutputFileId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(OutputFileId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_document_preview_task MODIFY COLUMN OutputFileId BINARY(16) NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_document_preview_task.RequestedByUserId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'RequestedByUserId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'RequestedByUserId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_document_preview_task MODIFY COLUMN RequestedByUserId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_document_preview_task SET RequestedByUserId = UNHEX(REPLACE(CONVERT(RequestedByUserId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(RequestedByUserId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_document_preview_task MODIFY COLUMN RequestedByUserId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_import_export_task.Id；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_import_export_task' AND COLUMN_NAME = 'Id');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_import_export_task' AND COLUMN_NAME = 'Id');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_import_export_task MODIFY COLUMN Id VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_import_export_task SET Id = UNHEX(REPLACE(CONVERT(Id USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(Id) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_import_export_task MODIFY COLUMN Id BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_import_export_task.TenantId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_import_export_task' AND COLUMN_NAME = 'TenantId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_import_export_task' AND COLUMN_NAME = 'TenantId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_import_export_task MODIFY COLUMN TenantId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_import_export_task SET TenantId = UNHEX(REPLACE(CONVERT(TenantId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(TenantId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_import_export_task MODIFY COLUMN TenantId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_import_export_task.SourceFileId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_import_export_task' AND COLUMN_NAME = 'SourceFileId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_import_export_task' AND COLUMN_NAME = 'SourceFileId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_import_export_task MODIFY COLUMN SourceFileId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_import_export_task SET SourceFileId = UNHEX(REPLACE(CONVERT(SourceFileId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(SourceFileId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_import_export_task MODIFY COLUMN SourceFileId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_import_export_task.RequestedByUserId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_import_export_task' AND COLUMN_NAME = 'RequestedByUserId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_import_export_task' AND COLUMN_NAME = 'RequestedByUserId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_import_export_task MODIFY COLUMN RequestedByUserId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_import_export_task SET RequestedByUserId = UNHEX(REPLACE(CONVERT(RequestedByUserId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(RequestedByUserId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_import_export_task MODIFY COLUMN RequestedByUserId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_import_export_task.ErrorReceiptFileId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_import_export_task' AND COLUMN_NAME = 'ErrorReceiptFileId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_import_export_task' AND COLUMN_NAME = 'ErrorReceiptFileId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_import_export_task MODIFY COLUMN ErrorReceiptFileId VARBINARY(36) NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_import_export_task SET ErrorReceiptFileId = UNHEX(REPLACE(CONVERT(ErrorReceiptFileId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(ErrorReceiptFileId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_import_export_task MODIFY COLUMN ErrorReceiptFileId BINARY(16) NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_reporting_data_source.Id；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_data_source' AND COLUMN_NAME = 'Id');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_data_source' AND COLUMN_NAME = 'Id');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_reporting_data_source MODIFY COLUMN Id VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_reporting_data_source SET Id = UNHEX(REPLACE(CONVERT(Id USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(Id) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_reporting_data_source MODIFY COLUMN Id BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_reporting_data_source.TenantId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_data_source' AND COLUMN_NAME = 'TenantId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_data_source' AND COLUMN_NAME = 'TenantId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_reporting_data_source MODIFY COLUMN TenantId VARBINARY(36) NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_reporting_data_source SET TenantId = UNHEX(REPLACE(CONVERT(TenantId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(TenantId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_reporting_data_source MODIFY COLUMN TenantId BINARY(16) NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_reporting_export_task.Id；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_export_task' AND COLUMN_NAME = 'Id');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_export_task' AND COLUMN_NAME = 'Id');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_reporting_export_task MODIFY COLUMN Id VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_reporting_export_task SET Id = UNHEX(REPLACE(CONVERT(Id USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(Id) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_reporting_export_task MODIFY COLUMN Id BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_reporting_export_task.TenantId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_export_task' AND COLUMN_NAME = 'TenantId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_export_task' AND COLUMN_NAME = 'TenantId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_reporting_export_task MODIFY COLUMN TenantId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_reporting_export_task SET TenantId = UNHEX(REPLACE(CONVERT(TenantId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(TenantId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_reporting_export_task MODIFY COLUMN TenantId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_reporting_export_task.DefinitionId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_export_task' AND COLUMN_NAME = 'DefinitionId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_export_task' AND COLUMN_NAME = 'DefinitionId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_reporting_export_task MODIFY COLUMN DefinitionId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_reporting_export_task SET DefinitionId = UNHEX(REPLACE(CONVERT(DefinitionId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(DefinitionId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_reporting_export_task MODIFY COLUMN DefinitionId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_reporting_export_task.OutputFileId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_export_task' AND COLUMN_NAME = 'OutputFileId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_export_task' AND COLUMN_NAME = 'OutputFileId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_reporting_export_task MODIFY COLUMN OutputFileId VARBINARY(36) NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_reporting_export_task SET OutputFileId = UNHEX(REPLACE(CONVERT(OutputFileId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(OutputFileId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_reporting_export_task MODIFY COLUMN OutputFileId BINARY(16) NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_reporting_export_task.RequestedByUserId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_export_task' AND COLUMN_NAME = 'RequestedByUserId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_export_task' AND COLUMN_NAME = 'RequestedByUserId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_reporting_export_task MODIFY COLUMN RequestedByUserId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_reporting_export_task SET RequestedByUserId = UNHEX(REPLACE(CONVERT(RequestedByUserId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(RequestedByUserId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_reporting_export_task MODIFY COLUMN RequestedByUserId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_printing_template.Id；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_printing_template' AND COLUMN_NAME = 'Id');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_printing_template' AND COLUMN_NAME = 'Id');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_printing_template MODIFY COLUMN Id VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_printing_template SET Id = UNHEX(REPLACE(CONVERT(Id USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(Id) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_printing_template MODIFY COLUMN Id BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_printing_template_version.Id；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_printing_template_version' AND COLUMN_NAME = 'Id');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_printing_template_version' AND COLUMN_NAME = 'Id');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_printing_template_version MODIFY COLUMN Id VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_printing_template_version SET Id = UNHEX(REPLACE(CONVERT(Id USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(Id) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_printing_template_version MODIFY COLUMN Id BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_printing_template_version.TemplateId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_printing_template_version' AND COLUMN_NAME = 'TemplateId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_printing_template_version' AND COLUMN_NAME = 'TemplateId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_printing_template_version MODIFY COLUMN TemplateId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_printing_template_version SET TemplateId = UNHEX(REPLACE(CONVERT(TemplateId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(TemplateId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_printing_template_version MODIFY COLUMN TemplateId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_printing_template_version.PublishedByUserId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_printing_template_version' AND COLUMN_NAME = 'PublishedByUserId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_printing_template_version' AND COLUMN_NAME = 'PublishedByUserId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_printing_template_version MODIFY COLUMN PublishedByUserId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_printing_template_version SET PublishedByUserId = UNHEX(REPLACE(CONVERT(PublishedByUserId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(PublishedByUserId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_printing_template_version MODIFY COLUMN PublishedByUserId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_ai_model_config.Id；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_model_config' AND COLUMN_NAME = 'Id');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_model_config' AND COLUMN_NAME = 'Id');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_ai_model_config MODIFY COLUMN Id VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_ai_model_config SET Id = UNHEX(REPLACE(CONVERT(Id USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(Id) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_ai_model_config MODIFY COLUMN Id BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_ai_model_config.TenantId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_model_config' AND COLUMN_NAME = 'TenantId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_model_config' AND COLUMN_NAME = 'TenantId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_ai_model_config MODIFY COLUMN TenantId VARBINARY(36) NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_ai_model_config SET TenantId = UNHEX(REPLACE(CONVERT(TenantId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(TenantId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_ai_model_config MODIFY COLUMN TenantId BINARY(16) NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_ai_tenant_quota.Id；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_tenant_quota' AND COLUMN_NAME = 'Id');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_tenant_quota' AND COLUMN_NAME = 'Id');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_ai_tenant_quota MODIFY COLUMN Id VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_ai_tenant_quota SET Id = UNHEX(REPLACE(CONVERT(Id USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(Id) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_ai_tenant_quota MODIFY COLUMN Id BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_ai_tenant_quota.TenantId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_tenant_quota' AND COLUMN_NAME = 'TenantId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_tenant_quota' AND COLUMN_NAME = 'TenantId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_ai_tenant_quota MODIFY COLUMN TenantId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_ai_tenant_quota SET TenantId = UNHEX(REPLACE(CONVERT(TenantId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(TenantId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_ai_tenant_quota MODIFY COLUMN TenantId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_ai_chat_session.Id；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_session' AND COLUMN_NAME = 'Id');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_session' AND COLUMN_NAME = 'Id');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_ai_chat_session MODIFY COLUMN Id VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_ai_chat_session SET Id = UNHEX(REPLACE(CONVERT(Id USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(Id) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_ai_chat_session MODIFY COLUMN Id BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_ai_chat_session.TenantId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_session' AND COLUMN_NAME = 'TenantId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_session' AND COLUMN_NAME = 'TenantId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_ai_chat_session MODIFY COLUMN TenantId VARBINARY(36) NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_ai_chat_session SET TenantId = UNHEX(REPLACE(CONVERT(TenantId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(TenantId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_ai_chat_session MODIFY COLUMN TenantId BINARY(16) NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_ai_chat_session.OwnerUserId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_session' AND COLUMN_NAME = 'OwnerUserId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_session' AND COLUMN_NAME = 'OwnerUserId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_ai_chat_session MODIFY COLUMN OwnerUserId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_ai_chat_session SET OwnerUserId = UNHEX(REPLACE(CONVERT(OwnerUserId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(OwnerUserId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_ai_chat_session MODIFY COLUMN OwnerUserId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_ai_chat_session.ModelConfigId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_session' AND COLUMN_NAME = 'ModelConfigId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_session' AND COLUMN_NAME = 'ModelConfigId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_ai_chat_session MODIFY COLUMN ModelConfigId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_ai_chat_session SET ModelConfigId = UNHEX(REPLACE(CONVERT(ModelConfigId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(ModelConfigId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_ai_chat_session MODIFY COLUMN ModelConfigId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_ai_chat_message.Id；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_message' AND COLUMN_NAME = 'Id');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_message' AND COLUMN_NAME = 'Id');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_ai_chat_message MODIFY COLUMN Id VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_ai_chat_message SET Id = UNHEX(REPLACE(CONVERT(Id USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(Id) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_ai_chat_message MODIFY COLUMN Id BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_ai_chat_message.SessionId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_message' AND COLUMN_NAME = 'SessionId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_message' AND COLUMN_NAME = 'SessionId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_ai_chat_message MODIFY COLUMN SessionId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_ai_chat_message SET SessionId = UNHEX(REPLACE(CONVERT(SessionId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(SessionId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_ai_chat_message MODIFY COLUMN SessionId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_ai_agent_tool_call.Id；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_agent_tool_call' AND COLUMN_NAME = 'Id');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_agent_tool_call' AND COLUMN_NAME = 'Id');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_ai_agent_tool_call MODIFY COLUMN Id VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_ai_agent_tool_call SET Id = UNHEX(REPLACE(CONVERT(Id USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(Id) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_ai_agent_tool_call MODIFY COLUMN Id BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_ai_agent_tool_call.TenantId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_agent_tool_call' AND COLUMN_NAME = 'TenantId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_agent_tool_call' AND COLUMN_NAME = 'TenantId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_ai_agent_tool_call MODIFY COLUMN TenantId VARBINARY(36) NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_ai_agent_tool_call SET TenantId = UNHEX(REPLACE(CONVERT(TenantId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(TenantId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_ai_agent_tool_call MODIFY COLUMN TenantId BINARY(16) NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_ai_agent_tool_call.ActorUserId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_agent_tool_call' AND COLUMN_NAME = 'ActorUserId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_agent_tool_call' AND COLUMN_NAME = 'ActorUserId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_ai_agent_tool_call MODIFY COLUMN ActorUserId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_ai_agent_tool_call SET ActorUserId = UNHEX(REPLACE(CONVERT(ActorUserId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(ActorUserId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_ai_agent_tool_call MODIFY COLUMN ActorUserId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_payment_merchant_config.Id；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_merchant_config' AND COLUMN_NAME = 'Id');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_merchant_config' AND COLUMN_NAME = 'Id');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_payment_merchant_config MODIFY COLUMN Id VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_payment_merchant_config SET Id = UNHEX(REPLACE(CONVERT(Id USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(Id) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_payment_merchant_config MODIFY COLUMN Id BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_payment_merchant_config.TenantId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_merchant_config' AND COLUMN_NAME = 'TenantId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_merchant_config' AND COLUMN_NAME = 'TenantId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_payment_merchant_config MODIFY COLUMN TenantId VARBINARY(36) NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_payment_merchant_config SET TenantId = UNHEX(REPLACE(CONVERT(TenantId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(TenantId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_payment_merchant_config MODIFY COLUMN TenantId BINARY(16) NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_payment_order.Id；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_order' AND COLUMN_NAME = 'Id');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_order' AND COLUMN_NAME = 'Id');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_payment_order MODIFY COLUMN Id VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_payment_order SET Id = UNHEX(REPLACE(CONVERT(Id USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(Id) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_payment_order MODIFY COLUMN Id BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_payment_order.TenantId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_order' AND COLUMN_NAME = 'TenantId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_order' AND COLUMN_NAME = 'TenantId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_payment_order MODIFY COLUMN TenantId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_payment_order SET TenantId = UNHEX(REPLACE(CONVERT(TenantId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(TenantId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_payment_order MODIFY COLUMN TenantId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_payment_order.MerchantConfigId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_order' AND COLUMN_NAME = 'MerchantConfigId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_order' AND COLUMN_NAME = 'MerchantConfigId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_payment_order MODIFY COLUMN MerchantConfigId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_payment_order SET MerchantConfigId = UNHEX(REPLACE(CONVERT(MerchantConfigId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(MerchantConfigId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_payment_order MODIFY COLUMN MerchantConfigId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_payment_notify_receipt.Id；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_notify_receipt' AND COLUMN_NAME = 'Id');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_notify_receipt' AND COLUMN_NAME = 'Id');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_payment_notify_receipt MODIFY COLUMN Id VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_payment_notify_receipt SET Id = UNHEX(REPLACE(CONVERT(Id USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(Id) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_payment_notify_receipt MODIFY COLUMN Id BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_payment_notify_receipt.MerchantConfigId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_notify_receipt' AND COLUMN_NAME = 'MerchantConfigId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_notify_receipt' AND COLUMN_NAME = 'MerchantConfigId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_payment_notify_receipt MODIFY COLUMN MerchantConfigId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_payment_notify_receipt SET MerchantConfigId = UNHEX(REPLACE(CONVERT(MerchantConfigId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(MerchantConfigId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_payment_notify_receipt MODIFY COLUMN MerchantConfigId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_payment_refund.Id；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_refund' AND COLUMN_NAME = 'Id');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_refund' AND COLUMN_NAME = 'Id');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_payment_refund MODIFY COLUMN Id VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_payment_refund SET Id = UNHEX(REPLACE(CONVERT(Id USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(Id) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_payment_refund MODIFY COLUMN Id BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_payment_refund.TenantId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_refund' AND COLUMN_NAME = 'TenantId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_refund' AND COLUMN_NAME = 'TenantId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_payment_refund MODIFY COLUMN TenantId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_payment_refund SET TenantId = UNHEX(REPLACE(CONVERT(TenantId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(TenantId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_payment_refund MODIFY COLUMN TenantId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_payment_refund.OrderId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_refund' AND COLUMN_NAME = 'OrderId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_refund' AND COLUMN_NAME = 'OrderId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_payment_refund MODIFY COLUMN OrderId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_payment_refund SET OrderId = UNHEX(REPLACE(CONVERT(OrderId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(OrderId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_payment_refund MODIFY COLUMN OrderId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_payment_refund.MerchantConfigId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_refund' AND COLUMN_NAME = 'MerchantConfigId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_payment_refund' AND COLUMN_NAME = 'MerchantConfigId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_payment_refund MODIFY COLUMN MerchantConfigId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_payment_refund SET MerchantConfigId = UNHEX(REPLACE(CONVERT(MerchantConfigId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(MerchantConfigId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_payment_refund MODIFY COLUMN MerchantConfigId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_goview_project.Id；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_goview_project' AND COLUMN_NAME = 'Id');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_goview_project' AND COLUMN_NAME = 'Id');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_goview_project MODIFY COLUMN Id VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_goview_project SET Id = UNHEX(REPLACE(CONVERT(Id USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(Id) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_goview_project MODIFY COLUMN Id BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_goview_project_version.Id；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_goview_project_version' AND COLUMN_NAME = 'Id');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_goview_project_version' AND COLUMN_NAME = 'Id');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_goview_project_version MODIFY COLUMN Id VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_goview_project_version SET Id = UNHEX(REPLACE(CONVERT(Id USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(Id) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_goview_project_version MODIFY COLUMN Id BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_goview_project_version.ProjectId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_goview_project_version' AND COLUMN_NAME = 'ProjectId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_goview_project_version' AND COLUMN_NAME = 'ProjectId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_goview_project_version MODIFY COLUMN ProjectId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_goview_project_version SET ProjectId = UNHEX(REPLACE(CONVERT(ProjectId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(ProjectId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_goview_project_version MODIFY COLUMN ProjectId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_goview_project_version.PublishedByUserId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_goview_project_version' AND COLUMN_NAME = 'PublishedByUserId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_goview_project_version' AND COLUMN_NAME = 'PublishedByUserId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_goview_project_version MODIFY COLUMN PublishedByUserId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_goview_project_version SET PublishedByUserId = UNHEX(REPLACE(CONVERT(PublishedByUserId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(PublishedByUserId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_goview_project_version MODIFY COLUMN PublishedByUserId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_k3cloud_connection_config.Id；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_k3cloud_connection_config' AND COLUMN_NAME = 'Id');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_k3cloud_connection_config' AND COLUMN_NAME = 'Id');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_k3cloud_connection_config MODIFY COLUMN Id VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_k3cloud_connection_config SET Id = UNHEX(REPLACE(CONVERT(Id USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(Id) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_k3cloud_connection_config MODIFY COLUMN Id BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_k3cloud_document_sync.Id；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_k3cloud_document_sync' AND COLUMN_NAME = 'Id');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_k3cloud_document_sync' AND COLUMN_NAME = 'Id');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_k3cloud_document_sync MODIFY COLUMN Id VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_k3cloud_document_sync SET Id = UNHEX(REPLACE(CONVERT(Id USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(Id) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_k3cloud_document_sync MODIFY COLUMN Id BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_k3cloud_document_sync.ConnectionConfigId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_k3cloud_document_sync' AND COLUMN_NAME = 'ConnectionConfigId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_k3cloud_document_sync' AND COLUMN_NAME = 'ConnectionConfigId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_k3cloud_document_sync MODIFY COLUMN ConnectionConfigId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_k3cloud_document_sync SET ConnectionConfigId = UNHEX(REPLACE(CONVERT(ConnectionConfigId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(ConnectionConfigId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_k3cloud_document_sync MODIFY COLUMN ConnectionConfigId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_k3cloud_document_sync.CreatedByUserId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_k3cloud_document_sync' AND COLUMN_NAME = 'CreatedByUserId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_k3cloud_document_sync' AND COLUMN_NAME = 'CreatedByUserId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_k3cloud_document_sync MODIFY COLUMN CreatedByUserId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_k3cloud_document_sync SET CreatedByUserId = UNHEX(REPLACE(CONVERT(CreatedByUserId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(CreatedByUserId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_k3cloud_document_sync MODIFY COLUMN CreatedByUserId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_ocr_provider_config.Id；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ocr_provider_config' AND COLUMN_NAME = 'Id');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ocr_provider_config' AND COLUMN_NAME = 'Id');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_ocr_provider_config MODIFY COLUMN Id VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_ocr_provider_config SET Id = UNHEX(REPLACE(CONVERT(Id USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(Id) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_ocr_provider_config MODIFY COLUMN Id BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_ocr_id_card_task.Id；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ocr_id_card_task' AND COLUMN_NAME = 'Id');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ocr_id_card_task' AND COLUMN_NAME = 'Id');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_ocr_id_card_task MODIFY COLUMN Id VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_ocr_id_card_task SET Id = UNHEX(REPLACE(CONVERT(Id USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(Id) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_ocr_id_card_task MODIFY COLUMN Id BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_ocr_id_card_task.SourceFileId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ocr_id_card_task' AND COLUMN_NAME = 'SourceFileId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ocr_id_card_task' AND COLUMN_NAME = 'SourceFileId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_ocr_id_card_task MODIFY COLUMN SourceFileId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_ocr_id_card_task SET SourceFileId = UNHEX(REPLACE(CONVERT(SourceFileId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(SourceFileId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_ocr_id_card_task MODIFY COLUMN SourceFileId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_ocr_id_card_task.ConfirmedByUserId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ocr_id_card_task' AND COLUMN_NAME = 'ConfirmedByUserId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ocr_id_card_task' AND COLUMN_NAME = 'ConfirmedByUserId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_ocr_id_card_task MODIFY COLUMN ConfirmedByUserId VARBINARY(36) NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_ocr_id_card_task SET ConfirmedByUserId = UNHEX(REPLACE(CONVERT(ConfirmedByUserId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(ConfirmedByUserId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_ocr_id_card_task MODIFY COLUMN ConfirmedByUserId BINARY(16) NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 转换 fn_ocr_id_card_task.CreatedByUserId；保留空值、现有索引与列说明。
SET @uuid_type := (SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ocr_id_card_task' AND COLUMN_NAME = 'CreatedByUserId');
SET @uuid_comment := (SELECT IF(COLUMN_COMMENT = '', 'UUID 标识，标准网络字节序', COLUMN_COMMENT) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ocr_id_card_task' AND COLUMN_NAME = 'CreatedByUserId');

SET @uuid_ddl := IF(@uuid_type IN ('char', 'varchar'), CONCAT('ALTER TABLE fn_ocr_id_card_task MODIFY COLUMN CreatedByUserId VARBINARY(36) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', 'UPDATE fn_ocr_id_card_task SET CreatedByUserId = UNHEX(REPLACE(CONVERT(CreatedByUserId USING ascii), ''-'', '''')) WHERE OCTET_LENGTH(CreatedByUserId) = 36', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

SET @uuid_ddl := IF(@uuid_type <> 'binary', CONCAT('ALTER TABLE fn_ocr_id_card_task MODIFY COLUMN CreatedByUserId BINARY(16) NOT NULL COMMENT ', QUOTE(@uuid_comment)), 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 恢复本模块文档关联；遗留孤儿必须显式处理，不跳过约束。
SET @uuid_ddl := IF(NOT EXISTS(SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_access_log' AND CONSTRAINT_NAME = 'FK_fn_document_access_log_Item'), 'ALTER TABLE fn_document_access_log ADD CONSTRAINT FK_fn_document_access_log_Item FOREIGN KEY (DocumentItemId) REFERENCES fn_document_item(Id)', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

-- 恢复本模块文档关联；遗留孤儿必须显式处理，不跳过约束。
SET @uuid_ddl := IF(NOT EXISTS(SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND CONSTRAINT_NAME = 'FK_fn_document_preview_task_Item'), 'ALTER TABLE fn_document_preview_task ADD CONSTRAINT FK_fn_document_preview_task_Item FOREIGN KEY (DocumentItemId) REFERENCES fn_document_item(Id)', 'SELECT 1');
PREPARE uuid_stmt FROM @uuid_ddl;
EXECUTE uuid_stmt;
DEALLOCATE PREPARE uuid_stmt;

DROP TEMPORARY TABLE fn_migrations_uuid_preflight;
