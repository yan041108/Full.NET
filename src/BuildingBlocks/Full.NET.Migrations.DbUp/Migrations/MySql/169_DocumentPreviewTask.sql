-- 169：Host 文档 Office 预览转换任务表。

CREATE TABLE IF NOT EXISTS fn_document_preview_task (
    Id char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL COMMENT '逻辑主键',
    DocumentItemId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL COMMENT '文档项标识',
    VersionId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NULL COMMENT '版本标识',
    DocumentTitle varchar(256) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL COMMENT '文档标题',
    SourceFileId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL COMMENT '源文件标识',
    SourceFileName varchar(260) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT '源文件名',
    SourceMimeType varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT '源 MIME 类型',
    OutputFileId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NULL COMMENT '输出文件标识',
    StatusKey varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL COMMENT '状态键',
    ProviderKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL COMMENT '存储提供程序键',
    ErrorCode varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT '错误码',
    RequestedByUserId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL COMMENT '请求人用户标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    StartedAtUtc datetime(6) NULL COMMENT '开始时间(UTC)',
    CompletedAtUtc datetime(6) NULL COMMENT '完成时间(UTC)',
    Version bigint NOT NULL COMMENT '乐观并发版本号',
    PRIMARY KEY (Id),
    CONSTRAINT FK_fn_document_preview_task_Item
        FOREIGN KEY (DocumentItemId) REFERENCES fn_document_item(Id),
    CONSTRAINT CK_fn_document_preview_task_StatusKey
        CHECK (StatusKey IN ('pending', 'processing', 'succeeded', 'failed'))
) COMMENT='Host 文档 Office 预览转换任务表';

SET @indexExists := (
    SELECT COUNT(1)
    FROM information_schema.statistics
    WHERE table_schema = DATABASE()
      AND table_name = 'fn_document_preview_task'
      AND index_name = 'IX_fn_document_preview_task_StatusKey_CreatedAtUtc'
);
SET @ddl := IF(
    @indexExists = 0,
    'CREATE INDEX IX_fn_document_preview_task_StatusKey_CreatedAtUtc ON fn_document_preview_task (StatusKey, CreatedAtUtc, Id)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @indexExists := (
    SELECT COUNT(1)
    FROM information_schema.statistics
    WHERE table_schema = DATABASE()
      AND table_name = 'fn_document_preview_task'
      AND index_name = 'IX_fn_document_preview_task_DocumentItemId'
);
SET @ddl := IF(
    @indexExists = 0,
    'CREATE INDEX IX_fn_document_preview_task_DocumentItemId ON fn_document_preview_task (DocumentItemId, CreatedAtUtc)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
