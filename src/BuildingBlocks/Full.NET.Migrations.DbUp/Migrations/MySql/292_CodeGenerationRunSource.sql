-- 292：用单条原子 ALTER 替换来源约束，允许冻结内联 Schema，不接受残缺模板对。
-- 旧约束已存在、仅删除一个或均已删除时都可重跑；DDL 失败不删除业务行。
DROP PROCEDURE IF EXISTS fn_codegeneration_run_source_boundary;
DELIMITER $$
CREATE PROCEDURE fn_codegeneration_run_source_boundary()
BEGIN
    DECLARE drops varchar(256) DEFAULT '';
    IF EXISTS (SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_schema = DATABASE() AND table_name = 'fn_codegeneration_run'
          AND constraint_name = 'CK_fn_codegeneration_run_Template' AND constraint_type = 'CHECK') THEN
        SET drops = CONCAT(drops, 'DROP CHECK CK_fn_codegeneration_run_Template, ');
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_schema = DATABASE() AND table_name = 'fn_codegeneration_run'
          AND constraint_name = 'CK_fn_codegeneration_run_ApplyTemplate' AND constraint_type = 'CHECK') THEN
        SET drops = CONCAT(drops, 'DROP CHECK CK_fn_codegeneration_run_ApplyTemplate, ');
    END IF;
    SET @fullnet_codegen_source_ddl = CONCAT('ALTER TABLE fn_codegeneration_run ', drops,
        'ADD CONSTRAINT CK_fn_codegeneration_run_Template CHECK (
            (TemplateId IS NULL AND TemplateVersion IS NULL)
            OR (TemplateId IS NOT NULL AND TemplateVersion IS NOT NULL AND TemplateVersion > 0)),
         ADD CONSTRAINT CK_fn_codegeneration_run_ApplyTemplate CHECK (
            (OperationKind IN (''preview'', ''apply'') AND SourceApplyRunId IS NULL
                AND (OperationKind = ''apply'' OR Status <> ''running''))
            OR (OperationKind = ''rollback'' AND SourceApplyRunId IS NOT NULL
                AND TemplateId IS NULL AND TemplateVersion IS NULL))');
    PREPARE fullnet_codegen_source_statement FROM @fullnet_codegen_source_ddl;
    EXECUTE fullnet_codegen_source_statement;
    DEALLOCATE PREPARE fullnet_codegen_source_statement;
END$$
DELIMITER ;
CALL fn_codegeneration_run_source_boundary();
DROP PROCEDURE IF EXISTS fn_codegeneration_run_source_boundary;
