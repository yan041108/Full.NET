-- 039：Host 流水号规则、作用域计数器与事务内幂等分配记录。
CREATE TABLE IF NOT EXISTS fn_serialnumbers_rule (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    RuleKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '规则键',
    DisplayName varchar(128) NOT NULL COMMENT '显示名称',
    Description varchar(512) NULL COMMENT '描述',
    Scope tinyint UNSIGNED NOT NULL COMMENT '作用域',
    ResetInterval tinyint UNSIGNED NOT NULL COMMENT '重置周期',
    Pattern varchar(128) NOT NULL COMMENT '编号模式',
    MinimumValue bigint NOT NULL COMMENT '最小值',
    MaximumValue bigint NOT NULL COMMENT '最大值',
    DisplayOrder int NOT NULL COMMENT '显示顺序',
    IsEnabled boolean NOT NULL COMMENT '是否启用',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CreatedByUserId BINARY(16) NOT NULL COMMENT '创建人用户标识',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    UpdatedByUserId BINARY(16) NULL COMMENT '更新人用户标识',
    Version bigint NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_serialnumbers_rule PRIMARY KEY (Id),
    CONSTRAINT CK_fn_serialnumbers_rule_Scope CHECK (Scope IN (0, 1)),
    CONSTRAINT CK_fn_serialnumbers_rule_ResetInterval
        CHECK (ResetInterval IN (0, 1, 2, 3)),
    CONSTRAINT CK_fn_serialnumbers_rule_ValueRange
        CHECK (MinimumValue >= 1 AND MaximumValue >= MinimumValue),
    UNIQUE KEY UX_fn_serialnumbers_rule_RuleKey (RuleKey)
) COMMENT='序列号规则表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_serialnumbers_counter (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    RuleId BINARY(16) NOT NULL COMMENT '规则标识',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    ScopeTenantKey BINARY(16)
        GENERATED ALWAYS AS
        (COALESCE(TenantId, 0x00000000000000000000000000000000))
        STORED COMMENT '作用域租户键',
    ResetBucket varchar(8) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '重置桶',
    LastValue bigint NOT NULL COMMENT '最后计数值',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    CONSTRAINT PK_fn_serialnumbers_counter PRIMARY KEY (Id),
    CONSTRAINT FK_fn_serialnumbers_counter_Rule
        FOREIGN KEY (RuleId) REFERENCES fn_serialnumbers_rule(Id),
    CONSTRAINT CK_fn_serialnumbers_counter_LastValue
        CHECK (LastValue >= 1),
    KEY IX_fn_serialnumbers_counter_RuleId (RuleId),
    UNIQUE KEY UX_fn_serialnumbers_counter_ScopeBucket
        (ScopeTenantKey, RuleId, ResetBucket)
) COMMENT='序列号计数器表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_serialnumbers_allocation (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    RuleId BINARY(16) NOT NULL COMMENT '规则标识',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    ScopeTenantKey BINARY(16)
        GENERATED ALWAYS AS
        (COALESCE(TenantId, 0x00000000000000000000000000000000))
        STORED COMMENT '作用域租户键',
    RuleKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '规则键',
    ResetBucket varchar(8) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '重置桶',
    IdempotencyKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '幂等键',
    SequenceValue bigint NOT NULL COMMENT '序列值',
    SerialNumber varchar(128) NOT NULL COMMENT '序列号',
    AllocatedAtUtc datetime(6) NOT NULL COMMENT '分配时间(UTC)',
    CONSTRAINT PK_fn_serialnumbers_allocation PRIMARY KEY (Id),
    CONSTRAINT FK_fn_serialnumbers_allocation_Rule
        FOREIGN KEY (RuleId) REFERENCES fn_serialnumbers_rule(Id),
    CONSTRAINT CK_fn_serialnumbers_allocation_SequenceValue
        CHECK (SequenceValue >= 1),
    KEY IX_fn_serialnumbers_allocation_RuleId (RuleId),
    UNIQUE KEY UX_fn_serialnumbers_allocation_Idempotency
        (ScopeTenantKey, RuleId, IdempotencyKey),
    UNIQUE KEY UX_fn_serialnumbers_allocation_Sequence
        (ScopeTenantKey, RuleId, ResetBucket, SequenceValue)
) COMMENT='序列号分配记录表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- MySQL DDL 会隐式提交，显式收敛同名但唯一性、列序或前缀错误的半完成索引。
DROP PROCEDURE IF EXISTS fn_serialnumbers_ensure_unique_index;
DELIMITER $$
CREATE PROCEDURE fn_serialnumbers_ensure_unique_index(
    IN pTableName varchar(64),
    IN pIndexName varchar(64),
    IN pKeyColumns varchar(512),
    IN pNonUnique int,
    IN pCreateSql text)
