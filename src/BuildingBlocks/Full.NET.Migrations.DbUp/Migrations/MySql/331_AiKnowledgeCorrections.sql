-- 校对原解析、不可变人工修订与独立 CAS 头；不回填或自动发布内容。
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_correction_source (
    Id binary(16) NOT NULL COMMENT '解析快照 UUID v7',
    VersionId binary(16) NOT NULL COMMENT '所属不可变文件版本',
    RequestKey binary(16) NOT NULL COMMENT '原解析请求重试身份',
    RequestHash varchar(64) NOT NULL COMMENT '原规范解析请求摘要',
    FileHash varchar(64) NOT NULL COMMENT '原文件完整字节摘要',
    ParserRuntimeId varchar(128) NOT NULL COMMENT '服务端冻结的解析运行时',
    SourceHash varchar(64) NOT NULL COMMENT '完整解析及运行时共同指纹',
    OriginalPayload longblob NOT NULL COMMENT '完整原解析线格式，最多十六 MiB',
    InitialPayload longblob NOT NULL COMMENT '初始工作台分块线格式，最多十二 MiB',
    InitialPayloadHash varchar(64) NOT NULL COMMENT '初始工作台分块载荷摘要',
    ActorUserId binary(16) NOT NULL COMMENT '原解析操作者，不作为授权缓存',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '解析快照创建时间 UTC',
    CleanupState int NOT NULL COMMENT '零可读一清理中二正文已清理',
    CONSTRAINT PK_fn_ai_knowledge_correction_source PRIMARY KEY (Id),
    CONSTRAINT CK_fn_ai_knowledge_correction_source_Bounds CHECK (OCTET_LENGTH(OriginalPayload)<=16777216 AND OCTET_LENGTH(InitialPayload)<=12582912 AND CleanupState IN (0,1,2))
) COMMENT='不可变原解析及首次工作台分块快照，不授予来源权限' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
SET @KnowledgeCorrectionIndexSql=(SELECT IF(EXISTS(SELECT 1 FROM information_schema.statistics WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_correction_source' AND INDEX_NAME='UX_fn_ai_knowledge_correction_source_VersionId_RequestKey'),'SELECT 1','CREATE UNIQUE INDEX UX_fn_ai_knowledge_correction_source_VersionId_RequestKey ON fn_ai_knowledge_correction_source (VersionId,RequestKey)'));
PREPARE KnowledgeCorrectionIndexStatement FROM @KnowledgeCorrectionIndexSql;
EXECUTE KnowledgeCorrectionIndexStatement;
DEALLOCATE PREPARE KnowledgeCorrectionIndexStatement;
SET @KnowledgeCorrectionIndexSql=(SELECT IF(EXISTS(SELECT 1 FROM information_schema.statistics WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_correction_source' AND INDEX_NAME='IX_fn_ai_knowledge_correction_source_VersionId_CreatedAtUtc_Id'),'SELECT 1','CREATE INDEX IX_fn_ai_knowledge_correction_source_VersionId_CreatedAtUtc_Id ON fn_ai_knowledge_correction_source (VersionId,CreatedAtUtc,Id)'));
PREPARE KnowledgeCorrectionIndexStatement FROM @KnowledgeCorrectionIndexSql;
EXECUTE KnowledgeCorrectionIndexStatement;
DEALLOCATE PREPARE KnowledgeCorrectionIndexStatement;
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_correction_revision (
    Id binary(16) NOT NULL COMMENT '人工修订 UUID v7',
    VersionId binary(16) NOT NULL COMMENT '所属不可变文件版本',
    SourceId binary(16) NOT NULL COMMENT '固定原解析快照',
    ParentId binary(16) NULL COMMENT '父修订，首次修订为空',
    RequestKey binary(16) NOT NULL COMMENT '原保存或恢复请求重试身份',
    RequestHash varchar(64) NOT NULL COMMENT '原规范请求摘要',
    Payload longblob NOT NULL COMMENT '规范修订线格式，最多十二 MiB',
    PayloadHash varchar(64) NOT NULL COMMENT '完整修订载荷摘要',
    ReasonText varchar(1000) NOT NULL COMMENT '人工说明，仅授权维护者可读',
    ActorUserId binary(16) NOT NULL COMMENT '实际修改者，不接受客户端声明',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '修订创建时间 UTC',
    DocumentVersion int NOT NULL COMMENT '提交后的文档维护版本',
    HeadVersion int NOT NULL COMMENT '提交后的独立修订头版本',
    CleanupState int NOT NULL COMMENT '零可读一清理中二正文已清理',
    CONSTRAINT PK_fn_ai_knowledge_correction_revision PRIMARY KEY (Id),
    CONSTRAINT CK_fn_ai_knowledge_correction_revision_Bounds CHECK (OCTET_LENGTH(Payload)<=12582912 AND CleanupState IN (0,1,2) AND DocumentVersion>0 AND HeadVersion>0)
) COMMENT='不可变人工校对修订及审计，不直接代表已发布' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
SET @KnowledgeCorrectionIndexSql=(SELECT IF(EXISTS(SELECT 1 FROM information_schema.statistics WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_correction_revision' AND INDEX_NAME='UX_fn_ai_knowledge_correction_revision_VersionId_RequestKey'),'SELECT 1','CREATE UNIQUE INDEX UX_fn_ai_knowledge_correction_revision_VersionId_RequestKey ON fn_ai_knowledge_correction_revision (VersionId,RequestKey)'));
PREPARE KnowledgeCorrectionIndexStatement FROM @KnowledgeCorrectionIndexSql;
EXECUTE KnowledgeCorrectionIndexStatement;
DEALLOCATE PREPARE KnowledgeCorrectionIndexStatement;
SET @KnowledgeCorrectionIndexSql=(SELECT IF(EXISTS(SELECT 1 FROM information_schema.statistics WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_correction_revision' AND INDEX_NAME='UX_fn_ai_knowledge_correction_revision_VersionId_HeadVersion'),'SELECT 1','CREATE UNIQUE INDEX UX_fn_ai_knowledge_correction_revision_VersionId_HeadVersion ON fn_ai_knowledge_correction_revision (VersionId,HeadVersion)'));
PREPARE KnowledgeCorrectionIndexStatement FROM @KnowledgeCorrectionIndexSql;
EXECUTE KnowledgeCorrectionIndexStatement;
DEALLOCATE PREPARE KnowledgeCorrectionIndexStatement;
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_correction_head (
    Id binary(16) NOT NULL COMMENT '文件版本 UUID，作为独立修订头身份',
    SourceId binary(16) NOT NULL COMMENT '当前工作台原解析快照',
    RevisionId binary(16) NULL COMMENT '当前人工修订，尚未修改时为空',
    Version int NOT NULL COMMENT '独立 CAS 版本，首次解析后为零',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '修订头更新时间 UTC',
    CONSTRAINT PK_fn_ai_knowledge_correction_head PRIMARY KEY (Id),
    CONSTRAINT CK_fn_ai_knowledge_correction_head_Bounds CHECK (Version>=0)
) COMMENT='每文件版本的人工修订头，不自动切换发布或重解析结果' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
