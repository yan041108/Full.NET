-- 103：补齐已发布流程定义对不可变表单版本的强制绑定。
DROP PROCEDURE IF EXISTS fn_workflow_definition_form_version_binding;
DELIMITER $$
CREATE PROCEDURE fn_workflow_definition_form_version_binding()
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_workflow_definition_version'
          AND COLUMN_NAME = 'FormVersionId') THEN
        ALTER TABLE fn_workflow_definition_version
            ADD COLUMN FormVersionId BINARY(16) NULL COMMENT '固定绑定的表单版本标识' AFTER DefinitionId;
    END IF;

    -- 102 尚未开放定义发布 API；若环境存在绕过应用写入的历史版本，必须人工确认绑定而不是猜测回填。
    IF EXISTS (SELECT 1 FROM fn_workflow_definition_version WHERE FormVersionId IS NULL) THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Workflow definition versions require an explicit FormVersionId before migration 103 can continue.';
    END IF;

    ALTER TABLE fn_workflow_definition_version MODIFY FormVersionId BINARY(16) NOT NULL COMMENT '固定绑定的表单版本标识';

    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_workflow_definition_version'
          AND CONSTRAINT_NAME = 'FK_fn_workflow_definition_version_FormVersion') THEN
        ALTER TABLE fn_workflow_definition_version
            ADD CONSTRAINT FK_fn_workflow_definition_version_FormVersion
            FOREIGN KEY (FormVersionId) REFERENCES fn_workflow_form_version(Id);
    END IF;
END$$
DELIMITER ;

CALL fn_workflow_definition_form_version_binding();
DROP PROCEDURE fn_workflow_definition_form_version_binding;
