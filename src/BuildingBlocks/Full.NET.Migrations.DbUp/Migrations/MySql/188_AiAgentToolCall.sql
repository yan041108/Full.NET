-- 188：Agent Tool 调用审计表。

CREATE TABLE IF NOT EXISTS fn_ai_agent_tool_call
(
    Id char(36) COLLATE utf8mb4_bin NOT NULL,
    TenantId char(36) COLLATE utf8mb4_bin NULL,
    ActorUserId char(36) COLLATE utf8mb4_bin NOT NULL,
    ToolName varchar(128) COLLATE utf8mb4_bin NOT NULL,
    PermissionCode varchar(128) COLLATE utf8mb4_bin NOT NULL,
    StatusKey varchar(16) COLLATE utf8mb4_bin NOT NULL,
    DurationMs int NULL,
    InputSummary varchar(512) NOT NULL,
    OutputSummary varchar(512) NULL,
    ErrorCode varchar(64) COLLATE utf8mb4_bin NULL,
    TraceId varchar(64) COLLATE utf8mb4_bin NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_ai_agent_tool_call_StatusKey
        CHECK (StatusKey IN ('succeeded', 'failed', 'denied'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

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
