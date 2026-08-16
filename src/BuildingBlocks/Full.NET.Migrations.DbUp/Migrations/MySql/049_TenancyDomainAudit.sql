-- 049：Tenancy 模块 B0 域内同事务审计。
-- 该表只承接与业务写入同事务提交的域内审计记录，不经过 Outbox；Outcome 固定为
-- success/failure 两种取值，禁止承载其他审计可靠性等级的数据。
CREATE TABLE IF NOT EXISTS fn_tenancy_domain_audit (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    ActionKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '操作键',
    EntityId BINARY(16) NOT NULL COMMENT '实体标识',
    Outcome varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '结果',
    ActorUserId BINARY(16) NULL COMMENT '操作者用户标识',
    ActorDisplayName varchar(128) NULL COMMENT '操作者显示名',
    TraceId varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '追踪标识',
    DiffSummaryJson text NULL COMMENT '差异摘要(JSON)',
    OccurredAtUtc datetime(6) NOT NULL COMMENT '发生时间(UTC)',
    CONSTRAINT PK_fn_tenancy_domain_audit PRIMARY KEY (Id),
    CONSTRAINT CK_fn_tenancy_domain_audit_Outcome
        CHECK (Outcome IN ('success', 'failure')),
    KEY IX_fn_tenancy_domain_audit_OccurredAtUtc_Id (OccurredAtUtc, Id)
) COMMENT='租户领域审计表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- MySQL DDL 会隐式提交，第二个索引独立收敛表已存在但索引缺失或形状错误的状态。
DROP PROCEDURE IF EXISTS fn_tenancy_domain_audit_tenant_index;
DELIMITER $$
CREATE PROCEDURE fn_tenancy_domain_audit_tenant_index()
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_tenancy_domain_audit'
          AND INDEX_NAME = 'IX_fn_tenancy_domain_audit_TenantId_OccurredAtUtc_Id'
    )
    AND
    (
        (
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_tenancy_domain_audit'
              AND INDEX_NAME = 'IX_fn_tenancy_domain_audit_TenantId_OccurredAtUtc_Id'
        ) <> 3
        OR EXISTS
        (
            SELECT 1
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_tenancy_domain_audit'
              AND INDEX_NAME = 'IX_fn_tenancy_domain_audit_TenantId_OccurredAtUtc_Id'
              AND
              (
                  NON_UNIQUE <> 1
                  OR SUB_PART IS NOT NULL
                  OR (SEQ_IN_INDEX = 1 AND COLUMN_NAME <> 'TenantId')
                  OR (SEQ_IN_INDEX = 2 AND COLUMN_NAME <> 'OccurredAtUtc')
                  OR (SEQ_IN_INDEX = 3 AND COLUMN_NAME <> 'Id')
              )
        )
    ) THEN
        DROP INDEX IX_fn_tenancy_domain_audit_TenantId_OccurredAtUtc_Id
            ON fn_tenancy_domain_audit;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_tenancy_domain_audit'
          AND INDEX_NAME = 'IX_fn_tenancy_domain_audit_TenantId_OccurredAtUtc_Id'
    ) THEN
        CREATE INDEX IX_fn_tenancy_domain_audit_TenantId_OccurredAtUtc_Id
            ON fn_tenancy_domain_audit (TenantId, OccurredAtUtc, Id);
    END IF;
END$$
DELIMITER ;

CALL fn_tenancy_domain_audit_tenant_index();
DROP PROCEDURE fn_tenancy_domain_audit_tenant_index;
