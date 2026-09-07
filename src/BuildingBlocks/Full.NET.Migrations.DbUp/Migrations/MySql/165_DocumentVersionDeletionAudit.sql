-- 165：文档历史版本删除审计表。

CREATE TABLE IF NOT EXISTS fn_document_version_deletion_audit (
    Id char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL COMMENT '逻辑主键',
    DocumentItemId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL COMMENT '文档项标识',
    VersionId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL COMMENT '版本标识',
    VersionNumber int NOT NULL COMMENT '版本号',
    FileId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL COMMENT '文件标识',
    ContentHash char(64) CHARACTER SET ascii COLLATE ascii_general_ci NULL COMMENT '内容哈希',
    SizeBytes bigint NOT NULL COMMENT '大小(字节)',
    UploadedByUserId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL COMMENT '上传人用户标识',
    VersionCreatedAtUtc datetime(6) NOT NULL COMMENT 'Version Created At(UTC)',
    DeletedAtUtc datetime(6) NOT NULL COMMENT '删除时间(UTC)',
    DeletedByUserId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NULL COMMENT '删除人用户标识',
    DeletedBySourceKey varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL COMMENT '删除来源键',
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_document_version_deletion_audit_SourceKey
        CHECK (DeletedBySourceKey IN ('manual', 'retention')),
    CONSTRAINT CK_fn_document_version_deletion_audit_Number
        CHECK (VersionNumber > 0)
) COMMENT='文档历史版本删除审计表';

SET @indexExists := (
    SELECT COUNT(1)
    FROM information_schema.statistics
    WHERE table_schema = DATABASE()
      AND table_name = 'fn_document_version_deletion_audit'
      AND index_name = 'IX_fn_document_version_deletion_audit_Item_DeletedAt'
);
SET @ddl := IF(
    @indexExists = 0,
    'CREATE INDEX IX_fn_document_version_deletion_audit_Item_DeletedAt ON fn_document_version_deletion_audit (DocumentItemId, DeletedAtUtc)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @indexExists := (
    SELECT COUNT(1)
    FROM information_schema.statistics
    WHERE table_schema = DATABASE()
      AND table_name = 'fn_document_version_deletion_audit'
      AND index_name = 'UX_fn_document_version_deletion_audit_VersionId'
);
SET @ddl := IF(
    @indexExists = 0,
    'CREATE UNIQUE INDEX UX_fn_document_version_deletion_audit_VersionId ON fn_document_version_deletion_audit (VersionId)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
