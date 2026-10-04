-- 248：InnoDB 原子创建本地会话约束；不补造旧任务身份，不保存令牌。
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_index_session (
    Id binary(16) NOT NULL COMMENT '逻辑主键',
    IndexTaskId binary(16) NOT NULL COMMENT '所属索引任务代次',
    ActorUserId binary(16) NOT NULL COMMENT '冻结执行者标识',
    TenantId binary(16) NULL COMMENT '可信租户标识',
    SessionId binary(16) NOT NULL COMMENT '原后台会话标识',
    SessionKind varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '后台会话种类',
    SecurityStamp varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '冻结安全戳',
    ActorScope varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '原会话执行者范围',
    EffectiveScope varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '原会话有效范围',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间（UTC）',
    CONSTRAINT PK_fn_ai_knowledge_index_session PRIMARY KEY (Id),
    CONSTRAINT FK_fn_ai_knowledge_index_session_IndexTaskId FOREIGN KEY (IndexTaskId) REFERENCES fn_ai_knowledge_index_task (Id),
    CONSTRAINT UX_fn_ai_knowledge_index_session_IndexTaskId UNIQUE (IndexTaskId),
    CONSTRAINT CK_fn_ai_knowledge_index_session_SessionKind CHECK (SessionKind IN ('refresh','oidc-application'))
) COMMENT='知识库索引任务不可替换的会话绑定' ENGINE=InnoDB;
-- 仅选择固定 CREATE UNIQUE INDEX/SELECT 1 恢复，不拼接业务输入。
SET @index_session_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_session' AND INDEX_NAME='UX_fn_ai_knowledge_index_session_IndexTaskId');
SET @index_session_ddl := IF(@index_session_exists=0, 'CREATE UNIQUE INDEX UX_fn_ai_knowledge_index_session_IndexTaskId ON fn_ai_knowledge_index_session (IndexTaskId)', 'SELECT 1');
PREPARE stmt FROM @index_session_ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
