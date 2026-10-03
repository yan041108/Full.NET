-- 243：文件版本不可变，导入日志先于外部 I/O 提交；建表、索引和注释逐项恢复。
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_version (
    Id binary(16) NOT NULL COMMENT '逻辑主键',
    DocumentId binary(16) NOT NULL COMMENT '所属文档标识',
    FileId binary(16) NOT NULL COMMENT 'Files 权威引用标识',
    VersionNumber int NOT NULL COMMENT '文档内连续文件版本号',
    OriginalFileName varchar(255) NOT NULL COMMENT '安全来源文件名',
    ContentType varchar(128) NOT NULL COMMENT '经校验的内容类型',
    ContentHash varchar(64) NOT NULL COMMENT '实际内容 SHA-256',
    SizeBytes bigint NOT NULL COMMENT '实际内容字节数',
    UploadedByUserId binary(16) NOT NULL COMMENT '上传主体标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间（UTC）',
    CONSTRAINT PK_fn_ai_knowledge_version PRIMARY KEY  (Id),
    CONSTRAINT FK_fn_ai_knowledge_version_Document FOREIGN KEY (DocumentId) REFERENCES fn_ai_knowledge_document (Id),
    CONSTRAINT UQ_fn_ai_knowledge_version_DocNumber UNIQUE (DocumentId, VersionNumber),
    CONSTRAINT CK_fn_ai_knowledge_version_Number CHECK (VersionNumber > 0),
    CONSTRAINT CK_fn_ai_knowledge_version_Size CHECK (SizeBytes > 0 AND SizeBytes <= 8388608)
) COMMENT='知识库不可变文件版本' ENGINE=InnoDB;
SET @knowledge_version_index_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_knowledge_version' AND INDEX_NAME = 'IX_fn_ai_knowledge_version_DocList');
SET @knowledge_version_ddl := IF(@knowledge_version_index_exists = 0, 'CREATE INDEX IX_fn_ai_knowledge_version_DocList ON fn_ai_knowledge_version (DocumentId, CreatedAtUtc DESC, Id DESC)', 'SELECT 1');
PREPARE stmt FROM @knowledge_version_ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_import (
    Id binary(16) NOT NULL COMMENT '逻辑主键',
    DocumentId binary(16) NOT NULL COMMENT '所属文档标识',
    VersionId binary(16) NOT NULL COMMENT '预先生成的版本标识',
    RequestKey binary(16) NOT NULL COMMENT '文档内稳定重试键',
    ExpectedDocumentVersion int NOT NULL COMMENT '请求预期文档并发版本',
    OriginalFileName varchar(255) NOT NULL COMMENT '安全来源文件名',
    ContentType varchar(128) NOT NULL COMMENT '经校验的内容类型',
    ContentHash varchar(64) NOT NULL COMMENT '重试匹配内容摘要',
    SizeBytes bigint NOT NULL COMMENT '重试匹配实际字节数',
    ActorUserId binary(16) NOT NULL COMMENT '授权上传主体',
    FileId binary(16) NULL COMMENT '已上传待绑定文件',
    StateKey varchar(32) NOT NULL COMMENT '持久化导入阶段',
    FailureCode varchar(160) NULL COMMENT '稳定失败码',
    LeaseToken binary(16) NULL COMMENT '有界执行租约令牌',
    LeaseExpiresAtUtc datetime(6) NULL COMMENT '租约过期时间（UTC）',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间（UTC）',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间（UTC）',
    CONSTRAINT PK_fn_ai_knowledge_import PRIMARY KEY  (Id),
    CONSTRAINT FK_fn_ai_knowledge_import_Document FOREIGN KEY (DocumentId) REFERENCES fn_ai_knowledge_document (Id),
    CONSTRAINT UQ_fn_ai_knowledge_import_DocRequest UNIQUE (DocumentId, RequestKey),
    CONSTRAINT UQ_fn_ai_knowledge_import_Version UNIQUE (VersionId),
    CONSTRAINT CK_fn_ai_knowledge_import_Version CHECK (ExpectedDocumentVersion > 0),
    CONSTRAINT CK_fn_ai_knowledge_import_Size CHECK (SizeBytes > 0 AND SizeBytes <= 8388608),
    CONSTRAINT CK_fn_ai_knowledge_import_State CHECK (StateKey IN ('awaiting_upload','file_uploaded','pending_confirmation','completed','cleanup_pending','failed')),
    CONSTRAINT CK_fn_ai_knowledge_import_Lease CHECK ((LeaseToken IS NULL AND LeaseExpiresAtUtc IS NULL) OR (LeaseToken IS NOT NULL AND LeaseExpiresAtUtc IS NOT NULL))
) COMMENT='知识库持久化导入及文件补偿日志' ENGINE=InnoDB;
SET @knowledge_version_index_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_knowledge_import' AND INDEX_NAME = 'IX_fn_ai_knowledge_import_DocState');
SET @knowledge_version_ddl := IF(@knowledge_version_index_exists = 0, 'CREATE INDEX IX_fn_ai_knowledge_import_DocState ON fn_ai_knowledge_import (DocumentId, StateKey, CreatedAtUtc, Id)', 'SELECT 1');
PREPARE stmt FROM @knowledge_version_ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
