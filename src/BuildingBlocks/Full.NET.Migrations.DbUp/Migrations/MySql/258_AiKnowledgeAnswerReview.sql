-- 258：仅审核固定标签，不保存或释放正文；反馈修改或撤回使旧审核绑定失效。
-- 重放恢复结构和说明，不重置审核版本；应用回退保留数据，删除反馈级联清理。
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_answer_review (
    Id binary(16) NOT NULL COMMENT '审核逻辑主键',
    TenantId binary(16) NULL COMMENT '可信租户；宿主为空',
    FeedbackId binary(16) NOT NULL COMMENT '同模块反馈标识',
    FeedbackVersion int NOT NULL COMMENT '本次审核的反馈版本',
    LineageHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '本次审核的完整来源摘要',
    ReviewerUserId binary(16) NOT NULL COMMENT '独立评审者标识',
    DecisionKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '固定批准或拒绝机器码',
    Version int NOT NULL COMMENT '单调递增审核并发版本',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间（UTC）',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间（UTC）',
    CONSTRAINT PK_fn_ai_knowledge_answer_review PRIMARY KEY (Id)
) COMMENT='独立反馈标签审核；绑定反馈版本与来源，删除反馈级联清理' ENGINE=InnoDB;
SET @FnAiReviewDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_review' AND CONSTRAINT_NAME='UX_fn_ai_knowledge_answer_review_FeedbackId'), 'ALTER TABLE fn_ai_knowledge_answer_review ADD CONSTRAINT UX_fn_ai_knowledge_answer_review_FeedbackId UNIQUE (FeedbackId)', 'SELECT 1');
PREPARE stmt FROM @FnAiReviewDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiReviewDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_review' AND CONSTRAINT_NAME='FK_fn_ai_knowledge_answer_review_FeedbackId'), 'ALTER TABLE fn_ai_knowledge_answer_review ADD CONSTRAINT FK_fn_ai_knowledge_answer_review_FeedbackId FOREIGN KEY (FeedbackId) REFERENCES fn_ai_knowledge_answer_feedback (Id) ON DELETE CASCADE', 'SELECT 1');
PREPARE stmt FROM @FnAiReviewDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiReviewDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_review' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_answer_review_State'), 'ALTER TABLE fn_ai_knowledge_answer_review ADD CONSTRAINT CK_fn_ai_knowledge_answer_review_State CHECK (Version>=1 AND FeedbackVersion>=1 AND DecisionKey IN (''approved'',''rejected'') AND OCTET_LENGTH(DecisionKey)=8 AND OCTET_LENGTH(LineageHash)=64 AND LineageHash NOT REGEXP ''[^0-9a-f]'')', 'SELECT 1');
PREPARE stmt FROM @FnAiReviewDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiReviewDdl := IF(EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_review' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_answer_review_State' AND ENFORCED='NO'), 'ALTER TABLE fn_ai_knowledge_answer_review ALTER CHECK CK_fn_ai_knowledge_answer_review_State ENFORCED', 'SELECT 1');
PREPARE stmt FROM @FnAiReviewDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiReviewDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_review' AND COLUMN_NAME='Id' AND COLUMN_COMMENT<>'审核逻辑主键'), 'ALTER TABLE fn_ai_knowledge_answer_review MODIFY COLUMN Id binary(16) NOT NULL COMMENT ''审核逻辑主键''', 'SELECT 1');
PREPARE stmt FROM @FnAiReviewDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiReviewDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_review' AND COLUMN_NAME='TenantId' AND COLUMN_COMMENT<>'可信租户；宿主为空'), 'ALTER TABLE fn_ai_knowledge_answer_review MODIFY COLUMN TenantId binary(16) NULL COMMENT ''可信租户；宿主为空''', 'SELECT 1');
PREPARE stmt FROM @FnAiReviewDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiReviewDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_review' AND COLUMN_NAME='FeedbackId' AND COLUMN_COMMENT<>'同模块反馈标识'), 'ALTER TABLE fn_ai_knowledge_answer_review MODIFY COLUMN FeedbackId binary(16) NOT NULL COMMENT ''同模块反馈标识''', 'SELECT 1');
PREPARE stmt FROM @FnAiReviewDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiReviewDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_review' AND COLUMN_NAME='FeedbackVersion' AND COLUMN_COMMENT<>'本次审核的反馈版本'), 'ALTER TABLE fn_ai_knowledge_answer_review MODIFY COLUMN FeedbackVersion int NOT NULL COMMENT ''本次审核的反馈版本''', 'SELECT 1');
PREPARE stmt FROM @FnAiReviewDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiReviewDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_review' AND COLUMN_NAME='LineageHash' AND COLUMN_COMMENT<>'本次审核的完整来源摘要'), 'ALTER TABLE fn_ai_knowledge_answer_review MODIFY COLUMN LineageHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT ''本次审核的完整来源摘要''', 'SELECT 1');
PREPARE stmt FROM @FnAiReviewDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiReviewDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_review' AND COLUMN_NAME='ReviewerUserId' AND COLUMN_COMMENT<>'独立评审者标识'), 'ALTER TABLE fn_ai_knowledge_answer_review MODIFY COLUMN ReviewerUserId binary(16) NOT NULL COMMENT ''独立评审者标识''', 'SELECT 1');
PREPARE stmt FROM @FnAiReviewDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiReviewDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_review' AND COLUMN_NAME='DecisionKey' AND COLUMN_COMMENT<>'固定批准或拒绝机器码'), 'ALTER TABLE fn_ai_knowledge_answer_review MODIFY COLUMN DecisionKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT ''固定批准或拒绝机器码''', 'SELECT 1');
PREPARE stmt FROM @FnAiReviewDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiReviewDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_review' AND COLUMN_NAME='Version' AND COLUMN_COMMENT<>'单调递增审核并发版本'), 'ALTER TABLE fn_ai_knowledge_answer_review MODIFY COLUMN Version int NOT NULL COMMENT ''单调递增审核并发版本''', 'SELECT 1');
PREPARE stmt FROM @FnAiReviewDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiReviewDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_review' AND COLUMN_NAME='CreatedAtUtc' AND COLUMN_COMMENT<>'创建时间（UTC）'), 'ALTER TABLE fn_ai_knowledge_answer_review MODIFY COLUMN CreatedAtUtc datetime(6) NOT NULL COMMENT ''创建时间（UTC）''', 'SELECT 1');
PREPARE stmt FROM @FnAiReviewDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiReviewDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_review' AND COLUMN_NAME='UpdatedAtUtc' AND COLUMN_COMMENT<>'更新时间（UTC）'), 'ALTER TABLE fn_ai_knowledge_answer_review MODIFY COLUMN UpdatedAtUtc datetime(6) NOT NULL COMMENT ''更新时间（UTC）''', 'SELECT 1');
PREPARE stmt FROM @FnAiReviewDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
ALTER TABLE fn_ai_knowledge_answer_review COMMENT='独立反馈标签审核；绑定反馈版本与来源，删除反馈级联清理';
