-- 188：Agent Tool 调用审计表。

CREATE TABLE IF NOT EXISTS fn_ai_agent_tool_call (
    Id char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '逻辑主键',
    TenantId char(36) COLLATE utf8mb4_bin NULL COMMENT '租户标识；NULL 表示 Host 级',
    ActorUserId char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '操作者用户标识',
    ToolName varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '工具名称',
    PermissionCode varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '权限码',
    StatusKey varchar(16) COLLATE utf8mb4_bin NOT NULL COMMENT '状态键',
    DurationMs int NULL COMMENT '耗时(毫秒)',
    InputSummary varchar(512) NOT NULL COMMENT '输入摘要',
    OutputSummary varchar(512) NULL COMMENT '输出摘要',
    ErrorCode varchar(64) COLLATE utf8mb4_bin NULL COMMENT '错误码',
    TraceId varchar(64) COLLATE utf8mb4_bin NULL COMMENT '追踪标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_ai_agent_tool_call_StatusKey
        CHECK (StatusKey IN ('succeeded', 'failed', 'denied'))
) COMMENT='人工智能智能体工具调用表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

SET @index_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_ai_agent_tool_call'
      AND INDEX_NAME = 'IX_fn_ai_agent_tool_call_CreatedAtUtc'
);
SET @ddl := IF(
    @index_exists = 0,
    'CREATE INDEX IX_fn_ai_agent_tool_call_CreatedAtUtc ON fn_ai_agent_tool_call (CreatedAtUtc DESC, Id)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @index_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_ai_agent_tool_call'
      AND INDEX_NAME = 'IX_fn_ai_agent_tool_call_TenantId_CreatedAtUtc'
);
SET @ddl := IF(
    @index_exists = 0,
    'CREATE INDEX IX_fn_ai_agent_tool_call_TenantId_CreatedAtUtc ON fn_ai_agent_tool_call (TenantId, CreatedAtUtc DESC, Id)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @index_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_ai_agent_tool_call'
      AND INDEX_NAME = 'IX_fn_ai_agent_tool_call_ToolName_CreatedAtUtc'
);
SET @ddl := IF(
    @index_exists = 0,
    'CREATE INDEX IX_fn_ai_agent_tool_call_ToolName_CreatedAtUtc ON fn_ai_agent_tool_call (ToolName, CreatedAtUtc DESC, Id)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
