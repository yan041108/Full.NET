-- 发布治理与完整代次分离；仅访问 AI 自有表，所有迁移步骤支持未记账重跑。
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_generation (
    Id binary(16) NOT NULL COMMENT '原索引任务 UUID v7',
    DocumentId binary(16) NOT NULL COMMENT '所属文档',
    VersionId binary(16) NOT NULL COMMENT '不可变文件版本',
    PlanHash varchar(64) NOT NULL COMMENT '规划摘要',
    ManifestHash varchar(64) NOT NULL COMMENT '清单摘要',
    ReceiptHash varchar(64) NOT NULL COMMENT '完整回执摘要',
    ReceiptJson longtext NOT NULL COMMENT '有界完整计量回执',
    LeaseToken binary(16) NOT NULL COMMENT '原完成租约身份',
    ActivatedAtUtc datetime(6) NOT NULL COMMENT '完成时刻',
    CleanupState int NOT NULL COMMENT '零可读一清理中二已清理',
    CONSTRAINT PK_fn_ai_knowledge_generation PRIMARY KEY (Id)
) COMMENT='完整索引代次与清理状态' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
SET @KnowledgePublicationIndexSql=(SELECT IF(EXISTS(SELECT 1 FROM information_schema.statistics WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_generation' AND INDEX_NAME='IX_fn_ai_knowledge_generation_DocumentId_VersionId'),'SELECT 1','CREATE INDEX IX_fn_ai_knowledge_generation_DocumentId_VersionId ON fn_ai_knowledge_generation (DocumentId,VersionId)'));
PREPARE KnowledgePublicationIndexStatement FROM @KnowledgePublicationIndexSql;
EXECUTE KnowledgePublicationIndexStatement;
DEALLOCATE PREPARE KnowledgePublicationIndexStatement;
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_publication (
    Id binary(16) NOT NULL COMMENT '发布提案 UUID v7',
    DocumentId binary(16) NOT NULL COMMENT '所属文档',
    GenerationId binary(16) NOT NULL COMMENT '固定完整代次',
    VersionId binary(16) NOT NULL COMMENT '固定文件版本',
    RequestKey binary(16) NOT NULL COMMENT '原创建重试身份',
    ActorUserId binary(16) NOT NULL COMMENT '原维护主体',
    SessionId binary(16) NOT NULL COMMENT '原登录会话',
    SessionKind varchar(32) NOT NULL COMMENT '受信会话种类',
    SecurityStamp varchar(128) NOT NULL COMMENT '原安全戳',
    ActorScope varchar(32) NOT NULL COMMENT '原主体范围',
    EffectiveScope varchar(160) NOT NULL COMMENT '原生效范围',
    SnapshotJson longtext NOT NULL COMMENT '冻结来源与发布策略',
    SnapshotHash varchar(64) NOT NULL COMMENT '冻结策略摘要',
    StateKey varchar(32) NOT NULL COMMENT '待审核批准拒绝已发布',
    EffectiveFromUtc datetime(6) NOT NULL COMMENT '生效起点',
    EffectiveToUtc datetime(6) NOT NULL COMMENT '失效终点',
    ExpectedHeadVersion int NOT NULL COMMENT '审核绑定发布头版本',
    DocumentVersion int NOT NULL COMMENT '审核绑定当前维护版本',
    KnowledgeBaseVersion int NOT NULL COMMENT '审核绑定目录策略版本',
    ReviewerUserId binary(16) NULL COMMENT '独立审核主体',
    ReviewedAtUtc datetime(6) NULL COMMENT '人工决定时刻',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时刻',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '最后推进时刻',
    Version int NOT NULL COMMENT '审核并发版本',
    CONSTRAINT PK_fn_ai_knowledge_publication PRIMARY KEY (Id)
) COMMENT='冻结发布审核提案与不可变内容证明' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
SET @KnowledgePublicationIndexSql=(SELECT IF(EXISTS(SELECT 1 FROM information_schema.statistics WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_publication' AND INDEX_NAME='UX_fn_ai_knowledge_publication_DocumentId_RequestKey'),'SELECT 1','CREATE UNIQUE INDEX UX_fn_ai_knowledge_publication_DocumentId_RequestKey ON fn_ai_knowledge_publication (DocumentId,RequestKey)'));
PREPARE KnowledgePublicationIndexStatement FROM @KnowledgePublicationIndexSql;
EXECUTE KnowledgePublicationIndexStatement;
DEALLOCATE PREPARE KnowledgePublicationIndexStatement;
SET @KnowledgePublicationIndexSql=(SELECT IF(EXISTS(SELECT 1 FROM information_schema.statistics WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_publication' AND INDEX_NAME='IX_fn_ai_knowledge_publication_DocumentId_CreatedAtUtc'),'SELECT 1','CREATE INDEX IX_fn_ai_knowledge_publication_DocumentId_CreatedAtUtc ON fn_ai_knowledge_publication (DocumentId,CreatedAtUtc)'));
PREPARE KnowledgePublicationIndexStatement FROM @KnowledgePublicationIndexSql;
EXECUTE KnowledgePublicationIndexStatement;
DEALLOCATE PREPARE KnowledgePublicationIndexStatement;
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_publication_head (
    Id binary(16) NOT NULL COMMENT '原文档 UUID v7',
    PublicationId binary(16) NULL COMMENT '当前发布提案下架为空',
    Version int NOT NULL COMMENT '发布切换并发版本',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '切换时刻',
    CONSTRAINT PK_fn_ai_knowledge_publication_head PRIMARY KEY (Id)
) COMMENT='永久发布头与原子并发版本' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_read_lease (
    Id binary(16) NOT NULL COMMENT '读取租约 UUID v7',
    GenerationId binary(16) NOT NULL COMMENT '被保护完整代次',
    TenantId binary(16) NULL COMMENT '可信租户空为平台',
    ActorUserId binary(16) NOT NULL COMMENT '已授权读取主体',
    ExpiresAtUtc datetime(6) NOT NULL COMMENT '不可延长的读取期限',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '取得时刻',
    CONSTRAINT PK_fn_ai_knowledge_read_lease PRIMARY KEY (Id)
) COMMENT='有界代次读取保护不缓存授权' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
SET @KnowledgePublicationIndexSql=(SELECT IF(EXISTS(SELECT 1 FROM information_schema.statistics WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_read_lease' AND INDEX_NAME='IX_fn_ai_knowledge_read_lease_GenerationId_ExpiresAtUtc'),'SELECT 1','CREATE INDEX IX_fn_ai_knowledge_read_lease_GenerationId_ExpiresAtUtc ON fn_ai_knowledge_read_lease (GenerationId,ExpiresAtUtc)'));
PREPARE KnowledgePublicationIndexStatement FROM @KnowledgePublicationIndexSql;
EXECUTE KnowledgePublicationIndexStatement;
DEALLOCATE PREPARE KnowledgePublicationIndexStatement;
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_folder (
    Id binary(16) NOT NULL COMMENT '文件夹 UUID v7',
    KnowledgeBaseId binary(16) NOT NULL COMMENT '原知识库',
    ParentId binary(16) NULL COMMENT '同库父文件夹',
    Name varchar(200) NOT NULL COMMENT '人工名称',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时刻',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '维护时刻',
    Version int NOT NULL COMMENT '乐观并发版本',
    CONSTRAINT PK_fn_ai_knowledge_folder PRIMARY KEY (Id)
) COMMENT='仅组织内容不继承授权的库内文件夹' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
SET @KnowledgePublicationIndexSql=(SELECT IF(EXISTS(SELECT 1 FROM information_schema.statistics WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_folder' AND INDEX_NAME='IX_fn_ai_knowledge_folder_KnowledgeBaseId_ParentId'),'SELECT 1','CREATE INDEX IX_fn_ai_knowledge_folder_KnowledgeBaseId_ParentId ON fn_ai_knowledge_folder (KnowledgeBaseId,ParentId)'));
PREPARE KnowledgePublicationIndexStatement FROM @KnowledgePublicationIndexSql;
EXECUTE KnowledgePublicationIndexStatement;
DEALLOCATE PREPARE KnowledgePublicationIndexStatement;
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_directory (
    Id binary(16) NOT NULL COMMENT '原文档 UUID v7',
    FolderId binary(16) NULL COMMENT '同库组织位置',
    TagsJson varchar(8192) NOT NULL COMMENT '规范人工标签不授予权限',
    ResponsibleUserId binary(16) NULL COMMENT '当前同库负责人',
    LastRequestKey binary(16) NOT NULL COMMENT '最近一次维护重试身份',
    RequestHash varchar(64) NOT NULL COMMENT '规范维护请求摘要',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '维护时刻',
    Version int NOT NULL COMMENT '独立组织版本',
    CONSTRAINT PK_fn_ai_knowledge_directory PRIMARY KEY (Id)
) COMMENT='与索引版本独立的文档组织信息' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
SET @KnowledgePublicationIndexSql=(SELECT IF(EXISTS(SELECT 1 FROM information_schema.statistics WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_directory' AND INDEX_NAME='IX_fn_ai_knowledge_directory_FolderId'),'SELECT 1','CREATE INDEX IX_fn_ai_knowledge_directory_FolderId ON fn_ai_knowledge_directory (FolderId)'));
PREPARE KnowledgePublicationIndexStatement FROM @KnowledgePublicationIndexSql;
EXECUTE KnowledgePublicationIndexStatement;
DEALLOCATE PREPARE KnowledgePublicationIndexStatement;
-- 保留已有完整候选证明，但不将历史索引自动视为人工发布。
INSERT INTO fn_ai_knowledge_generation (Id,DocumentId,VersionId,PlanHash,ManifestHash,ReceiptHash,ReceiptJson,LeaseToken,ActivatedAtUtc,CleanupState)
SELECT active.IndexTaskId,active.DocumentId,active.VersionId,active.PlanHash,active.ManifestHash,active.ReceiptHash,active.ReceiptJson,active.LeaseToken,active.ActivatedAtUtc,0
FROM fn_ai_knowledge_active_generation AS active WHERE NOT EXISTS (SELECT 1 FROM fn_ai_knowledge_generation AS archived WHERE archived.Id=active.IndexTaskId);
-- 发布决定与头变更同事务保存最小不可变审计，不使用 Outbox。
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_publication_event (
 Id binary(16) NOT NULL COMMENT '事件 UUID v7',
 DocumentId binary(16) NOT NULL COMMENT '所属文档',
 PublicationId binary(16) NOT NULL COMMENT '原发布身份',
 ActorUserId binary(16) NOT NULL COMMENT '当前动作主体',
 SessionId binary(16) NOT NULL COMMENT '原受信会话',
 ActionKey varchar(32) NOT NULL COMMENT '稳定动作码',
 OccurredAtUtc datetime(6) NOT NULL COMMENT '发生时刻',
 CONSTRAINT PK_fn_ai_knowledge_publication_event PRIMARY KEY (Id)
) COMMENT='发布治理不可变动作审计' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
