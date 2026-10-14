-- 259：新增可空诊断列；旧运行不回填，应用回退保留此列，重放不得改写运行及租约。
SET @FnAiTraceDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_agent_run' AND COLUMN_NAME='TraceParent'),
    'ALTER TABLE fn_ai_agent_run ADD COLUMN TraceParent varchar(55) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT ''运行创建时的 W3C 上下文；仅作恢复追踪链接''', 'SELECT 1');
PREPARE stmt FROM @FnAiTraceDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiTraceDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_agent_run' AND COLUMN_NAME='TraceParent'
      AND COLUMN_COMMENT<>'运行创建时的 W3C 上下文；仅作恢复追踪链接'),
    'ALTER TABLE fn_ai_agent_run MODIFY COLUMN TraceParent varchar(55) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT ''运行创建时的 W3C 上下文；仅作恢复追踪链接''', 'SELECT 1');
PREPARE stmt FROM @FnAiTraceDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
