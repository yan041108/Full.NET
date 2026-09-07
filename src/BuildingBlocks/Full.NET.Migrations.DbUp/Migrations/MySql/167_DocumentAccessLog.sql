-- 167：文档访问日志表。

CREATE TABLE IF NOT EXISTS fn_document_access_log (
    Id char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL COMMENT '逻辑主键',
    DocumentItemId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL COMMENT '文档项标识',
    DocumentTitle varchar(256) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL COMMENT '文档标题',
    AccessTypeKey varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL COMMENT '访问类型键',
    SourceKey varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL COMMENT '来源键',
    ActorUserId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NULL COMMENT '操作者用户标识',
    OccurredAtUtc datetime(6) NOT NULL COMMENT '发生时间(UTC)',
    ClientIpFingerprint varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '客户端 IP 指纹',
    PRIMARY KEY (Id),
    CONSTRAINT FK_fn_document_access_log_Item
        FOREIGN KEY (DocumentItemId) REFERENCES fn_document_item(Id),
    CONSTRAINT CK_fn_document_access_log_AccessTypeKey
        CHECK (AccessTypeKey IN ('download', 'preview', 'share_access')),
    CONSTRAINT CK_fn_document_access_log_SourceKey
        CHECK (SourceKey IN ('authenticated', 'share'))
) COMMENT='文档访问日志表';

SET @indexExists := (
    SELECT COUNT(1)
    FROM information_schema.statistics
    WHERE table_schema = DATABASE()
      AND table_name = 'fn_document_access_log'
      AND index_name = 'IX_fn_document_access_log_OccurredAtUtc_Id'
);
SET @ddl := IF(
    @indexExists = 0,
    'CREATE INDEX IX_fn_document_access_log_OccurredAtUtc_Id ON fn_document_access_log (OccurredAtUtc, Id)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @indexExists := (
    SELECT COUNT(1)
    FROM information_schema.statistics
    WHERE table_schema = DATABASE()
      AND table_name = 'fn_document_access_log'
      AND index_name = 'IX_fn_document_access_log_DocumentItemId'
);
SET @ddl := IF(
    @indexExists = 0,
    'CREATE INDEX IX_fn_document_access_log_DocumentItemId ON fn_document_access_log (DocumentItemId, OccurredAtUtc)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
