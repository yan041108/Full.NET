CREATE TABLE IF NOT EXISTS fn_identity_organization_user_unit_projection (
    TenantId binary(16) NOT NULL COMMENT '租户标识',
    AssignmentId binary(16) NOT NULL COMMENT '源隶属标识',
    UserId binary(16) NOT NULL COMMENT '用户标识',
    UnitId binary(16) NOT NULL COMMENT '部门标识',
    IsActive boolean NOT NULL COMMENT '是否启用',
    SourceVersion bigint NOT NULL COMMENT '源版本',
    SourceUpdatedAtUtc datetime(6) NOT NULL COMMENT '源更新时间 UTC',
 CONSTRAINT PK_fn_identity_organization_user_unit_projection PRIMARY KEY (TenantId,AssignmentId),
 INDEX IX_fn_identity_organization_user_unit_projection_UnitUser (TenantId,UnitId,IsActive,UserId)
) COMMENT='身份模块机构隶属读投影' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
-- 源模块按版本恢复发布；中途失败后可重试新增列。
DROP PROCEDURE IF EXISTS fn_organization_user_unit_projection_column;
DELIMITER $$
CREATE PROCEDURE fn_organization_user_unit_projection_column()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS
        WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_identity_organization_user_unit_projection'
          AND INDEX_NAME='IX_fn_identity_organization_user_unit_projection_UnitUser') THEN
        CREATE INDEX IX_fn_identity_organization_user_unit_projection_UnitUser
            ON fn_identity_organization_user_unit_projection(TenantId,UnitId,IsActive,UserId);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_organization_user_unit'
          AND COLUMN_NAME='PublishedProjectionVersion') THEN
        ALTER TABLE fn_organization_user_unit ADD COLUMN PublishedProjectionVersion int NOT NULL DEFAULT 0
            COMMENT '已发布读投影源版本';
    END IF;
END$$
DELIMITER ;
CALL fn_organization_user_unit_projection_column();
DROP PROCEDURE IF EXISTS fn_organization_user_unit_projection_column;
