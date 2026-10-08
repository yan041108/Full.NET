-- 256：仅保存固定标签和引用标识；不保存答案正文，不自动进入评估或模型输入。
-- 重放只恢复结构与说明；应用回退保留数据，物理删除消息级联清理本人反馈。
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_answer_feedback (
    Id binary(16) NOT NULL COMMENT '反馈逻辑主键',
    TenantId binary(16) NULL COMMENT '可信租户；宿主为空',
    SessionId binary(16) NOT NULL COMMENT '本人会话标识',
    MessageId binary(16) NOT NULL COMMENT '实际已完成知识答案标识',
    ActorUserId binary(16) NOT NULL COMMENT '提交用户标识',
    RatingKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '固定评价机器码',
    ReasonKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '固定原因机器码',
    CitationIdsJson varchar(512) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '实际答案引用标识数组',
    LineageHash char(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '完整答案依赖 SHA256 摘要',
    StatusKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '待审核或已撤回状态',
    Version int NOT NULL COMMENT '乐观并发版本',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间（UTC）',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间（UTC）',
    CONSTRAINT PK_fn_ai_knowledge_answer_feedback PRIMARY KEY (Id)
) COMMENT='本人知识答案未审核反馈；撤回清空载荷，删除消息级联清理' ENGINE=InnoDB;
SET @FnAiFeedbackDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_feedback' AND CONSTRAINT_NAME='UX_fn_ai_knowledge_answer_feedback_MessageId'), 'ALTER TABLE fn_ai_knowledge_answer_feedback ADD CONSTRAINT UX_fn_ai_knowledge_answer_feedback_MessageId UNIQUE (MessageId)', 'SELECT 1');
PREPARE stmt FROM @FnAiFeedbackDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiFeedbackDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_feedback' AND CONSTRAINT_NAME='FK_fn_ai_knowledge_answer_feedback_MessageId'), 'ALTER TABLE fn_ai_knowledge_answer_feedback ADD CONSTRAINT FK_fn_ai_knowledge_answer_feedback_MessageId FOREIGN KEY (MessageId) REFERENCES fn_ai_chat_message (Id) ON DELETE CASCADE', 'SELECT 1');
PREPARE stmt FROM @FnAiFeedbackDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiFeedbackDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_feedback' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_answer_feedback_State'), 'ALTER TABLE fn_ai_knowledge_answer_feedback ADD CONSTRAINT CK_fn_ai_knowledge_answer_feedback_State CHECK (Version>=1 AND JSON_VALID(CitationIdsJson)=1 AND JSON_TYPE(CitationIdsJson)=''ARRAY'' AND ((StatusKey=''pending_review'' AND OCTET_LENGTH(StatusKey)=14 AND RatingKey IS NOT NULL AND ReasonKey IS NOT NULL AND LineageHash IS NOT NULL AND OCTET_LENGTH(LineageHash)=64 AND LineageHash NOT REGEXP ''[^0-9a-f]'' AND ((RatingKey=''helpful'' AND OCTET_LENGTH(RatingKey)=7 AND ReasonKey IN (''accurate'',''useful'')) OR (RatingKey=''unhelpful'' AND OCTET_LENGTH(RatingKey)=9 AND ReasonKey IN (''incomplete'',''unsupported'',''outdated'',''other''))) AND OCTET_LENGTH(ReasonKey)=CASE ReasonKey WHEN ''accurate'' THEN 8 WHEN ''useful'' THEN 6 WHEN ''incomplete'' THEN 10 WHEN ''unsupported'' THEN 11 WHEN ''outdated'' THEN 8 WHEN ''other'' THEN 5 ELSE 0 END) OR (StatusKey=''withdrawn'' AND OCTET_LENGTH(StatusKey)=9 AND RatingKey IS NULL AND ReasonKey IS NULL AND LineageHash IS NULL AND CitationIdsJson=''[]'' AND OCTET_LENGTH(CitationIdsJson)=2)))', 'SELECT 1');
PREPARE stmt FROM @FnAiFeedbackDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiFeedbackDdl := IF(EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_feedback' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_answer_feedback_State' AND ENFORCED='NO'), 'ALTER TABLE fn_ai_knowledge_answer_feedback ALTER CHECK CK_fn_ai_knowledge_answer_feedback_State ENFORCED', 'SELECT 1');
PREPARE stmt FROM @FnAiFeedbackDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiFeedbackDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_feedback' AND COLUMN_NAME='Id' AND COLUMN_COMMENT<>'反馈逻辑主键'), 'ALTER TABLE fn_ai_knowledge_answer_feedback MODIFY COLUMN Id binary(16) NOT NULL COMMENT ''反馈逻辑主键''', 'SELECT 1');
PREPARE stmt FROM @FnAiFeedbackDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiFeedbackDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_feedback' AND COLUMN_NAME='TenantId' AND COLUMN_COMMENT<>'可信租户；宿主为空'), 'ALTER TABLE fn_ai_knowledge_answer_feedback MODIFY COLUMN TenantId binary(16) NULL COMMENT ''可信租户；宿主为空''', 'SELECT 1');
PREPARE stmt FROM @FnAiFeedbackDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiFeedbackDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_feedback' AND COLUMN_NAME='SessionId' AND COLUMN_COMMENT<>'本人会话标识'), 'ALTER TABLE fn_ai_knowledge_answer_feedback MODIFY COLUMN SessionId binary(16) NOT NULL COMMENT ''本人会话标识''', 'SELECT 1');
PREPARE stmt FROM @FnAiFeedbackDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiFeedbackDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_feedback' AND COLUMN_NAME='MessageId' AND COLUMN_COMMENT<>'实际已完成知识答案标识'), 'ALTER TABLE fn_ai_knowledge_answer_feedback MODIFY COLUMN MessageId binary(16) NOT NULL COMMENT ''实际已完成知识答案标识''', 'SELECT 1');
PREPARE stmt FROM @FnAiFeedbackDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiFeedbackDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_feedback' AND COLUMN_NAME='ActorUserId' AND COLUMN_COMMENT<>'提交用户标识'), 'ALTER TABLE fn_ai_knowledge_answer_feedback MODIFY COLUMN ActorUserId binary(16) NOT NULL COMMENT ''提交用户标识''', 'SELECT 1');
PREPARE stmt FROM @FnAiFeedbackDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiFeedbackDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_feedback' AND COLUMN_NAME='RatingKey' AND COLUMN_COMMENT<>'固定评价机器码'), 'ALTER TABLE fn_ai_knowledge_answer_feedback MODIFY COLUMN RatingKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT ''固定评价机器码''', 'SELECT 1');
PREPARE stmt FROM @FnAiFeedbackDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiFeedbackDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_feedback' AND COLUMN_NAME='ReasonKey' AND COLUMN_COMMENT<>'固定原因机器码'), 'ALTER TABLE fn_ai_knowledge_answer_feedback MODIFY COLUMN ReasonKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT ''固定原因机器码''', 'SELECT 1');
PREPARE stmt FROM @FnAiFeedbackDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiFeedbackDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_feedback' AND COLUMN_NAME='CitationIdsJson' AND COLUMN_COMMENT<>'实际答案引用标识数组'), 'ALTER TABLE fn_ai_knowledge_answer_feedback MODIFY COLUMN CitationIdsJson varchar(512) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT ''实际答案引用标识数组''', 'SELECT 1');
PREPARE stmt FROM @FnAiFeedbackDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiFeedbackDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_feedback' AND COLUMN_NAME='LineageHash' AND COLUMN_COMMENT<>'完整答案依赖 SHA256 摘要'), 'ALTER TABLE fn_ai_knowledge_answer_feedback MODIFY COLUMN LineageHash char(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT ''完整答案依赖 SHA256 摘要''', 'SELECT 1');
PREPARE stmt FROM @FnAiFeedbackDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiFeedbackDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_feedback' AND COLUMN_NAME='StatusKey' AND COLUMN_COMMENT<>'待审核或已撤回状态'), 'ALTER TABLE fn_ai_knowledge_answer_feedback MODIFY COLUMN StatusKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT ''待审核或已撤回状态''', 'SELECT 1');
PREPARE stmt FROM @FnAiFeedbackDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiFeedbackDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_feedback' AND COLUMN_NAME='Version' AND COLUMN_COMMENT<>'乐观并发版本'), 'ALTER TABLE fn_ai_knowledge_answer_feedback MODIFY COLUMN Version int NOT NULL COMMENT ''乐观并发版本''', 'SELECT 1');
PREPARE stmt FROM @FnAiFeedbackDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiFeedbackDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_feedback' AND COLUMN_NAME='CreatedAtUtc' AND COLUMN_COMMENT<>'创建时间（UTC）'), 'ALTER TABLE fn_ai_knowledge_answer_feedback MODIFY COLUMN CreatedAtUtc datetime(6) NOT NULL COMMENT ''创建时间（UTC）''', 'SELECT 1');
PREPARE stmt FROM @FnAiFeedbackDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiFeedbackDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_feedback' AND COLUMN_NAME='UpdatedAtUtc' AND COLUMN_COMMENT<>'更新时间（UTC）'), 'ALTER TABLE fn_ai_knowledge_answer_feedback MODIFY COLUMN UpdatedAtUtc datetime(6) NOT NULL COMMENT ''更新时间（UTC）''', 'SELECT 1');
PREPARE stmt FROM @FnAiFeedbackDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
ALTER TABLE fn_ai_knowledge_answer_feedback COMMENT='本人知识答案未审核反馈；撤回清空载荷，删除消息级联清理';
