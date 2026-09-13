-- 210：扩展工具执行意图与取消状态，不删除历史数据。
-- MySQL DDL 隐式提交，按约束存在性分别恢复 DROP/ADD 阶段。
SET @constraint_exists := (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_agent_tool_call'
      AND CONSTRAINT_NAME = 'CK_fn_ai_agent_tool_call_StatusKey' AND CONSTRAINT_TYPE = 'CHECK');
SET @ddl := IF(@constraint_exists > 0,
    'ALTER TABLE fn_ai_agent_tool_call DROP CHECK CK_fn_ai_agent_tool_call_StatusKey', 'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @constraint_exists := (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_agent_tool_call'
      AND CONSTRAINT_NAME = 'CK_fn_ai_agent_tool_call_StatusKey' AND CONSTRAINT_TYPE = 'CHECK');
SET @ddl := IF(@constraint_exists = 0,
    'ALTER TABLE fn_ai_agent_tool_call ADD CONSTRAINT CK_fn_ai_agent_tool_call_StatusKey CHECK (StatusKey IN (''started'', ''succeeded'', ''failed'', ''denied'', ''cancelled''))', 'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
