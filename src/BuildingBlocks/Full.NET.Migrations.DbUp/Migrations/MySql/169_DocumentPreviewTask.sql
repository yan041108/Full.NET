-- 169：Host 文档 Office 预览转换任务表。

CREATE TABLE IF NOT EXISTS fn_document_preview_task
(
    Id char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    DocumentItemId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    VersionId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NULL,
    DocumentTitle varchar(256) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
    SourceFileId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    SourceFileName varchar(260) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL,
    SourceMimeType varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL,
    OutputFileId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NULL,
    StatusKey varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
    ProviderKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
    ErrorCode varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL,
    RequestedByUserId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    StartedAtUtc datetime(6) NULL,
    CompletedAtUtc datetime(6) NULL,
    Version bigint NOT NULL,
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