BEGIN
    DECLARE vActualColumns varchar(512);
    DECLARE vNonUnique int;
    DECLARE vPrefixCount int;

    SELECT
        GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX SEPARATOR ','),
        MAX(NON_UNIQUE),
        SUM(CASE WHEN SUB_PART IS NULL THEN 0 ELSE 1 END)
    INTO vActualColumns, vNonUnique, vPrefixCount
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = pTableName
      AND INDEX_NAME = pIndexName;

    IF vActualColumns IS NOT NULL
       AND
       (
           vActualColumns <> pKeyColumns
           OR vNonUnique <> pNonUnique
           OR vPrefixCount <> 0
       ) THEN
        SET @SerialNumberDdl = CONCAT(
            'DROP INDEX `',
            REPLACE(pIndexName, '`', '``'),
            '` ON `',
            REPLACE(pTableName, '`', '``'),
            '`');
        PREPARE serial_number_statement FROM @SerialNumberDdl;
        EXECUTE serial_number_statement;
        DEALLOCATE PREPARE serial_number_statement;
        SET vActualColumns = NULL;
    END IF;

    IF vActualColumns IS NULL THEN
        SET @SerialNumberDdl = pCreateSql;
        PREPARE serial_number_statement FROM @SerialNumberDdl;
        EXECUTE serial_number_statement;
        DEALLOCATE PREPARE serial_number_statement;
    END IF;
END$$
DELIMITER ;

CALL fn_serialnumbers_ensure_unique_index(
    'fn_serialnumbers_counter',
    'IX_fn_serialnumbers_counter_RuleId',
    'RuleId',
    1,
    'CREATE INDEX IX_fn_serialnumbers_counter_RuleId ON fn_serialnumbers_counter(RuleId)');
CALL fn_serialnumbers_ensure_unique_index(
    'fn_serialnumbers_allocation',
    'IX_fn_serialnumbers_allocation_RuleId',
    'RuleId',
    1,
    'CREATE INDEX IX_fn_serialnumbers_allocation_RuleId ON fn_serialnumbers_allocation(RuleId)');
CALL fn_serialnumbers_ensure_unique_index(
    'fn_serialnumbers_rule',
    'UX_fn_serialnumbers_rule_RuleKey',
    'RuleKey',
    0,
    'CREATE UNIQUE INDEX UX_fn_serialnumbers_rule_RuleKey ON fn_serialnumbers_rule(RuleKey)');
CALL fn_serialnumbers_ensure_unique_index(
    'fn_serialnumbers_counter',
    'UX_fn_serialnumbers_counter_ScopeBucket',
    'ScopeTenantKey,RuleId,ResetBucket',
    0,
    'CREATE UNIQUE INDEX UX_fn_serialnumbers_counter_ScopeBucket ON fn_serialnumbers_counter(ScopeTenantKey, RuleId, ResetBucket)');
CALL fn_serialnumbers_ensure_unique_index(
    'fn_serialnumbers_allocation',
    'UX_fn_serialnumbers_allocation_Idempotency',
    'ScopeTenantKey,RuleId,IdempotencyKey',
    0,
    'CREATE UNIQUE INDEX UX_fn_serialnumbers_allocation_Idempotency ON fn_serialnumbers_allocation(ScopeTenantKey, RuleId, IdempotencyKey)');
CALL fn_serialnumbers_ensure_unique_index(
    'fn_serialnumbers_allocation',
    'UX_fn_serialnumbers_allocation_Sequence',
    'ScopeTenantKey,RuleId,ResetBucket,SequenceValue',
    0,
    'CREATE UNIQUE INDEX UX_fn_serialnumbers_allocation_Sequence ON fn_serialnumbers_allocation(ScopeTenantKey, RuleId, ResetBucket, SequenceValue)');

DROP PROCEDURE fn_serialnumbers_ensure_unique_index;
