-- DDL 可能在记账前独立提交；按实际结构恢复，再补齐系统操作人约束。
DROP PROCEDURE IF EXISTS fn_workflow_restore_system_audit_305;
DELIMITER $$
CREATE PROCEDURE fn_workflow_restore_system_audit_305()
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_domain_audit' AND COLUMN_NAME='ActorUserId' AND IS_NULLABLE='NO') THEN
        ALTER TABLE fn_workflow_domain_audit MODIFY COLUMN ActorUserId BINARY(16) NULL COMMENT '人工操作人标识；系统选人裁定为空';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_domain_audit' AND CONSTRAINT_NAME='ck_fn_workflow_domain_audit_actor' AND CONSTRAINT_TYPE='CHECK') THEN
        ALTER TABLE fn_workflow_domain_audit ADD CONSTRAINT ck_fn_workflow_domain_audit_actor
            CHECK (ActorUserId IS NOT NULL OR (OperationKey='assignee.handle' AND ResourceTypeKey='step'));
    END IF;
END$$
DELIMITER ;
CALL fn_workflow_restore_system_audit_305();
DROP PROCEDURE IF EXISTS fn_workflow_restore_system_audit_305;
