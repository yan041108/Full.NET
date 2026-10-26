-- 290：静态结构探测收敛半完成 DDL，不使用动态 SQL 或重分配已有 claim。
DROP PROCEDURE IF EXISTS fn_workflow_human_views_recover;
DELIMITER $$
CREATE PROCEDURE fn_workflow_human_views_recover()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_form_submission_attachment' AND COLUMN_NAME='ClaimReferenceId') THEN
        ALTER TABLE fn_workflow_form_submission_attachment ADD COLUMN ClaimReferenceId BINARY(16) NULL COMMENT 'Files 引用预留的明确归属身份';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_form_submission_attachment' AND COLUMN_NAME='OwnerResourceId') THEN
        ALTER TABLE fn_workflow_form_submission_attachment ADD COLUMN OwnerResourceId BINARY(16) NULL COMMENT '租户文件所属资源身份，Host 文件为空';
    END IF;
    UPDATE fn_workflow_form_submission_attachment SET ClaimReferenceId=SubmissionId WHERE ClaimReferenceId IS NULL;
    IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_form_submission_attachment' AND COLUMN_NAME='ClaimReferenceId' AND IS_NULLABLE='YES') THEN
        ALTER TABLE fn_workflow_form_submission_attachment MODIFY COLUMN ClaimReferenceId BINARY(16) NOT NULL COMMENT 'Files 引用预留的明确归属身份';
    END IF;
    CREATE TABLE IF NOT EXISTS fn_workflow_todo_view (
        Id BINARY(16) NOT NULL COMMENT '逻辑主键，复用待办标识',
        InstanceId BINARY(16) NOT NULL COMMENT '工作流实例标识',
        TenantScopeKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '可信租户作用域键',
        InputJson longtext NOT NULL COMMENT '本次人工激活的固定完整输入',
        InputRevision bigint NOT NULL COMMENT '人工激活时的表单修订',
        OutputJson longtext NULL COMMENT '本人办理成功时的固定完整输出',
        OutputRevision bigint NULL COMMENT '本人办理成功时的表单修订',
        CONSTRAINT pk_fn_workflow_todo_view PRIMARY KEY (Id),
        CONSTRAINT fk_fn_workflow_todo_view_todo FOREIGN KEY (Id) REFERENCES fn_workflow_todo (Id),
        CONSTRAINT fk_fn_workflow_todo_view_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance (Id)
    ) COMMENT='工作流本人活动输入输出视图' ENGINE=InnoDB;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_form_submission_attachment' AND INDEX_NAME='ix_fn_workflow_form_submission_attachment_claim') THEN
        CREATE INDEX ix_fn_workflow_form_submission_attachment_claim ON fn_workflow_form_submission_attachment (ClaimReferenceId, FileId, TenantScopeKey);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_form_submission_attachment' AND INDEX_NAME='ix_fn_workflow_form_submission_attachment_file_owner') THEN
        CREATE INDEX ix_fn_workflow_form_submission_attachment_file_owner ON fn_workflow_form_submission_attachment (FileId, TenantScopeKey, InstanceId, OwnerResourceId);
    END IF;
    -- 不能为历史或缺乏完整分支输入证明的记录伪造快照，读取端保持拒绝。
    INSERT INTO fn_workflow_todo_view (Id, InstanceId, TenantScopeKey, InputJson, InputRevision)
    SELECT todo.Id, todo.InstanceId, instance.TenantScopeKey, submission.SubmissionJson, submission.Revision
    FROM fn_workflow_todo AS todo
    INNER JOIN fn_workflow_instance AS instance ON instance.Id=todo.InstanceId
    INNER JOIN fn_workflow_step AS step ON step.Id=todo.StepId
    INNER JOIN fn_workflow_form_submission AS submission ON submission.InstanceId=instance.Id
    WHERE todo.StatusKey='active' AND step.ParallelJoinId IS NULL
      AND NOT EXISTS (SELECT 1 FROM fn_workflow_todo_view AS existing WHERE existing.Id=todo.Id);
END$$
DELIMITER ;
CALL fn_workflow_human_views_recover();
DROP PROCEDURE IF EXISTS fn_workflow_human_views_recover;
