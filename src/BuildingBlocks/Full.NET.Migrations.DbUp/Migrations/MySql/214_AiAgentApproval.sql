-- 214：Agent 审批、委托与工具审计扩展；expand-only，支持幂等重跑。
CREATE TABLE IF NOT EXISTS fn_ai_agent_approval (
        Id binary(16) NOT NULL COMMENT '逻辑主键',
        ScopeKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '可信范围',
        TenantId binary(16) NULL COMMENT '租户标识',
        RunId binary(16) NOT NULL COMMENT '所属运行',
        OperationId binary(16) NOT NULL COMMENT '工具操作幂等键',
        SessionId binary(16) NOT NULL COMMENT '绑定会话',
        ToolName varchar(128) NOT NULL COMMENT '工具名',
        ToolVersion int NOT NULL COMMENT '工具版本',
        ArgumentsHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '参数摘要',
        ArgumentsProtected longtext NOT NULL COMMENT '受保护参数 JSON',
        PolicyVersion int NOT NULL COMMENT '策略版本',
        PresentationJson longtext NOT NULL COMMENT '可读展示 JSON',
        RequestedBy binary(16) NOT NULL COMMENT '发起人',
        ApproverId binary(16) NULL COMMENT '审批人',
        DecisionKey varchar(16) NOT NULL COMMENT '决策键',
        ExpiresAtUtc datetime(6) NOT NULL COMMENT '过期 UTC',
        ConsumedAtUtc datetime(6) NULL COMMENT '消费 UTC',
        Version bigint NOT NULL COMMENT '乐观版本',
        CreatedAtUtc datetime(6) NOT NULL COMMENT '创建 UTC',
        UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新 UTC',
        CONSTRAINT PK_fn_ai_agent_approval PRIMARY KEY (Id),
        UNIQUE KEY UX_fn_ai_agent_approval_Operation (OperationId),
        KEY IX_fn_ai_agent_approval_Run (RunId, CreatedAtUtc),
        CONSTRAINT CK_fn_ai_agent_approval_DecisionKey CHECK (DecisionKey IN ('pending', 'approved', 'denied'))
) COMMENT='人工智能 Agent 审批表' ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS fn_ai_agent_delegation (
        Id binary(16) NOT NULL COMMENT '逻辑主键',
        ScopeKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '可信范围',
        TenantId binary(16) NULL COMMENT '租户标识',
        GrantorUserId binary(16) NOT NULL COMMENT '委托人',
        GranteeUserId binary(16) NOT NULL COMMENT '被委托人',
        ToolName varchar(128) NULL COMMENT '限定工具',
        PermissionCode varchar(128) NULL COMMENT '限定权限',
        ExpiresAtUtc datetime(6) NOT NULL COMMENT '过期 UTC',
        RevokedAtUtc datetime(6) NULL COMMENT '撤销 UTC',
        Version bigint NOT NULL COMMENT '乐观版本',
        CreatedAtUtc datetime(6) NOT NULL COMMENT '创建 UTC',
        UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新 UTC',
        CONSTRAINT PK_fn_ai_agent_delegation PRIMARY KEY (Id),
        KEY IX_fn_ai_agent_delegation_Grantee (GranteeUserId, ExpiresAtUtc)
) COMMENT='人工智能 Agent 委托表' ENGINE=InnoDB;
SET @run_exists := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_agent_tool_call' AND COLUMN_NAME = 'RunId');
SET @ddl := IF(@run_exists = 0, 'ALTER TABLE fn_ai_agent_tool_call ADD COLUMN RunId binary(16) NULL COMMENT ''所属运行''', 'SELECT 1');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @step_exists := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_agent_tool_call' AND COLUMN_NAME = 'StepId');
SET @ddl := IF(@step_exists = 0, 'ALTER TABLE fn_ai_agent_tool_call ADD COLUMN StepId binary(16) NULL COMMENT ''步骤标识''', 'SELECT 1');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @hash_exists := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_agent_tool_call' AND COLUMN_NAME = 'ArgumentsHash');
SET @ddl := IF(@hash_exists = 0, 'ALTER TABLE fn_ai_agent_tool_call ADD COLUMN ArgumentsHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT ''参数摘要''', 'SELECT 1');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @approval_exists := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_agent_tool_call' AND COLUMN_NAME = 'ApprovalId');
SET @ddl := IF(@approval_exists = 0, 'ALTER TABLE fn_ai_agent_tool_call ADD COLUMN ApprovalId binary(16) NULL COMMENT ''审批引用''', 'SELECT 1');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @receipt_exists := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_agent_tool_call' AND COLUMN_NAME = 'ReceiptId');
SET @ddl := IF(@receipt_exists = 0, 'ALTER TABLE fn_ai_agent_tool_call ADD COLUMN ReceiptId binary(16) NULL COMMENT ''业务回执引用''', 'SELECT 1');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
