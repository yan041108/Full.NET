-- 预览引用、尝试与删除事实分别持久化；每步结构探测支持未记账的半完成重跑。
CREATE TABLE IF NOT EXISTS fn_document_preview_reference (
        Id binary(16) NOT NULL COMMENT '不可复用的应用 UUID v7 引用标识',
        DocumentId binary(16) NOT NULL COMMENT '文档标识，无级联关系',
        TaskId binary(16) NOT NULL COMMENT '预览任务标识',
        FileId binary(16) NOT NULL COMMENT '源或预留输出文件标识',
        Purpose varchar(16) NOT NULL COMMENT 'source 或 output',
        StateKey varchar(24) NOT NULL COMMENT 'preparing claimed 或 released',
        ReleaseAfterUtc datetime(6) NULL COMMENT '有效租约保护截止时间',
        CreatedAtUtc datetime(6) NOT NULL COMMENT '意图创建时间',
        CONSTRAINT PK_fn_document_preview_reference PRIMARY KEY (Id)
    ) COMMENT='文档预览源及输出的独立引用意图';
CREATE TABLE IF NOT EXISTS fn_document_preview_attempt (
        Id binary(16) NOT NULL COMMENT '应用 UUID v7 尝试标识',
        TaskId binary(16) NOT NULL COMMENT '预览任务标识',
        ClaimVersion bigint NOT NULL COMMENT '领取版本，用于拒绝迟到执行者',
        StartedAtUtc datetime(6) NOT NULL COMMENT '本次领取时间',
        CompletedAtUtc datetime(6) NULL COMMENT '本次结束时间',
        ErrorCode varchar(128) NULL COMMENT '稳定终止原因',
        CONSTRAINT PK_fn_document_preview_attempt PRIMARY KEY (Id)
    ) COMMENT='文档预览领取尝试事实';
CREATE TABLE IF NOT EXISTS fn_document_deletion_fact (
        Id binary(16) NOT NULL COMMENT '应用 UUID v7 审计标识',
        DocumentId binary(16) NOT NULL COMMENT '被删除资源标识',
        ActorUserId binary(16) NOT NULL COMMENT '永久删除操作者',
        OccurredAtUtc datetime(6) NOT NULL COMMENT '开始永久删除时间',
        RetainUntilUtc datetime(6) NOT NULL COMMENT '最小事实保留截止时间',
        ReasonKey varchar(32) NOT NULL COMMENT '删除原因机器码',
        VersionCount bigint NOT NULL COMMENT '开始删除时版本数量',
        PreviewTaskCount bigint NOT NULL COMMENT '开始删除时预览任务数量',
        CONSTRAINT PK_fn_document_deletion_fact PRIMARY KEY (Id)
    ) COMMENT='文档永久删除最小事实，独立于父文档保留';
DROP PROCEDURE IF EXISTS fn_migrate_document_preview_lifecycle;
DELIMITER $$
CREATE PROCEDURE fn_migrate_document_preview_lifecycle()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'SourceReferenceId') THEN
        ALTER TABLE fn_document_preview_task ADD SourceReferenceId binary(16) NULL COMMENT '源文件独立引用意图';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'OutputReferenceId') THEN
        ALTER TABLE fn_document_preview_task ADD OutputReferenceId binary(16) NULL COMMENT '输出文件独立引用意图';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'OutputOwnerId') THEN
        ALTER TABLE fn_document_preview_task ADD OutputOwnerId binary(16) NULL COMMENT '固定的文档创建者';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'AttemptCount') THEN
        ALTER TABLE fn_document_preview_task ADD AttemptCount int NOT NULL DEFAULT 0 COMMENT '已领取次数，含崩溃重领';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'NextAttemptAtUtc') THEN
        ALTER TABLE fn_document_preview_task ADD NextAttemptAtUtc datetime(6) NULL COMMENT '失败退避后可领取时间';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_reference' AND INDEX_NAME = 'UX_fn_document_preview_reference_TaskId') THEN
        CREATE UNIQUE INDEX UX_fn_document_preview_reference_TaskId ON fn_document_preview_reference (TaskId, Purpose);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_reference' AND INDEX_NAME = 'IX_fn_document_preview_reference_FileId') THEN
        CREATE INDEX IX_fn_document_preview_reference_FileId ON fn_document_preview_reference (FileId, StateKey);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_attempt' AND INDEX_NAME = 'UX_fn_document_preview_attempt_TaskId') THEN
        CREATE UNIQUE INDEX UX_fn_document_preview_attempt_TaskId ON fn_document_preview_attempt (TaskId, ClaimVersion);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_deletion_fact' AND INDEX_NAME = 'UX_fn_document_deletion_fact_DocumentId') THEN
        CREATE UNIQUE INDEX UX_fn_document_deletion_fact_DocumentId ON fn_document_deletion_fact (DocumentId);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_deletion_fact' AND INDEX_NAME = 'IX_fn_document_deletion_fact_RetainUntilUtc') THEN
        CREATE INDEX IX_fn_document_deletion_fact_RetainUntilUtc ON fn_document_deletion_fact (RetainUntilUtc, Id);
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.CHECK_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'CK_fn_document_preview_task_StatusKey' AND CHECK_CLAUSE NOT LIKE '%preparing%') THEN
        ALTER TABLE fn_document_preview_task DROP CHECK CK_fn_document_preview_task_StatusKey;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.CHECK_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'CK_fn_document_preview_task_StatusKey') THEN
        ALTER TABLE fn_document_preview_task ADD CONSTRAINT CK_fn_document_preview_task_StatusKey CHECK (StatusKey IN ('preparing','pending','processing','succeeded','failed','cancelled'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'LeaseExpiresAtUtc') THEN
        ALTER TABLE fn_document_preview_task ADD LeaseExpiresAtUtc datetime(6) NULL COMMENT '领取时固定的租约截止，热配置不提前回收';
    END IF;
END$$
DELIMITER ;
CALL fn_migrate_document_preview_lifecycle();
DROP PROCEDURE IF EXISTS fn_migrate_document_preview_lifecycle;
UPDATE fn_document_preview_task SET StatusKey = 'failed', ErrorCode = 'document.preview.lifecycle_upgrade_required' WHERE SourceReferenceId IS NULL AND StatusKey IN ('pending', 'processing');
