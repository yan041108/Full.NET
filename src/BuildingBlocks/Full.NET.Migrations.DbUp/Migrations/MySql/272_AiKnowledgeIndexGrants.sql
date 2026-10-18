-- 272：显式有界增量收费授权；expand 新表，不创建授权、不启动 Worker、不调用模型。
-- 回退应用前先关闭派发开关；保留授权及未知费用记录。每项 DDL 独立探测，可恢复未记账的部分部署。
CREATE TABLE IF NOT EXISTS fn_ai_index_grant (
    Id BINARY(16) NOT NULL COMMENT '授权逻辑主键',
    TenantId BINARY(16) NULL COMMENT '可信租户；宿主为空',
    KnowledgeBaseId BINARY(16) NOT NULL COMMENT '本模块知识库标识',
    OwnerUserId BINARY(16) NOT NULL COMMENT '原授权所有者标识',
    ClientRequestId BINARY(16) NOT NULL COMMENT '所有者在知识库内的幂等键',
    KnowledgeBaseVersion INT NOT NULL COMMENT '冻结的知识库及模型批准修订',
    ModelConfigId BINARY(16) NOT NULL COMMENT '原批准向量模型配置',
    ModelConfigVersion INT NOT NULL COMMENT '原批准模型配置修订',
    SessionJson TEXT NOT NULL COMMENT '冻结原交互会话绑定；不包含访问令牌且不可替换',
    ExpiresAtUtc DATETIME(6) NOT NULL COMMENT '原授权到期时间 UTC；不得隐式延长',
    DocumentLimit INT NOT NULL COMMENT '最多准入任务数；失败及未知不返还',
    AdmittedDocuments INT NOT NULL COMMENT '已消耗的任务准入次数',
    InputTokenLimitPerDocument BIGINT NOT NULL COMMENT '每任务累计输入预留上限；不是费用硬限',
    IsRevoked TINYINT(1) NOT NULL COMMENT '是否已由原所有者明确撤销',
    Version INT NOT NULL COMMENT '准入和撤销共享并发修订',
    CreatedAtUtc DATETIME(6) NOT NULL COMMENT '授权创建时间 UTC',
    CONSTRAINT PK_fn_ai_index_grant PRIMARY KEY (Id)
) COMMENT='所有者原会话的有界增量收费授权；默认关闭且不可接管' ENGINE=InnoDB;
SET @FnAiGrantDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_index_grant' AND CONSTRAINT_NAME='UX_fn_ai_index_grant_KnowledgeBaseId_OwnerUserId_ClientRequestId'), 'ALTER TABLE fn_ai_index_grant ADD CONSTRAINT UX_fn_ai_index_grant_KnowledgeBaseId_OwnerUserId_ClientRequestId UNIQUE (KnowledgeBaseId,OwnerUserId,ClientRequestId)', 'SELECT 1');
PREPARE FnAiGrantStatement FROM @FnAiGrantDdl;
EXECUTE FnAiGrantStatement;
DEALLOCATE PREPARE FnAiGrantStatement;
SET @FnAiGrantDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_index_grant' AND CONSTRAINT_NAME='CK_fn_ai_index_grant_State'), 'ALTER TABLE fn_ai_index_grant ADD CONSTRAINT CK_fn_ai_index_grant_State CHECK (KnowledgeBaseVersion>=1 AND ModelConfigVersion>=1 AND DocumentLimit BETWEEN 1 AND 64 AND AdmittedDocuments>=0 AND AdmittedDocuments<=DocumentLimit AND InputTokenLimitPerDocument BETWEEN 1 AND 1000000 AND Version>=1 AND IsRevoked IN (0,1) AND CHAR_LENGTH(SessionJson) BETWEEN 1 AND 4096)', 'SELECT 1');
PREPARE FnAiGrantStatement FROM @FnAiGrantDdl;
EXECUTE FnAiGrantStatement;
DEALLOCATE PREPARE FnAiGrantStatement;
SET @FnAiGrantDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_index_grant' AND CONSTRAINT_NAME='FK_fn_ai_index_grant_KnowledgeBaseId'), 'ALTER TABLE fn_ai_index_grant ADD CONSTRAINT FK_fn_ai_index_grant_KnowledgeBaseId FOREIGN KEY (KnowledgeBaseId) REFERENCES fn_ai_knowledge_base (Id)', 'SELECT 1');
PREPARE FnAiGrantStatement FROM @FnAiGrantDdl;
EXECUTE FnAiGrantStatement;
DEALLOCATE PREPARE FnAiGrantStatement;
SET @FnAiGrantDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_index_grant' AND CONSTRAINT_NAME='FK_fn_ai_index_grant_ModelConfigId'), 'ALTER TABLE fn_ai_index_grant ADD CONSTRAINT FK_fn_ai_index_grant_ModelConfigId FOREIGN KEY (ModelConfigId) REFERENCES fn_ai_model_config (Id)', 'SELECT 1');
PREPARE FnAiGrantStatement FROM @FnAiGrantDdl;
EXECUTE FnAiGrantStatement;
DEALLOCATE PREPARE FnAiGrantStatement;
SET @FnAiGrantDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_index_grant' AND INDEX_NAME='IX_fn_ai_index_grant_IsRevoked_ExpiresAtUtc_Id'), 'CREATE INDEX IX_fn_ai_index_grant_IsRevoked_ExpiresAtUtc_Id ON fn_ai_index_grant (IsRevoked,ExpiresAtUtc,Id)', 'SELECT 1');
PREPARE FnAiGrantStatement FROM @FnAiGrantDdl;
EXECUTE FnAiGrantStatement;
DEALLOCATE PREPARE FnAiGrantStatement;
SET @FnAiGrantDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_index_grant' AND INDEX_NAME='IX_fn_ai_index_grant_KnowledgeBaseId_OwnerUserId_Id'), 'CREATE INDEX IX_fn_ai_index_grant_KnowledgeBaseId_OwnerUserId_Id ON fn_ai_index_grant (KnowledgeBaseId,OwnerUserId,Id)', 'SELECT 1');
PREPARE FnAiGrantStatement FROM @FnAiGrantDdl;
EXECUTE FnAiGrantStatement;
DEALLOCATE PREPARE FnAiGrantStatement;
CREATE TABLE IF NOT EXISTS fn_ai_index_grant_item (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '与原授权一致的可信租户；宿主为空',
    GrantId BINARY(16) NOT NULL COMMENT '原增量收费授权标识',
    TaskId BINARY(16) NOT NULL COMMENT '唯一原索引任务标识；禁止接管其他代次',
    LastBatchOrdinal INT NOT NULL COMMENT '已顺序预留的最后批次序号；初始为负一',
    ReservedInputTokens BIGINT NOT NULL COMMENT '本任务已消耗的输入预留总量；不因未知或失败返还',
    CONSTRAINT PK_fn_ai_index_grant_item PRIMARY KEY (Id)
) COMMENT='增量授权与原索引任务的不可替换绑定及累计输入预留' ENGINE=InnoDB;
SET @FnAiGrantDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_index_grant_item' AND CONSTRAINT_NAME='UX_fn_ai_index_grant_item_TaskId'), 'ALTER TABLE fn_ai_index_grant_item ADD CONSTRAINT UX_fn_ai_index_grant_item_TaskId UNIQUE (TaskId)', 'SELECT 1');
PREPARE FnAiGrantStatement FROM @FnAiGrantDdl;
EXECUTE FnAiGrantStatement;
DEALLOCATE PREPARE FnAiGrantStatement;
SET @FnAiGrantDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_index_grant_item' AND CONSTRAINT_NAME='CK_fn_ai_index_grant_item_State'), 'ALTER TABLE fn_ai_index_grant_item ADD CONSTRAINT CK_fn_ai_index_grant_item_State CHECK (LastBatchOrdinal BETWEEN -1 AND 4095 AND ReservedInputTokens BETWEEN 0 AND 1000000)', 'SELECT 1');
PREPARE FnAiGrantStatement FROM @FnAiGrantDdl;
EXECUTE FnAiGrantStatement;
DEALLOCATE PREPARE FnAiGrantStatement;
SET @FnAiGrantDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_index_grant_item' AND CONSTRAINT_NAME='FK_fn_ai_index_grant_item_GrantId'), 'ALTER TABLE fn_ai_index_grant_item ADD CONSTRAINT FK_fn_ai_index_grant_item_GrantId FOREIGN KEY (GrantId) REFERENCES fn_ai_index_grant (Id)', 'SELECT 1');
PREPARE FnAiGrantStatement FROM @FnAiGrantDdl;
EXECUTE FnAiGrantStatement;
DEALLOCATE PREPARE FnAiGrantStatement;
SET @FnAiGrantDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_index_grant_item' AND CONSTRAINT_NAME='FK_fn_ai_index_grant_item_TaskId'), 'ALTER TABLE fn_ai_index_grant_item ADD CONSTRAINT FK_fn_ai_index_grant_item_TaskId FOREIGN KEY (TaskId) REFERENCES fn_ai_knowledge_index_task (Id)', 'SELECT 1');
PREPARE FnAiGrantStatement FROM @FnAiGrantDdl;
EXECUTE FnAiGrantStatement;
DEALLOCATE PREPARE FnAiGrantStatement;
SET @FnAiGrantDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_index_grant_item' AND INDEX_NAME='IX_fn_ai_index_grant_item_GrantId'), 'CREATE INDEX IX_fn_ai_index_grant_item_GrantId ON fn_ai_index_grant_item (GrantId)', 'SELECT 1');
PREPARE FnAiGrantStatement FROM @FnAiGrantDdl;
EXECUTE FnAiGrantStatement;
DEALLOCATE PREPARE FnAiGrantStatement;
