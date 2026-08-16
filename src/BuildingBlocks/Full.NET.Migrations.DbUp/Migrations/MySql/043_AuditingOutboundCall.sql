-- 043：出站调用审计汇总表。

CREATE TABLE IF NOT EXISTS fn_auditing_outbound_call (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    OccurredAtUtc datetime(6) NOT NULL COMMENT '发生时间(UTC)',
    ProviderKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '存储提供程序键',
    OperationKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '操作键',
    DestinationHostCategory varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '目标主机类别',
    StatusCode int NOT NULL COMMENT 'HTTP 状态码',
    Succeeded tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否成功',
    DurationMs int NOT NULL COMMENT '耗时(毫秒)',
    RetryCount int NOT NULL DEFAULT 0 COMMENT '重试次数',
    TraceId varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '追踪标识',
    SafeErrorCode varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '安全错误码',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    UserId BINARY(16) NULL COMMENT '用户标识',
    CONSTRAINT PK_fn_auditing_outbound_call PRIMARY KEY (Id),
    CONSTRAINT CK_fn_auditing_outbound_call_StatusCode
        CHECK (StatusCode BETWEEN 0 AND 999),
    CONSTRAINT CK_fn_auditing_outbound_call_DurationMs
        CHECK (DurationMs >= 0),
    CONSTRAINT CK_fn_auditing_outbound_call_RetryCount
        CHECK (RetryCount >= 0),
    KEY IX_fn_auditing_outbound_call_OccurredAtUtc_Id (OccurredAtUtc, Id),
    KEY IX_fn_auditing_outbound_call_ProviderKey_OccurredAtUtc (ProviderKey, OccurredAtUtc)
) COMMENT='审计出站调用表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP PROCEDURE IF EXISTS fn_auditing_outbound_call_boundary;
DELIMITER $$
CREATE PROCEDURE fn_auditing_outbound_call_boundary()
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_auditing_outbound_call'
          AND INDEX_NAME = 'IX_fn_auditing_outbound_call_OccurredAtUtc_Id'
    )
    AND
    (
        (
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_auditing_outbound_call'
              AND INDEX_NAME = 'IX_fn_auditing_outbound_call_OccurredAtUtc_Id'
        ) <> 2
        OR EXISTS
        (
            SELECT 1
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_auditing_outbound_call'
              AND INDEX_NAME = 'IX_fn_auditing_outbound_call_OccurredAtUtc_Id'
              AND
              (
                  NON_UNIQUE <> 1
                  OR SUB_PART IS NOT NULL
                  OR (SEQ_IN_INDEX = 1 AND COLUMN_NAME <> 'OccurredAtUtc')
                  OR (SEQ_IN_INDEX = 2 AND COLUMN_NAME <> 'Id')
              )
        )
    ) THEN
        DROP INDEX IX_fn_auditing_outbound_call_OccurredAtUtc_Id
            ON fn_auditing_outbound_call;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_auditing_outbound_call'
          AND INDEX_NAME = 'IX_fn_auditing_outbound_call_OccurredAtUtc_Id'
    ) THEN
        CREATE INDEX IX_fn_auditing_outbound_call_OccurredAtUtc_Id
            ON fn_auditing_outbound_call (OccurredAtUtc, Id);
    END IF;

    IF EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_auditing_outbound_call'
          AND INDEX_NAME = 'IX_fn_auditing_outbound_call_ProviderKey_OccurredAtUtc'
    )
    AND
    (
        (
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_auditing_outbound_call'
              AND INDEX_NAME = 'IX_fn_auditing_outbound_call_ProviderKey_OccurredAtUtc'
        ) <> 2
        OR EXISTS
        (
            SELECT 1
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_auditing_outbound_call'
              AND INDEX_NAME = 'IX_fn_auditing_outbound_call_ProviderKey_OccurredAtUtc'
              AND
              (
                  NON_UNIQUE <> 1
                  OR SUB_PART IS NOT NULL
                  OR (SEQ_IN_INDEX = 1 AND COLUMN_NAME <> 'ProviderKey')
                  OR (SEQ_IN_INDEX = 2 AND COLUMN_NAME <> 'OccurredAtUtc')
              )
        )
    ) THEN
        DROP INDEX IX_fn_auditing_outbound_call_ProviderKey_OccurredAtUtc
            ON fn_auditing_outbound_call;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_auditing_outbound_call'
          AND INDEX_NAME = 'IX_fn_auditing_outbound_call_ProviderKey_OccurredAtUtc'
    ) THEN
        CREATE INDEX IX_fn_auditing_outbound_call_ProviderKey_OccurredAtUtc
            ON fn_auditing_outbound_call (ProviderKey, OccurredAtUtc);
    END IF;
END$$
DELIMITER ;

CALL fn_auditing_outbound_call_boundary();
DROP PROCEDURE fn_auditing_outbound_call_boundary;
