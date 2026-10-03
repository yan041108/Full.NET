-- 239：为操作日志预留可空受限详情与绝对到期时间，并建立独立清理检查点；不回填历史记录。
-- MySQL DDL 会隐式提交，逐列探测使 DbUp 未记账且仅完成一列时仍能安全收敛；大表 DDL 锁需在发布窗口评估。
SET @ContextColumnExists = (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_auditing_operation_log'
      AND COLUMN_NAME = 'ContextJson'
);
SET @AddContextColumn = IF(@ContextColumnExists = 0,
    'ALTER TABLE fn_auditing_operation_log ADD COLUMN ContextJson TEXT NULL COMMENT ''版本化且受限的操作详情 JSON''',
    'SELECT 1');
PREPARE AddOperationContextColumn FROM @AddContextColumn;
EXECUTE AddOperationContextColumn;
DEALLOCATE PREPARE AddOperationContextColumn;

SET @ExpiryColumnExists = (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_auditing_operation_log'
      AND COLUMN_NAME = 'DetailsExpiresAtUtc'
);
SET @AddExpiryColumn = IF(@ExpiryColumnExists = 0,
    'ALTER TABLE fn_auditing_operation_log ADD COLUMN DetailsExpiresAtUtc DATETIME(6) NULL COMMENT ''操作详情绝对到期时间(UTC)''',
    'SELECT 1');
PREPARE AddOperationExpiryColumn FROM @AddExpiryColumn;
EXECUTE AddOperationExpiryColumn;
DEALLOCATE PREPARE AddOperationExpiryColumn;

-- MySQL 无过滤索引；到期时间位于首列，使清理按范围扫描而非扫描整张审计表。
SET @ExpiryIndexExists = (
    SELECT COUNT(*) FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_auditing_operation_log'
      AND INDEX_NAME = 'IX_fn_auditing_operation_log_DetailsExpiresAtUtc_Id'
);
SET @AddExpiryIndex = IF(@ExpiryIndexExists = 0,
    'CREATE INDEX IX_fn_auditing_operation_log_DetailsExpiresAtUtc_Id ON fn_auditing_operation_log(DetailsExpiresAtUtc, Id)',
    'SELECT 1');
PREPARE AddOperationExpiryIndex FROM @AddExpiryIndex;
EXECUTE AddOperationExpiryIndex;
DEALLOCATE PREPARE AddOperationExpiryIndex;

-- 仅允许固定 StateKey=1 的 Worker 成功检查点；唯一约束阻止多实例插入多行。
CREATE TABLE IF NOT EXISTS fn_auditing_details_cleanup_state
(
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    StateKey TINYINT UNSIGNED NOT NULL COMMENT '唯一状态键；固定为1',
    LastSuccessfulCleanupAtUtc DATETIME(6) NOT NULL COMMENT '最近成功清理并检查积压的时间(UTC)',
    OldestExpiredAtUtc DATETIME(6) NULL COMMENT '检查时最早未清理详情的到期时间(UTC)',
    CONSTRAINT PK_fn_auditing_details_cleanup_state PRIMARY KEY (Id),
    CONSTRAINT UQ_fn_auditing_details_cleanup_state_StateKey UNIQUE (StateKey)
) COMMENT='操作详情清理共享检查点' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
